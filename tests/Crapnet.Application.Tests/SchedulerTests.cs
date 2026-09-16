using System.Collections.Concurrent;
using Crapnet.Application.Abstractions;
using Crapnet.Application.Engine;
using Xunit;

namespace Crapnet.Application.Tests;

public class PacketSchedulerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private sealed class Harness : IDisposable
    {
        public FakeClock Clock { get; } = new();
        public ConcurrentQueue<(CapturedPacket Packet, int Copies)> Released { get; } = new();
        public ConcurrentQueue<CapturedPacket> Discarded { get; } = new();
        public PacketScheduler Scheduler { get; }

        public Harness(int capacity = 32_768)
        {
            Scheduler = new PacketScheduler(Clock, (p, c) => Released.Enqueue((p, c)), Discarded.Enqueue)
            {
                Capacity = capacity,
            };
            Scheduler.Start();
        }

        /// <summary>Waits for a number of releases, so tests never depend on thread timing.</summary>
        public bool WaitForReleases(int count)
        {
            var deadline = DateTime.UtcNow + Patience;
            while (DateTime.UtcNow < deadline)
            {
                if (Released.Count >= count) return true;
                Thread.Sleep(5);
            }

            return false;
        }

        public void Dispose() => Scheduler.Dispose();
    }

    private static CapturedPacket Packet(int marker)
    {
        var packet = new CapturedPacket(64) { Length = 4 };
        packet.Buffer[0] = (byte)marker;
        return packet;
    }

    [Fact]
    public void HoldsAPacketUntilItsReleaseTime()
    {
        using var harness = new Harness();

        harness.Scheduler.TrySchedule(Packet(1), releaseAtMilliseconds: 500, extraCopies: 0);

        Thread.Sleep(100);
        Assert.Empty(harness.Released);

        harness.Clock.ElapsedMilliseconds = 500;

        Assert.True(harness.WaitForReleases(1));
        Assert.Single(harness.Released);
    }

    [Fact]
    public void ReleasesInReleaseTimeOrderRatherThanArrivalOrder()
    {
        using var harness = new Harness();

        harness.Scheduler.TrySchedule(Packet(1), 900, 0);
        harness.Scheduler.TrySchedule(Packet(2), 300, 0);
        harness.Scheduler.TrySchedule(Packet(3), 600, 0);

        harness.Clock.ElapsedMilliseconds = 1_000;
        Assert.True(harness.WaitForReleases(3));

        var order = harness.Released.Select(entry => entry.Packet.Buffer[0]).ToArray();
        Assert.Equal(new byte[] { 2, 3, 1 }, order);
    }

    [Fact]
    public void KeepsArrivalOrderWithinTheSameMillisecond()
    {
        using var harness = new Harness();

        // PriorityQueue is not a stable heap, so without the arrival sequence tiebreaker this
        // would invent reordering that no rule asked for.
        for (var i = 0; i < 64; i++) harness.Scheduler.TrySchedule(Packet(i), 100, 0);

        harness.Clock.ElapsedMilliseconds = 100;
        Assert.True(harness.WaitForReleases(64));

        var order = harness.Released.Select(entry => entry.Packet.Buffer[0]).ToArray();
        Assert.Equal(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray(), order);
    }

    [Fact]
    public void CarriesTheDuplicateCountThrough()
    {
        using var harness = new Harness();

        harness.Scheduler.TrySchedule(Packet(1), 10, extraCopies: 4);
        harness.Clock.ElapsedMilliseconds = 10;

        Assert.True(harness.WaitForReleases(1));
        Assert.Equal(4, harness.Released.Single().Copies);
    }

    [Fact]
    public void ReleasesImmediatelyWhenAlreadyDue()
    {
        using var harness = new Harness();
        harness.Clock.ElapsedMilliseconds = 5_000;

        harness.Scheduler.TrySchedule(Packet(1), 1_000, 0);

        Assert.True(harness.WaitForReleases(1));
    }

    [Fact]
    public void RefusesWorkOnceFull()
    {
        using var harness = new Harness(capacity: 4);

        for (var i = 0; i < 4; i++)
            Assert.True(harness.Scheduler.TrySchedule(Packet(i), 10_000, 0));

        Assert.False(harness.Scheduler.TrySchedule(Packet(99), 10_000, 0));
    }

    [Fact]
    public void ReportsHowMuchIsHeld()
    {
        using var harness = new Harness();

        harness.Scheduler.TrySchedule(Packet(1), 10_000, 0);
        harness.Scheduler.TrySchedule(Packet(2), 10_000, 0);

        Assert.Equal(2, harness.Scheduler.Count);
    }

    [Fact]
    public void StoppingReturnsHeldPacketsInsteadOfSendingThem()
    {
        using var harness = new Harness();

        harness.Scheduler.TrySchedule(Packet(1), 10_000, 0);
        harness.Scheduler.TrySchedule(Packet(2), 10_000, 0);

        harness.Scheduler.Stop();

        // Flushing a burst of stale traffic on shutdown would confuse whatever is being tested,
        // so held packets go back to the pool.
        Assert.Empty(harness.Released);
        Assert.Equal(2, harness.Discarded.Count);
        Assert.Equal(0, harness.Scheduler.Count);
    }

    [Fact]
    public void RefusesWorkAfterStopping()
    {
        using var harness = new Harness();
        harness.Scheduler.Stop();

        Assert.False(harness.Scheduler.TrySchedule(Packet(1), 0, 0));
    }

    [Fact]
    public void SurvivesConcurrentProducers()
    {
        using var harness = new Harness();
        const int perThread = 250;
        const int threads = 4;

        Parallel.For(0, threads, thread =>
        {
            for (var i = 0; i < perThread; i++)
                harness.Scheduler.TrySchedule(Packet(0), 50, 0);
        });

        harness.Clock.ElapsedMilliseconds = 50;

        Assert.True(harness.WaitForReleases(threads * perThread));
        Assert.Equal(threads * perThread, harness.Released.Count);
    }

    [Fact]
    public void StoppingTwiceIsHarmless()
    {
        var harness = new Harness();
        harness.Scheduler.Stop();

        var exception = Record.Exception(() => harness.Scheduler.Stop());

        Assert.Null(exception);
        harness.Dispose();
    }
}
