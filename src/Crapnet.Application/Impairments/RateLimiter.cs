namespace Crapnet.Application.Impairments;

/// <summary>
/// Models a link of fixed capacity by handing each packet a virtual finish time.
/// </summary>
/// <remarks>
/// Rather than a classic token bucket, this tracks when the link next falls idle and serialises
/// packets behind that point. Two properties come out of it for free, and both matter: queueing
/// delay grows naturally as offered load exceeds capacity, and packets never overtake one another,
/// so a bandwidth cap does not silently reorder a TCP stream the way a token bucket can.
/// </remarks>
public sealed class RateLimiter
{
    private long _nextFreeMilliseconds;

    /// <summary>Link capacity in bits per second.</summary>
    public int BitsPerSecond { get; private set; }

    /// <summary>
    /// How far the link may run ahead of itself after an idle period, expressed in milliseconds of
    /// capacity. This is the burst allowance: without it, an idle link would still meter the very
    /// first packet.
    /// </summary>
    public int BurstMilliseconds { get; private set; }

    /// <summary>Longest queue we will build before shedding load, mirroring a real device's buffer.</summary>
    public int MaxQueueMilliseconds { get; set; } = 2_000;

    public void Configure(int kilobitsPerSecond, int burstKilobits)
    {
        BitsPerSecond = Math.Max(1, kilobitsPerSecond) * 1000;
        var burstBits = Math.Max(1, burstKilobits) * 1000;
        BurstMilliseconds = (int)Math.Clamp((long)(burstBits * 1000.0 / BitsPerSecond), 1, 10_000);
    }

    public void Reset() => _nextFreeMilliseconds = 0;

    /// <summary>
    /// Reserves capacity for a packet and returns when it may leave, or null when the queue is
    /// already too long and the packet should be shed instead.
    /// </summary>
    public long? Reserve(int packetBytes, long nowMilliseconds)
    {
        // Let an idle link accumulate credit, but only up to the burst allowance. Without this
        // clamp a link idle for an hour would forward an unbounded burst at full speed.
        var earliest = nowMilliseconds - BurstMilliseconds;
        if (_nextFreeMilliseconds < earliest) _nextFreeMilliseconds = earliest;

        var releaseAt = Math.Max(nowMilliseconds, _nextFreeMilliseconds);
        if (releaseAt - nowMilliseconds > MaxQueueMilliseconds) return null;

        var transmitMilliseconds = (long)Math.Ceiling(packetBytes * 8 * 1000.0 / BitsPerSecond);
        _nextFreeMilliseconds = releaseAt + transmitMilliseconds;
        return releaseAt;
    }
}
