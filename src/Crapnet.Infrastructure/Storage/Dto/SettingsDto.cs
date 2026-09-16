namespace Crapnet.Infrastructure.Storage.Dto;

/// <summary>
/// On-disk shape of <see cref="Crapnet.Application.Abstractions.AppSettings"/>.
/// </summary>
/// <remarks>
/// Every member is nullable and settable, which is the whole point of having a DTO: the domain
/// records are immutable, carry <c>required</c> members and expose computed properties that must
/// not be written back. A file that predates a field, or that a user has hand-edited into a
/// partial state, deserialises here without a fight and is repaired during mapping.
/// </remarks>
internal sealed class SettingsDto
{
    public HotspotConfigurationDto? Hotspot { get; set; }
    public string? LastProfileName { get; set; }

    /// <summary>Enums travel as text so that renumbering them cannot rewrite the user's choice.</summary>
    public string? CaptureScope { get; set; }

    public bool? DisableSharingOnStop { get; set; }
}

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Hotspot.HotspotConfiguration"/>.</summary>
internal sealed class HotspotConfigurationDto
{
    public string? Ssid { get; set; }
    public string? Passphrase { get; set; }
    public string? Band { get; set; }
    public string? UplinkAdapterId { get; set; }

    /// <summary>CIDR text, for example <c>192.168.137.0/24</c>.</summary>
    public string? Subnet { get; set; }
}
