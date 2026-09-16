using System.Text;

namespace Crapnet.Domain.Networking;

/// <summary>An inclusive range of transport ports.</summary>
public readonly struct PortRange
{
    public ushort Start { get; }
    public ushort End { get; }

    public PortRange(ushort start, ushort end)
    {
        if (end < start) (start, end) = (end, start);
        Start = start;
        End = end;
    }

    public static PortRange Single(ushort port) => new(port, port);

    public bool Contains(ushort port) => port >= Start && port <= End;

    public override string ToString() => Start == End ? Start.ToString() : $"{Start}-{End}";
}

/// <summary>
/// The port side of a match rule: a set of ranges, or "any" when left empty.
/// </summary>
/// <remarks>
/// Accepts a comma-separated mix of single ports and ranges, for example <c>53, 80, 8000-8100</c>.
/// A leading <c>!</c> negates the whole set, so <c>!443</c> reads as "everything except HTTPS".
/// </remarks>
public sealed class PortSelector
{
    public static readonly PortSelector Any = new(Array.Empty<PortRange>(), negated: false, string.Empty);

    private readonly PortRange[] _ranges;

    private PortSelector(PortRange[] ranges, bool negated, string text)
    {
        _ranges = ranges;
        IsNegated = negated;
        Text = text;
    }

    public IReadOnlyList<PortRange> Ranges => _ranges;
    public bool IsNegated { get; }
    public string Text { get; }
    public bool MatchesAnything => _ranges.Length == 0 && !IsNegated;

    public bool Matches(ushort port)
    {
        if (_ranges.Length == 0) return !IsNegated;

        foreach (var range in _ranges)
        {
            if (range.Contains(port)) return !IsNegated;
        }

        return IsNegated;
    }

    public static PortSelector Parse(string? text)
        => TryParse(text, out var selector, out var error) ? selector : throw new FormatException(error);

    public static bool TryParse(string? text, out PortSelector selector, out string error)
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
            selector = negated ? new PortSelector([], true, text.Trim()) : Any;
            return true;
        }

        var ranges = new List<PortRange>();
        foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-');
            if (dash > 0)
            {
                if (!ushort.TryParse(part[..dash].Trim(), out var start) ||
                    !ushort.TryParse(part[(dash + 1)..].Trim(), out var end))
                {
                    error = $"'{part}' is not a port range.";
                    return false;
                }

                ranges.Add(new PortRange(start, end));
            }
            else
            {
                if (!ushort.TryParse(part, out var port))
                {
                    error = $"'{part}' is not a port.";
                    return false;
                }

                ranges.Add(PortRange.Single(port));
            }
        }

        if (ranges.Count == 0)
        {
            error = "No ports were given.";
            return false;
        }

        selector = new PortSelector(ranges.ToArray(), negated, text.Trim());
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
