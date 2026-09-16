namespace Crapnet.Domain.Hotspot;

/// <summary>Lifecycle of the access point.</summary>
public enum HotspotState
{
    Unknown = 0,
    Unavailable = 1,
    Stopped = 2,
    Starting = 3,
    Running = 4,
    Stopping = 5,
    Failed = 6,
}

/// <summary>Why the platform will or will not let us start tethering.</summary>
public enum HotspotAvailability
{
    Available = 0,
    NoWifiAdapter = 1,
    WifiOff = 2,
    NotSupportedByDriver = 3,
    BlockedByPolicy = 4,

    /// <summary>
    /// There is no connection to share. Distinct from <see cref="Unknown"/> on purpose: this one
    /// means "plug the cable in", which is a thing the user can act on.
    /// </summary>
    NoUplink = 5,

    Unknown = 6,
}

public static class HotspotStateExtensions
{
    public static bool IsBusy(this HotspotState state)
        => state is HotspotState.Starting or HotspotState.Stopping;

    public static bool IsOn(this HotspotState state)
        => state is HotspotState.Running;
}
