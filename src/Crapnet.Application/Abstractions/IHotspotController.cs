using Crapnet.Domain.Hotspot;

namespace Crapnet.Application.Abstractions;

/// <summary>Port for driving the Windows access point.</summary>
public interface IHotspotController
{
    HotspotState State { get; }

    /// <summary>Adapter id of the access point interface, once it exists. Needed to wire up sharing.</summary>
    string? HotspotAdapterId { get; }

    event EventHandler<HotspotState>? StateChanged;

    Task<HotspotAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts the access point, reconfiguring the SSID and passphrase first if needed.</summary>
    Task StartAsync(HotspotConfiguration configuration, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Currently associated devices, with addresses filled in where a DHCP lease is known.</summary>
    Task<IReadOnlyList<TetheredClient>> GetClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads back the SSID and passphrase Windows is actually using.</summary>
    Task<HotspotConfiguration?> ReadConfigurationAsync(CancellationToken cancellationToken = default);
}

/// <summary>Port for the Internet Connection Sharing link that gives the hotspot its uplink.</summary>
public interface IInternetSharingController
{
    /// <summary>False when the platform's sharing component is unavailable.</summary>
    bool IsSupported { get; }

    SharingState GetState();

    /// <summary>
    /// Shares <paramref name="publicAdapterId"/>'s connection with <paramref name="privateAdapterId"/>.
    /// Windows permits exactly one share at a time, so any existing one is torn down first.
    /// </summary>
    void Enable(string publicAdapterId, string privateAdapterId);

    void Disable();
}

/// <summary>Port for enumerating interfaces, so the user can pick which one supplies the internet.</summary>
public interface INetworkAdapterProvider
{
    IReadOnlyList<NetworkAdapter> GetAdapters();

    NetworkAdapter? FindById(string adapterId);

    /// <summary>The IPv4 subnet an adapter currently sits on, if it has an address.</summary>
    Crapnet.Domain.Networking.Ipv4Subnet? GetSubnet(string adapterId);
}
