using Crapnet.Application.Abstractions;

namespace Crapnet.Application.Tests;

/// <summary>A clock the test moves by hand, so scheduling can be asserted without waiting.</summary>
public sealed class FakeClock : IClock
{
    public long ElapsedMilliseconds { get; set; }

    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Advance(long milliseconds) => ElapsedMilliseconds += milliseconds;
}

/// <summary>
/// Randomness with the dice pre-loaded, so every probabilistic impairment becomes a deterministic
/// assertion rather than a flaky one.
/// </summary>
public sealed class ScriptedRandom : IRandomSource
{
    private readonly Queue<double> _doubles = new();
    private readonly Queue<int> _ints = new();

    /// <summary>Used once the scripted values run out.</summary>
    public double DefaultDouble { get; set; } = 0.5d;

    public int DefaultInt { get; set; }

    public byte FillByte { get; set; } = 0xEE;

    public int DoublesTaken { get; private set; }

    public ScriptedRandom Doubles(params double[] values)
    {
        foreach (var value in values) _doubles.Enqueue(value);
        return this;
    }

    public ScriptedRandom Ints(params int[] values)
    {
        foreach (var value in values) _ints.Enqueue(value);
        return this;
    }

    public double NextDouble()
    {
        DoublesTaken++;
        return _doubles.Count > 0 ? _doubles.Dequeue() : DefaultDouble;
    }

    public int Next(int minInclusive, int maxExclusive)
    {
        if (_ints.Count > 0) return Math.Clamp(_ints.Dequeue(), minInclusive, maxExclusive - 1);
        return Math.Clamp(DefaultInt, minInclusive, maxExclusive - 1);
    }

    public void NextBytes(Span<byte> buffer) => buffer.Fill(FillByte);

    /// <summary>A source that always fails a probability check, whatever the chance.</summary>
    public static ScriptedRandom Never() => new() { DefaultDouble = 1d };

    /// <summary>A source that always passes a probability check below 100%.</summary>
    public static ScriptedRandom Always() => new() { DefaultDouble = 0d };
}
