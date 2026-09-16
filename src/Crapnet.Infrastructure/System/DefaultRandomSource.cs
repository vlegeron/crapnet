using Crapnet.Application.Abstractions;

// The namespace deliberately does not mirror the folder name. A namespace called
// Crapnet.Infrastructure.System would shadow the global System namespace for every file in this
// assembly, so that any sibling writing System.Runtime.InteropServices.X would stop compiling.
namespace Crapnet.Infrastructure.SystemServices;

/// <summary>
/// The production randomness source, backed by <see cref="Random.Shared"/>.
/// </summary>
/// <remarks>
/// <see cref="Random.Shared"/> is thread-safe, which matters here: impairment decisions are taken
/// on whichever worker drains the capture queue. A plain <see cref="Random"/> shared between
/// threads silently degrades into returning zeros once its state is torn.
/// </remarks>
public sealed class DefaultRandomSource : IRandomSource
{
    /// <summary>A single shared instance; the underlying generator is already shared and safe.</summary>
    public static readonly DefaultRandomSource Instance = new();

    /// <inheritdoc />
    public double NextDouble() => Random.Shared.NextDouble();

    /// <inheritdoc />
    public int Next(int minInclusive, int maxExclusive) => Random.Shared.Next(minInclusive, maxExclusive);

    /// <inheritdoc />
    public void NextBytes(Span<byte> buffer) => Random.Shared.NextBytes(buffer);
}
