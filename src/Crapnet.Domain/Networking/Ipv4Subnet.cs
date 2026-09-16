namespace Crapnet.Domain.Networking;

/// <summary>A CIDR block, used mainly to describe the hotspot's private subnet.</summary>
public readonly struct Ipv4Subnet : IEquatable<Ipv4Subnet>
{
    /// <summary>The subnet Internet Connection Sharing uses by default.</summary>
    public static readonly Ipv4Subnet IcsDefault = new(new Ipv4Address(192, 168, 137, 0), 24);

    public Ipv4Address Network { get; }
    public int PrefixLength { get; }

    public Ipv4Subnet(Ipv4Address address, int prefixLength)
    {
        if (prefixLength is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(prefixLength), prefixLength, "Prefix length must be 0-32.");

        PrefixLength = prefixLength;
        Network = new Ipv4Address(address.Value & MaskFor(prefixLength));
    }

    public static uint MaskFor(int prefixLength)
        => prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);

    public Ipv4Address Mask => new(MaskFor(PrefixLength));
    public Ipv4Address FirstAddress => Network;
    public Ipv4Address LastAddress => new(Network.Value | ~MaskFor(PrefixLength));

    /// <summary>The address ICS assigns to the Windows host itself (first usable address).</summary>
    public Ipv4Address GatewayAddress => PrefixLength >= 31 ? Network : new(Network.Value + 1);

    public bool Contains(Ipv4Address address)
        => (address.Value & MaskFor(PrefixLength)) == Network.Value;

    public static Ipv4Subnet Parse(string text)
        => TryParse(text, out var subnet) ? subnet : throw new FormatException($"'{text}' is not a valid IPv4 subnet.");

    public static bool TryParse(string? text, out Ipv4Subnet subnet)
    {
        subnet = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        var slash = trimmed.IndexOf('/');
        if (slash < 0)
        {
            if (!Ipv4Address.TryParse(trimmed, out var single)) return false;
            subnet = new Ipv4Subnet(single, 32);
            return true;
        }

        if (!Ipv4Address.TryParse(trimmed[..slash], out var address)) return false;
        if (!int.TryParse(trimmed[(slash + 1)..], out var prefix) || prefix is < 0 or > 32) return false;

        subnet = new Ipv4Subnet(address, prefix);
        return true;
    }

    public bool Equals(Ipv4Subnet other) => Network == other.Network && PrefixLength == other.PrefixLength;
    public override bool Equals(object? obj) => obj is Ipv4Subnet other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Network, PrefixLength);
    public override string ToString() => $"{Network}/{PrefixLength}";
}
