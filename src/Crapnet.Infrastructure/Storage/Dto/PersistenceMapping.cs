using Crapnet.Application.Abstractions;
using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Impairments;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Rules;

namespace Crapnet.Infrastructure.Storage.Dto;

/// <summary>
/// Translates between the domain records and their on-disk shapes.
/// </summary>
/// <remarks>
/// <para>
/// Reading is deliberately total: every conversion has a defined answer for missing, malformed or
/// out-of-range input, and none of them throw. A profile the user spent an afternoon on should
/// not be lost because one field was hand-edited into nonsense, and a half-written file left by a
/// power cut should degrade to the stock profile rather than wedge the app on start-up.
/// </para>
/// <para>
/// Value objects are persisted through their text form. Selectors keep the expression the user
/// typed, percentages travel as plain numbers, and subnets as CIDR — none of them can be
/// reconstructed by a serialiser, since they have no public parameterless constructor and enforce
/// invariants in their factories.
/// </para>
/// </remarks>
internal static class PersistenceMapping
{
    public static SettingsDto ToDto(AppSettings settings) => new()
    {
        Hotspot = ToDto(settings.Hotspot),
        LastProfileName = settings.LastProfileName,
        CaptureScope = settings.CaptureScope.ToString(),
        DisableSharingOnStop = settings.DisableSharingOnStop,
    };

    public static AppSettings ToDomain(SettingsDto? dto)
    {
        if (dto is null) return AppSettings.Default;

        return new AppSettings
        {
            Hotspot = ToDomain(dto.Hotspot),
            LastProfileName = Blank(dto.LastProfileName),
            CaptureScope = ParseEnum(dto.CaptureScope, AppSettings.Default.CaptureScope),
            DisableSharingOnStop = dto.DisableSharingOnStop ?? AppSettings.Default.DisableSharingOnStop,
        };
    }

    public static HotspotConfigurationDto ToDto(HotspotConfiguration configuration) => new()
    {
        Ssid = configuration.Ssid,
        Passphrase = configuration.Passphrase,
        Band = configuration.Band.ToString(),
        UplinkAdapterId = configuration.UplinkAdapterId,
        Subnet = configuration.Subnet.ToString(),
    };

    public static HotspotConfiguration ToDomain(HotspotConfigurationDto? dto)
    {
        if (dto is null) return HotspotConfiguration.Default;

        return new HotspotConfiguration
        {
            // An empty SSID or a too-short passphrase would be rejected by Windows anyway, so the
            // stock values are a better landing place than a configuration that cannot start.
            Ssid = Blank(dto.Ssid) ?? HotspotConfiguration.Default.Ssid,
            Passphrase = Blank(dto.Passphrase) ?? HotspotConfiguration.Default.Passphrase,
            Band = ParseEnum(dto.Band, HotspotBand.Auto),
            UplinkAdapterId = Blank(dto.UplinkAdapterId),
            Subnet = Ipv4Subnet.TryParse(dto.Subnet, out var subnet) ? subnet : Ipv4Subnet.IcsDefault,
        };
    }

    public static ProfileDto ToDto(Profile profile) => new()
    {
        Name = profile.Name,
        Description = profile.Description,
        Rules = profile.Rules.Select(ToDto).ToList(),
    };

    public static Profile ToDomain(ProfileDto? dto, string fallbackName)
    {
        if (dto is null) return Profile.Empty(fallbackName);

        return new Profile
        {
            Name = Blank(dto.Name) ?? fallbackName,
            Description = dto.Description ?? string.Empty,
            Rules = (dto.Rules ?? []).Select(ToDomain).ToList(),
        };
    }

    public static RuleDto ToDto(Rule rule) => new()
    {
        Id = rule.Id,
        Name = rule.Name,
        Enabled = rule.Enabled,
        Match = ToDto(rule.Match),
        Impairments = ToDto(rule.Impairments),
    };

    public static Rule ToDomain(RuleDto? dto)
    {
        if (dto is null) return Rule.Create("Untitled rule");

        return new Rule
        {
            // A duplicated or absent id would break reordering and in-place edits, both of which
            // address rules by identity, so mint a fresh one rather than trust the file.
            Id = dto.Id is { } id && id != Guid.Empty ? id : Guid.NewGuid(),
            Name = Blank(dto.Name) ?? "Untitled rule",
            Enabled = dto.Enabled ?? true,
            Match = ToDomain(dto.Match),
            Impairments = ToDomain(dto.Impairments),
        };
    }

    public static MatchCriteriaDto ToDto(MatchCriteria criteria) => new()
    {
        Direction = criteria.Direction.ToString(),
        Protocol = criteria.Protocol.ToString(),
        DeviceAddresses = Blank(criteria.DeviceAddresses.Text),
        RemoteAddresses = Blank(criteria.RemoteAddresses.Text),
        DevicePorts = Blank(criteria.DevicePorts.Text),
        RemotePorts = Blank(criteria.RemotePorts.Text),
    };

    public static MatchCriteria ToDomain(MatchCriteriaDto? dto)
    {
        if (dto is null) return MatchCriteria.Any;

        return new MatchCriteria
        {
            Direction = ParseEnum(dto.Direction, DirectionFilter.Both),
            Protocol = ParseEnum(dto.Protocol, ProtocolFilter.Any),
            DeviceAddresses = ParseAddresses(dto.DeviceAddresses),
            RemoteAddresses = ParseAddresses(dto.RemoteAddresses),
            DevicePorts = ParsePorts(dto.DevicePorts),
            RemotePorts = ParsePorts(dto.RemotePorts),
        };
    }

    public static ImpairmentSetDto ToDto(ImpairmentSet set) => new()
    {
        Block = new BlockDto { Enabled = set.Block.Enabled },
        Drop = new DropDto { Enabled = set.Drop.Enabled, Chance = set.Drop.Chance.Value },
        Reset = new ResetDto { Enabled = set.Reset.Enabled, Chance = set.Reset.Chance.Value },
        Tamper = new TamperDto
        {
            Enabled = set.Tamper.Enabled,
            Chance = set.Tamper.Chance.Value,
            MaxBytes = set.Tamper.MaxBytes,
            RecalculateChecksum = set.Tamper.RecalculateChecksum,
        },
        Duplicate = new DuplicateDto
        {
            Enabled = set.Duplicate.Enabled,
            Chance = set.Duplicate.Chance.Value,
            Copies = set.Duplicate.Copies,
        },
        Bandwidth = new BandwidthDto
        {
            Enabled = set.Bandwidth.Enabled,
            KilobitsPerSecond = set.Bandwidth.KilobitsPerSecond,
            BurstKilobits = set.Bandwidth.BurstKilobits,
        },
        Throttle = new ThrottleDto
        {
            Enabled = set.Throttle.Enabled,
            TimeframeMilliseconds = set.Throttle.TimeframeMilliseconds,
            Chance = set.Throttle.Chance.Value,
            DropThrottled = set.Throttle.DropThrottled,
        },
        Reorder = new ReorderDto
        {
            Enabled = set.Reorder.Enabled,
            Chance = set.Reorder.Chance.Value,
            MaxDelayMilliseconds = set.Reorder.MaxDelayMilliseconds,
        },
        Lag = new LagDto
        {
            Enabled = set.Lag.Enabled,
            DelayMilliseconds = set.Lag.DelayMilliseconds,
            JitterMilliseconds = set.Lag.JitterMilliseconds,
            Chance = set.Lag.Chance.Value,
        },
    };

    public static ImpairmentSet ToDomain(ImpairmentSetDto? dto)
    {
        if (dto is null) return ImpairmentSet.None;

        // Every setting is rebuilt through its constructor rather than with object initialisers,
        // because that is where the domain clamps delays, byte counts and copy factors.
        return new ImpairmentSet
        {
            Block = new BlockSetting(dto.Block?.Enabled ?? false),
            Drop = new DropSetting(dto.Drop?.Enabled ?? false, Chance(dto.Drop?.Chance)),
            Reset = new ResetSetting(dto.Reset?.Enabled ?? false, Chance(dto.Reset?.Chance)),
            Tamper = new TamperSetting(
                dto.Tamper?.Enabled ?? false,
                Chance(dto.Tamper?.Chance),
                dto.Tamper?.MaxBytes ?? TamperSetting.Off.MaxBytes,
                dto.Tamper?.RecalculateChecksum ?? true),
            Duplicate = new DuplicateSetting(
                dto.Duplicate?.Enabled ?? false,
                Chance(dto.Duplicate?.Chance),
                dto.Duplicate?.Copies ?? DuplicateSetting.Off.Copies),
            Bandwidth = new BandwidthSetting(
                dto.Bandwidth?.Enabled ?? false,
                dto.Bandwidth?.KilobitsPerSecond ?? BandwidthSetting.Off.KilobitsPerSecond,
                dto.Bandwidth?.BurstKilobits ?? BandwidthSetting.Off.BurstKilobits),
            Throttle = new ThrottleSetting(
                dto.Throttle?.Enabled ?? false,
                dto.Throttle?.TimeframeMilliseconds ?? ThrottleSetting.Off.TimeframeMilliseconds,
                Chance(dto.Throttle?.Chance),
                dto.Throttle?.DropThrottled ?? false),
            Reorder = new ReorderSetting(
                dto.Reorder?.Enabled ?? false,
                Chance(dto.Reorder?.Chance),
                dto.Reorder?.MaxDelayMilliseconds ?? ReorderSetting.Off.MaxDelayMilliseconds),
            Lag = new LagSetting(
                dto.Lag?.Enabled ?? false,
                dto.Lag?.DelayMilliseconds ?? LagSetting.Off.DelayMilliseconds,
                dto.Lag?.JitterMilliseconds ?? LagSetting.Off.JitterMilliseconds,
                Chance(dto.Lag?.Chance)),
        };
    }

    /// <summary>Null means "the setting's own default", which each constructor supplies.</summary>
    private static Percentage? Chance(double? value)
        => value is { } percent && !double.IsNaN(percent) ? new Percentage(percent) : null;

    private static Ipv4Selector ParseAddresses(string? text)
        => Ipv4Selector.TryParse(text, out var selector, out _) ? selector : Ipv4Selector.Any;

    private static PortSelector ParsePorts(string? text)
        => PortSelector.TryParse(text, out var selector, out _) ? selector : PortSelector.Any;

    private static TEnum ParseEnum<TEnum>(string? text, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(text, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : fallback;

    /// <summary>Collapses whitespace-only text to null, so "absent" and "blank" behave alike.</summary>
    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
