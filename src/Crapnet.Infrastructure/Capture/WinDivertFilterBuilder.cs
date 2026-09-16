using System.Globalization;
using Crapnet.Application.Abstractions;
using Crapnet.Domain.Networking;

namespace Crapnet.Infrastructure.Capture;

/// <summary>
/// Translates a platform-neutral <see cref="CaptureFilter"/> into WinDivert filter syntax.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately pure and static: the filter string is the one part of the capture adapter that can
/// be wrong in a way no exception reveals — a filter that is too broad quietly impairs the whole
/// machine — so it is kept free of handles and state and is verified by unit tests instead.
/// </para>
/// <para>
/// Everything is scoped to the hotspot subnet. Without that, opening the forward layer would also
/// catch any other routing the host does, and opening the network layer would catch Crapnet's own
/// traffic.
/// </para>
/// </remarks>
public static class WinDivertFilterBuilder
{
    /// <summary>Builds the filter expression for a capture.</summary>
    /// <param name="filter">The capture description, whose <see cref="CaptureFilter.DeviceSubnet"/> bounds the expression.</param>
    /// <returns>A WinDivert filter string.</returns>
    public static string Build(CaptureFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return BuildForSubnet(filter.DeviceSubnet);
    }

    /// <summary>Builds the filter expression that matches traffic to or from a subnet.</summary>
    /// <param name="subnet">The hotspot subnet whose clients are under test.</param>
    /// <returns>A WinDivert filter string.</returns>
    /// <remarks>
    /// <para>
    /// The leading <c>ip</c> term restricts the capture to IPv4. ICS is an IPv4 NAT, so a tethered
    /// client's traffic is always IPv4 by the time it reaches the forward layer; matching IPv6 as
    /// well would only pick up the host's own traffic, which is not ours to impair.
    /// </para>
    /// <para>
    /// The subnet is expressed as a pair of range comparisons rather than as CIDR. WinDivert grew
    /// CIDR literals late in the 2.x line, and a filter that fails to compile on an older driver
    /// surfaces as a bare <c>ERROR_INVALID_PARAMETER</c>; range comparisons work everywhere.
    /// </para>
    /// </remarks>
    public static string BuildForSubnet(Ipv4Subnet subnet)
    {
        var first = Format(subnet.FirstAddress);
        var last = Format(subnet.LastAddress);

        return string.Create(CultureInfo.InvariantCulture,
            $"ip and (ip.SrcAddr >= {first} and ip.SrcAddr <= {last} or ip.DstAddr >= {first} and ip.DstAddr <= {last})");
    }

    /// <summary>
    /// The WinDivert layers a scope maps onto, in the order handles should be opened.
    /// </summary>
    /// <remarks>
    /// <see cref="CaptureScope.Both"/> needs two handles because WinDivert binds one handle to one
    /// layer; there is no combined layer to ask for.
    /// </remarks>
    public static IReadOnlyList<WinDivertLayer> LayersFor(CaptureScope scope) => scope switch
    {
        CaptureScope.Forwarded => [WinDivertLayer.NetworkForward],
        CaptureScope.Local => [WinDivertLayer.Network],
        CaptureScope.Both => [WinDivertLayer.NetworkForward, WinDivertLayer.Network],
        _ => [WinDivertLayer.NetworkForward],
    };

    private static string Format(Ipv4Address address) => address.ToString();
}
