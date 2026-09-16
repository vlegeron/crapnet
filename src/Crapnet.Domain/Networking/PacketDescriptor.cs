namespace Crapnet.Domain.Networking;

/// <summary>
/// The facts about a captured packet that a rule can match on.
/// </summary>
/// <remarks>
/// Addresses are named by role rather than by src/dst. A rule written as
/// "device 192.168.137.42, remote port 443" then means the same thing in both directions,
/// which is almost always what you want when shaping one phone's traffic.
/// </remarks>
public readonly record struct PacketDescriptor(
    LinkDirection Direction,
    TransportProtocol Protocol,
    Ipv4Address DeviceAddress,
    ushort DevicePort,
    Ipv4Address RemoteAddress,
    ushort RemotePort,
    int TotalLength,
    TcpFlags TcpFlags)
{
    public Ipv4Address SourceAddress
        => Direction == LinkDirection.Uplink ? DeviceAddress : RemoteAddress;

    public Ipv4Address DestinationAddress
        => Direction == LinkDirection.Uplink ? RemoteAddress : DeviceAddress;

    public ushort SourcePort => Direction == LinkDirection.Uplink ? DevicePort : RemotePort;
    public ushort DestinationPort => Direction == LinkDirection.Uplink ? RemotePort : DevicePort;

    public bool HasPorts => Protocol.HasPorts();
}
