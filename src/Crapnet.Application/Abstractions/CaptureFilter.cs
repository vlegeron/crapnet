using Crapnet.Domain.Networking;

namespace Crapnet.Application.Abstractions;

/// <summary>Which part of the stack to intercept.</summary>
public enum CaptureScope
{
    /// <summary>
    /// Traffic Windows is routing on behalf of somebody else. This is where tethered client
    /// traffic lives and is the right default.
    /// </summary>
    Forwarded = 0,

    /// <summary>Traffic to and from the Windows host itself. Useful when the host is the test target.</summary>
    Local = 1,

    /// <summary>Both of the above, at the cost of seeing each packet twice on some paths.</summary>
    Both = 2,
}

/// <summary>
/// Describes the slice of traffic the gateway should hand to the engine.
/// </summary>
/// <remarks>
/// Kept free of any platform filter syntax: the infrastructure adapter translates this into
/// whatever its capture driver speaks.
/// </remarks>
public sealed record CaptureFilter
{
    /// <summary>The hotspot's subnet. Packets are classified uplink/downlink against it.</summary>
    public required Ipv4Subnet DeviceSubnet { get; init; }

    public CaptureScope Scope { get; init; } = CaptureScope.Forwarded;

    /// <summary>
    /// Driver queue depth in packets. Too small and bursts are lost by the driver rather than by
    /// our own rules, which would quietly distort results.
    /// </summary>
    public int QueueLength { get; init; } = 8192;

    /// <summary>How long the driver may hold a packet before giving up on us, in milliseconds.</summary>
    public int QueueTimeMilliseconds { get; init; } = 2000;

    /// <summary>Relative priority against other capture clients on the machine.</summary>
    public short Priority { get; init; }
}
