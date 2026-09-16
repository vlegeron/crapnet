using System.Buffers.Binary;
using Crapnet.Application.Impairments;
using Xunit;

namespace Crapnet.Application.Tests;

/// <summary>Builds real IPv4 packets so the byte-level helpers are tested against the wire format.</summary>
public static class PacketBuilder
{
    public const byte FlagFin = 0x01;
    public const byte FlagSyn = 0x02;
    public const byte FlagRst = 0x04;
    public const byte FlagAck = 0x10;

    public static byte[] Tcp(
        uint sequence = 1000,
        uint acknowledgement = 2000,
        byte flags = FlagAck,
        int payloadLength = 0,
        int optionBytes = 0,
        int ipOptionBytes = 0)
    {
        var ipHeader = 20 + ipOptionBytes;
        var tcpHeader = 20 + optionBytes;
        var total = ipHeader + tcpHeader + payloadLength;
        var packet = new byte[total];

        packet[0] = (byte)(0x40 | (ipHeader / 4));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)total);
        packet[8] = 64;                 // TTL
        packet[9] = 6;                  // TCP
        WriteAddress(packet.AsSpan(12, 4), 192, 168, 137, 42);
        WriteAddress(packet.AsSpan(16, 4), 93, 184, 216, 34);

        var tcp = packet.AsSpan(ipHeader);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[0..2], 51_000);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[2..4], 443);
        BinaryPrimitives.WriteUInt32BigEndian(tcp[4..8], sequence);
        BinaryPrimitives.WriteUInt32BigEndian(tcp[8..12], acknowledgement);
        tcp[12] = (byte)(tcpHeader / 4 << 4);
        tcp[13] = flags;
        BinaryPrimitives.WriteUInt16BigEndian(tcp[14..16], 8192);

        for (var i = 0; i < payloadLength; i++) tcp[tcpHeader + i] = (byte)(i & 0xFF);

        return packet;
    }

    public static byte[] Udp(int payloadLength = 32)
    {
        var packet = new byte[20 + 8 + payloadLength];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 17;
        WriteAddress(packet.AsSpan(12, 4), 192, 168, 137, 42);
        WriteAddress(packet.AsSpan(16, 4), 1, 1, 1, 1);
        return packet;
    }

    private static void WriteAddress(Span<byte> target, byte a, byte b, byte c, byte d)
    {
        target[0] = a; target[1] = b; target[2] = c; target[3] = d;
    }

    public static (uint Sequence, uint Acknowledgement, byte Flags) ReadTcp(byte[] packet)
    {
        var ipHeader = (packet[0] & 0x0F) * 4;
        var tcp = packet.AsSpan(ipHeader);
        return (
            BinaryPrimitives.ReadUInt32BigEndian(tcp[4..8]),
            BinaryPrimitives.ReadUInt32BigEndian(tcp[8..12]),
            tcp[13]);
    }
}

public class TcpResetWriterTests
{
    [Fact]
    public void SwapsTheEndpointsSoTheResetGoesBack()
    {
        var packet = PacketBuilder.Tcp();

        Assert.True(TcpResetWriter.TryRewriteAsReset(packet, out _));

        Assert.Equal(new byte[] { 93, 184, 216, 34 }, packet[12..16]);
        Assert.Equal(new byte[] { 192, 168, 137, 42 }, packet[16..20]);
        Assert.Equal(443, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(20, 2)));
        Assert.Equal(51_000, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(22, 2)));
    }

    [Fact]
    public void SetsRstAndAck()
    {
        var packet = PacketBuilder.Tcp();
        TcpResetWriter.TryRewriteAsReset(packet, out _);

        var (_, _, flags) = PacketBuilder.ReadTcp(packet);
        Assert.Equal(PacketBuilder.FlagRst | PacketBuilder.FlagAck, flags);
    }

    [Fact]
    public void AcknowledgesThePayloadItIsRejecting()
    {
        var packet = PacketBuilder.Tcp(sequence: 5_000, acknowledgement: 9_000, payloadLength: 100);
        TcpResetWriter.TryRewriteAsReset(packet, out _);

        var (sequence, acknowledgement, _) = PacketBuilder.ReadTcp(packet);
        Assert.Equal(9_000u, sequence);              // taken from the original ACK
        Assert.Equal(5_100u, acknowledgement);       // original sequence plus the payload
    }

    [Fact]
    public void CountsSynAsOneSequenceNumber()
    {
        var packet = PacketBuilder.Tcp(sequence: 5_000, flags: PacketBuilder.FlagSyn);
        TcpResetWriter.TryRewriteAsReset(packet, out _);

        var (sequence, acknowledgement, _) = PacketBuilder.ReadTcp(packet);
        Assert.Equal(0u, sequence);                  // a bare SYN carries no ACK to echo
        Assert.Equal(5_001u, acknowledgement);
    }

    [Fact]
    public void CountsFinAsOneSequenceNumber()
    {
        var packet = PacketBuilder.Tcp(sequence: 7_000, flags: PacketBuilder.FlagFin | PacketBuilder.FlagAck);
        TcpResetWriter.TryRewriteAsReset(packet, out _);

        Assert.Equal(7_001u, PacketBuilder.ReadTcp(packet).Acknowledgement);
    }

    [Fact]
    public void WrapsTheSequenceSpaceInsteadOfOverflowing()
    {
        var packet = PacketBuilder.Tcp(sequence: uint.MaxValue - 10, payloadLength: 100);

        var exception = Record.Exception(() => TcpResetWriter.TryRewriteAsReset(packet, out _));

        Assert.Null(exception);
        Assert.Equal(89u, PacketBuilder.ReadTcp(packet).Acknowledgement);
    }

    [Fact]
    public void TruncatesToAHeaderOnlyPacket()
    {
        var packet = PacketBuilder.Tcp(payloadLength: 500, optionBytes: 12);

        Assert.True(TcpResetWriter.TryRewriteAsReset(packet, out var length));

        Assert.Equal(40, length);
        Assert.Equal(40, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2, 2)));
        Assert.Equal(5 << 4, packet[32]);   // data offset back down to 20 bytes, options gone
    }

    [Fact]
    public void HandlesIpOptions()
    {
        var packet = PacketBuilder.Tcp(ipOptionBytes: 8, payloadLength: 40);

        Assert.True(TcpResetWriter.TryRewriteAsReset(packet, out var length));
        Assert.Equal(48, length);
    }

    [Fact]
    public void ZeroesTheChecksumsForRecalculation()
    {
        var packet = PacketBuilder.Tcp();
        TcpResetWriter.TryRewriteAsReset(packet, out _);

        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(10, 2)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(36, 2)));
    }

    [Fact]
    public void RefusesToAnswerAResetWithAReset()
    {
        var packet = PacketBuilder.Tcp(flags: PacketBuilder.FlagRst);
        Assert.False(TcpResetWriter.TryRewriteAsReset(packet, out _));
    }

    [Fact]
    public void RejectsNonTcp() => Assert.False(TcpResetWriter.TryRewriteAsReset(PacketBuilder.Udp(), out _));

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(19)]
    [InlineData(25)]
    public void RejectsTruncatedBuffers(int length)
        => Assert.False(TcpResetWriter.TryRewriteAsReset(new byte[length], out _));

    [Fact]
    public void RejectsANonIpv4Version()
    {
        var packet = PacketBuilder.Tcp();
        packet[0] = 0x65;   // version 6
        Assert.False(TcpResetWriter.TryRewriteAsReset(packet, out _));
    }

    [Fact]
    public void RejectsAnImpossibleDataOffset()
    {
        var packet = PacketBuilder.Tcp();
        packet[32] = 0xF0;  // 60-byte TCP header that does not fit
        Assert.False(TcpResetWriter.TryRewriteAsReset(packet, out _));
    }
}

public class PacketMutatorTests
{
    [Fact]
    public void ChangesAtMostTheRequestedNumberOfBytes()
    {
        var payload = new byte[100];
        var changed = PacketMutator.Tamper(payload, maxBytes: 4, new ScriptedRandom { DefaultInt = 0 }.Ints(4));

        Assert.Equal(4, changed);
    }

    [Fact]
    public void NeverExceedsThePayloadLength()
    {
        var payload = new byte[3];
        var changed = PacketMutator.Tamper(payload, maxBytes: 500, new ScriptedRandom().Ints(3));

        Assert.True(changed <= payload.Length);
    }

    [Fact]
    public void ActuallyRewritesBytes()
    {
        var payload = new byte[16];
        PacketMutator.Tamper(payload, maxBytes: 1, new ScriptedRandom { FillByte = 0xAB }.Ints(1, 7));

        Assert.Equal(0xAB, payload[7]);
    }

    [Fact]
    public void DoesNothingToAnEmptyPayload()
        => Assert.Equal(0, PacketMutator.Tamper(Span<byte>.Empty, 8, new ScriptedRandom()));

    [Fact]
    public void DoesNothingWhenTheBudgetIsZero()
        => Assert.Equal(0, PacketMutator.Tamper(new byte[64], 0, new ScriptedRandom()));
}
