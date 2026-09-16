using Crapnet.Domain.Networking;
using Xunit;

namespace Crapnet.Domain.Tests;

public class Ipv4AddressTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("192.168.137.1")]
    [InlineData("255.255.255.255")]
    [InlineData("8.8.8.8")]
    public void RoundTripsThroughText(string text)
    {
        Assert.True(Ipv4Address.TryParse(text, out var address));
        Assert.Equal(text, address.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.4.5")]
    [InlineData("256.0.0.1")]
    [InlineData("1.2.3.-1")]
    [InlineData("a.b.c.d")]
    [InlineData("1.2.3.")]
    [InlineData("1..3.4")]
    public void RejectsMalformedText(string? text)
        => Assert.False(Ipv4Address.TryParse(text, out _));

    [Fact]
    public void OrdersNumericallyByOctet()
    {
        Assert.True(Ipv4Address.Parse("10.0.0.1") < Ipv4Address.Parse("10.0.1.0"));
        Assert.True(Ipv4Address.Parse("192.168.1.1") > Ipv4Address.Parse("10.255.255.255"));
    }

    [Fact]
    public void ConvertsBetweenHostAndNetworkOrder()
    {
        var address = Ipv4Address.Parse("192.168.137.42");
        Assert.Equal(address, Ipv4Address.FromNetworkOrder(address.ToNetworkOrder()));
    }
}

public class Ipv4SubnetTests
{
    [Fact]
    public void MasksTheHostPortionOnConstruction()
    {
        var subnet = new Ipv4Subnet(Ipv4Address.Parse("192.168.137.55"), 24);
        Assert.Equal("192.168.137.0", subnet.Network.ToString());
        Assert.Equal("192.168.137.0/24", subnet.ToString());
    }

    [Fact]
    public void ExposesItsBoundsAndGateway()
    {
        var subnet = Ipv4Subnet.IcsDefault;
        Assert.Equal("192.168.137.0", subnet.FirstAddress.ToString());
        Assert.Equal("192.168.137.255", subnet.LastAddress.ToString());
        Assert.Equal("192.168.137.1", subnet.GatewayAddress.ToString());
    }

    [Theory]
    [InlineData("192.168.137.1", true)]
    [InlineData("192.168.137.254", true)]
    [InlineData("192.168.138.1", false)]
    [InlineData("8.8.8.8", false)]
    public void ReportsMembership(string candidate, bool expected)
        => Assert.Equal(expected, Ipv4Subnet.IcsDefault.Contains(Ipv4Address.Parse(candidate)));

    [Fact]
    public void ParsesBareAddressAsSingleHost()
    {
        Assert.True(Ipv4Subnet.TryParse("10.1.2.3", out var subnet));
        Assert.Equal(32, subnet.PrefixLength);
    }

    [Theory]
    [InlineData("192.168.1.0/33")]
    [InlineData("192.168.1.0/-1")]
    [InlineData("nonsense/24")]
    public void RejectsMalformedText(string text) => Assert.False(Ipv4Subnet.TryParse(text, out _));
}

public class Ipv4SelectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("*")]
    public void EmptyExpressionMatchesEverything(string? text)
    {
        Assert.True(Ipv4Selector.TryParse(text, out var selector, out _));
        Assert.True(selector.MatchesAnything);
        Assert.True(selector.Matches(Ipv4Address.Parse("1.2.3.4")));
    }

    [Fact]
    public void MatchesASingleAddress()
    {
        var selector = Ipv4Selector.Parse("192.168.137.42");
        Assert.True(selector.Matches(Ipv4Address.Parse("192.168.137.42")));
        Assert.False(selector.Matches(Ipv4Address.Parse("192.168.137.43")));
    }

    [Fact]
    public void MatchesCidrAndExplicitRangesTogether()
    {
        var selector = Ipv4Selector.Parse("10.0.0.0/8, 1.1.1.1-1.1.1.9");
        Assert.True(selector.Matches(Ipv4Address.Parse("10.255.0.1")));
        Assert.True(selector.Matches(Ipv4Address.Parse("1.1.1.5")));
        Assert.False(selector.Matches(Ipv4Address.Parse("1.1.1.10")));
        Assert.False(selector.Matches(Ipv4Address.Parse("11.0.0.1")));
    }

    [Fact]
    public void NegationInvertsTheWholeSet()
    {
        var selector = Ipv4Selector.Parse("!10.0.0.0/8");
        Assert.False(selector.Matches(Ipv4Address.Parse("10.1.1.1")));
        Assert.True(selector.Matches(Ipv4Address.Parse("8.8.8.8")));
    }

    [Fact]
    public void NegatedWildcardMatchesNothing()
    {
        var selector = Ipv4Selector.Parse("!*");
        Assert.False(selector.MatchesAnything);
        Assert.False(selector.Matches(Ipv4Address.Parse("8.8.8.8")));
    }

    [Fact]
    public void ReportsTheOffendingPart()
    {
        Assert.False(Ipv4Selector.TryParse("10.0.0.1, banana", out _, out var error));
        Assert.Contains("banana", error);
    }

    [Fact]
    public void PreservesTheTypedExpression()
        => Assert.Equal("10.0.0.0/8, 1.1.1.1", Ipv4Selector.Parse("10.0.0.0/8, 1.1.1.1").Text);
}

public class PortSelectorTests
{
    [Fact]
    public void EmptyMatchesEverything()
    {
        Assert.True(PortSelector.Any.MatchesAnything);
        Assert.True(PortSelector.Any.Matches(443));
    }

    [Fact]
    public void MatchesListsAndRanges()
    {
        var selector = PortSelector.Parse("53, 80, 8000-8100");
        Assert.True(selector.Matches(53));
        Assert.True(selector.Matches(8050));
        Assert.True(selector.Matches(8100));
        Assert.False(selector.Matches(8101));
        Assert.False(selector.Matches(443));
    }

    [Fact]
    public void NegationInverts()
    {
        var selector = PortSelector.Parse("!443");
        Assert.False(selector.Matches(443));
        Assert.True(selector.Matches(80));
    }

    [Fact]
    public void NormalisesReversedRanges()
    {
        var selector = PortSelector.Parse("9000-8000");
        Assert.True(selector.Matches(8500));
    }

    [Theory]
    [InlineData("70000")]
    [InlineData("80-")]
    [InlineData("abc")]
    public void RejectsMalformedText(string text) => Assert.False(PortSelector.TryParse(text, out _, out _));
}
