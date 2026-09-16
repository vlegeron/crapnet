using Crapnet.Domain.Networking;

namespace Crapnet.Domain.Hotspot;

/// <summary>A device currently associated with the hotspot.</summary>
public sealed record TetheredClient
{
    public required string MacAddress { get; init; }

    /// <summary>Address leased by ICS. Unset until the client has completed DHCP.</summary>
    public Ipv4Address Address { get; init; }

    public string? HostName { get; init; }
    public DateTimeOffset LastSeen { get; init; }

    public bool HasAddress => Address != Ipv4Address.Any;

    /// <summary>What the UI shows: the hostname if the client offered one, else its address.</summary>
    public string DisplayName
        => !string.IsNullOrWhiteSpace(HostName) ? HostName!
            : HasAddress ? Address.ToString()
            : MacAddress;
}

/// <summary>A network interface that can act as the internet-facing side of the share.</summary>
public sealed record NetworkAdapter
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool IsUp { get; init; }

    /// <summary>True when the adapter looks like the wireless AP rather than an uplink.</summary>
    public bool IsWireless { get; init; }

    /// <summary>True when the adapter currently has a usable route to the internet.</summary>
    public bool HasInternet { get; init; }

    public Ipv4Address Address { get; init; }

    public override string ToString() => Name;
}

/// <summary>State of the Internet Connection Sharing link between two adapters.</summary>
public sealed record SharingState
{
    public static readonly SharingState Disabled = new();

    public bool IsEnabled { get; init; }

    /// <summary>The adapter the internet comes from, normally the Ethernet NIC.</summary>
    public string? PublicAdapterId { get; init; }

    /// <summary>The adapter the internet is shared to, i.e. the hotspot.</summary>
    public string? PrivateAdapterId { get; init; }
}
