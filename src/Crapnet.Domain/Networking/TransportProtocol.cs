namespace Crapnet.Domain.Networking;

/// <summary>IP protocol numbers Crapnet can distinguish.</summary>
public enum TransportProtocol : byte
{
    Other = 0,
    Icmp = 1,
    Tcp = 6,
    Udp = 17,
}

/// <summary>The protocol constraint of a rule.</summary>
public enum ProtocolFilter
{
    Any = 0,
    Tcp = 1,
    Udp = 2,
    Icmp = 3,
}

public static class ProtocolFilterExtensions
{
    public static bool Matches(this ProtocolFilter filter, TransportProtocol protocol) => filter switch
    {
        ProtocolFilter.Any => true,
        ProtocolFilter.Tcp => protocol == TransportProtocol.Tcp,
        ProtocolFilter.Udp => protocol == TransportProtocol.Udp,
        ProtocolFilter.Icmp => protocol == TransportProtocol.Icmp,
        _ => true,
    };

    /// <summary>True when the protocol carries ports, and so port constraints are meaningful.</summary>
    public static bool HasPorts(this TransportProtocol protocol)
        => protocol is TransportProtocol.Tcp or TransportProtocol.Udp;
}

/// <summary>TCP header flags, used by rules that target connection setup or teardown.</summary>
[Flags]
public enum TcpFlags : byte
{
    None = 0,
    Fin = 1 << 0,
    Syn = 1 << 1,
    Rst = 1 << 2,
    Psh = 1 << 3,
    Ack = 1 << 4,
    Urg = 1 << 5,
}
