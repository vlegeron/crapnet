namespace Crapnet.Domain.Impairments;

/// <summary>Common shape of every impairment's configuration.</summary>
public interface IImpairmentSetting
{
    ImpairmentKind Kind { get; }

    /// <summary>Whether this impairment participates at all. Every impairment toggles independently.</summary>
    bool Enabled { get; }

    /// <summary>A short, human-readable summary for the UI, e.g. "120 ms ±30".</summary>
    string Describe();
}

/// <summary>Drops every matched packet.</summary>
public sealed record BlockSetting : IImpairmentSetting
{
    public static readonly BlockSetting Off = new();

    public BlockSetting(bool enabled = false) => Enabled = enabled;

    public bool Enabled { get; init; }
    public ImpairmentKind Kind => ImpairmentKind.Block;
    public string Describe() => "all traffic";
}

/// <summary>Drops a share of matched packets outright.</summary>
public sealed record DropSetting : IImpairmentSetting
{
    public static readonly DropSetting Off = new();

    public DropSetting(bool enabled = false, Percentage? chance = null)
    {
        Enabled = enabled;
        Chance = chance ?? new Percentage(10d);
    }

    public bool Enabled { get; init; }
    public Percentage Chance { get; init; }
    public ImpairmentKind Kind => ImpairmentKind.Drop;
    public string Describe() => Chance.ToString();
}

/// <summary>Holds matched packets back for a while before letting them through.</summary>
public sealed record LagSetting : IImpairmentSetting
{
    public static readonly LagSetting Off = new();

    public LagSetting(bool enabled = false, int delayMilliseconds = 100, int jitterMilliseconds = 0, Percentage? chance = null)
    {
        Enabled = enabled;
        DelayMilliseconds = Math.Clamp(delayMilliseconds, 0, 60_000);
        JitterMilliseconds = Math.Clamp(jitterMilliseconds, 0, 60_000);
        Chance = chance ?? Percentage.Full;
    }

    public bool Enabled { get; init; }

    /// <summary>Base one-way delay. Round-trip cost is roughly double when both directions are impaired.</summary>
    public int DelayMilliseconds { get; init; }

    /// <summary>Random spread added on top, drawn per packet from [-jitter, +jitter].</summary>
    public int JitterMilliseconds { get; init; }

    /// <summary>Share of matched packets that get delayed; the rest pass straight through.</summary>
    public Percentage Chance { get; init; }

    public ImpairmentKind Kind => ImpairmentKind.Lag;

    public string Describe()
        => JitterMilliseconds > 0
            ? $"{DelayMilliseconds} ms ±{JitterMilliseconds}"
            : $"{DelayMilliseconds} ms";
}

/// <summary>Collects traffic for a window and then releases it at once, imitating a stalling link.</summary>
public sealed record ThrottleSetting : IImpairmentSetting
{
    public static readonly ThrottleSetting Off = new();

    public ThrottleSetting(bool enabled = false, int timeframeMilliseconds = 300, Percentage? chance = null, bool dropThrottled = false)
    {
        Enabled = enabled;
        TimeframeMilliseconds = Math.Clamp(timeframeMilliseconds, 10, 60_000);
        Chance = chance ?? new Percentage(20d);
        DropThrottled = dropThrottled;
    }

    public bool Enabled { get; init; }

    /// <summary>How long a stall lasts once one starts.</summary>
    public int TimeframeMilliseconds { get; init; }

    /// <summary>Chance, evaluated per packet while idle, of starting a new stall.</summary>
    public Percentage Chance { get; init; }

    /// <summary>Discard the held burst instead of releasing it, which models a buffer overrun.</summary>
    public bool DropThrottled { get; init; }

    public ImpairmentKind Kind => ImpairmentKind.Throttle;

    public string Describe()
        => DropThrottled ? $"{TimeframeMilliseconds} ms, dropped" : $"{TimeframeMilliseconds} ms";
}

/// <summary>Re-sends matched packets, imitating a link that echoes frames.</summary>
public sealed record DuplicateSetting : IImpairmentSetting
{
    public static readonly DuplicateSetting Off = new();

    public DuplicateSetting(bool enabled = false, Percentage? chance = null, int copies = 1)
    {
        Enabled = enabled;
        Chance = chance ?? new Percentage(10d);
        Copies = Math.Clamp(copies, 1, 32);
    }

    public bool Enabled { get; init; }
    public Percentage Chance { get; init; }

    /// <summary>Extra copies sent in addition to the original.</summary>
    public int Copies { get; init; }

    public ImpairmentKind Kind => ImpairmentKind.Duplicate;
    public string Describe() => $"{Chance} ×{Copies}";
}

/// <summary>Delays a share of packets just enough to let later ones overtake them.</summary>
public sealed record ReorderSetting : IImpairmentSetting
{
    public static readonly ReorderSetting Off = new();

    public ReorderSetting(bool enabled = false, Percentage? chance = null, int maxDelayMilliseconds = 60)
    {
        Enabled = enabled;
        Chance = chance ?? new Percentage(10d);
        MaxDelayMilliseconds = Math.Clamp(maxDelayMilliseconds, 1, 10_000);
    }

    public bool Enabled { get; init; }
    public Percentage Chance { get; init; }

    /// <summary>Upper bound on how far back a packet can be pushed.</summary>
    public int MaxDelayMilliseconds { get; init; }

    public ImpairmentKind Kind => ImpairmentKind.Reorder;
    public string Describe() => $"{Chance} ≤{MaxDelayMilliseconds} ms";
}

/// <summary>Flips payload bytes, with or without repairing the checksum afterwards.</summary>
public sealed record TamperSetting : IImpairmentSetting
{
    public static readonly TamperSetting Off = new();

    public TamperSetting(bool enabled = false, Percentage? chance = null, int maxBytes = 4, bool recalculateChecksum = true)
    {
        Enabled = enabled;
        Chance = chance ?? new Percentage(5d);
        MaxBytes = Math.Clamp(maxBytes, 1, 1500);
        RecalculateChecksum = recalculateChecksum;
    }

    public bool Enabled { get; init; }
    public Percentage Chance { get; init; }

    /// <summary>Upper bound on how many payload bytes get rewritten per packet.</summary>
    public int MaxBytes { get; init; }

    /// <summary>
    /// Repair checksums after corrupting. Leave off to have the receiver drop the packet instead,
    /// which is what a genuinely noisy radio looks like.
    /// </summary>
    public bool RecalculateChecksum { get; init; }

    public ImpairmentKind Kind => ImpairmentKind.Tamper;
    public string Describe() => RecalculateChecksum ? $"{Chance} ≤{MaxBytes} B" : $"{Chance} ≤{MaxBytes} B, bad sum";
}

/// <summary>Caps sustained throughput using a token bucket.</summary>
public sealed record BandwidthSetting : IImpairmentSetting
{
    public static readonly BandwidthSetting Off = new();

    public BandwidthSetting(bool enabled = false, int kilobitsPerSecond = 1024, int burstKilobits = 0)
    {
        Enabled = enabled;
        KilobitsPerSecond = Math.Clamp(kilobitsPerSecond, 1, 1_000_000);
        BurstKilobits = Math.Clamp(burstKilobits, 0, 1_000_000);
    }

    public bool Enabled { get; init; }
    public int KilobitsPerSecond { get; init; }

    /// <summary>Bucket depth. Zero means "one second of traffic", which suits most tests.</summary>
    public int BurstKilobits { get; init; }

    public int EffectiveBurstKilobits => BurstKilobits > 0 ? BurstKilobits : KilobitsPerSecond;

    public ImpairmentKind Kind => ImpairmentKind.Bandwidth;

    public string Describe()
        => KilobitsPerSecond >= 1000
            ? $"{KilobitsPerSecond / 1000d:0.##} Mbps"
            : $"{KilobitsPerSecond} kbps";
}

/// <summary>Replies to matched TCP packets with a reset rather than forwarding them.</summary>
public sealed record ResetSetting : IImpairmentSetting
{
    public static readonly ResetSetting Off = new();

    public ResetSetting(bool enabled = false, Percentage? chance = null)
    {
        Enabled = enabled;
        Chance = chance ?? new Percentage(100d);
    }

    public bool Enabled { get; init; }
    public Percentage Chance { get; init; }
    public ImpairmentKind Kind => ImpairmentKind.Reset;
    public string Describe() => Chance.ToString();
}
