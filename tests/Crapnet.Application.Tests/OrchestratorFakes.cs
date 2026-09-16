using Crapnet.Application.Abstractions;
using Crapnet.Application.Engine;
using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Rules;

namespace Crapnet.Application.Tests;

public sealed class FakeHotspotController : IHotspotController
{
    public HotspotState State { get; private set; } = HotspotState.Stopped;
    public string? HotspotAdapterId { get; set; } = "hotspot-adapter";
    public bool SharesUplinkAutomatically { get; set; } = true;

    public HotspotAvailability Availability { get; set; } = HotspotAvailability.Available;
    public Exception? StartFailure { get; set; }
    public IReadOnlyList<TetheredClient> Clients { get; set; } = Array.Empty<TetheredClient>();

    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public HotspotConfiguration? LastConfiguration { get; private set; }

    public event EventHandler<HotspotState>? StateChanged;

    public Task<HotspotAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Availability);

    public Task StartAsync(HotspotConfiguration configuration, CancellationToken cancellationToken = default)
    {
        LastConfiguration = configuration;
        if (StartFailure is not null) throw StartFailure;

        StartCount++;
        State = HotspotState.Running;
        StateChanged?.Invoke(this, State);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCount++;
        State = HotspotState.Stopped;
        StateChanged?.Invoke(this, State);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TetheredClient>> GetClientsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Clients);

    public Task<HotspotConfiguration?> ReadConfigurationAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<HotspotConfiguration?>(LastConfiguration);
}

public sealed class FakeSharingController : IInternetSharingController
{
    public bool IsSupported { get; set; } = true;
    public Exception? EnableFailure { get; set; }

    public int EnableCount { get; private set; }
    public int DisableCount { get; private set; }
    public string? PublicAdapterId { get; private set; }
    public string? PrivateAdapterId { get; private set; }

    private bool _enabled;

    public SharingState GetState() => _enabled
        ? new SharingState { IsEnabled = true, PublicAdapterId = PublicAdapterId, PrivateAdapterId = PrivateAdapterId }
        : SharingState.Disabled;

    public void Enable(string publicAdapterId, string privateAdapterId)
    {
        if (EnableFailure is not null) throw EnableFailure;

        EnableCount++;
        PublicAdapterId = publicAdapterId;
        PrivateAdapterId = privateAdapterId;
        _enabled = true;
    }

    public void Disable()
    {
        DisableCount++;
        _enabled = false;
    }
}

public sealed class FakeAdapterProvider : INetworkAdapterProvider
{
    public Dictionary<string, Ipv4Subnet> Subnets { get; } = new();
    public List<NetworkAdapter> Adapters { get; } = [];

    public IReadOnlyList<NetworkAdapter> GetAdapters() => Adapters;

    public NetworkAdapter? FindById(string adapterId)
        => Adapters.FirstOrDefault(adapter => adapter.Id == adapterId);

    public Ipv4Subnet? GetSubnet(string adapterId)
        => Subnets.TryGetValue(adapterId, out var subnet) ? subnet : null;
}

public sealed class FakeEngine : IImpairmentEngine
{
    public EngineState State { get; private set; } = EngineState.Stopped;
    public bool Bypass { get; set; }

    public Exception? StartFailure { get; set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public EngineOptions? LastOptions { get; private set; }
    public Profile? LastAppliedProfile { get; private set; }

    public event EventHandler<EngineState>? StateChanged;
    public event EventHandler<string>? Faulted;

    public void Start(EngineOptions options)
    {
        LastOptions = options;
        if (StartFailure is not null) throw StartFailure;

        StartCount++;
        State = EngineState.Running;
        StateChanged?.Invoke(this, State);
    }

    public void Stop()
    {
        StopCount++;
        State = EngineState.Stopped;
        StateChanged?.Invoke(this, State);
    }

    public void Apply(Profile profile) => LastAppliedProfile = profile;

    public EngineStatistics Snapshot() => new() { PacketsSeen = 42 };

    public void RaiseFault(string message)
    {
        State = EngineState.Faulted;
        Faulted?.Invoke(this, message);
    }

    public void Dispose() { }
}

public sealed class FakePrivilegeProbe : IPrivilegeProbe
{
    public bool IsElevated { get; set; } = true;
}

public sealed class FakeDriverProbe : ICaptureDriverProbe
{
    public string? Problem { get; set; }

    public string? Diagnose() => Problem;
}
