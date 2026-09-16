using Crapnet.Application.Abstractions;
using Crapnet.Application.Engine;
using Crapnet.Application.UseCases;
using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Profiles;
using Crapnet.Domain.Rules;
using Xunit;

namespace Crapnet.Application.Tests;

public class SessionOrchestratorTests
{
    private sealed class Fixture : IDisposable
    {
        public FakeHotspotController Hotspot { get; } = new();
        public FakeSharingController Sharing { get; } = new();
        public FakeAdapterProvider Adapters { get; } = new();
        public FakeEngine Engine { get; } = new();
        public FakePrivilegeProbe Privileges { get; } = new();
        public FakeDriverProbe Driver { get; } = new();
        public SessionOrchestrator Orchestrator { get; }

        public Fixture()
            => Orchestrator = new SessionOrchestrator(Hotspot, Sharing, Adapters, Engine, Privileges, Driver);

        public void Dispose() => Orchestrator.Dispose();
    }

    private static StartRequest Request(string? uplink = "eth0", Profile? profile = null) => new()
    {
        Hotspot = HotspotConfiguration.Default with { UplinkAdapterId = uplink },
        Profile = profile ?? BuiltInProfiles.Clean,
    };

    [Fact]
    public async Task StartsTheHotspotThenTheEngine()
    {
        using var fixture = new Fixture();

        var result = await fixture.Orchestrator.StartAsync(Request());

        Assert.True(result.Success);
        Assert.Equal(1, fixture.Hotspot.StartCount);
        Assert.Equal(1, fixture.Engine.StartCount);
    }

    [Fact]
    public async Task RefusesToStartWithoutElevation()
    {
        using var fixture = new Fixture();
        fixture.Privileges.IsElevated = false;

        var result = await fixture.Orchestrator.StartAsync(Request());

        Assert.False(result.Success);
        Assert.Contains("administrator", result.Error);

        // Nothing may be brought up when we already know the session cannot work.
        Assert.Equal(0, fixture.Hotspot.StartCount);
        Assert.Equal(0, fixture.Engine.StartCount);
    }

    [Fact]
    public async Task RefusesToStartWithoutTheCaptureDriver()
    {
        using var fixture = new Fixture();
        fixture.Driver.Problem = "capture driver missing";

        var result = await fixture.Orchestrator.StartAsync(Request());

        Assert.False(result.Success);
        Assert.Contains("capture driver", result.Error);
    }

    [Fact]
    public async Task ImpairsAnAlreadyRunningHotspotWithoutPreflightingTheDriverTwice()
    {
        using var fixture = new Fixture();
        fixture.Privileges.IsElevated = false;

        // Engine off means no driver and no elevation are needed, so this must still come up.
        var result = await fixture.Orchestrator.StartAsync(Request() with { StartEngine = false });

        Assert.True(result.Success);
        Assert.Equal(1, fixture.Hotspot.StartCount);
        Assert.Equal(0, fixture.Engine.StartCount);
    }

    [Fact]
    public async Task RejectsAnInvalidHotspotConfiguration()
    {
        using var fixture = new Fixture();
        var request = Request() with { Hotspot = HotspotConfiguration.Default with { Passphrase = "short" } };

        var result = await fixture.Orchestrator.StartAsync(request);

        Assert.False(result.Success);
        Assert.Equal(0, fixture.Hotspot.StartCount);
    }

    [Fact]
    public async Task StopsTheHotspotWhenTheEngineFailsToStart()
    {
        using var fixture = new Fixture();
        fixture.Engine.StartFailure = new InvalidOperationException("driver refused");

        var result = await fixture.Orchestrator.StartAsync(Request());

        // A half-started session is worse than none: the phone associates and has no route out.
        Assert.False(result.Success);
        Assert.Equal(1, fixture.Hotspot.StartCount);
        Assert.Equal(1, fixture.Hotspot.StopCount);
    }

    [Fact]
    public async Task StopsTheHotspotWhenSharingFails()
    {
        using var fixture = new Fixture();
        fixture.Hotspot.SharesUplinkAutomatically = false;
        fixture.Sharing.EnableFailure = new InvalidOperationException("ICS refused");

        var result = await fixture.Orchestrator.StartAsync(Request());

        Assert.False(result.Success);
        Assert.Equal(1, fixture.Hotspot.StopCount);
        Assert.Equal(0, fixture.Engine.StartCount);
    }

    [Fact]
    public async Task SkipsSharingWhenTheHotspotAlreadyDoesIt()
    {
        using var fixture = new Fixture();
        fixture.Hotspot.SharesUplinkAutomatically = true;

        await fixture.Orchestrator.StartAsync(Request());

        // Mobile Hotspot already shares the profile it was created from; driving ICS on top of
        // that is redundant and can break it.
        Assert.Equal(0, fixture.Sharing.EnableCount);
    }

    [Fact]
    public async Task FallsBackToSharingWhenTheHotspotDoesNot()
    {
        using var fixture = new Fixture();
        fixture.Hotspot.SharesUplinkAutomatically = false;

        await fixture.Orchestrator.StartAsync(Request());

        Assert.Equal(1, fixture.Sharing.EnableCount);
        Assert.Equal("eth0", fixture.Sharing.PublicAdapterId);
        Assert.Equal("hotspot-adapter", fixture.Sharing.PrivateAdapterId);
    }

    [Fact]
    public async Task ComplainsWhenSharingIsNeededButNoUplinkWasPicked()
    {
        using var fixture = new Fixture();
        fixture.Hotspot.SharesUplinkAutomatically = false;

        var result = await fixture.Orchestrator.StartAsync(Request(uplink: null));

        Assert.False(result.Success);
        Assert.Contains("adapter", result.Error);
    }

    [Fact]
    public async Task LeavesSharingAloneWhenItWasNotOursToEnable()
    {
        using var fixture = new Fixture();
        fixture.Hotspot.SharesUplinkAutomatically = true;

        await fixture.Orchestrator.StartAsync(Request());
        await fixture.Orchestrator.StopAsync();

        // The user may have had sharing configured before we arrived.
        Assert.Equal(0, fixture.Sharing.DisableCount);
    }

    [Fact]
    public async Task TearsDownSharingItEnabledItself()
    {
        using var fixture = new Fixture();
        fixture.Hotspot.SharesUplinkAutomatically = false;

        await fixture.Orchestrator.StartAsync(Request());
        await fixture.Orchestrator.StopAsync();

        Assert.Equal(1, fixture.Sharing.DisableCount);
    }

    [Fact]
    public async Task StopBringsEverythingDown()
    {
        using var fixture = new Fixture();
        await fixture.Orchestrator.StartAsync(Request());

        await fixture.Orchestrator.StopAsync();

        Assert.Equal(1, fixture.Engine.StopCount);
        Assert.Equal(1, fixture.Hotspot.StopCount);
    }

    [Fact]
    public async Task PrefersTheHotspotAdapterSubnetOverTheConfiguredOne()
    {
        using var fixture = new Fixture();
        var live = Ipv4Subnet.Parse("10.42.0.0/24");
        fixture.Adapters.Subnets["hotspot-adapter"] = live;

        await fixture.Orchestrator.StartAsync(Request());

        // Windows does not always hand out the documented 192.168.137.0/24, and getting this
        // wrong would invert every uplink/downlink classification.
        Assert.Equal(live, fixture.Engine.LastOptions!.DeviceSubnet);
    }

    [Fact]
    public async Task FallsBackToTheConfiguredSubnetWhenTheAdapterHasNoAddress()
    {
        using var fixture = new Fixture();

        await fixture.Orchestrator.StartAsync(Request());

        Assert.Equal(Ipv4Subnet.IcsDefault, fixture.Engine.LastOptions!.DeviceSubnet);
    }

    [Fact]
    public async Task PassesTheUplinkThroughOnTheHotspotConfiguration()
    {
        using var fixture = new Fixture();

        await fixture.Orchestrator.StartAsync(Request(uplink: "eth7"));

        Assert.Equal("eth7", fixture.Hotspot.LastConfiguration!.UplinkAdapterId);
    }

    [Fact]
    public void ApplyProfileReachesTheEngine()
    {
        using var fixture = new Fixture();

        fixture.Orchestrator.ApplyProfile(BuiltInProfiles.DeadAir);

        Assert.Equal("Dead air", fixture.Engine.LastAppliedProfile!.Name);
    }

    [Fact]
    public void BypassIsReportedInTheStatusItPublishes()
    {
        using var fixture = new Fixture();
        SessionStatus? published = null;
        fixture.Orchestrator.StatusChanged += (_, status) => published = status;

        fixture.Orchestrator.Bypass = true;

        Assert.True(fixture.Engine.Bypass);
        Assert.True(published?.Bypass);
        Assert.True(fixture.Orchestrator.Status.Bypass);
    }

    [Fact]
    public void SettingBypassToItsCurrentValuePublishesNothing()
    {
        using var fixture = new Fixture();
        var events = 0;
        fixture.Orchestrator.StatusChanged += (_, _) => events++;

        fixture.Orchestrator.Bypass = false;

        Assert.Equal(0, events);
    }

    [Fact]
    public void AnEngineFaultSurfacesInTheStatus()
    {
        using var fixture = new Fixture();

        fixture.Engine.RaiseFault("the capture died");

        Assert.Equal(EngineState.Faulted, fixture.Orchestrator.Status.Engine);
        Assert.Equal("the capture died", fixture.Orchestrator.Status.Message);
    }

    [Fact]
    public void PreflightReportsEveryBlockerAtOnce()
    {
        using var fixture = new Fixture();
        fixture.Privileges.IsElevated = false;
        fixture.Driver.Problem = "capture driver missing";

        Assert.Equal(2, fixture.Orchestrator.Preflight().Count);
    }

    [Fact]
    public void PreflightIsSilentWhenEverythingIsInPlace()
    {
        using var fixture = new Fixture();

        Assert.Empty(fixture.Orchestrator.Preflight());
    }
}
