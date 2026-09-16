using Crapnet.Domain.Impairments;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Profiles;
using Crapnet.Domain.Rules;
using Xunit;

namespace Crapnet.Domain.Tests;

public static class Packets
{
    public static PacketDescriptor Https(
        LinkDirection direction = LinkDirection.Uplink,
        string device = "192.168.137.42",
        string remote = "93.184.216.34") => new(
            direction,
            TransportProtocol.Tcp,
            Ipv4Address.Parse(device),
            51_000,
            Ipv4Address.Parse(remote),
            443,
            1400,
            TcpFlags.Ack);

    public static PacketDescriptor Dns(LinkDirection direction = LinkDirection.Uplink) => new(
        direction,
        TransportProtocol.Udp,
        Ipv4Address.Parse("192.168.137.42"),
        44_000,
        Ipv4Address.Parse("1.1.1.1"),
        53,
        90,
        TcpFlags.None);

    public static PacketDescriptor Ping(LinkDirection direction = LinkDirection.Uplink) => new(
        direction,
        TransportProtocol.Icmp,
        Ipv4Address.Parse("192.168.137.42"),
        0,
        Ipv4Address.Parse("8.8.8.8"),
        0,
        84,
        TcpFlags.None);
}

public class MatchCriteriaTests
{
    [Fact]
    public void DefaultCriteriaMatchEverything()
    {
        Assert.True(MatchCriteria.Any.MatchesEverything);
        Assert.True(MatchCriteria.Any.Matches(Packets.Https()));
        Assert.True(MatchCriteria.Any.Matches(Packets.Ping()));
    }

    [Theory]
    [InlineData(DirectionFilter.Both, LinkDirection.Uplink, true)]
    [InlineData(DirectionFilter.Both, LinkDirection.Downlink, true)]
    [InlineData(DirectionFilter.Uplink, LinkDirection.Uplink, true)]
    [InlineData(DirectionFilter.Uplink, LinkDirection.Downlink, false)]
    [InlineData(DirectionFilter.Downlink, LinkDirection.Downlink, true)]
    [InlineData(DirectionFilter.Downlink, LinkDirection.Uplink, false)]
    public void ConstrainsDirection(DirectionFilter filter, LinkDirection actual, bool expected)
    {
        var criteria = new MatchCriteria { Direction = filter };
        Assert.Equal(expected, criteria.Matches(Packets.Https(actual)));
    }

    [Fact]
    public void ConstrainsProtocol()
    {
        var criteria = new MatchCriteria { Protocol = ProtocolFilter.Udp };
        Assert.True(criteria.Matches(Packets.Dns()));
        Assert.False(criteria.Matches(Packets.Https()));
    }

    [Fact]
    public void MatchesTheSameDeviceInBothDirections()
    {
        var criteria = new MatchCriteria { DeviceAddresses = Ipv4Selector.Parse("192.168.137.42") };

        // The point of naming addresses by role: one rule, both directions, no duplication.
        Assert.True(criteria.Matches(Packets.Https(LinkDirection.Uplink)));
        Assert.True(criteria.Matches(Packets.Https(LinkDirection.Downlink)));
        Assert.False(criteria.Matches(Packets.Https(device: "192.168.137.99")));
    }

    [Fact]
    public void ConstrainsRemoteAddressIndependentlyOfDevice()
    {
        var criteria = new MatchCriteria { RemoteAddresses = Ipv4Selector.Parse("93.184.216.0/24") };
        Assert.True(criteria.Matches(Packets.Https()));
        Assert.False(criteria.Matches(Packets.Https(remote: "8.8.8.8")));
    }

    [Fact]
    public void ConstrainsRemotePort()
    {
        var criteria = new MatchCriteria { RemotePorts = PortSelector.Parse("443") };
        Assert.True(criteria.Matches(Packets.Https()));
        Assert.False(criteria.Matches(Packets.Dns()));
    }

    [Fact]
    public void APortConstraintExcludesProtocolsWithoutPorts()
    {
        // ICMP cannot satisfy "port 443", so it must fall out of the rule rather than sneak in
        // through a port comparison against zero.
        var criteria = new MatchCriteria { RemotePorts = PortSelector.Parse("443") };
        Assert.False(criteria.Matches(Packets.Ping()));
    }

    [Fact]
    public void NoPortConstraintStillMatchesIcmp()
        => Assert.True(new MatchCriteria { Protocol = ProtocolFilter.Icmp }.Matches(Packets.Ping()));

    [Fact]
    public void CombinesEveryDimension()
    {
        var criteria = new MatchCriteria
        {
            Direction = DirectionFilter.Uplink,
            Protocol = ProtocolFilter.Tcp,
            DeviceAddresses = Ipv4Selector.Parse("192.168.137.0/24"),
            RemotePorts = PortSelector.Parse("443"),
        };

        Assert.True(criteria.Matches(Packets.Https()));
        Assert.False(criteria.Matches(Packets.Https(LinkDirection.Downlink)));
        Assert.False(criteria.Matches(Packets.Dns()));
    }

    [Fact]
    public void DescribesItselfCompactly()
    {
        Assert.Equal("all traffic", MatchCriteria.Any.Describe());
        var criteria = new MatchCriteria
        {
            Direction = DirectionFilter.Uplink,
            Protocol = ProtocolFilter.Tcp,
            RemotePorts = PortSelector.Parse("443"),
        };
        Assert.Equal("↑  TCP  :443", criteria.Describe());
    }
}

public class ProfileTests
{
    private static Rule Blocking(string name, MatchCriteria match) => Rule.Create(name) with
    {
        Match = match,
        Impairments = new ImpairmentSet { Block = new BlockSetting(enabled: true) },
    };

    [Fact]
    public void ResolvesTheFirstMatchingRule()
    {
        var first = Blocking("dns", new MatchCriteria { RemotePorts = PortSelector.Parse("53") });
        var second = Blocking("everything", MatchCriteria.Any);
        var profile = new Profile { Name = "p", Rules = [first, second] };

        Assert.Equal(first.Id, profile.Resolve(Packets.Dns())!.Id);
        Assert.Equal(second.Id, profile.Resolve(Packets.Https())!.Id);
    }

    [Fact]
    public void ARuleWithNoImpairmentsActsAsAnException()
    {
        // This is the documented way to carve an exception out of a broad rule: the narrow rule
        // claims the packet first and does nothing to it.
        var allow = Rule.Create("keep dns clean") with
        {
            Match = new MatchCriteria { RemotePorts = PortSelector.Parse("53") },
        };
        var breakEverything = Blocking("everything", MatchCriteria.Any);
        var profile = new Profile { Name = "p", Rules = [allow, breakEverything] };

        var claimed = profile.Resolve(Packets.Dns());
        Assert.Equal(allow.Id, claimed!.Id);
        Assert.False(claimed.Impairments.AnyEnabled);
    }

    [Fact]
    public void SkipsDisabledRules()
    {
        var disabled = Blocking("off", MatchCriteria.Any) with { Enabled = false };
        var profile = new Profile { Name = "p", Rules = [disabled] };
        Assert.Null(profile.Resolve(Packets.Https()));
    }

    [Fact]
    public void ReturnsNullWhenNothingMatches()
    {
        var profile = new Profile
        {
            Name = "p",
            Rules = [Blocking("udp only", new MatchCriteria { Protocol = ProtocolFilter.Udp })],
        };
        Assert.Null(profile.Resolve(Packets.Https()));
    }

    [Fact]
    public void ReorderingChangesWhichRuleWins()
    {
        var dns = Blocking("dns", new MatchCriteria { RemotePorts = PortSelector.Parse("53") });
        var all = Blocking("everything", MatchCriteria.Any);
        var profile = new Profile { Name = "p", Rules = [dns, all] };

        Assert.Equal(dns.Id, profile.Resolve(Packets.Dns())!.Id);

        var flipped = profile.MoveRule(all.Id, 0);
        Assert.Equal(all.Id, flipped.Resolve(Packets.Dns())!.Id);
    }

    [Fact]
    public void WithRuleReplacesAnExistingRuleInPlace()
    {
        var rule = Blocking("a", MatchCriteria.Any);
        var profile = new Profile { Name = "p", Rules = [rule] };

        var updated = profile.WithRule(rule with { Name = "renamed" });

        Assert.Single(updated.Rules);
        Assert.Equal("renamed", updated.Rules[0].Name);
    }

    [Fact]
    public void WithoutRuleRemovesIt()
    {
        var rule = Blocking("a", MatchCriteria.Any);
        var profile = new Profile { Name = "p", Rules = [rule] };
        Assert.Empty(profile.WithoutRule(rule.Id).Rules);
    }
}

public class ImpairmentSetTests
{
    [Fact]
    public void NothingIsEnabledByDefault()
    {
        Assert.False(ImpairmentSet.None.AnyEnabled);
        Assert.Empty(ImpairmentSet.None.Active);
    }

    [Theory]
    [InlineData(ImpairmentKind.Block)]
    [InlineData(ImpairmentKind.Drop)]
    [InlineData(ImpairmentKind.Reset)]
    [InlineData(ImpairmentKind.Tamper)]
    [InlineData(ImpairmentKind.Duplicate)]
    [InlineData(ImpairmentKind.Bandwidth)]
    [InlineData(ImpairmentKind.Throttle)]
    [InlineData(ImpairmentKind.Reorder)]
    [InlineData(ImpairmentKind.Lag)]
    public void EachImpairmentTogglesIndependently(ImpairmentKind kind)
    {
        var set = ImpairmentSet.None.WithEnabled(kind, true);

        Assert.True(set.IsEnabled(kind));
        Assert.Single(set.Active);
        Assert.All(set.All.Where(s => s.Kind != kind), s => Assert.False(s.Enabled));
    }

    [Fact]
    public void TogglingOffKeepsTheConfiguredParameters()
    {
        var set = new ImpairmentSet { Lag = new LagSetting(enabled: true, delayMilliseconds: 250, jitterMilliseconds: 40) };

        var off = set.WithEnabled(ImpairmentKind.Lag, false);

        Assert.False(off.Lag.Enabled);
        Assert.Equal(250, off.Lag.DelayMilliseconds);
        Assert.Equal(40, off.Lag.JitterMilliseconds);
    }

    [Fact]
    public void AppliesImpairmentsInADefinedOrder()
    {
        var order = ImpairmentSet.None.All.Select(s => s.Kind).ToArray();
        Assert.Equal(ImpairmentKind.Block, order[0]);
        Assert.Equal(ImpairmentKind.Lag, order[^1]);
        Assert.Equal(Enum.GetValues<ImpairmentKind>().Length, order.Length);
    }
}

public class PercentageTests
{
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(42.5, 42.5)]
    [InlineData(100, 100)]
    [InlineData(1000, 100)]
    [InlineData(double.NaN, 0)]
    public void ClampsToZeroHundred(double input, double expected)
        => Assert.Equal(expected, new Percentage(input).Value);

    [Fact]
    public void ConvertsToAFraction() => Assert.Equal(0.25d, new Percentage(25).AsFraction, 10);
}

public class BuiltInProfileTests
{
    [Fact]
    public void EveryPresetHasRules()
        => Assert.All(BuiltInProfiles.All, profile => Assert.NotEmpty(profile.Rules));

    [Fact]
    public void PresetNamesAreUnique()
        => Assert.Equal(BuiltInProfiles.All.Count, BuiltInProfiles.All.Select(p => p.Name).Distinct().Count());

    [Fact]
    public void CleanChangesNothing() => Assert.False(BuiltInProfiles.Clean.AnyActive);

    [Fact]
    public void EveryOtherPresetActuallyDoesSomething()
        => Assert.All(BuiltInProfiles.All.Where(p => p != BuiltInProfiles.Clean), p => Assert.True(p.AnyActive));

    [Fact]
    public void DeadAirBlocksEverything()
        => Assert.NotNull(BuiltInProfiles.DeadAir.Resolve(Packets.Https()));

    [Fact]
    public void NoDnsOnlyTouchesDns()
    {
        Assert.NotNull(BuiltInProfiles.DnsBlackhole.Resolve(Packets.Dns()));
        Assert.Null(BuiltInProfiles.DnsBlackhole.Resolve(Packets.Https()));
    }

    [Fact]
    public void LookupIsCaseInsensitive() => Assert.NotNull(BuiltInProfiles.FindByName("dead air"));
}
