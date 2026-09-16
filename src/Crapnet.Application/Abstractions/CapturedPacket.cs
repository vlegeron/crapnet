using Crapnet.Domain.Networking;

namespace Crapnet.Application.Abstractions;

/// <summary>
/// One packet lifted off the wire, owned by the gateway that produced it.
/// </summary>
/// <remarks>
/// Instances are pooled and reused, so the engine must hand every packet back to the gateway
/// exactly once via <see cref="IPacketGateway.Release"/>. <see cref="GatewayToken"/> carries the
/// platform's own addressing data (for WinDivert, the WINDIVERT_ADDRESS that says which interface
/// to reinject on); nothing above the infrastructure layer looks inside it.
/// </remarks>
public sealed class CapturedPacket
{
    public CapturedPacket(int capacity) => Buffer = new byte[capacity];

    /// <summary>Backing store, at least <see cref="Length"/> bytes long.</summary>
    public byte[] Buffer { get; }

    /// <summary>Bytes of the packet actually present in <see cref="Buffer"/>.</summary>
    public int Length { get; set; }

    /// <summary>Parsed header facts used for rule matching.</summary>
    public PacketDescriptor Descriptor { get; set; }

    /// <summary>Offset of the transport payload within <see cref="Buffer"/>.</summary>
    public int PayloadOffset { get; set; }

    /// <summary>Length of the transport payload, which may be zero for a bare ACK.</summary>
    public int PayloadLength { get; set; }

    /// <summary>Opaque per-packet state belonging to the gateway. Never interpreted above infrastructure.</summary>
    public object? GatewayToken { get; set; }

    public Span<byte> Span => Buffer.AsSpan(0, Length);
    public ReadOnlySpan<byte> ReadOnlySpan => Buffer.AsSpan(0, Length);
    public Span<byte> PayloadSpan => Buffer.AsSpan(PayloadOffset, PayloadLength);

    public void Reset()
    {
        Length = 0;
        PayloadOffset = 0;
        PayloadLength = 0;
        Descriptor = default;
    }
}
