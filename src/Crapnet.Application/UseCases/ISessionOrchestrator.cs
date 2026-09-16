using Crapnet.Application.Abstractions;
using Crapnet.Application.Engine;
using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Rules;

namespace Crapnet.Application.UseCases;

/// <summary>What the caller wants brought up.</summary>
public sealed record StartRequest
{
    public required HotspotConfiguration Hotspot { get; init; }

    /// <summary>Adapter whose internet connection is shared with the hotspot. Null skips sharing.</summary>
    public string? UplinkAdapterId { get; init; }

    public Profile Profile { get; init; } = Profile.Empty();

    public CaptureScope Scope { get; init; } = CaptureScope.Forwarded;

    /// <summary>Leave off to impair an access point that is already running.</summary>
    public bool StartHotspot { get; init; } = true;

    public bool EnableSharing { get; init; } = true;

    /// <summary>Leave off to bring the hotspot up without touching traffic yet.</summary>
    public bool StartEngine { get; init; } = true;
}

public sealed record StartResult(bool Success, string? Error = null)
{
    public static readonly StartResult Ok = new(true);
    public static StartResult Fail(string error) => new(false, error);
}

/// <summary>A single view of everything the UI needs to render the current state.</summary>
public sealed record SessionStatus
{
    public static readonly SessionStatus Idle = new();

    public HotspotState Hotspot { get; init; } = HotspotState.Unknown;
    public EngineState Engine { get; init; } = EngineState.Stopped;
    public bool SharingEnabled { get; init; }

    /// <summary>Last error or notable event, suitable for showing to the user.</summary>
    public string? Message { get; init; }

    public bool IsBusy => Hotspot.IsBusy() || Engine is EngineState.Starting or EngineState.Stopping;
    public bool IsLive => Hotspot.IsOn() || Engine == EngineState.Running;
}

/// <summary>
/// Brings the hotspot, the internet share and the impairment engine up and down as one unit,
/// and unwinds cleanly if a later step fails.
/// </summary>
public interface ISessionOrchestrator : IDisposable
{
    SessionStatus Status { get; }

    event EventHandler<SessionStatus>? StatusChanged;

    /// <summary>Pass traffic through untouched without detaching from the network.</summary>
    bool Bypass { get; set; }

    Task<StartResult> StartAsync(StartRequest request, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies an edited rule set to the running engine straight away.</summary>
    void ApplyProfile(Profile profile);

    EngineStatistics Snapshot();

    Task<IReadOnlyList<TetheredClient>> GetClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>Problems that would stop a start from working, e.g. no driver or no elevation.</summary>
    IReadOnlyList<string> Preflight();
}
