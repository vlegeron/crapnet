using System.Text;

namespace Crapnet.Domain.Networking;

/// <summary>An inclusive range of IPv4 addresses.</summary>
public readonly struct Ipv4Range
{
    public Ipv4Address Start { get; }
    public Ipv4Address End { get; }

    public Ipv4Range(Ipv4Address start, Ipv4Address end)
    {
        if (end < start) (start, end) = (end, start);
        Start = start;
        End = end;
    }

    public static Ipv4Range Single(Ipv4Address address) => new(address, address);
    public static Ipv4Range FromSubnet(Ipv4Subnet subnet) => new(subnet.FirstAddress, subnet.LastAddress);

    public bool Contains(Ipv4Address address) => address >= Start && address <= End;

    public override string ToString() => Start == End ? Start.ToString() : $"{Start}-{End}";
}

/// <summary>
/// The address side of a match rule: a set of ranges, or "any" when left empty.
/// </summary>
/// <remarks>
/// Accepts a comma-separated mix of plain addresses, CIDR blocks and explicit ranges, for example
/// <c>192.168.137.42, 10.0.0.0/8, 1.1.1.1-1.1.1.9</c>. A leading <c>!</c> negates the whole set.
/// </remarks>
public sealed class Ipv4Selector
{
    public static readonly Ipv4Selector Any = new(Array.Empty<Ipv4Range>(), negated: false, string.Empty);

    private readonly Ipv4Range[] _ranges;

    private Ipv4Selector(Ipv4Range[] ranges, bool negated, string text)
    {
        _ranges = ranges;
        IsNegated = negated;
        Text = text;
    }

    public IReadOnlyList<Ipv4Range> Ranges => _ranges;
    public bool IsNegated { get; }

    /// <summary>The original expression, preserved so the UI can round-trip what the user typed.</summary>
    public string Text { get; }

    public bool MatchesAnything => _ranges.Length == 0 && !IsNegated;

    public bool Matches(Ipv4Address address)
    {
        if (_ranges.Length == 0) return !IsNegated;

        foreach (var range in _ranges)
        {
            if (range.Contains(address)) return !IsNegated;
        }

        return IsNegated;
    }

    public static Ipv4Selector Parse(string? text)
        => TryParse(text, out var selector, out var error)
            ? selector
            : throw new FormatException(error);

    public static bool TryParse(string? text, out Ipv4Selector selector, out string error)
    {
        selector = Any;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text)) return true;

        var trimmed = text.Trim();
        var negated = false;
        if (trimmed.StartsWith('!'))
        {
            negated = true;
            trimmed = trimmed[1..].Trim();
        }

        if (trimmed.Length == 0 || trimmed == "*")
        {
            selector = negated ? new Ipv4Selector([], true, text.Trim()) : Any;
            return true;
        }

        var ranges = new List<Ipv4Range>();
        foreach (var rawPart in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParsePart(rawPart, out var range))
            {
                error = $"'{rawPart}' is not an address, CIDR block or range.";
                return false;
            }

            ranges.Add(range);
        }

        if (ranges.Count == 0)
        {
            error = "No addresses were given.";
            return false;
        }

        selector = new Ipv4Selector(ranges.ToArray(), negated, text.Trim());
        return true;
    }

    private static bool TryParsePart(string part, out Ipv4Range range)
    {
        range = default;

        if (part.Contains('/'))
        {
            if (!Ipv4Subnet.TryParse(part, out var subnet)) return false;
            range = Ipv4Range.FromSubnet(subnet);
            return true;
        }

        var dash = part.IndexOf('-');
        if (dash > 0)
        {
            if (!Ipv4Address.TryParse(part[..dash], out var start)) return false;
            if (!Ipv4Address.TryParse(part[(dash + 1)..], out var end)) return false;
            range = new Ipv4Range(start, end);
            return true;
        }

        if (!Ipv4Address.TryParse(part, out var single)) return false;
        range = Ipv4Range.Single(single);
        return true;
    }

    public override string ToString()
    {
        if (MatchesAnything) return "any";
        var builder = new StringBuilder();
        if (IsNegated) builder.Append('!');
        builder.AppendJoin(", ", _ranges.Select(r => r.ToString()));
        return builder.ToString();
    }
}
