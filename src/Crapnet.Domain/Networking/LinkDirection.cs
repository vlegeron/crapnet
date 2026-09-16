namespace Crapnet.Domain.Networking;

/// <summary>
/// Which way a packet is travelling, expressed relative to the device under test rather than
/// relative to the Windows host.
/// </summary>
/// <remarks>
/// Raw capture direction ("inbound"/"outbound") is ambiguous once traffic is being routed on
/// behalf of somebody else, so Crapnet classifies every packet by comparing it against the
/// hotspot subnet instead. That keeps rules readable: "uplink" is always what the phone sent.
/// </remarks>
public enum LinkDirection
{
    /// <summary>Device to internet: the tethered client is the source.</summary>
    Uplink,

    /// <summary>Internet to device: the tethered client is the destination.</summary>
    Downlink,
}

/// <summary>The direction constraint of a rule.</summary>
public enum DirectionFilter
{
    Both = 0,
    Uplink = 1,
    Downlink = 2,
}

public static class DirectionFilterExtensions
{
    public static bool Matches(this DirectionFilter filter, LinkDirection direction) => filter switch
    {
        DirectionFilter.Both => true,
        DirectionFilter.Uplink => direction == LinkDirection.Uplink,
        DirectionFilter.Downlink => direction == LinkDirection.Downlink,
        _ => true,
    };
}
