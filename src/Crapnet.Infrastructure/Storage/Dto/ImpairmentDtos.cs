namespace Crapnet.Infrastructure.Storage.Dto;

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Impairments.ImpairmentSet"/>.</summary>
/// <remarks>
/// A missing member means "off with stock parameters", so a rule that only uses lag writes a
/// handful of lines rather than the whole matrix.
/// </remarks>
internal sealed class ImpairmentSetDto
{
    public BlockDto? Block { get; set; }
    public DropDto? Drop { get; set; }
    public ResetDto? Reset { get; set; }
    public TamperDto? Tamper { get; set; }
    public DuplicateDto? Duplicate { get; set; }
    public BandwidthDto? Bandwidth { get; set; }
    public ThrottleDto? Throttle { get; set; }
    public ReorderDto? Reorder { get; set; }
    public LagDto? Lag { get; set; }
}

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Impairments.BlockSetting"/>.</summary>
internal sealed class BlockDto
{
    public bool? Enabled { get; set; }
}

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Impairments.DropSetting"/>.</summary>
internal sealed class DropDto
{
    public bool? Enabled { get; set; }

    /// <summary>Probability in percent. <c>Percentage</c> itself has no parameterless constructor.</summary>
    public double? Chance { get; set; }
}

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Impairments.ResetSetting"/>.</summary>
internal sealed class ResetDto
{
    public bool? Enabled { get; set; }
    public double? Chance { get; set; }
}

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Impairments.TamperSetting"/>.</summary>
internal sealed class TamperDto
{
    public bool? Enabled { get; set; }
    public double? Chance { get; set; }
    public int? MaxBytes { get; set; }
    public bool? RecalculateChecksum { get; set; }
}

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Impairments.DuplicateSetting"/>.</summary>
internal sealed class DuplicateDto
{
    public bool? Enabled { get; set; }
    public double? Chance { get; set; }
    public int? Copies { get; set; }
}

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Impairments.BandwidthSetting"/>.</summary>
internal sealed class BandwidthDto
{
    public bool? Enabled { get; set; }
    public int? KilobitsPerSecond { get; set; }
    public int? BurstKilobits { get; set; }
}

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Impairments.ThrottleSetting"/>.</summary>
internal sealed class ThrottleDto
{
    public bool? Enabled { get; set; }
    public int? TimeframeMilliseconds { get; set; }
    public double? Chance { get; set; }
    public bool? DropThrottled { get; set; }
}

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Impairments.ReorderSetting"/>.</summary>
internal sealed class ReorderDto
{
    public bool? Enabled { get; set; }
    public double? Chance { get; set; }
    public int? MaxDelayMilliseconds { get; set; }
}

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Impairments.LagSetting"/>.</summary>
internal sealed class LagDto
{
    public bool? Enabled { get; set; }
    public int? DelayMilliseconds { get; set; }
    public int? JitterMilliseconds { get; set; }
    public double? Chance { get; set; }
}
