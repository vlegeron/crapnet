using Crapnet.Application.Impairments;
using Xunit;

namespace Crapnet.Application.Tests;

public class RateLimiterTests
{
    /// <summary>1 Mbps, so a 1250-byte packet (10 kbit) occupies the link for exactly 10 ms.</summary>
    private static RateLimiter OneMegabit()
    {
        var limiter = new RateLimiter();
        limiter.Configure(kilobitsPerSecond: 1000, burstKilobits: 1000);
        return limiter;
    }

    [Fact]
    public void AnIdleLinkForwardsImmediately()
        => Assert.Equal(0, OneMegabit().Reserve(1250, nowMilliseconds: 0));

    [Fact]
    public void BackToBackPacketsQueueAtTheLinkRate()
    {
        var limiter = OneMegabit();

        Assert.Equal(0, limiter.Reserve(1250, 0));
        Assert.Equal(10, limiter.Reserve(1250, 0));
        Assert.Equal(20, limiter.Reserve(1250, 0));
    }

    [Fact]
    public void QueueingDelayTracksOfferedLoad()
    {
        var limiter = OneMegabit();
        for (var i = 0; i < 50; i++) limiter.Reserve(1250, 0);

        // Fifty 10 ms packets offered at once means the next one waits half a second.
        Assert.Equal(500, limiter.Reserve(1250, 0));
    }

    [Fact]
    public void PacketsNeverOvertakeOneAnother()
    {
        var limiter = OneMegabit();
        long previous = -1;

        for (var i = 0; i < 100; i++)
        {
            var release = limiter.Reserve(1250, 0);
            Assert.NotNull(release);
            Assert.True(release!.Value >= previous, "a rate cap must not reorder a stream");
            previous = release.Value;
        }
    }

    [Fact]
    public void ShedsLoadOnceTheBufferIsFull()
    {
        var limiter = OneMegabit();
        limiter.MaxQueueMilliseconds = 100;

        // The buffer holds packets due within 100 ms. The first leaves at once and ten more queue
        // at 10 ms apart, so eleven fit and the twelfth is shed.
        for (var i = 0; i < 11; i++) Assert.NotNull(limiter.Reserve(1250, 0));
        Assert.Null(limiter.Reserve(1250, 0));
    }

    [Fact]
    public void RecoversOnceTheQueueDrains()
    {
        var limiter = OneMegabit();
        limiter.MaxQueueMilliseconds = 100;
        for (var i = 0; i < 20; i++) limiter.Reserve(1250, 0);

        Assert.NotNull(limiter.Reserve(1250, 1_000));
    }

    [Fact]
    public void AnIdleLinkEarnsAtMostOneBurstOfCredit()
    {
        var limiter = OneMegabit();
        limiter.Reserve(1250, 0);

        // After a long idle the link has earned one burst allowance of credit: one second of
        // capacity, which is 101 packets of 10 ms each counting the one that spends the last of
        // it. The next packet starts paying the rate again.
        const long later = 100_000;
        var immediate = 0;
        while (limiter.Reserve(1250, later) == later) immediate++;

        Assert.Equal(101, immediate);
    }

    [Fact]
    public void BiggerPacketsOccupyTheLinkForLonger()
    {
        var limiter = OneMegabit();
        limiter.Reserve(12_500, 0);           // 100 kbit at 1 Mbps = 100 ms
        Assert.Equal(100, limiter.Reserve(1250, 0));
    }

    [Fact]
    public void ResetClearsTheBacklog()
    {
        var limiter = OneMegabit();
        for (var i = 0; i < 20; i++) limiter.Reserve(1250, 0);

        limiter.Reset();

        Assert.Equal(0, limiter.Reserve(1250, 0));
    }
}
