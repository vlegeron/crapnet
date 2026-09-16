using System.Buffers.Binary;
using Crapnet.Domain.Networking;

namespace Crapnet.Infrastructure.Capture;

/// <summary>
/// Reads the header facts Crapnet's rules match on out of a raw IPv4 packet.
/// </summary>
/// <remarks>
/// <para>
/// Pure and static, over spans: this runs once per packet on the capture thread, so it allocates
/// nothing, and because it touches no driver it is exercised directly by unit tests rather than
/// only on a live hotspot.
/// </para>
/// <para>
/// Every field read is bounds-checked and every length is validated against what is actually in the
/// buffer. The input comes off a network shared with an untrusted device, so a truncated or
/// deliberately malformed header has to end in <c>false</c>, never in an exception on the capture
/// thread and never in a read past the packet.
/// </para>
/// </remarks>
public static class Ipv4PacketParser
{
    /// <summary>Smallest legal IPv4 header, with no options.</summary>
    public const int MinimumIpv4HeaderLength = 20;

    private const int MinimumTcpHeaderLength = 20;
    private const int UdpHeaderLength = 8;

    /// <summary>
    /// Parses an IPv4 packet and classifies it against the hotspot subnet.
    /// </summary>
    /// <param name="packet">The packet, starting at the IPv4 header.</param>
    /// <param name="deviceSubnet">The hotspot subnet, used to decide which end is the device.</param>
    /// <param name="descriptor">The parsed header facts, valid only when this returns true.</param>
    /// <param name="payloadOffset">Offset of the transport payload within <paramref name="packet"/>.</param>
    /// <param name="payloadLength">Length of the transport payload, which is legitimately zero for a bare ACK.</param>
    /// <returns>True when the packet was understood; false when it is not IPv4 or is malformed.</returns>
    public static bool TryParse(
        ReadOnlySpan<byte> packet,
        Ipv4Subnet deviceSubnet,
        out PacketDescriptor descriptor,
        out int payloadOffset,
        out int payloadLength)
    {
        descriptor = default;
        payloadOffset = 0;
        payloadLength = 0;

        if (packet.Length < MinimumIpv4HeaderLength) return false;
        if ((packet[0] >> 4) != 4) return false;

        var headerLength = (packet[0] & 0x0F) * 4;
        if (headerLength < MinimumIpv4HeaderLength || headerLength > packet.Length) return false;

        // The header's own total-length field is advisory here. Large-send offload hands us
        // segments whose field reads 0 or lags the real size, so the buffer wins whenever the two
        // disagree and the field is only used to trim trailing padding.
        var declaredLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        var totalLength = declaredLength >= headerLength && declaredLength <= packet.Length
            ? declaredLength
            : packet.Length;

        var source = Ipv4Address.FromNetworkOrder(BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]));
        var destination = Ipv4Address.FromNetworkOrder(BinaryPrimitives.ReadUInt32LittleEndian(packet[16..]));

        var protocol = ToTransportProtocol(packet[9]);

        // A non-zero fragment offset means the transport header lives in the first fragment, not
        // this one, so there are no ports to read and the whole body is opaque payload.
        var fragmentOffset = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]) & 0x1FFF;
        var hasTransportHeader = fragmentOffset == 0;

        ushort sourcePort = 0;
        ushort destinationPort = 0;
        var flags = TcpFlags.None;

        payloadOffset = headerLength;
        payloadLength = totalLength - headerLength;

        if (hasTransportHeader && protocol == TransportProtocol.Tcp)
        {
            if (totalLength - headerLength < MinimumTcpHeaderLength) return false;

            var tcp = packet[headerLength..totalLength];
            sourcePort = BinaryPrimitives.ReadUInt16BigEndian(tcp);
            destinationPort = BinaryPrimitives.ReadUInt16BigEndian(tcp[2..]);

            var dataOffset = (tcp[12] >> 4) * 4;
            if (dataOffset < MinimumTcpHeaderLength || dataOffset > tcp.Length) return false;

            flags = ToTcpFlags(tcp[13]);
            payloadOffset = headerLength + dataOffset;
            payloadLength = totalLength - payloadOffset;
        }
        else if (hasTransportHeader && protocol == TransportProtocol.Udp)
        {
            if (totalLength - headerLength < UdpHeaderLength) return false;

            var udp = packet[headerLength..totalLength];
            sourcePort = BinaryPrimitives.ReadUInt16BigEndian(udp);
            destinationPort = BinaryPrimitives.ReadUInt16BigEndian(udp[2..]);

            payloadOffset = headerLength + UdpHeaderLength;
            payloadLength = totalLength - payloadOffset;
        }

        // ICMP and everything else keep ports of zero and treat the body as payload: rules that
        // constrain ports simply never match them, which is the behaviour a tester expects.

        // Direction is decided by the subnet rather than by the driver's inbound/outbound bit,
        // which describes the Windows host and says nothing useful about routed traffic. When
        // neither end is in the subnet — a packet the filter should have excluded, or one seen
        // while the hotspot subnet is being reconfigured — we fall back to Downlink and call the
        // destination the device, so the packet still forwards untouched instead of being dropped
        // for want of a classification.
        LinkDirection direction;
        Ipv4Address deviceAddress;
        Ipv4Address remoteAddress;
        ushort devicePort;
        ushort remotePort;

        if (deviceSubnet.Contains(source))
        {
            direction = LinkDirection.Uplink;
            deviceAddress = source;
            devicePort = sourcePort;
            remoteAddress = destination;
            remotePort = destinationPort;
        }
        else
        {
            direction = LinkDirection.Downlink;
            deviceAddress = destination;
            devicePort = destinationPort;
            remoteAddress = source;
            remotePort = sourcePort;
        }

        descriptor = new PacketDescriptor(
            direction,
            protocol,
            deviceAddress,
            devicePort,
            remoteAddress,
            remotePort,
            totalLength,
            flags);

        return true;
    }

    /// <summary>Maps an IP protocol number onto the protocols rules can name.</summary>
    public static TransportProtocol ToTransportProtocol(byte protocolNumber) => protocolNumber switch
    {
        1 => TransportProtocol.Icmp,
        6 => TransportProtocol.Tcp,
        17 => TransportProtocol.Udp,
        _ => TransportProtocol.Other,
    };

    /// <summary>Maps the TCP header's flag byte onto <see cref="TcpFlags"/>.</summary>
    /// <remarks>
    /// The two high bits (ECE and CWR) have no domain meaning for impairment rules and are dropped
    /// rather than modelled.
    /// </remarks>
    public static TcpFlags ToTcpFlags(byte headerByte) => (TcpFlags)(headerByte & 0x3F);
}
