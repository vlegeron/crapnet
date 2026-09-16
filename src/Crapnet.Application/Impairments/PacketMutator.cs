using System.Buffers.Binary;
using Crapnet.Application.Abstractions;
using Crapnet.Domain.Networking;

namespace Crapnet.Application.Impairments;

/// <summary>
/// In-place edits to packet bytes. Pure span work, kept out of the capture adapter so it can be
/// tested against hand-built packets.
/// </summary>
public static class PacketMutator
{
    /// <summary>
    /// Overwrites up to <paramref name="maxBytes"/> random payload bytes with random values and
    /// returns how many were changed.
    /// </summary>
    /// <remarks>
    /// Only the transport payload is touched. Corrupting headers instead would mostly produce
    /// packets the stack discards for uninteresting reasons, whereas payload damage is what a
    /// noisy radio actually delivers.
    /// </remarks>
    public static int Tamper(Span<byte> payload, int maxBytes, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (payload.IsEmpty || maxBytes <= 0) return 0;

        var budget = Math.Min(maxBytes, payload.Length);
        var count = random.Next(1, budget + 1);

        Span<byte> replacements = stackalloc byte[Math.Min(count, 256)];
        random.NextBytes(replacements);

        for (var i = 0; i < count; i++)
        {
            var index = random.Next(0, payload.Length);
            payload[index] = replacements[i % replacements.Length];
        }

        return count;
    }
}

/// <summary>
/// Rewrites a captured TCP packet into a reset aimed back at whoever sent it.
/// </summary>
/// <remarks>
/// Rewriting in place rather than crafting a new packet keeps the capture adapter's addressing
/// data intact, so the reset is reinjected on the same path the original arrived on and gets
/// routed back to its sender without the engine needing to understand the platform's addressing.
/// </remarks>
public static class TcpResetWriter
{
    private const int TcpHeaderLength = 20;
    private const byte FlagFin = 0x01;
    private const byte FlagSyn = 0x02;
    private const byte FlagRst = 0x04;
    private const byte FlagAck = 0x10;

    /// <summary>
    /// Turns <paramref name="packet"/> into a RST/ACK addressed to its own source.
    /// Returns false, leaving the buffer untouched, for anything that is not a well-formed
    /// IPv4 TCP packet.
    /// </summary>
    public static bool TryRewriteAsReset(Span<byte> packet, out int newLength)
    {
        newLength = 0;

        if (packet.Length < 20) return false;
        if ((packet[0] >> 4) != 4) return false;

        var ipHeaderLength = (packet[0] & 0x0F) * 4;
        if (ipHeaderLength < 20 || packet.Length < ipHeaderLength + TcpHeaderLength) return false;
        if (packet[9] != (byte)TransportProtocol.Tcp) return false;

        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..4]);
        if (totalLength > packet.Length) totalLength = (ushort)packet.Length;

        var tcp = packet[ipHeaderLength..];
        var dataOffset = (tcp[12] >> 4) * 4;
        if (dataOffset < TcpHeaderLength || ipHeaderLength + dataOffset > totalLength) return false;

        var originalFlags = tcp[13];

        // Never answer a reset with a reset; that is how you build a packet storm.
        if ((originalFlags & FlagRst) != 0) return false;

        var sequence = BinaryPrimitives.ReadUInt32BigEndian(tcp[4..8]);
        var acknowledgement = BinaryPrimitives.ReadUInt32BigEndian(tcp[8..12]);
        var payloadLength = totalLength - ipHeaderLength - dataOffset;

        // SYN and FIN each occupy one sequence number even though they carry no payload, so the
        // acknowledgement we send back has to account for them or the peer ignores the reset.
        var consumed = (uint)payloadLength;
        if ((originalFlags & (FlagSyn | FlagFin)) != 0) consumed++;

        var resetSequence = (originalFlags & FlagAck) != 0 ? acknowledgement : 0u;
        var resetAcknowledgement = unchecked(sequence + consumed);

        // Swap the endpoints so the reset travels back the way the packet came.
        Span<byte> address = stackalloc byte[4];
        packet.Slice(12, 4).CopyTo(address);
        packet.Slice(16, 4).CopyTo(packet.Slice(12, 4));
        address.CopyTo(packet.Slice(16, 4));

        var sourcePort = BinaryPrimitives.ReadUInt16BigEndian(tcp[0..2]);
        var destinationPort = BinaryPrimitives.ReadUInt16BigEndian(tcp[2..4]);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[0..2], destinationPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[2..4], sourcePort);

        BinaryPrimitives.WriteUInt32BigEndian(tcp[4..8], resetSequence);
        BinaryPrimitives.WriteUInt32BigEndian(tcp[8..12], resetAcknowledgement);

        tcp[12] = TcpHeaderLength / 4 << 4;      // header length, no options
        tcp[13] = FlagRst | FlagAck;
        BinaryPrimitives.WriteUInt16BigEndian(tcp[14..16], 0);   // window
        BinaryPrimitives.WriteUInt16BigEndian(tcp[16..18], 0);   // checksum, recalculated on send
        BinaryPrimitives.WriteUInt16BigEndian(tcp[18..20], 0);   // urgent pointer

        newLength = ipHeaderLength + TcpHeaderLength;
        BinaryPrimitives.WriteUInt16BigEndian(packet[2..4], (ushort)newLength);
        BinaryPrimitives.WriteUInt16BigEndian(packet[10..12], 0); // IP checksum, recalculated on send

        return true;
    }
}
