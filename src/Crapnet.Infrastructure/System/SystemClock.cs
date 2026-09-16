using System.Diagnostics;
using Crapnet.Application.Abstractions;

// The namespace deliberately does not mirror the folder name. A namespace called
// Crapnet.Infrastructure.System would shadow the global System namespace for every file in this
// assembly, so that any sibling writing System.Runtime.InteropServices.X would stop compiling.
namespace Crapnet.Infrastructure.SystemServices;

/// <summary>
/// The real clock: a process-wide <see cref="Stopwatch"/> for elapsed time and the system clock
/// for wall time.
/// </summary>
/// <remarks>
/// The two are kept apart on purpose. Impairment scheduling measures durations, and wall time can
/// jump backwards across a time-zone change or an NTP correction, which would make a queued
/// packet look due decades early. The stopwatch is monotonic, so it cannot.
/// </remarks>
public sealed class SystemClock : IClock
{
    /// <summary>A single shared instance; the underlying stopwatch is stateless from outside.</summary>
    public static readonly SystemClock Instance = new();

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    /// <inheritdoc />
    public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
