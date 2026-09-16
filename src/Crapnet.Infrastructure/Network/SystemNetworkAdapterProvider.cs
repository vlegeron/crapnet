using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using Crapnet.Application.Abstractions;
using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Networking;

namespace Crapnet.Infrastructure.Network;

/// <summary>
/// Enumerates interfaces through <see cref="NetworkInterface"/>.
/// </summary>
/// <remarks>
/// Deliberately not cached. Adapters appear and disappear while Crapnet runs — starting Mobile
/// Hotspot conjures a virtual Wi-Fi adapter out of nowhere — so a stale list would show the user
/// choices that no longer exist. The enumeration is cheap enough to redo on every refresh.
/// </remarks>
public sealed class SystemNetworkAdapterProvider : INetworkAdapterProvider
{
    /// <inheritdoc />
    public IReadOnlyList<NetworkAdapter> GetAdapters()
    {
        var adapters = new List<NetworkAdapter>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Loopback can never be an uplink and never hosts the access point, so it is only
            // noise in the picker.
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            adapters.Add(Describe(nic));
        }

        return adapters;
    }

    /// <inheritdoc />
    public NetworkAdapter? FindById(string adapterId)
    {
        var nic = FindInterface(adapterId);
        return nic is null ? null : Describe(nic);
    }

    /// <inheritdoc />
    public Ipv4Subnet? GetSubnet(string adapterId)
    {
        var nic = FindInterface(adapterId);
        if (nic is null) return null;

        var unicast = FindIpv4Unicast(nic);
        if (unicast is null) return null;

        if (!TryConvert(unicast.Address, out var address)) return null;
        if (!TryPrefixLength(unicast, out var prefixLength)) return null;

        return new Ipv4Subnet(address, prefixLength);
    }

    private static NetworkInterface? FindInterface(string adapterId)
    {
        if (string.IsNullOrWhiteSpace(adapterId)) return null;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            // Interface ids are GUIDs in braces on Windows and the casing is not guaranteed to
            // match what we persisted in an earlier run.
            if (string.Equals(nic.Id, adapterId, StringComparison.OrdinalIgnoreCase)) return nic;
        }

        return null;
    }

    private static NetworkAdapter Describe(NetworkInterface nic)
    {
        var unicast = FindIpv4Unicast(nic);
        var address = Ipv4Address.Any;
        if (unicast is not null) TryConvert(unicast.Address, out address);

        return new NetworkAdapter
        {
            Id = nic.Id,
            Name = nic.Name,
            Description = nic.Description,
            IsUp = nic.OperationalStatus == OperationalStatus.Up,
            IsWireless = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
            HasInternet = nic.OperationalStatus == OperationalStatus.Up && HasDefaultRoute(nic),
            Address = address,
        };
    }

    /// <summary>
    /// Approximates "this adapter can reach the internet" as "it is up and has a gateway".
    /// </summary>
    /// <remarks>
    /// Actually probing would mean sending traffic, which is exactly what Crapnet is about to
    /// start mangling. A gateway is the signal the user is really choosing on anyway: it is what
    /// separates the Ethernet NIC from the hotspot adapter, which is deliberately gateway-less.
    /// </remarks>
    private static bool HasDefaultRoute(NetworkInterface nic)
    {
        try
        {
            foreach (var gateway in nic.GetIPProperties().GatewayAddresses)
            {
                if (gateway.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (!TryConvert(gateway.Address, out var address)) continue;
                if (address != Ipv4Address.Any) return true;
            }
        }
        catch (NetworkInformationException)
        {
            // An adapter can vanish between enumeration and interrogation; treat it as offline.
        }

        return false;
    }

    private static UnicastIPAddressInformation? FindIpv4Unicast(NetworkInterface nic)
    {
        try
        {
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork) return unicast;
            }
        }
        catch (NetworkInformationException)
        {
        }

        return null;
    }

    private static bool TryPrefixLength(UnicastIPAddressInformation unicast, out int prefixLength)
    {
        prefixLength = 0;

        var mask = unicast.IPv4Mask;
        if (mask is null || mask.AddressFamily != AddressFamily.InterNetwork) return false;
        if (!TryConvert(mask, out var maskAddress) || maskAddress == Ipv4Address.Any) return false;

        prefixLength = BitOperations.PopCount(maskAddress.Value);

        // A mask with holes in it is not a CIDR prefix and would silently widen the subnet.
        return Ipv4Subnet.MaskFor(prefixLength) == maskAddress.Value;
    }

    private static bool TryConvert(IPAddress source, out Ipv4Address address)
    {
        address = Ipv4Address.Any;
        if (source.AddressFamily != AddressFamily.InterNetwork) return false;

        Span<byte> octets = stackalloc byte[4];
        if (!source.TryWriteBytes(octets, out var written) || written != 4) return false;

        address = new Ipv4Address(octets[0], octets[1], octets[2], octets[3]);
        return true;
    }
}
