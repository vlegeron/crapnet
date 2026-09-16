using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Rules;

namespace Crapnet.Application.Abstractions;

/// <summary>User preferences that outlive a session.</summary>
public sealed record AppSettings
{
    public static readonly AppSettings Default = new();

    public HotspotConfiguration Hotspot { get; init; } = HotspotConfiguration.Default;

    public string? LastProfileName { get; init; }

    public CaptureScope CaptureScope { get; init; } = CaptureScope.Forwarded;

    /// <summary>Tear the sharing link down again when the hotspot stops.</summary>
    public bool DisableSharingOnStop { get; init; } = true;
}

/// <summary>Port for persisting profiles and settings.</summary>
public interface IProfileStore
{
    Task<IReadOnlyList<Profile>> LoadProfilesAsync(CancellationToken cancellationToken = default);

    Task SaveProfileAsync(Profile profile, CancellationToken cancellationToken = default);

    Task DeleteProfileAsync(string name, CancellationToken cancellationToken = default);

    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
