using Crapnet.Domain.Networking;

namespace Crapnet.Domain.Rules;

/// <summary>
/// The conditions a packet must satisfy for a rule to claim it.
/// </summary>
/// <remarks>
/// Every dimension defaults to "any", so an empty criteria set matches all tethered traffic.
/// Addresses and ports are named for the device and the remote peer rather than source and
/// destination, which keeps a rule meaningful regardless of direction.
/// </remarks>
public sealed record MatchCriteria
{
    public static readonly MatchCriteria Any = new();

    public DirectionFilter Direction { get; init; } = DirectionFilter.Both;
    public ProtocolFilter Protocol { get; init; } = ProtocolFilter.Any;

    /// <summary>Which tethered clients this applies to.</summary>
    public Ipv4Selector DeviceAddresses { get; init; } = Ipv4Selector.Any;

    /// <summary>Which internet-side peers this applies to.</summary>
    public Ipv4Selector RemoteAddresses { get; init; } = Ipv4Selector.Any;

    /// <summary>Ports on the device side. Ignored for protocols without ports.</summary>
    public PortSelector DevicePorts { get; init; } = PortSelector.Any;

    /// <summary>Ports on the remote side, which is normally the interesting one (443, 53, ...).</summary>
    public PortSelector RemotePorts { get; init; } = PortSelector.Any;

    public bool MatchesEverything
        => Direction == DirectionFilter.Both
           && Protocol == ProtocolFilter.Any
           && DeviceAddresses.MatchesAnything
           && RemoteAddresses.MatchesAnything
           && DevicePorts.MatchesAnything
           && RemotePorts.MatchesAnything;

    public bool Matches(in PacketDescriptor packet)
    {
        if (!Direction.Matches(packet.Direction)) return false;
        if (!Protocol.Matches(packet.Protocol)) return false;
        if (!DeviceAddresses.Matches(packet.DeviceAddress)) return false;
        if (!RemoteAddresses.Matches(packet.RemoteAddress)) return false;

        // ICMP and friends carry no ports. A port constraint cannot be satisfied by such a packet,
        // so it excludes them rather than silently matching everything.
        if (!DevicePorts.MatchesAnything || !RemotePorts.MatchesAnything)
        {
            if (!packet.HasPorts) return false;
            if (!DevicePorts.Matches(packet.DevicePort)) return false;
            if (!RemotePorts.Matches(packet.RemotePort)) return false;
        }

        return true;
    }

    /// <summary>A compact description for the rule list, e.g. "↑ TCP :443".</summary>
    public string Describe()
    {
        var parts = new List<string>(6);

        parts.Add(Direction switch
        {
            DirectionFilter.Uplink => "↑",
            DirectionFilter.Downlink => "↓",
            _ => "↕",
        });

        if (Protocol != ProtocolFilter.Any) parts.Add(Protocol.ToString().ToUpperInvariant());
        if (!DeviceAddresses.MatchesAnything) parts.Add($"dev {DeviceAddresses}");
        if (!RemoteAddresses.MatchesAnything) parts.Add($"to {RemoteAddresses}");
        if (!DevicePorts.MatchesAnything) parts.Add($"dev :{DevicePorts}");
        if (!RemotePorts.MatchesAnything) parts.Add($":{RemotePorts}");

        return parts.Count == 1 ? "all traffic" : string.Join("  ", parts);
    }
}
