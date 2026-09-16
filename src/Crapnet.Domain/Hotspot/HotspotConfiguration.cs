using Crapnet.Domain.Networking;

namespace Crapnet.Domain.Hotspot;

/// <summary>Radio band requested for the access point.</summary>
public enum HotspotBand
{
    Auto = 0,
    TwoPointFourGigahertz = 1,
    FiveGigahertz = 2,
}

/// <summary>Settings for the access point the device under test connects to.</summary>
public sealed record HotspotConfiguration
{
    public const int MinimumPassphraseLength = 8;
    public const int MaximumPassphraseLength = 63;

    public static readonly HotspotConfiguration Default = new()
    {
        Ssid = "crapnet",
        Passphrase = "crapnet1234",
    };

    public required string Ssid { get; init; }
    public required string Passphrase { get; init; }
    public HotspotBand Band { get; init; } = HotspotBand.Auto;

    /// <summary>
    /// The subnet Windows hands out to tethered clients. Crapnet uses it to tell uplink from
    /// downlink, and to scope the packet capture filter.
    /// </summary>
    public Ipv4Subnet Subnet { get; init; } = Ipv4Subnet.IcsDefault;

    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Ssid))
            yield return "Network name cannot be empty.";
        else if (Ssid.Length > 32)
            yield return "Network name cannot exceed 32 characters.";

        if (Passphrase.Length < MinimumPassphraseLength)
            yield return $"Password must be at least {MinimumPassphraseLength} characters.";
        else if (Passphrase.Length > MaximumPassphraseLength)
            yield return $"Password cannot exceed {MaximumPassphraseLength} characters.";
    }

    public bool IsValid => !Validate().Any();
}
