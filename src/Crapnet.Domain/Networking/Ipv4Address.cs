using System.Globalization;

namespace Crapnet.Domain.Networking;

/// <summary>
/// An IPv4 address held as a host-order <see cref="uint"/>.
/// </summary>
/// <remarks>
/// Crapnet matches on IPv4 only. The hotspot path is Internet Connection Sharing, which is an
/// IPv4 NAT: every client address we can meaningfully target is IPv4. Keeping the address as a
/// bare <see cref="uint"/> means rule matching on the capture hot path allocates nothing.
/// </remarks>
public readonly struct Ipv4Address : IEquatable<Ipv4Address>, IComparable<Ipv4Address>
{
    public static readonly Ipv4Address Any = new(0u);
    public static readonly Ipv4Address Broadcast = new(uint.MaxValue);

    /// <summary>The address in host byte order, so that numeric ordering matches address ordering.</summary>
    public uint Value { get; }

    public Ipv4Address(uint hostOrderValue) => Value = hostOrderValue;

    public Ipv4Address(byte a, byte b, byte c, byte d)
        => Value = ((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d;

    /// <summary>Reads an address from the wire, where octets are laid out big-endian.</summary>
    public static Ipv4Address FromNetworkOrder(uint networkOrderValue)
        => new(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(networkOrderValue));

    public uint ToNetworkOrder() => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(Value);

    public static Ipv4Address Parse(string text)
        => TryParse(text, out var address)
            ? address
            : throw new FormatException($"'{text}' is not a valid IPv4 address.");

    public static bool TryParse(string? text, out Ipv4Address address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        Span<byte> octets = stackalloc byte[4];
        var span = text.AsSpan().Trim();
        var index = 0;

        foreach (var part in EnumerateSegments(span))
        {
            if (index == 4) return false;
            if (part.Length is 0 or > 3) return false;
            if (!byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out octets[index])) return false;
            index++;
        }

        if (index != 4) return false;
        address = new Ipv4Address(octets[0], octets[1], octets[2], octets[3]);
        return true;
    }

    private static IEnumerable<string> EnumerateSegments(ReadOnlySpan<char> span)
    {
        // Span cannot be captured by an iterator, so materialise the (at most five) segments.
        var segments = new List<string>(4);
        var start = 0;
        for (var i = 0; i <= span.Length; i++)
        {
            if (i != span.Length && span[i] != '.') continue;
            segments.Add(span[start..i].ToString());
            start = i + 1;
            if (segments.Count > 4) break;
        }
        return segments;
    }

    public byte this[int octetIndex] => (byte)(Value >> (24 - (octetIndex * 8)));

    public bool Equals(Ipv4Address other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Ipv4Address other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(Ipv4Address other) => Value.CompareTo(other.Value);

    public override string ToString() => $"{this[0]}.{this[1]}.{this[2]}.{this[3]}";

    public static bool operator ==(Ipv4Address left, Ipv4Address right) => left.Equals(right);
    public static bool operator !=(Ipv4Address left, Ipv4Address right) => !left.Equals(right);
    public static bool operator <(Ipv4Address left, Ipv4Address right) => left.Value < right.Value;
    public static bool operator >(Ipv4Address left, Ipv4Address right) => left.Value > right.Value;
    public static bool operator <=(Ipv4Address left, Ipv4Address right) => left.Value <= right.Value;
    public static bool operator >=(Ipv4Address left, Ipv4Address right) => left.Value >= right.Value;
}
