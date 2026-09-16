using Crapnet.Application.Abstractions;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Rules;

namespace Crapnet.Application.Engine;

public enum EngineState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Faulted = 4,
}

/// <summary>What the engine needs in order to attach to the network.</summary>
public sealed record EngineOptions
{
    /// <summary>The hotspot subnet, used both to scope the capture and to classify direction.</summary>
    public required Ipv4Subnet DeviceSubnet { get; init; }

    public Profile Profile { get; init; } = Profile.Empty();

    public CaptureScope Scope { get; init; } = CaptureScope.Forwarded;

    public CaptureFilter ToFilter() => new()
    {
        DeviceSubnet = DeviceSubnet,
        Scope = Scope,
    };
}

/// <summary>A point-in-time read of what the engine has done. Cheap enough to poll for the UI.</summary>
public readonly record struct EngineStatistics
{
    public long PacketsSeen { get; init; }
    public long PacketsMatched { get; init; }
    public long PacketsForwarded { get; init; }
    public long PacketsDropped { get; init; }
    public long PacketsDelayed { get; init; }
    public long PacketsDuplicated { get; init; }
    public long PacketsTampered { get; init; }
    public long PacketsReset { get; init; }
    public long PacketsReordered { get; init; }

    public long BytesUplink { get; init; }
    public long BytesDownlink { get; init; }

    /// <summary>Packets currently held back waiting for their release time.</summary>
    public int QueueDepth { get; init; }

    /// <summary>Throughput over the last sampling window.</summary>
    public double UplinkBitsPerSecond { get; init; }
    public double DownlinkBitsPerSecond { get; init; }

    public TimeSpan Uptime { get; init; }

    public long BytesTotal => BytesUplink + BytesDownlink;

    public double DropRate => PacketsSeen == 0 ? 0d : (double)PacketsDropped / PacketsSeen * 100d;
}

/// <summary>
/// Drives the capture loop and applies the active profile to everything it sees.
/// </summary>
public interface IImpairmentEngine : IDisposable
{
    EngineState State { get; }

    /// <summary>
    /// Pass all traffic through untouched while staying attached to the network. Lets you compare
    /// impaired against clean behaviour without tearing the capture down.
    /// </summary>
    bool Bypass { get; set; }

    event EventHandler<EngineState>? StateChanged;

    /// <summary>Raised when the capture loop dies; the message is safe to show to the user.</summary>
    event EventHandler<string>? Faulted;

    void Start(EngineOptions options);

    void Stop();

    /// <summary>Swaps in a new rule set without interrupting the capture, so toggles apply instantly.</summary>
    void Apply(Profile profile);

    EngineStatistics Snapshot();
}
