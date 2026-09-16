using Crapnet.Application.Abstractions;
using Crapnet.Application.Engine;
using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crapnet.Application.UseCases;

/// <summary>
/// Sequences the access point, the internet share and the capture engine, and unwinds whatever it
/// already brought up if a later step fails.
/// </summary>
/// <remarks>
/// The three parts fail independently and in different ways — no Wi-Fi radio, sharing refused, no
/// capture driver — and a half-started session is worse than none, because the phone associates to
/// an access point with no route out and the tester blames the app under test. So every start is
/// all-or-nothing.
/// </remarks>
public sealed class SessionOrchestrator : ISessionOrchestrator
{
    private readonly IHotspotController _hotspot;
    private readonly IInternetSharingController _sharing;
    private readonly INetworkAdapterProvider _adapters;
    private readonly IImpairmentEngine _engine;
    private readonly IPrivilegeProbe _privileges;
    private readonly ICaptureDriverProbe _driver;
    private readonly ILogger<SessionOrchestrator> _logger;
    private readonly SemaphoreSlim _transition = new(1, 1);

    private SessionStatus _status = SessionStatus.Idle;
    private bool _weEnabledSharing;

    public SessionOrchestrator(
        IHotspotController hotspot,
        IInternetSharingController sharing,
        INetworkAdapterProvider adapters,
        IImpairmentEngine engine,
        IPrivilegeProbe privileges,
        ICaptureDriverProbe driver,
        ILogger<SessionOrchestrator>? logger = null)
    {
        _hotspot = hotspot ?? throw new ArgumentNullException(nameof(hotspot));
        _sharing = sharing ?? throw new ArgumentNullException(nameof(sharing));
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _privileges = privileges ?? throw new ArgumentNullException(nameof(privileges));
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _logger = logger ?? NullLogger<SessionOrchestrator>.Instance;

        _hotspot.StateChanged += OnHotspotStateChanged;
        _engine.StateChanged += OnEngineStateChanged;
        _engine.Faulted += OnEngineFaulted;
    }

    public SessionStatus Status => _status;

    public event EventHandler<SessionStatus>? StatusChanged;

    public bool Bypass
    {
        get => _engine.Bypass;
        set => _engine.Bypass = value;
    }

    public IReadOnlyList<string> Preflight()
    {
        var problems = new List<string>(2);

        if (!_privileges.IsElevated)
            problems.Add("Crapnet needs to run as administrator to capture traffic and share a connection.");

        var driverProblem = _driver.Diagnose();
        if (driverProblem is not null) problems.Add(driverProblem);

        return problems;
    }

    public async Task<StartResult> StartAsync(StartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);

        var hotspotStarted = false;
        try
        {
            if (request.StartEngine)
            {
                var blockers = Preflight();
                if (blockers.Count > 0) return StartResult.Fail(string.Join(" ", blockers));
            }

            var configuration = request.Hotspot with { UplinkAdapterId = request.UplinkAdapterId };

            var invalid = configuration.Validate().ToList();
            if (invalid.Count > 0) return StartResult.Fail(string.Join(" ", invalid));

            if (request.StartHotspot)
            {
                Publish(_status with { Hotspot = HotspotState.Starting, Message = null });
                await _hotspot.StartAsync(configuration, cancellationToken).ConfigureAwait(false);
                hotspotStarted = true;
            }

            if (request.EnableSharing && !TryEnableSharing(request, out var sharingError))
            {
                await UnwindAsync(hotspotStarted, engineStarted: false).ConfigureAwait(false);
                return StartResult.Fail(sharingError!);
            }

            if (request.StartEngine)
            {
                _engine.Start(new EngineOptions
                {
                    DeviceSubnet = ResolveSubnet(configuration),
                    Profile = request.Profile,
                    Scope = request.Scope,
                });
            }

            Publish(_status with
            {
                Hotspot = _hotspot.State,
                Engine = _engine.State,
                SharingEnabled = _sharing.GetState().IsEnabled,
                Message = null,
            });

            return StartResult.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Starting the session failed.");
            await UnwindAsync(hotspotStarted, engineStarted: _engine.State == EngineState.Running).ConfigureAwait(false);
            Publish(_status with { Message = ex.Message });
            return StartResult.Fail(ex.Message);
        }
        finally
        {
            _transition.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await UnwindAsync(stopHotspot: true, engineStarted: true).ConfigureAwait(false);
            Publish(_status with
            {
                Hotspot = _hotspot.State,
                Engine = _engine.State,
                SharingEnabled = false,
            });
        }
        finally
        {
            _transition.Release();
        }
    }

    public void ApplyProfile(Profile profile) => _engine.Apply(profile);

    public EngineStatistics Snapshot() => _engine.Snapshot();

    public Task<IReadOnlyList<TetheredClient>> GetClientsAsync(CancellationToken cancellationToken = default)
        => _hotspot.GetClientsAsync(cancellationToken);

    /// <summary>
    /// Works out which subnet the tethered devices sit on. The live address of the access point
    /// adapter is authoritative, because Windows does not always hand out the documented
    /// 192.168.137.0/24 — the configured value is only a fallback.
    /// </summary>
    private Ipv4Subnet ResolveSubnet(HotspotConfiguration configuration)
    {
        var adapterId = _hotspot.HotspotAdapterId;
        if (adapterId is null) return configuration.Subnet;

        var subnet = _adapters.GetSubnet(adapterId);
        return subnet ?? configuration.Subnet;
    }

    private bool TryEnableSharing(StartRequest request, out string? error)
    {
        error = null;

        // Windows Mobile Hotspot already shares the connection profile it was created from, so
        // driving Internet Connection Sharing on top of it would be redundant and can break it.
        if (_hotspot.SharesUplinkAutomatically) return true;

        if (request.UplinkAdapterId is null)
        {
            error = "Pick the adapter that provides the internet connection.";
            return false;
        }

        var hotspotAdapterId = _hotspot.HotspotAdapterId;
        if (hotspotAdapterId is null)
        {
            error = "The hotspot adapter is not available yet.";
            return false;
        }

        if (!_sharing.IsSupported)
        {
            error = "Internet Connection Sharing is not available on this machine.";
            return false;
        }

        try
        {
            _sharing.Enable(request.UplinkAdapterId, hotspotAdapterId);
            _weEnabledSharing = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enabling internet sharing failed.");
            error = $"Could not share the internet connection: {ex.Message}";
            return false;
        }
    }

    private async Task UnwindAsync(bool stopHotspot, bool engineStarted)
    {
        if (engineStarted)
        {
            try { _engine.Stop(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Stopping the engine failed."); }
        }

        // Only tear down sharing we turned on ourselves; the user may have had it configured.
        if (_weEnabledSharing)
        {
            try { _sharing.Disable(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Disabling internet sharing failed."); }
            _weEnabledSharing = false;
        }

        if (stopHotspot)
        {
            try { await _hotspot.StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Stopping the hotspot failed."); }
        }
    }

    private void OnHotspotStateChanged(object? sender, HotspotState state)
        => Publish(_status with { Hotspot = state });

    private void OnEngineStateChanged(object? sender, EngineState state)
        => Publish(_status with { Engine = state });

    private void OnEngineFaulted(object? sender, string message)
        => Publish(_status with { Engine = EngineState.Faulted, Message = message });

    private void Publish(SessionStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        _hotspot.StateChanged -= OnHotspotStateChanged;
        _engine.StateChanged -= OnEngineStateChanged;
        _engine.Faulted -= OnEngineFaulted;

        _engine.Dispose();
        _transition.Dispose();
    }
}
