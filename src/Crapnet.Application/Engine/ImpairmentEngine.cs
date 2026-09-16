using Crapnet.Application.Abstractions;
using Crapnet.Application.Impairments;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crapnet.Application.Engine;

/// <summary>
/// Pulls packets off the capture gateway, asks the pipeline what to do with each one, and either
/// sends, delays or discards it.
/// </summary>
/// <remarks>
/// The capture loop owns all per-rule state and runs single-threaded, so the pipeline never needs
/// locking on its hot path. Rule edits arrive from the UI thread through a single volatile
/// handover rather than a lock: the capture thread picks the new profile up at the top of its next
/// iteration, which is what makes toggling an impairment take effect immediately without
/// interrupting the capture.
/// </remarks>
public sealed class ImpairmentEngine : IImpairmentEngine
{
    private readonly IPacketGateway _gateway;
    private readonly IClock _clock;
    private readonly IRandomSource _random;
    private readonly ILogger<ImpairmentEngine> _logger;
    private readonly ImpairmentPipeline _pipeline;
    private readonly RuleStateTable _states = new();
    private readonly object _lifecycle = new();

    private PacketScheduler? _scheduler;
    private CancellationTokenSource? _cancellation;
    private Thread? _captureThread;
    private EngineState _state = EngineState.Stopped;

    private Profile _activeProfile = Profile.Empty();
    private Profile? _pendingProfile;

    private long _packetsSeen;
    private long _packetsMatched;
    private long _packetsForwarded;
    private long _packetsDropped;
    private long _packetsDelayed;
    private long _packetsDuplicated;
    private long _packetsTampered;
    private long _packetsReset;
    private long _packetsReordered;
    private long _bytesUplink;
    private long _bytesDownlink;

    private long _startedAtMilliseconds;
    private long _lastSampleMilliseconds;
    private long _lastSampleUplinkBytes;
    private long _lastSampleDownlinkBytes;

    public ImpairmentEngine(
        IPacketGateway gateway,
        IClock clock,
        IRandomSource random,
        ILogger<ImpairmentEngine>? logger = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _logger = logger ?? NullLogger<ImpairmentEngine>.Instance;
        _pipeline = new ImpairmentPipeline(random);
    }

    public EngineState State
    {
        get { lock (_lifecycle) return _state; }
    }

    public bool Bypass { get; set; }

    public event EventHandler<EngineState>? StateChanged;
    public event EventHandler<string>? Faulted;

    public void Start(EngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (_lifecycle)
        {
            if (_state is EngineState.Running or EngineState.Starting) return;
            SetState(EngineState.Starting);
        }

        try
        {
            _activeProfile = options.Profile;
            _states.Clear();
            _states.Retain(options.Profile);
            ResetCounters();

            _gateway.Open(options.ToFilter());

            _scheduler = new PacketScheduler(_clock, Emit, _gateway.Release);
            _scheduler.Start();

            _cancellation = new CancellationTokenSource();
            _captureThread = new Thread(() => CaptureLoop(_cancellation.Token))
            {
                IsBackground = true,
                Name = "crapnet-capture",
                Priority = ThreadPriority.AboveNormal,
            };
            _captureThread.Start();

            _startedAtMilliseconds = _clock.ElapsedMilliseconds;
            _lastSampleMilliseconds = _startedAtMilliseconds;
            SetState(EngineState.Running);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start the impairment engine.");
            SafeStop();
            SetState(EngineState.Faulted);
            Faulted?.Invoke(this, ex.Message);
            throw;
        }
    }

    public void Stop()
    {
        lock (_lifecycle)
        {
            if (_state is EngineState.Stopped or EngineState.Stopping) return;
            SetState(EngineState.Stopping);
        }

        SafeStop();
        SetState(EngineState.Stopped);
    }

    public void Apply(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Publish for the capture thread to collect. If it is not running, adopt it directly so a
        // later Start sees the edit.
        if (State == EngineState.Running) Volatile.Write(ref _pendingProfile, profile);
        else _activeProfile = profile;
    }

    public EngineStatistics Snapshot()
    {
        var now = _clock.ElapsedMilliseconds;
        var uplinkBytes = Interlocked.Read(ref _bytesUplink);
        var downlinkBytes = Interlocked.Read(ref _bytesDownlink);

        // Rates are measured between successive calls, which suits the UI polling this on a timer.
        var elapsed = now - _lastSampleMilliseconds;
        double uplinkRate = 0, downlinkRate = 0;
        if (elapsed >= 50)
        {
            uplinkRate = (uplinkBytes - _lastSampleUplinkBytes) * 8_000d / elapsed;
            downlinkRate = (downlinkBytes - _lastSampleDownlinkBytes) * 8_000d / elapsed;

            _lastSampleMilliseconds = now;
            _lastSampleUplinkBytes = uplinkBytes;
            _lastSampleDownlinkBytes = downlinkBytes;
        }

        return new EngineStatistics
        {
            PacketsSeen = Interlocked.Read(ref _packetsSeen),
            PacketsMatched = Interlocked.Read(ref _packetsMatched),
            PacketsForwarded = Interlocked.Read(ref _packetsForwarded),
            PacketsDropped = Interlocked.Read(ref _packetsDropped),
            PacketsDelayed = Interlocked.Read(ref _packetsDelayed),
            PacketsDuplicated = Interlocked.Read(ref _packetsDuplicated),
            PacketsTampered = Interlocked.Read(ref _packetsTampered),
            PacketsReset = Interlocked.Read(ref _packetsReset),
            PacketsReordered = Interlocked.Read(ref _packetsReordered),
            BytesUplink = uplinkBytes,
            BytesDownlink = downlinkBytes,
            QueueDepth = _scheduler?.Count ?? 0,
            UplinkBitsPerSecond = Math.Max(0, uplinkRate),
            DownlinkBitsPerSecond = Math.Max(0, downlinkRate),
            Uptime = State == EngineState.Running
                ? TimeSpan.FromMilliseconds(now - _startedAtMilliseconds)
                : TimeSpan.Zero,
        };
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_gateway.TryReceive(out var packet, cancellationToken)) break;
                if (packet is null) continue;

                Process(packet);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The capture loop stopped unexpectedly.");
            SetState(EngineState.Faulted);
            Faulted?.Invoke(this, ex.Message);
        }
    }

    private void Process(CapturedPacket packet)
    {
        var now = _clock.ElapsedMilliseconds;
        Interlocked.Increment(ref _packetsSeen);

        if (packet.Descriptor.Direction == LinkDirection.Uplink)
            Interlocked.Add(ref _bytesUplink, packet.Length);
        else
            Interlocked.Add(ref _bytesDownlink, packet.Length);

        if (Bypass)
        {
            Emit(packet, 0);
            return;
        }

        var pending = Interlocked.Exchange(ref _pendingProfile, null);
        if (pending is not null)
        {
            _activeProfile = pending;
            _states.Retain(pending);
        }

        var plan = _pipeline.Evaluate(_activeProfile, _states, packet.Descriptor, packet.Length, now);
        if (plan.Rule is not null) Interlocked.Increment(ref _packetsMatched);

        switch (plan.Action)
        {
            case PacketAction.Discard:
                Interlocked.Increment(ref _packetsDropped);
                _gateway.Release(packet);
                return;

            case PacketAction.Reset:
                EmitReset(packet);
                return;

            default:
                Forward(packet, plan, now);
                return;
        }
    }

    private void Forward(CapturedPacket packet, in PacketPlan plan, long now)
    {
        if (plan.Tamper && plan.Rule is not null)
        {
            var changed = PacketMutator.Tamper(packet.PayloadSpan, plan.Rule.Impairments.Tamper.MaxBytes, _random);
            if (changed > 0)
            {
                Interlocked.Increment(ref _packetsTampered);

                // Leaving the checksum stale is the whole point of the "bad sum" mode: the peer's
                // own stack then discards the packet, which is what real corruption looks like.
                if (!plan.LeaveChecksumBroken) _gateway.RecalculateChecksums(packet);
            }
        }

        if (plan.Reordered) Interlocked.Increment(ref _packetsReordered);

        if (!plan.IsDelayed(now))
        {
            Emit(packet, plan.ExtraCopies);
            return;
        }

        if (_scheduler?.TrySchedule(packet, plan.ReleaseAtMilliseconds, plan.ExtraCopies) == true)
        {
            Interlocked.Increment(ref _packetsDelayed);
            return;
        }

        // The hold queue is full, so the packet is shed rather than sent early: sending it now
        // would quietly undo the delay the user asked for.
        Interlocked.Increment(ref _packetsDropped);
        _gateway.Release(packet);
    }

    private void EmitReset(CapturedPacket packet)
    {
        if (TcpResetWriter.TryRewriteAsReset(packet.Span, out var length))
        {
            packet.Length = length;
            _gateway.RecalculateChecksums(packet);
            Interlocked.Increment(ref _packetsReset);
        }

        // A packet we could not turn into a reset is forwarded untouched rather than lost.
        Emit(packet, 0);
    }

    private void Emit(CapturedPacket packet, int extraCopies)
    {
        try
        {
            _gateway.Send(packet);
            Interlocked.Increment(ref _packetsForwarded);

            for (var i = 0; i < extraCopies; i++)
            {
                _gateway.Send(packet);
                Interlocked.Increment(ref _packetsDuplicated);
            }
        }
        catch (Exception ex)
        {
            // One unsendable packet must not take the engine down; the network is allowed to be
            // unreliable, that is the subject under test.
            _logger.LogDebug(ex, "Dropping a packet that could not be reinjected.");
        }
        finally
        {
            _gateway.Release(packet);
        }
    }

    private void SafeStop()
    {
        try { _cancellation?.Cancel(); } catch (Exception ex) { _logger.LogDebug(ex, "Cancellation failed."); }

        // Closing the gateway is what unblocks a capture thread parked in a blocking receive.
        try { _gateway.Close(); } catch (Exception ex) { _logger.LogDebug(ex, "Closing the gateway failed."); }

        _captureThread?.Join(TimeSpan.FromSeconds(2));
        _captureThread = null;

        _scheduler?.Stop();
        _scheduler?.Dispose();
        _scheduler = null;

        _cancellation?.Dispose();
        _cancellation = null;

        _states.Clear();
    }

    private void ResetCounters()
    {
        Interlocked.Exchange(ref _packetsSeen, 0);
        Interlocked.Exchange(ref _packetsMatched, 0);
        Interlocked.Exchange(ref _packetsForwarded, 0);
        Interlocked.Exchange(ref _packetsDropped, 0);
        Interlocked.Exchange(ref _packetsDelayed, 0);
        Interlocked.Exchange(ref _packetsDuplicated, 0);
        Interlocked.Exchange(ref _packetsTampered, 0);
        Interlocked.Exchange(ref _packetsReset, 0);
        Interlocked.Exchange(ref _packetsReordered, 0);
        Interlocked.Exchange(ref _bytesUplink, 0);
        Interlocked.Exchange(ref _bytesDownlink, 0);
        _lastSampleUplinkBytes = 0;
        _lastSampleDownlinkBytes = 0;
    }

    private void SetState(EngineState state)
    {
        lock (_lifecycle)
        {
            if (_state == state) return;
            _state = state;
        }

        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        Stop();
        _gateway.Dispose();
    }
}
