using Crapnet.Application.Impairments;
using Crapnet.Domain.Impairments;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Rules;
using Xunit;

namespace Crapnet.Application.Tests;

public class ImpairmentPipelineTests
{
    private const long Now = 10_000;

    private static PacketDescriptor Packet(
        LinkDirection direction = LinkDirection.Uplink,
        TransportProtocol protocol = TransportProtocol.Tcp) => new(
            direction,
            protocol,
            Ipv4Address.Parse("192.168.137.42"),
            51_000,
            Ipv4Address.Parse("93.184.216.34"),
            443,
            1400,
            TcpFlags.Ack);

    private static Profile ProfileWith(ImpairmentSet impairments) => new()
    {
        Name = "test",
        Rules = [Rule.Create("everything") with { Impairments = impairments }],
    };

    private static PacketPlan Run(ImpairmentSet impairments, ScriptedRandom random, int length = 1400,
        long now = Now, PacketDescriptor? packet = null, RuleStateTable? states = null)
    {
        var pipeline = new ImpairmentPipeline(random);
        var descriptor = packet ?? Packet();
        return pipeline.Evaluate(ProfileWith(impairments), states ?? new RuleStateTable(), descriptor, length, now);
    }

    [Fact]
    public void UnmatchedTrafficPassesStraightThrough()
    {
        var pipeline = new ImpairmentPipeline(ScriptedRandom.Always());
        var empty = new Profile { Name = "empty" };

        var plan = pipeline.Evaluate(empty, new RuleStateTable(), Packet(), 1400, Now);

        Assert.Equal(PacketAction.Forward, plan.Action);
        Assert.False(plan.IsDelayed(Now));
        Assert.Null(plan.Rule);
    }

    [Fact]
    public void ARuleWithNothingEnabledForwardsButStillClaimsThePacket()
    {
        var plan = Run(ImpairmentSet.None, ScriptedRandom.Always());

        Assert.Equal(PacketAction.Forward, plan.Action);
        Assert.NotNull(plan.Rule);
        Assert.False(plan.IsDelayed(Now));
    }

    [Fact]
    public void BlockDiscardsEverything()
    {
        var plan = Run(new ImpairmentSet { Block = new BlockSetting(enabled: true) }, ScriptedRandom.Never());

        Assert.Equal(PacketAction.Discard, plan.Action);
        Assert.Equal(DiscardReason.Blocked, plan.Discard);
    }

    [Fact]
    public void DropUsesTheConfiguredChance()
    {
        var set = new ImpairmentSet { Drop = new DropSetting(enabled: true, chance: 30d) };

        Assert.Equal(PacketAction.Discard, Run(set, new ScriptedRandom().Doubles(0.29)).Action);
        Assert.Equal(PacketAction.Forward, Run(set, new ScriptedRandom().Doubles(0.31)).Action);
    }

    [Fact]
    public void AZeroChanceNeverRollsTheDice()
    {
        var random = new ScriptedRandom();
        Run(new ImpairmentSet { Drop = new DropSetting(enabled: true, chance: 0d) }, random);

        // Short-circuiting matters: a disabled-by-zero impairment must not consume randomness and
        // shift the outcome of the impairments after it.
        Assert.Equal(0, random.DoublesTaken);
    }

    [Fact]
    public void ACertainChanceAlwaysFires()
    {
        var random = new ScriptedRandom { DefaultDouble = 0.999999d };
        var plan = Run(new ImpairmentSet { Drop = new DropSetting(enabled: true, chance: 100d) }, random);

        Assert.Equal(PacketAction.Discard, plan.Action);
        Assert.Equal(0, random.DoublesTaken);
    }

    [Fact]
    public void LagPushesTheReleaseTimeOut()
    {
        var set = new ImpairmentSet { Lag = new LagSetting(enabled: true, delayMilliseconds: 250) };
        var plan = Run(set, ScriptedRandom.Always());

        Assert.Equal(Now + 250, plan.ReleaseAtMilliseconds);
        Assert.True(plan.IsDelayed(Now));
    }

    [Fact]
    public void JitterMovesTheDelayBothWays()
    {
        var set = new ImpairmentSet { Lag = new LagSetting(enabled: true, delayMilliseconds: 100, jitterMilliseconds: 40) };

        Assert.Equal(Now + 140, Run(set, ScriptedRandom.Always().Ints(40)).ReleaseAtMilliseconds);
        Assert.Equal(Now + 60, Run(set, ScriptedRandom.Always().Ints(-40)).ReleaseAtMilliseconds);
    }

    [Fact]
    public void DelayNeverGoesNegative()
    {
        var set = new ImpairmentSet { Lag = new LagSetting(enabled: true, delayMilliseconds: 10, jitterMilliseconds: 60) };
        var plan = Run(set, ScriptedRandom.Always().Ints(-60));

        Assert.Equal(Now, plan.ReleaseAtMilliseconds);
    }

    [Fact]
    public void LagCanApplyToOnlySomePackets()
    {
        var set = new ImpairmentSet { Lag = new LagSetting(enabled: true, delayMilliseconds: 200, chance: 50d) };

        Assert.True(Run(set, new ScriptedRandom().Doubles(0.4)).IsDelayed(Now));
        Assert.False(Run(set, new ScriptedRandom().Doubles(0.6)).IsDelayed(Now));
    }

    [Fact]
    public void DuplicateAsksForExtraCopies()
    {
        var set = new ImpairmentSet { Duplicate = new DuplicateSetting(enabled: true, chance: 100d, copies: 3) };

        Assert.Equal(3, Run(set, ScriptedRandom.Always()).ExtraCopies);
        Assert.Equal(0, Run(new ImpairmentSet
        {
            Duplicate = new DuplicateSetting(enabled: true, chance: 10d, copies: 3),
        }, ScriptedRandom.Never()).ExtraCopies);
    }

    [Fact]
    public void ResetOnlyAppliesToTcp()
    {
        var set = new ImpairmentSet { Reset = new ResetSetting(enabled: true) };

        Assert.Equal(PacketAction.Reset, Run(set, ScriptedRandom.Always()).Action);

        // UDP has nothing to reset, so the impairment stands aside rather than eating the packet.
        var udp = Run(set, ScriptedRandom.Always(), packet: Packet(protocol: TransportProtocol.Udp));
        Assert.Equal(PacketAction.Forward, udp.Action);
    }

    [Fact]
    public void TamperFlagsTheChecksumPolicy()
    {
        var repaired = Run(new ImpairmentSet
        {
            Tamper = new TamperSetting(enabled: true, chance: 100d, recalculateChecksum: true),
        }, ScriptedRandom.Always());

        var broken = Run(new ImpairmentSet
        {
            Tamper = new TamperSetting(enabled: true, chance: 100d, recalculateChecksum: false),
        }, ScriptedRandom.Always());

        Assert.True(repaired.Tamper);
        Assert.False(repaired.LeaveChecksumBroken);
        Assert.True(broken.Tamper);
        Assert.True(broken.LeaveChecksumBroken);
    }

    [Fact]
    public void ReorderAddsABoundedExtraDelay()
    {
        var set = new ImpairmentSet { Reorder = new ReorderSetting(enabled: true, chance: 100d, maxDelayMilliseconds: 80) };
        var plan = Run(set, ScriptedRandom.Always().Ints(55));

        Assert.True(plan.Reordered);
        Assert.Equal(Now + 55, plan.ReleaseAtMilliseconds);
    }

    [Fact]
    public void DelaysFromDifferentImpairmentsAccumulate()
    {
        var set = new ImpairmentSet
        {
            Reorder = new ReorderSetting(enabled: true, chance: 100d, maxDelayMilliseconds: 50),
            Lag = new LagSetting(enabled: true, delayMilliseconds: 100),
        };

        var plan = Run(set, ScriptedRandom.Always().Ints(30));
        Assert.Equal(Now + 130, plan.ReleaseAtMilliseconds);
    }

    [Fact]
    public void DroppingWinsOverDelaying()
    {
        // Ordering guarantee: nothing is queued for a packet that was going to be discarded.
        var set = new ImpairmentSet
        {
            Drop = new DropSetting(enabled: true, chance: 100d),
            Lag = new LagSetting(enabled: true, delayMilliseconds: 500),
        };

        var plan = Run(set, ScriptedRandom.Always());

        Assert.Equal(PacketAction.Discard, plan.Action);
        Assert.Equal(DiscardReason.Dropped, plan.Discard);
    }

    [Fact]
    public void BlockWinsOverEverythingElse()
    {
        var set = new ImpairmentSet
        {
            Block = new BlockSetting(enabled: true),
            Drop = new DropSetting(enabled: true, chance: 0d),
            Lag = new LagSetting(enabled: true, delayMilliseconds: 500),
        };

        Assert.Equal(DiscardReason.Blocked, Run(set, ScriptedRandom.Never()).Discard);
    }
}

public class ThrottlePipelineTests
{
    private const long Now = 1_000;

    private static PacketDescriptor Packet() => new(
        LinkDirection.Uplink, TransportProtocol.Tcp,
        Ipv4Address.Parse("192.168.137.42"), 51_000,
        Ipv4Address.Parse("93.184.216.34"), 443, 1400, TcpFlags.Ack);

    private static Profile ProfileWith(ImpairmentSet set) => new()
    {
        Name = "t",
        Rules = [Rule.Create("everything") with { Impairments = set }],
    };

    [Fact]
    public void AStallHoldsTrafficUntilTheWindowCloses()
    {
        var set = new ImpairmentSet
        {
            Throttle = new ThrottleSetting(enabled: true, timeframeMilliseconds: 300, chance: 100d),
        };
        var pipeline = new ImpairmentPipeline(ScriptedRandom.Always());
        var states = new RuleStateTable();
        var profile = ProfileWith(set);

        var first = pipeline.Evaluate(profile, states, Packet(), 1400, Now);
        Assert.Equal(Now + 300, first.ReleaseAtMilliseconds);

        // A packet arriving mid-stall joins the same burst rather than starting a new stall.
        var second = pipeline.Evaluate(profile, states, Packet(), 1400, Now + 100);
        Assert.Equal(Now + 300, second.ReleaseAtMilliseconds);
    }

    [Fact]
    public void TrafficFlowsAgainOnceTheWindowHasPassed()
    {
        var set = new ImpairmentSet
        {
            Throttle = new ThrottleSetting(enabled: true, timeframeMilliseconds: 300, chance: 50d),
        };
        var random = new ScriptedRandom().Doubles(0.1, 0.9);
        var pipeline = new ImpairmentPipeline(random);
        var states = new RuleStateTable();
        var profile = ProfileWith(set);

        pipeline.Evaluate(profile, states, Packet(), 1400, Now);
        var later = pipeline.Evaluate(profile, states, Packet(), 1400, Now + 400);

        Assert.Equal(Now + 400, later.ReleaseAtMilliseconds);
    }

    [Fact]
    public void DropThrottledBinsTheBurstInsteadOfReleasingIt()
    {
        var set = new ImpairmentSet
        {
            Throttle = new ThrottleSetting(enabled: true, timeframeMilliseconds: 300, chance: 100d, dropThrottled: true),
        };
        var pipeline = new ImpairmentPipeline(ScriptedRandom.Always());
        var states = new RuleStateTable();
        var profile = ProfileWith(set);

        var first = pipeline.Evaluate(profile, states, Packet(), 1400, Now);
        var second = pipeline.Evaluate(profile, states, Packet(), 1400, Now + 50);

        Assert.Equal(DiscardReason.ThrottleDiscarded, first.Discard);
        Assert.Equal(DiscardReason.ThrottleDiscarded, second.Discard);
    }
}
