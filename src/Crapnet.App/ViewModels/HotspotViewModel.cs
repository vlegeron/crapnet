using System.Collections.ObjectModel;
using Crapnet.Application.Abstractions;
using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Networking;

namespace Crapnet.App.ViewModels;

/// <summary>A radio band as the header's drop-down shows it.</summary>
public sealed record BandOption(HotspotBand Value, string Label);

/// <summary>A capture scope as the header's picker shows it.</summary>
public sealed record ScopeOption(CaptureScope Value, string Label);

/// <summary>
/// The access point half of the header: name, password, band, uplink and capture scope.
/// </summary>
/// <remarks>
/// Held apart from the rule editor because these settings are what a session is started with,
/// whereas rules can be rewritten underneath a session that is already running.
/// </remarks>
public sealed class HotspotViewModel : EditableViewModel
{
    public static readonly IReadOnlyList<BandOption> Bands =
    [
        new(HotspotBand.Auto, "auto"),
        new(HotspotBand.TwoPointFourGigahertz, "2.4 GHz"),
        new(HotspotBand.FiveGigahertz, "5 GHz"),
    ];

    /// <summary>Forwarded traffic is the tethered device; local is the Windows host itself.</summary>
    public static readonly IReadOnlyList<ScopeOption> Scopes =
    [
        new(CaptureScope.Forwarded, "FWD"),
        new(CaptureScope.Local, "HOST"),
        new(CaptureScope.Both, "ALL"),
    ];

    private string _ssid = HotspotConfiguration.Default.Ssid;
    private string _passphrase = HotspotConfiguration.Default.Passphrase;
    private bool _revealPassphrase;
    private BandOption _band = Bands[0];
    private ScopeOption _scope = Scopes[0];
    private NetworkAdapter? _uplink;
    private Ipv4Subnet _subnet = Ipv4Subnet.IcsDefault;

    public ObservableCollection<NetworkAdapter> Adapters { get; } = [];

    public IReadOnlyList<BandOption> BandOptions => Bands;

    public IReadOnlyList<ScopeOption> ScopeOptions => Scopes;

    public string Ssid
    {
        get => _ssid;
        set => SetProperty(ref _ssid, value);
    }

    public string Passphrase
    {
        get => _passphrase;
        set => SetProperty(ref _passphrase, value);
    }

    /// <summary>Shows the password in clear, for reading it out to whoever holds the device.</summary>
    public bool RevealPassphrase
    {
        get => _revealPassphrase;
        set => SetProperty(ref _revealPassphrase, value);
    }

    public BandOption Band
    {
        get => _band;
        set => SetProperty(ref _band, value);
    }

    public ScopeOption Scope
    {
        get => _scope;
        set => SetProperty(ref _scope, value);
    }

    /// <summary>The adapter whose internet connection is shared with the hotspot.</summary>
    public NetworkAdapter? Uplink
    {
        get => _uplink;
        set => SetProperty(ref _uplink, value);
    }

    /// <summary>
    /// The subnet handed to tethered clients. Not editable here: it comes from whatever Internet
    /// Connection Sharing is actually using, and is carried through so a saved session restores it.
    /// </summary>
    public Ipv4Subnet Subnet
    {
        get => _subnet;
        set => SetProperty(ref _subnet, value);
    }

    /// <summary>First complaint from the domain's own validation, or empty when the settings are fine.</summary>
    public string ValidationMessage => ToConfiguration().Validate().FirstOrDefault() ?? string.Empty;

    public bool IsValid => ValidationMessage.Length == 0;

    public HotspotConfiguration ToConfiguration() => new()
    {
        Ssid = Ssid,
        Passphrase = Passphrase,
        Band = Band.Value,
        UplinkAdapterId = Uplink?.Id,
        Subnet = Subnet,
    };

    public void LoadFrom(AppSettings settings)
    {
        Ssid = settings.Hotspot.Ssid;
        Passphrase = settings.Hotspot.Passphrase;
        Band = Bands.FirstOrDefault(option => option.Value == settings.Hotspot.Band) ?? Bands[0];
        Scope = Scopes.FirstOrDefault(option => option.Value == settings.CaptureScope) ?? Scopes[0];
        Subnet = settings.Hotspot.Subnet;

        var adapterId = settings.UplinkAdapterId ?? settings.Hotspot.UplinkAdapterId;
        if (adapterId is not null)
        {
            Uplink = Adapters.FirstOrDefault(adapter => adapter.Id == adapterId) ?? Uplink;
        }
    }

    public AppSettings ToSettings(AppSettings current) => current with
    {
        Hotspot = ToConfiguration(),
        UplinkAdapterId = Uplink?.Id,
        CaptureScope = Scope.Value,
    };

    /// <summary>
    /// Replaces the adapter list, keeping the current choice if it survived and otherwise falling
    /// back to the first wired adapter that has a route out.
    /// </summary>
    public void SetAdapters(IReadOnlyList<NetworkAdapter> adapters)
    {
        var previousId = Uplink?.Id;

        Adapters.Clear();
        foreach (var adapter in adapters) Adapters.Add(adapter);

        Uplink = Adapters.FirstOrDefault(adapter => adapter.Id == previousId)
                 ?? Adapters.FirstOrDefault(adapter => adapter is { HasInternet: true, IsWireless: false })
                 ?? Adapters.FirstOrDefault(adapter => adapter.HasInternet)
                 ?? Adapters.FirstOrDefault();
    }

    protected override void OnEdited()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(IsValid));
    }
}
