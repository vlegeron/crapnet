namespace Crapnet.Domain.Impairments;

/// <summary>
/// The full set of impairments attached to a rule. Each one toggles on its own, so a rule can
/// combine, say, 200 ms of lag with 2% loss while everything else stays inert.
/// </summary>
public sealed record ImpairmentSet
{
    public static readonly ImpairmentSet None = new();

    public BlockSetting Block { get; init; } = BlockSetting.Off;
    public DropSetting Drop { get; init; } = DropSetting.Off;
    public ResetSetting Reset { get; init; } = ResetSetting.Off;
    public TamperSetting Tamper { get; init; } = TamperSetting.Off;
    public DuplicateSetting Duplicate { get; init; } = DuplicateSetting.Off;
    public BandwidthSetting Bandwidth { get; init; } = BandwidthSetting.Off;
    public ThrottleSetting Throttle { get; init; } = ThrottleSetting.Off;
    public ReorderSetting Reorder { get; init; } = ReorderSetting.Off;
    public LagSetting Lag { get; init; } = LagSetting.Off;

    /// <summary>Every setting, in the order the pipeline applies them.</summary>
    public IEnumerable<IImpairmentSetting> All
    {
        get
        {
            yield return Block;
            yield return Drop;
            yield return Reset;
            yield return Tamper;
            yield return Duplicate;
            yield return Bandwidth;
            yield return Throttle;
            yield return Reorder;
            yield return Lag;
        }
    }

    public IEnumerable<IImpairmentSetting> Active => All.Where(setting => setting.Enabled);

    public bool AnyEnabled => All.Any(setting => setting.Enabled);

    public bool IsEnabled(ImpairmentKind kind) => Get(kind).Enabled;

    public IImpairmentSetting Get(ImpairmentKind kind) => kind switch
    {
        ImpairmentKind.Block => Block,
        ImpairmentKind.Drop => Drop,
        ImpairmentKind.Reset => Reset,
        ImpairmentKind.Tamper => Tamper,
        ImpairmentKind.Duplicate => Duplicate,
        ImpairmentKind.Bandwidth => Bandwidth,
        ImpairmentKind.Throttle => Throttle,
        ImpairmentKind.Reorder => Reorder,
        ImpairmentKind.Lag => Lag,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown impairment."),
    };

    /// <summary>Returns a copy with one impairment switched on or off, leaving its parameters intact.</summary>
    public ImpairmentSet WithEnabled(ImpairmentKind kind, bool enabled) => kind switch
    {
        ImpairmentKind.Block => this with { Block = Block with { Enabled = enabled } },
        ImpairmentKind.Drop => this with { Drop = Drop with { Enabled = enabled } },
        ImpairmentKind.Reset => this with { Reset = Reset with { Enabled = enabled } },
        ImpairmentKind.Tamper => this with { Tamper = Tamper with { Enabled = enabled } },
        ImpairmentKind.Duplicate => this with { Duplicate = Duplicate with { Enabled = enabled } },
        ImpairmentKind.Bandwidth => this with { Bandwidth = Bandwidth with { Enabled = enabled } },
        ImpairmentKind.Throttle => this with { Throttle = Throttle with { Enabled = enabled } },
        ImpairmentKind.Reorder => this with { Reorder = Reorder with { Enabled = enabled } },
        ImpairmentKind.Lag => this with { Lag = Lag with { Enabled = enabled } },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown impairment."),
    };

    /// <summary>True when the set can only ever discard traffic, so there is nothing to schedule.</summary>
    public bool IsPureDiscard
        => Block.Enabled && !Lag.Enabled && !Throttle.Enabled && !Reorder.Enabled && !Bandwidth.Enabled;
}
