namespace Crapnet.Application.Abstractions;

/// <summary>
/// A monotonic clock. Injected rather than called statically so scheduling can be tested without
/// real time passing.
/// </summary>
public interface IClock
{
    /// <summary>Milliseconds from an arbitrary origin, guaranteed never to go backwards.</summary>
    long ElapsedMilliseconds { get; }

    DateTimeOffset UtcNow { get; }
}

/// <summary>Randomness, injected so that impairment decisions are reproducible under test.</summary>
public interface IRandomSource
{
    /// <summary>A value in [0, 1).</summary>
    double NextDouble();

    /// <summary>A value in [minInclusive, maxExclusive).</summary>
    int Next(int minInclusive, int maxExclusive);

    void NextBytes(Span<byte> buffer);
}

/// <summary>Reports whether the process holds the privileges the driver and sharing APIs demand.</summary>
public interface IPrivilegeProbe
{
    bool IsElevated { get; }
}

/// <summary>Reports whether the packet capture driver is installed and loadable.</summary>
public interface ICaptureDriverProbe
{
    /// <summary>Null when everything is in place, otherwise a message explaining what is missing.</summary>
    string? Diagnose();
}
