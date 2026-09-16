using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Crapnet.Application.Abstractions;
using Crapnet.Domain.Networking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crapnet.Infrastructure.Capture;

/// <summary>
/// The WinDivert implementation of <see cref="IPacketGateway"/>.
/// </summary>
/// <remarks>
/// <para>
/// One reader thread per open handle parks in <c>WinDivertRecv</c> and pushes packets into a
/// bounded queue that <see cref="TryReceive"/> drains. The indirection buys two things: a
/// <see cref="CaptureScope.Both"/> capture needs two handles but the engine still wants a single
/// blocking receive, and the queue bound means a stalled engine applies backpressure to the driver
/// instead of growing the heap until the process dies.
/// </para>
/// <para>
/// Nothing here is a hot path by accident. Buffers are pooled, the descriptor is parsed once on the
/// reader thread, and the driver's own address record rides along on the packet so reinjection
/// needs no lookup.
/// </para>
/// </remarks>
public sealed class WinDivertPacketGateway : IPacketGateway
{
    /// <summary>
    /// Size of every packet buffer.
    /// </summary>
    /// <remarks>
    /// Sized to the largest an IPv4 packet can claim to be rather than to the link MTU: with large
    /// send offload enabled the forward layer routinely hands over segments of tens of kilobytes,
    /// and a short buffer would truncate them.
    /// </remarks>
    public const int PacketBufferSize = 65535;

    /// <summary>
    /// How many packet instances to keep alive between uses.
    /// </summary>
    /// <remarks>
    /// Each one pins 64 KB, so an unbounded pool would hold megabytes hostage after a single
    /// traffic burst. Past this many, allocation is cheaper than retention and the excess is simply
    /// collected.
    /// </remarks>
    public const int MaxPooledPackets = 256;

    // WinDivert clamps these itself and fails the call outside the range, which would surface as an
    // opaque ERROR_INVALID_PARAMETER long after the offending value was configured.
    private const int MinQueueLength = 32;
    private const int MaxQueueLength = 16384;
    private const int MinQueueTimeMilliseconds = 100;
    private const int MaxQueueTimeMilliseconds = 16000;

    private readonly ILogger<WinDivertPacketGateway> _logger;
    private readonly ConcurrentBag<CapturedPacket> _pool = [];
    private readonly List<CaptureHandle> _handles = [];
    private readonly List<Thread> _readers = [];
    private readonly object _lifecycleLock = new();

    private BlockingCollection<CapturedPacket>? _queue;
    private Ipv4Subnet _deviceSubnet;
    private int _pooledCount;
    private volatile bool _closing;
    private volatile bool _isOpen;
    private bool _disposed;

    /// <summary>Creates a gateway that logs nothing.</summary>
    public WinDivertPacketGateway()
        : this(NullLogger<WinDivertPacketGateway>.Instance)
    {
    }

    /// <summary>Creates a gateway that reports driver-level trouble through <paramref name="logger"/>.</summary>
    public WinDivertPacketGateway(ILogger<WinDivertPacketGateway> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public bool IsOpen => _isOpen;

    /// <summary>The filter string handed to the driver, kept for diagnostics. Null until opened.</summary>
    public string? ActiveFilter { get; private set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The gateway is already open, or the interop layout check failed.</exception>
    /// <exception cref="Win32Exception">The driver refused to open; the message names the likely cause.</exception>
    public void Open(CaptureFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lifecycleLock)
        {
            if (_isOpen) throw new InvalidOperationException("The capture is already open.");

            WinDivertNative.VerifyLayout();

            _deviceSubnet = filter.DeviceSubnet;
            ActiveFilter = WinDivertFilterBuilder.Build(filter);
            _closing = false;

            var queueLength = Math.Clamp(filter.QueueLength, MinQueueLength, MaxQueueLength);
            var queueTime = Math.Clamp(filter.QueueTimeMilliseconds, MinQueueTimeMilliseconds, MaxQueueTimeMilliseconds);

            // Matching the queue bound to the driver's own depth keeps the two backlogs the same
            // size, so a slow engine stalls the driver at roughly the point the driver would have
            // started discarding packets on its own.
            _queue = new BlockingCollection<CapturedPacket>(queueLength);

            try
            {
                foreach (var layer in WinDivertFilterBuilder.LayersFor(filter.Scope))
                {
                    var handle = WinDivertNative.WinDivertOpen(
                        ActiveFilter, layer, filter.Priority, WinDivertOpenFlags.None);

                    if (handle == WinDivertNative.InvalidHandleValue)
                    {
                        var error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(error, WinDivertNative.DescribeOpenError(error));
                    }

                    _handles.Add(new CaptureHandle(handle, layer));
                    ConfigureQueue(handle, queueLength, queueTime);
                }

                _isOpen = true;

                foreach (var captured in _handles)
                {
                    var reader = new Thread(() => ReadLoop(captured))
                    {
                        IsBackground = true,
                        Name = $"crapnet-windivert-{captured.Layer}",
                        Priority = ThreadPriority.AboveNormal,
                    };

                    _readers.Add(reader);
                    reader.Start();
                }
            }
            catch
            {
                CloseCore();
                throw;
            }
        }
    }

    /// <inheritdoc />
    public bool TryReceive(out CapturedPacket? packet, CancellationToken cancellationToken)
    {
        packet = null;
        var queue = _queue;
        if (queue is null) return false;

        try
        {
            if (!queue.TryTake(out var taken, Timeout.Infinite, cancellationToken)) return false;
            packet = taken;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            // Close raced the wait; an already-closed gateway simply has nothing more to give.
            return false;
        }
        catch (InvalidOperationException)
        {
            // The queue was marked complete while we waited, which is the ordinary end of capture.
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Failures are logged rather than thrown. A reinjection that the driver refuses is
    /// indistinguishable in effect from the packet being dropped, and a capture loop that dies
    /// because one packet out of millions could not be re-sent would be far worse than the loss.
    /// </remarks>
    public unsafe void Send(CapturedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (!_isOpen || packet.Length <= 0) return;

        if (packet.GatewayToken is not WinDivertPacketToken token || token.Handle == 0)
        {
            _logger.LogWarning("Refusing to send a packet that carries no WinDivert address.");
            return;
        }

        // The address is copied to the stack for the call, so the token stays pristine and the
        // Duplicate impairment can send the same packet as many times as it likes.
        var address = token.Address;
        uint sent;

        bool ok;
        fixed (byte* buffer = packet.Buffer)
        {
            ok = WinDivertNative.WinDivertSend(token.Handle, buffer, (uint)packet.Length, &sent, &address);
        }

        if (!ok && !_closing)
        {
            _logger.LogWarning(
                "WinDivertSend dropped a {Length}-byte packet: {Error}",
                packet.Length,
                new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
    }

    /// <inheritdoc />
    public void Release(CapturedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        packet.Reset();

        // Racing increments can let the pool overshoot slightly; that is a handful of extra buffers,
        // not a leak, and is cheaper than serialising every release behind a lock.
        if (Volatile.Read(ref _pooledCount) >= MaxPooledPackets) return;

        Interlocked.Increment(ref _pooledCount);
        _pool.Add(packet);
    }

    /// <inheritdoc />
    public unsafe void RecalculateChecksums(CapturedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Length <= 0) return;

        var token = packet.GatewayToken as WinDivertPacketToken;
        var address = token?.Address ?? WinDivertAddress.CreateOutbound(WinDivertLayer.NetworkForward);

        bool ok;
        fixed (byte* buffer = packet.Buffer)
        {
            // Flags of zero means "recompute every checksum you understand", which is what an
            // arbitrary payload edit calls for.
            ok = WinDivertNative.WinDivertHelperCalcChecksums(buffer, (uint)packet.Length, &address, 0UL);
        }

        if (!ok)
        {
            _logger.LogWarning(
                "WinDivertHelperCalcChecksums failed for a {Length}-byte packet; it will go out with stale checksums.",
                packet.Length);
            return;
        }

        // The helper rewrites the address's checksum-valid bits to describe what it just computed,
        // so the updated record has to go back onto the packet: reinjecting with the stale bits
        // would tell the driver to trust checksums that no longer match the payload.
        if (token is not null) token.Address = address;
    }

    /// <inheritdoc />
    public void Close()
    {
        lock (_lifecycleLock)
        {
            CloseCore();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            CloseCore();
        }
    }

    private static void ConfigureQueue(nint handle, int queueLength, int queueTimeMilliseconds)
    {
        if (!WinDivertNative.WinDivertSetParam(handle, WinDivertParam.QueueLength, (ulong)queueLength))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinDivert rejected the requested queue length.");

        if (!WinDivertNative.WinDivertSetParam(handle, WinDivertParam.QueueTime, (ulong)queueTimeMilliseconds))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinDivert rejected the requested queue time.");
    }

    private unsafe void ReadLoop(CaptureHandle capture)
    {
        var handle = capture.Handle;

        while (!_closing)
        {
            var packet = Rent();
            var token = TokenFor(packet);
            token.Handle = handle;

            uint received;
            bool ok;

            fixed (byte* buffer = packet.Buffer)
            fixed (WinDivertAddress* address = &token.Address)
            {
                ok = WinDivertNative.WinDivertRecv(handle, buffer, (uint)packet.Buffer.Length, &received, address);
            }

            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                Release(packet);

                // NoData is the shutdown handshake rather than a fault: Close called
                // WinDivertShutdown, which is the only thing that can wake a thread parked in
                // WinDivertRecv.
                if (_closing ||
                    error is WinDivertNative.ErrorCodes.NoData
                        or WinDivertNative.ErrorCodes.InvalidHandle
                        or WinDivertNative.ErrorCodes.OperationAborted)
                {
                    break;
                }

                _logger.LogError("WinDivertRecv failed: {Error}", new Win32Exception(error).Message);
                continue;
            }

            packet.Length = (int)received;

            if (!Ipv4PacketParser.TryParse(
                    packet.ReadOnlySpan,
                    _deviceSubnet,
                    out var descriptor,
                    out var payloadOffset,
                    out var payloadLength))
            {
                // Anything we cannot describe cannot be matched by a rule either, so it goes
                // straight back on the wire instead of reaching the engine as an unclassifiable
                // packet the engine would have to special-case.
                Send(packet);
                Release(packet);
                continue;
            }

            packet.Descriptor = descriptor;
            packet.PayloadOffset = payloadOffset;
            packet.PayloadLength = payloadLength;

            if (!Enqueue(packet)) break;
        }
    }

    /// <summary>
    /// Hands a packet to the consumer, waiting for room rather than discarding it.
    /// </summary>
    /// <remarks>
    /// The wait is a polled <c>TryAdd</c> rather than a blocking <c>Add</c> so that Close never has
    /// to join a thread that is parked on a full queue nobody is draining.
    /// </remarks>
    private bool Enqueue(CapturedPacket packet)
    {
        var queue = _queue;

        while (queue is not null && !_closing)
        {
            try
            {
                if (queue.TryAdd(packet, 100)) return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
                break;
            }
        }

        // Nobody will ever take this one, so it must not leak out of the pool's accounting.
        Send(packet);
        Release(packet);
        return false;
    }

    private CapturedPacket Rent()
    {
        if (_pool.TryTake(out var pooled))
        {
            Interlocked.Decrement(ref _pooledCount);
            pooled.Reset();
            return pooled;
        }

        return new CapturedPacket(PacketBufferSize) { GatewayToken = new WinDivertPacketToken() };
    }

    private static WinDivertPacketToken TokenFor(CapturedPacket packet)
    {
        if (packet.GatewayToken is WinDivertPacketToken existing) return existing;

        var token = new WinDivertPacketToken();
        packet.GatewayToken = token;
        return token;
    }

    /// <summary>
    /// Tears the capture down. Must be called under <see cref="_lifecycleLock"/>.
    /// </summary>
    /// <remarks>
    /// The order matters. <c>WinDivertShutdown</c> is what releases a thread blocked inside
    /// <c>WinDivertRecv</c> — there is no cancellable overload and killing the thread would leak
    /// the driver's pending I/O — so every handle is shut down first, the readers are given the
    /// chance to notice, and only then are the handles closed.
    /// </remarks>
    private void CloseCore()
    {
        _closing = true;
        _isOpen = false;

        foreach (var capture in _handles)
        {
            WinDivertNative.WinDivertShutdown(capture.Handle, WinDivertShutdownHow.Both);
        }

        // Draining first means a reader stuck mid-enqueue finds room and exits on its own.
        DrainQueue();

        foreach (var reader in _readers)
        {
            if (reader.IsAlive) reader.Join(TimeSpan.FromSeconds(2));
        }

        _readers.Clear();

        foreach (var capture in _handles)
        {
            WinDivertNative.WinDivertClose(capture.Handle);
        }

        _handles.Clear();

        var queue = _queue;
        _queue = null;

        if (queue is not null)
        {
            queue.CompleteAdding();
            while (queue.TryTake(out var leftover)) Release(leftover);
            queue.Dispose();
        }

        ActiveFilter = null;
    }

    private void DrainQueue()
    {
        var queue = _queue;
        if (queue is null) return;

        while (queue.TryTake(out var packet)) Release(packet);
    }

    /// <summary>An open driver handle paired with the layer it was opened on.</summary>
    private readonly record struct CaptureHandle(nint Handle, WinDivertLayer Layer);

    /// <summary>
    /// Per-packet state the gateway attaches to a <see cref="CapturedPacket"/>.
    /// </summary>
    /// <remarks>
    /// A class rather than a struct so that the 80-byte address can be written in place by the
    /// driver and read back for reinjection without ever being copied through a boxing conversion.
    /// </remarks>
    private sealed class WinDivertPacketToken
    {
        /// <summary>The handle the packet was captured on, and the one it must be reinjected through.</summary>
        public nint Handle;

        /// <summary>The driver's addressing record for this packet.</summary>
        public WinDivertAddress Address;
    }
}
