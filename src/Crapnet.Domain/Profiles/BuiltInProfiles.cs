using Crapnet.Domain.Impairments;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Rules;

namespace Crapnet.Domain.Profiles;

/// <summary>
/// Ready-made scenarios covering the conditions an Android app is most often asked to survive.
/// </summary>
/// <remarks>
/// These are starting points rather than calibrated models of real radios. Load one, then adjust.
/// Because the first matching rule wins, a rule with no impairments enabled acts as an explicit
/// "leave this alone" exception when placed above a broader rule.
/// </remarks>
public static class BuiltInProfiles
{
    public static Profile Clean { get; } = new()
    {
        Name = "Clean",
        Description = "Pass everything through untouched. Use it as a baseline.",
        Rules =
        [
            Rule.Create("Everything") with { Impairments = ImpairmentSet.None },
        ],
    };

    public static Profile Edge { get; } = new()
    {
        Name = "2G / EDGE",
        Description = "Narrow pipe, long round trips, occasional loss.",
        Rules =
        [
            Rule.Create("Everything") with
            {
                Impairments = new ImpairmentSet
                {
                    Bandwidth = new BandwidthSetting(enabled: true, kilobitsPerSecond: 200),
                    Lag = new LagSetting(enabled: true, delayMilliseconds: 350, jitterMilliseconds: 150),
                    Drop = new DropSetting(enabled: true, chance: 1d),
                },
            },
        ],
    };

    public static Profile ThreeG { get; } = new()
    {
        Name = "3G",
        Description = "Usable but sluggish mobile data.",
        Rules =
        [
            Rule.Create("Everything") with
            {
                Impairments = new ImpairmentSet
                {
                    Bandwidth = new BandwidthSetting(enabled: true, kilobitsPerSecond: 1500),
                    Lag = new LagSetting(enabled: true, delayMilliseconds: 150, jitterMilliseconds: 50),
                    Drop = new DropSetting(enabled: true, chance: 0.5d),
                },
            },
        ],
    };

    public static Profile WeakLte { get; } = new()
    {
        Name = "LTE, weak signal",
        Description = "Decent throughput spoiled by loss and jitter at the cell edge.",
        Rules =
        [
            Rule.Create("Everything") with
            {
                Impairments = new ImpairmentSet
                {
                    Bandwidth = new BandwidthSetting(enabled: true, kilobitsPerSecond: 4000),
                    Lag = new LagSetting(enabled: true, delayMilliseconds: 80, jitterMilliseconds: 70),
                    Drop = new DropSetting(enabled: true, chance: 2d),
                },
            },
        ],
    };

    public static Profile LossyWifi { get; } = new()
    {
        Name = "Lossy Wi-Fi",
        Description = "A crowded channel: loss, duplicates and packets arriving out of order.",
        Rules =
        [
            Rule.Create("Everything") with
            {
                Impairments = new ImpairmentSet
                {
                    Drop = new DropSetting(enabled: true, chance: 10d),
                    Duplicate = new DuplicateSetting(enabled: true, chance: 2d),
                    Reorder = new ReorderSetting(enabled: true, chance: 5d, maxDelayMilliseconds: 80),
                    Lag = new LagSetting(enabled: true, delayMilliseconds: 30, jitterMilliseconds: 60),
                },
            },
        ],
    };

    public static Profile Bufferbloat { get; } = new()
    {
        Name = "Bufferbloat",
        Description = "Traffic stalls, then arrives all at once. Hard on timeouts and progress bars.",
        Rules =
        [
            Rule.Create("Everything") with
            {
                Impairments = new ImpairmentSet
                {
                    Bandwidth = new BandwidthSetting(enabled: true, kilobitsPerSecond: 1000),
                    Throttle = new ThrottleSetting(enabled: true, timeframeMilliseconds: 800, chance: 25d),
                },
            },
        ],
    };

    public static Profile Satellite { get; } = new()
    {
        Name = "Satellite",
        Description = "High, steady latency with plenty of bandwidth behind it.",
        Rules =
        [
            Rule.Create("Everything") with
            {
                Impairments = new ImpairmentSet
                {
                    Lag = new LagSetting(enabled: true, delayMilliseconds: 600, jitterMilliseconds: 40),
                    Bandwidth = new BandwidthSetting(enabled: true, kilobitsPerSecond: 8000),
                },
            },
        ],
    };

    public static Profile DeadAir { get; } = new()
    {
        Name = "Dead air",
        Description = "Associated to the access point but nothing gets through. The classic hotel Wi-Fi.",
        Rules =
        [
            Rule.Create("Blackhole") with
            {
                Impairments = new ImpairmentSet { Block = new BlockSetting(enabled: true) },
            },
        ],
    };

    public static Profile DnsBlackhole { get; } = new()
    {
        Name = "No DNS",
        Description = "Everything routes, but name resolution never answers.",
        Rules =
        [
            Rule.Create("DNS") with
            {
                Match = new MatchCriteria
                {
                    Protocol = ProtocolFilter.Udp,
                    RemotePorts = PortSelector.Parse("53"),
                },
                Impairments = new ImpairmentSet { Block = new BlockSetting(enabled: true) },
            },
        ],
    };

    public static Profile TlsRefused { get; } = new()
    {
        Name = "TLS refused",
        Description = "Plain HTTP and DNS work; every HTTPS connection is reset.",
        Rules =
        [
            Rule.Create("HTTPS") with
            {
                Match = new MatchCriteria
                {
                    Protocol = ProtocolFilter.Tcp,
                    RemotePorts = PortSelector.Parse("443"),
                },
                Impairments = new ImpairmentSet { Reset = new ResetSetting(enabled: true) },
            },
        ],
    };

    public static Profile Corruption { get; } = new()
    {
        Name = "Corruption",
        Description = "Bytes arrive damaged with the checksum left wrong, so the peer discards them.",
        Rules =
        [
            Rule.Create("Everything") with
            {
                Impairments = new ImpairmentSet
                {
                    Tamper = new TamperSetting(enabled: true, chance: 5d, maxBytes: 8, recalculateChecksum: false),
                },
            },
        ],
    };

    /// <summary>All built-in profiles, in the order the UI should offer them.</summary>
    public static IReadOnlyList<Profile> All { get; } =
    [
        Clean,
        Edge,
        ThreeG,
        WeakLte,
        LossyWifi,
        Bufferbloat,
        Satellite,
        DeadAir,
        DnsBlackhole,
        TlsRefused,
        Corruption,
    ];

    public static Profile? FindByName(string name)
        => All.FirstOrDefault(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));
}
