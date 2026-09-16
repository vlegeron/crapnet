using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Crapnet.Application.Abstractions;
using Crapnet.Application.Engine;
using Crapnet.Application.UseCases;
using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Profiles;
using Crapnet.Domain.Rules;

namespace Crapnet.App.ViewModels;

/// <summary>A profile as the drop-down lists it. Built-ins cannot be overwritten or deleted.</summary>
public sealed record ProfileEntry(string Name, Profile Profile, bool IsBuiltIn);

/// <summary>
/// The window's whole state: hotspot settings, the rule list being edited, live statistics and
/// the session itself.
/// </summary>
/// <remarks>
/// Edits are pushed into the running engine rather than waiting for an apply button, because
/// watching a change take effect on the device is the entire point of the tool. Every editable
/// piece bubbles a <see cref="EditableViewModel.Changed"/> event here, which restarts a short
/// timer; when it expires the rule list is projected back into a fresh <see cref="Profile"/> and
/// handed to the orchestrator. Dragging a slider therefore produces one apply per pause rather
/// than one per pixel.
/// </remarks>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>Long enough to swallow a slider drag, short enough to feel immediate.</summary>
    private static readonly TimeSpan ApplyDelay = TimeSpan.FromMilliseconds(150);

    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Association changes are rare and the query is comparatively expensive.</summary>
    private static readonly TimeSpan ClientInterval = TimeSpan.FromSeconds(2);

    private readonly ISessionOrchestrator _orchestrator;
    private readonly IProfileStore _store;
    private readonly INetworkAdapterProvider _adapters;

    private readonly DispatcherTimer? _applyTimer;
    private readonly DispatcherTimer? _sampleTimer;
    private readonly DispatcherTimer? _settingsTimer;

    private AppSettings _settings = AppSettings.Default;
    private DateTimeOffset _nextClientPoll = DateTimeOffset.MinValue;
    private bool _loadingProfile;
    private bool _disposed;

    public MainWindowViewModel(ISessionOrchestrator orchestrator, IProfileStore store, INetworkAdapterProvider adapters)
    {
        _orchestrator = orchestrator;
        _store = store;
        _adapters = adapters;

        Hotspot.Changed += OnHotspotEdited;
        _orchestrator.StatusChanged += OnStatusChanged;

        LoadProfile(BuiltInProfiles.Clean);
        ApplyStatus(_orchestrator.Status);

        // The previewer has no dispatcher loop worth driving, and design data is static anyway.
        if (Design.IsDesignMode) return;

        _applyTimer = new DispatcherTimer { Interval = ApplyDelay };
        _applyTimer.Tick += (_, _) => ApplyProfileNow();

        _sampleTimer = new DispatcherTimer { Interval = SampleInterval };
        _sampleTimer.Tick += (_, _) => Sample();
        _sampleTimer.Start();

        _settingsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _settingsTimer.Tick += (_, _) => PersistSettings();
    }

    public HotspotViewModel Hotspot { get; } = new();

    public StatisticsViewModel Stats { get; } = new();

    public ObservableCollection<RuleViewModel> Rules { get; } = [];

    public ObservableCollection<ProfileEntry> Profiles { get; } = [];

    public ObservableCollection<ClientViewModel> Clients { get; } = [];

    /// <summary>Problems that would stop a start from working, e.g. no driver or no elevation.</summary>
    public ObservableCollection<string> PreflightIssues { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveRuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveRuleUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveRuleDownCommand))]
    private RuleViewModel? _selectedRule;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    private ProfileEntry? _selectedProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    private string _profileName = BuiltInProfiles.Clean.Name;

    /// <summary>Last notable event from the orchestrator, shown in the footer.</summary>
    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _hasPreflightIssues;

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isFaulted;

    /// <summary>Short word for the footer's engine chip: ARMED, BYPASS, BUSY, FAULT or IDLE.</summary>
    [ObservableProperty]
    private string _engineText = "IDLE";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClients))]
    private int _clientCount;

    private bool _bypass;

    /// <summary>Pass traffic through untouched without tearing the capture down.</summary>
    public bool Bypass
    {
        get => _bypass;
        set
        {
            if (_bypass == value) return;

            // The orchestrator is the source of truth; its status event is what settles the
            // property, so the two can never disagree.
            _orchestrator.Bypass = value;
            ApplyStatus(_orchestrator.Status);
        }
    }

    public bool HasSelection => SelectedRule is not null;

    public bool HasClients => ClientCount > 0;

    /// <summary>Loads settings, profiles and adapters, then runs preflight.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            Hotspot.SetAdapters(_adapters.GetAdapters());

            _settings = await _store.LoadSettingsAsync().ConfigureAwait(true);
            Hotspot.LoadFrom(_settings);

            await ReloadProfilesAsync(_settings.LastProfileName).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // A missing or corrupt settings file must not stop the tool from opening.
            Message = ex.Message;
        }

        RunPreflight();
    }

    /// <summary>Narrows the selected rule to one tethered client, from the client flyout.</summary>
    public void UseClient(ClientViewModel client)
    {
        if (!client.HasAddress) return;

        SelectedRule?.Match.RestrictToDevice(client.Address);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _applyTimer?.Stop();
        _sampleTimer?.Stop();
        _settingsTimer?.Stop();

        _orchestrator.StatusChanged -= OnStatusChanged;
        Hotspot.Changed -= OnHotspotEdited;

        foreach (var rule in Rules) rule.Changed -= OnRuleEdited;
    }

    /// <summary>Writes settings out now, for the window's closing handler.</summary>
    public Task FlushSettingsAsync()
    {
        _settingsTimer?.Stop();
        return _store.SaveSettingsAsync(CurrentSettings());
    }

    [RelayCommand]
    private async Task ToggleSessionAsync()
    {
        if (_orchestrator.Status.IsBusy) return;

        if (_orchestrator.Status.IsLive)
        {
            await _orchestrator.StopAsync().ConfigureAwait(true);
            Stats.Reset();
            Clients.Clear();
            ClientCount = 0;
            return;
        }

        if (!Hotspot.IsValid)
        {
            Message = Hotspot.ValidationMessage;
            return;
        }

        var request = new StartRequest
        {
            Hotspot = Hotspot.ToConfiguration(),
            Profile = BuildProfile(),
            Scope = Hotspot.Scope.Value,
        };

        var result = await _orchestrator.StartAsync(request).ConfigureAwait(true);
        if (!result.Success) Message = result.Error;

        PersistSettings();
    }

    [RelayCommand]
    private void AddRule()
    {
        var rule = Rule.Create($"Rule {Rules.Count + 1}");
        var viewModel = Attach(new RuleViewModel(rule));

        var index = SelectedRule is null ? Rules.Count : Rules.IndexOf(SelectedRule) + 1;
        Rules.Insert(index, viewModel);
        SelectedRule = viewModel;
        ScheduleApply();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveRule()
    {
        if (SelectedRule is not { } rule) return;

        var index = Rules.IndexOf(rule);
        rule.Changed -= OnRuleEdited;
        Rules.Remove(rule);

        SelectedRule = Rules.Count == 0 ? null : Rules[Math.Clamp(index, 0, Rules.Count - 1)];
        ScheduleApply();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveRuleUp() => MoveRule(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveRuleDown() => MoveRule(1);

    [RelayCommand(CanExecute = nameof(CanSaveProfile))]
    private async Task SaveProfileAsync()
    {
        var profile = BuildProfile();

        try
        {
            await _store.SaveProfileAsync(profile).ConfigureAwait(true);
            await ReloadProfilesAsync(profile.Name).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { IsBuiltIn: false } entry) return;

        try
        {
            await _store.DeleteProfileAsync(entry.Name).ConfigureAwait(true);
            await ReloadProfilesAsync(BuiltInProfiles.Clean.Name).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand]
    private void DismissPreflight() => HasPreflightIssues = false;

    [RelayCommand]
    private void ClearMessage() => Message = null;

    // Saving over a built-in name would leave two entries with the same label in the drop-down and
    // no way to tell which one the picker would load.
    private bool CanSaveProfile
        => !string.IsNullOrWhiteSpace(ProfileName) && BuiltInProfiles.FindByName(ProfileName.Trim()) is null;

    private bool CanDeleteProfile => SelectedProfile is { IsBuiltIn: false };

    partial void OnSelectedProfileChanged(ProfileEntry? value)
    {
        if (value is null || _loadingProfile) return;

        LoadProfile(value.Profile);
        ScheduleApply();
        PersistSettings();
    }

    private void MoveRule(int offset)
    {
        if (SelectedRule is not { } rule) return;

        var index = Rules.IndexOf(rule);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Rules.Count) return;

        Rules.Move(index, target);
        ScheduleApply();
    }

    /// <summary>Replaces the rule list with a profile's, keeping the editor pointed at something.</summary>
    private void LoadProfile(Profile profile)
    {
        foreach (var existing in Rules) existing.Changed -= OnRuleEdited;
        Rules.Clear();

        foreach (var rule in profile.Rules) Rules.Add(Attach(new RuleViewModel(rule)));

        ProfileName = profile.Name;
        SelectedRule = Rules.FirstOrDefault();
    }

    private RuleViewModel Attach(RuleViewModel rule)
    {
        rule.Changed += OnRuleEdited;
        return rule;
    }

    private async Task ReloadProfilesAsync(string? preferredName)
    {
        var stored = await _store.LoadProfilesAsync().ConfigureAwait(true);

        Profiles.Clear();
        foreach (var profile in BuiltInProfiles.All) Profiles.Add(new ProfileEntry(profile.Name, profile, true));
        foreach (var profile in stored) Profiles.Add(new ProfileEntry(profile.Name, profile, false));

        var chosen = Profiles.FirstOrDefault(entry => entry.Name == preferredName) ?? Profiles.FirstOrDefault();
        if (chosen is null) return;

        _loadingProfile = true;
        SelectedProfile = chosen;
        _loadingProfile = false;

        LoadProfile(chosen.Profile);
    }

    private Profile BuildProfile() => new()
    {
        Name = string.IsNullOrWhiteSpace(ProfileName) ? "Untitled" : ProfileName.Trim(),
        Description = SelectedProfile?.Profile.Description ?? string.Empty,
        Rules = Rules.Select(rule => rule.ToRule()).ToList(),
    };

    private void OnRuleEdited(object? sender, EventArgs e) => ScheduleApply();

    private void OnHotspotEdited(object? sender, EventArgs e)
    {
        _settingsTimer?.Stop();
        _settingsTimer?.Start();
    }

    private void ScheduleApply()
    {
        if (_applyTimer is null)
        {
            ApplyProfileNow();
            return;
        }

        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private void ApplyProfileNow()
    {
        _applyTimer?.Stop();

        // A rule with an unparseable address would silently match nothing. Leave the engine on the
        // last coherent rule set until the field is valid again.
        if (Rules.Any(rule => !rule.IsValid)) return;

        _orchestrator.ApplyProfile(BuildProfile());
    }

    private void Sample()
    {
        Stats.Update(_orchestrator.Snapshot());

        if (!IsLive || DateTimeOffset.UtcNow < _nextClientPoll) return;

        _nextClientPoll = DateTimeOffset.UtcNow + ClientInterval;
        _ = RefreshClientsAsync();
    }

    private async Task RefreshClientsAsync()
    {
        IReadOnlyList<TetheredClient> clients;
        try
        {
            clients = await _orchestrator.GetClientsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            return;
        }

        Clients.Clear();
        foreach (var client in clients) Clients.Add(new ClientViewModel(client, UseClient));
        ClientCount = Clients.Count;
    }

    private void RunPreflight()
    {
        PreflightIssues.Clear();
        foreach (var issue in _orchestrator.Preflight()) PreflightIssues.Add(issue);
        HasPreflightIssues = PreflightIssues.Count > 0;
    }

    private void OnStatusChanged(object? sender, SessionStatus status)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyStatus(status);
            return;
        }

        // The orchestrator raises this from whichever thread finished the work.
        Dispatcher.UIThread.Post(() => ApplyStatus(status));
    }

    private void ApplyStatus(SessionStatus status)
    {
        SetProperty(ref _bypass, status.Bypass, nameof(Bypass));

        IsLive = status.IsLive;
        IsBusy = status.IsBusy;
        IsFaulted = status.Engine == EngineState.Faulted || status.Hotspot == HotspotState.Failed;

        EngineText = status switch
        {
            { IsBusy: true } => "BUSY",
            { Engine: EngineState.Faulted } => "FAULT",
            { Engine: EngineState.Running } => status.Bypass ? "BYPASS" : "ARMED",
            _ => "IDLE",
        };

        if (status.Message is { Length: > 0 }) Message = status.Message;
        if (!status.IsLive) ClientCount = 0;
    }

    private AppSettings CurrentSettings()
    {
        _settings = Hotspot.ToSettings(_settings) with { LastProfileName = SelectedProfile?.Name ?? ProfileName };
        return _settings;
    }

    private void PersistSettings()
    {
        _settingsTimer?.Stop();

        // Fire and forget: losing a preference write is not worth blocking the UI thread over.
        _ = _store.SaveSettingsAsync(CurrentSettings());
    }
}
