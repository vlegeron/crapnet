using Crapnet.Domain.Impairments;

namespace Crapnet.App.ViewModels;

/// <summary>Blackhole: no parameters, just the switch.</summary>
public sealed class BlockCardViewModel : ImpairmentCardViewModel
{
    public BlockCardViewModel(BlockSetting setting)
        : base(setting, "BLOCK", "⊘")
        => Initialise();

    public override ImpairmentSet ApplyTo(ImpairmentSet set)
        => set with { Block = new BlockSetting(IsEnabled) };
}

/// <summary>Random loss.</summary>
public sealed class DropCardViewModel : ImpairmentCardViewModel
{
    public DropCardViewModel(DropSetting setting)
        : base(setting, "DROP", "✕")
    {
        Chance = Percent(setting.Chance);
        Initialise(Chance);
    }

    public NumericField Chance { get; }

    public override ImpairmentSet ApplyTo(ImpairmentSet set)
        => set with { Drop = new DropSetting(IsEnabled, Chance.Value) };

    internal static NumericField Percent(Percentage value) => new(value.Value, 0d, 100d, "0.##") { Step = 0.5d };
}

/// <summary>TCP connections refused rather than forwarded.</summary>
public sealed class ResetCardViewModel : ImpairmentCardViewModel
{
    public ResetCardViewModel(ResetSetting setting)
        : base(setting, "RESET", "↯")
    {
        Chance = DropCardViewModel.Percent(setting.Chance);
        Initialise(Chance);
    }

    public NumericField Chance { get; }

    public override ImpairmentSet ApplyTo(ImpairmentSet set)
        => set with { Reset = new ResetSetting(IsEnabled, Chance.Value) };
}

/// <summary>Payload corruption, with or without a repaired checksum.</summary>
public sealed class TamperCardViewModel : ImpairmentCardViewModel
{
    private bool _fixChecksum;

    public TamperCardViewModel(TamperSetting setting)
        : base(setting, "TAMPER", "≠")
    {
        Chance = DropCardViewModel.Percent(setting.Chance);
        MaxBytes = new NumericField(setting.MaxBytes, 1d, 1500d, "0");
        _fixChecksum = setting.RecalculateChecksum;
        Initialise(Chance, MaxBytes);
    }

    public NumericField Chance { get; }

    public NumericField MaxBytes { get; }

    /// <summary>Off leaves the checksum wrong, so the far end discards the packet itself.</summary>
    public bool FixChecksum
    {
        get => _fixChecksum;
        set => SetProperty(ref _fixChecksum, value);
    }

    public override ImpairmentSet ApplyTo(ImpairmentSet set)
        => set with { Tamper = new TamperSetting(IsEnabled, Chance.Value, MaxBytes.IntValue, FixChecksum) };
}

/// <summary>Echoed packets.</summary>
public sealed class DuplicateCardViewModel : ImpairmentCardViewModel
{
    public DuplicateCardViewModel(DuplicateSetting setting)
        : base(setting, "DUP", "⧉")
    {
        Chance = DropCardViewModel.Percent(setting.Chance);
        Copies = new NumericField(setting.Copies, 1d, 32d, "0");
        Initialise(Chance, Copies);
    }

    public NumericField Chance { get; }

    public NumericField Copies { get; }

    public override ImpairmentSet ApplyTo(ImpairmentSet set)
        => set with { Duplicate = new DuplicateSetting(IsEnabled, Chance.Value, Copies.IntValue) };
}

/// <summary>Sustained throughput cap.</summary>
public sealed class BandwidthCardViewModel : ImpairmentCardViewModel
{
    public BandwidthCardViewModel(BandwidthSetting setting)
        : base(setting, "BANDWIDTH", "▤")
    {
        Rate = new NumericField(setting.KilobitsPerSecond, 1d, 1_000_000d, "0");
        Initialise(Rate);
    }

    public NumericField Rate { get; }

    public override ImpairmentSet ApplyTo(ImpairmentSet set)
        => set with { Bandwidth = new BandwidthSetting(IsEnabled, Rate.IntValue) };
}

/// <summary>Stall-then-burst behaviour.</summary>
public sealed class ThrottleCardViewModel : ImpairmentCardViewModel
{
    private bool _dropThrottled;

    public ThrottleCardViewModel(ThrottleSetting setting)
        : base(setting, "THROTTLE", "◷")
    {
        Timeframe = new NumericField(setting.TimeframeMilliseconds, 10d, 60_000d, "0");
        Chance = DropCardViewModel.Percent(setting.Chance);
        _dropThrottled = setting.DropThrottled;
        Initialise(Timeframe, Chance);
    }

    public NumericField Timeframe { get; }

    public NumericField Chance { get; }

    /// <summary>Throw the held burst away instead of releasing it: a buffer that overran.</summary>
    public bool DropThrottled
    {
        get => _dropThrottled;
        set => SetProperty(ref _dropThrottled, value);
    }

    public override ImpairmentSet ApplyTo(ImpairmentSet set)
        => set with { Throttle = new ThrottleSetting(IsEnabled, Timeframe.IntValue, Chance.Value, DropThrottled) };
}

/// <summary>Packets pushed behind the ones that follow them.</summary>
public sealed class ReorderCardViewModel : ImpairmentCardViewModel
{
    public ReorderCardViewModel(ReorderSetting setting)
        : base(setting, "REORDER", "⇄")
    {
        Chance = DropCardViewModel.Percent(setting.Chance);
        MaxDelay = new NumericField(setting.MaxDelayMilliseconds, 1d, 10_000d, "0");
        Initialise(Chance, MaxDelay);
    }

    public NumericField Chance { get; }

    public NumericField MaxDelay { get; }

    public override ImpairmentSet ApplyTo(ImpairmentSet set)
        => set with { Reorder = new ReorderSetting(IsEnabled, Chance.Value, MaxDelay.IntValue) };
}

/// <summary>Added latency, optionally jittered.</summary>
public sealed class LagCardViewModel : ImpairmentCardViewModel
{
    public LagCardViewModel(LagSetting setting)
        : base(setting, "LAG", "⧗")
    {
        Delay = new NumericField(setting.DelayMilliseconds, 0d, 60_000d, "0");
        Jitter = new NumericField(setting.JitterMilliseconds, 0d, 60_000d, "0");
        Chance = DropCardViewModel.Percent(setting.Chance);
        Initialise(Delay, Jitter, Chance);
    }

    public NumericField Delay { get; }

    public NumericField Jitter { get; }

    public NumericField Chance { get; }

    public override ImpairmentSet ApplyTo(ImpairmentSet set)
        => set with { Lag = new LagSetting(IsEnabled, Delay.IntValue, Jitter.IntValue, Chance.Value) };
}
