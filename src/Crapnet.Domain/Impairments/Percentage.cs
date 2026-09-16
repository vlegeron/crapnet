using System.Globalization;

namespace Crapnet.Domain.Impairments;

/// <summary>A probability expressed in percent, always clamped to 0-100.</summary>
public readonly struct Percentage : IEquatable<Percentage>, IComparable<Percentage>
{
    public static readonly Percentage Zero = new(0d);
    public static readonly Percentage Full = new(100d);

    public double Value { get; }

    public Percentage(double value)
        => Value = double.IsNaN(value) ? 0d : Math.Clamp(value, 0d, 100d);

    public double AsFraction => Value / 100d;
    public bool IsZero => Value <= 0d;
    public bool IsCertain => Value >= 100d;

    public static implicit operator Percentage(double value) => new(value);

    public static Percentage Parse(string text)
        => double.TryParse(text.TrimEnd('%', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? new Percentage(value)
            : throw new FormatException($"'{text}' is not a percentage.");

    public bool Equals(Percentage other) => Value.Equals(other.Value);
    public override bool Equals(object? obj) => obj is Percentage other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(Percentage other) => Value.CompareTo(other.Value);
    public override string ToString() => $"{Value.ToString("0.##", CultureInfo.InvariantCulture)}%";
}
