using Crapnet.Application.Abstractions;
using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Networking;
using Crapnet.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Networking.NetworkOperators;
using ConnectionProfile = Windows.Networking.Connectivity.ConnectionProfile;
using WindowsHostName = Windows.Networking.HostName;
using WindowsHostNameType = Windows.Networking.HostNameType;
using WindowsNetworkInformation = Windows.Networking.Connectivity.NetworkInformation;

namespace Crapnet.Infrastructure.Hotspot;

/// <summary>
/// Drives the Windows Mobile Hotspot through the WinRT tethering API.
/// </summary>
/// <remarks>
/// <para>
/// The API is organised around a connection profile rather than around an adapter:
/// <c>NetworkOperatorTetheringManager.CreateFromConnectionProfile</c> takes the connection whose
/// internet is to be handed out, and starting the access point shares it. That single fact shapes
/// the whole adapter. It is why <see cref="HotspotConfiguration.UplinkAdapterId"/> is resolved to
/// a profile before anything else happens, and why
/// <see cref="SharesUplinkAutomatically"/> is <see langword="true"/>: passing the Ethernet profile
/// is what gives the phone its internet, so there is no separate sharing step to perform and
/// <see cref="IInternetSharingController"/> stays out of the way.
/// </para>
/// <para>
/// Mobile Hotspot is absent or refused on plenty of real machines — no Wi-Fi radio, a driver that
/// will not act as an access point, a group policy, a stripped SKU. Every call here treats that as
/// an ordinary answer rather than an exception: the state collapses to
/// <see cref="HotspotState.Unavailable"/>, the reason is logged, and the caller can fall back to
/// Internet Connection Sharing over some other access point.
/// </para>
/// <para>
/// The projected WinRT objects are runtime callable wrappers with no <c>IDisposable</c> surface of
/// their own; their lifetime ends with the last managed reference. The manager is cached only
/// because re-creating it per call means an RPC to the tethering service each time.
/// </para>
/// </remarks>
public sealed class WindowsHotspotController : IHotspotController
{
    private readonly INetworkAdapterProvider _adapters;
    private readonly IClock _clock;
    private readonly ILogger<WindowsHotspotController> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private NetworkOperatorTetheringManager? _manager;
    private Guid _managerAdapterId;
    private HotspotState _state = HotspotState.Unknown;
    private Ipv4Subnet _subnet = Ipv4Subnet.IcsDefault;
    private string? _hotspotAdapterId;

    public WindowsHotspotController(
        INetworkAdapterProvider adapters,
        IClock clock,
        ILogger<WindowsHotspotController>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(clock);

        _adapters = adapters;
        _clock = clock;
        _logger = logger ?? NullLogger<WindowsHotspotController>.Instance;
    }

    /// <inheritdoc />
    public HotspotState State => _state;

    /// <inheritdoc />
    /// <remarks>
    /// The tethering API never names the adapter it creates, so it is recognised by its address
    /// instead: the access point interface is the one holding the gateway address of the private
    /// subnet. That is stable across Windows versions and does not depend on the interface's
    /// display name, which is localised.
    /// </remarks>
    public string? HotspotAdapterId => _state.IsOn() ? _hotspotAdapterId ??= ResolveHotspotAdapterId() : null;

    /// <inheritdoc />
    public bool SharesUplinkAutomatically => true;

    /// <inheritdoc />
    public event EventHandler<HotspotState>? StateChanged;

    /// <inheritdoc />
    public async Task<HotspotAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Ask about the uplink already in use where there is one, so that availability and
            // the next start are answered for the same connection.
            var profile = ResolveProfile(_managerAdapterId == Guid.Empty ? null : _managerAdapterId.ToString("B"));
            if (profile is null)
            {
                SetState(HotspotState.Unavailable);
                _logger.LogInformation("No connection profile is available to share.");
                return HotspotAvailability.NoUplink;
            }

            TetheringCapability capability;
            try
            {
                capability = NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(profile);
            }
            catch (Exception exception)
            {
                SetState(HotspotState.Unavailable);
                _logger.LogWarning(exception, "The tethering API refused to report its capability.");
                return HotspotAvailability.Unknown;
            }

            if (capability != TetheringCapability.Enabled)
            {
                SetState(HotspotState.Unavailable);
                _logger.LogInformation("Mobile Hotspot is unavailable: {Capability}.", capability);
            }
            else
            {
                RefreshState();
            }

            return Translate(capability);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(HotspotConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manager = AcquireManager(configuration.UplinkAdapterId);
            _subnet = configuration.Subnet;

            await ApplyAccessPointConfigurationAsync(manager, configuration, cancellationToken).ConfigureAwait(false);

            if (ReadOperationalState(manager) == TetheringOperationalState.On)
            {
                // Already up, and the configuration above has been made to match. Restarting would
                // only drop the device under test off the network for no reason.
                Settle(manager);
                return;
            }

            SetState(HotspotState.Starting);

            NetworkOperatorTetheringOperationResult result;
            try
            {
                result = await manager.StartTetheringAsync().AsTask(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetState(HotspotState.Failed);
                throw new InvalidOperationException($"Mobile Hotspot could not be started: {exception.Message}", exception);
            }

            if (result.Status != TetheringOperationStatus.Success)
            {
                SetState(HotspotState.Failed);
                throw new InvalidOperationException(Explain(result));
            }

            Settle(manager);
            _logger.LogInformation("Access point '{Ssid}' is up on {Subnet}.", configuration.Ssid, _subnet);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manager = _manager;
            if (manager is null || ReadOperationalState(manager) == TetheringOperationalState.Off)
            {
                // Nothing of ours is running. Stopping is meant to be idempotent, because it also
                // runs on the way out of a failed start.
                SetState(HotspotState.Stopped);
                _hotspotAdapterId = null;
                return;
            }

            SetState(HotspotState.Stopping);

            NetworkOperatorTetheringOperationResult result;
            try
            {
                result = await manager.StopTetheringAsync().AsTask(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetState(HotspotState.Failed);
                throw new InvalidOperationException($"Mobile Hotspot could not be stopped: {exception.Message}", exception);
            }

            if (result.Status != TetheringOperationStatus.Success)
            {
                SetState(HotspotState.Failed);
                throw new InvalidOperationException(Explain(result));
            }

            _hotspotAdapterId = null;
            Settle(manager);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TetheredClient>> GetClientsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NetworkOperatorTetheringClient> associated;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manager = _manager;
            if (manager is null) return Array.Empty<TetheredClient>();

            try
            {
                associated = manager.GetTetheringClients();
            }
            catch (Exception exception)
            {
                // Asking while the radio is down is normal, not exceptional.
                _logger.LogDebug(exception, "The tethering client list could not be read.");
                return Array.Empty<TetheredClient>();
            }
        }
        finally
        {
            _gate.Release();
        }

        return await DescribeAsync(associated, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<HotspotConfiguration?> ReadConfigurationAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manager = _manager;
            if (manager is null) return null;

            NetworkOperatorTetheringAccessPointConfiguration current;
            try
            {
                current = manager.GetCurrentAccessPointConfiguration();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "The live access point configuration could not be read.");
                return null;
            }

            var subnet = HotspotAdapterId is { } adapterId ? _adapters.GetSubnet(adapterId) : null;

            return new HotspotConfiguration
            {
                Ssid = current.Ssid ?? string.Empty,
                Passphrase = current.Passphrase ?? string.Empty,
                Band = TryReadBand(current, out var band) ? Translate(band) : HotspotBand.Auto,
                UplinkAdapterId = _managerAdapterId == Guid.Empty ? null : _managerAdapterId.ToString("B"),
                Subnet = subnet ?? _subnet,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<TetheredClient>> DescribeAsync(
        IReadOnlyList<NetworkOperatorTetheringClient> associated,
        CancellationToken cancellationToken)
    {
        if (associated.Count == 0) return Array.Empty<TetheredClient>();

        var seenAt = _clock.UtcNow;
        var clients = new List<TetheredClient>(associated.Count);
        var unresolved = false;

        foreach (var client in associated)
        {
            var address = ReadAddress(client);
            if (address == Ipv4Address.Any) unresolved = true;

            clients.Add(new TetheredClient
            {
                MacAddress = client.MacAddress ?? string.Empty,
                Address = address,
                HostName = ReadHostName(client),
                LastSeen = seenAt,
            });
        }

        // The neighbour cache is only worth a process launch when somebody is actually missing an
        // address: Windows already volunteers one for clients that announced a host name.
        if (!unresolved) return clients;

        var neighbours = await ArpTable.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (neighbours.Count == 0) return clients;

        for (var i = 0; i < clients.Count; i++)
        {
            if (clients[i].HasAddress) continue;

            var mac = ArpTable.NormalizeMac(clients[i].MacAddress);
            if (mac.Length == 0 || !neighbours.TryGetValue(mac, out var address)) continue;

            clients[i] = clients[i] with { Address = address };
        }

        return clients;
    }

    private Ipv4Address ReadAddress(NetworkOperatorTetheringClient client)
    {
        foreach (var hostName in SafeHostNames(client))
        {
            if (hostName.Type != WindowsHostNameType.Ipv4) continue;
            if (Ipv4Address.TryParse(hostName.CanonicalName, out var address)) return address;
        }

        return Ipv4Address.Any;
    }

    private string? ReadHostName(NetworkOperatorTetheringClient client)
    {
        foreach (var hostName in SafeHostNames(client))
        {
            if (hostName.Type == WindowsHostNameType.DomainName) return hostName.DisplayName;
        }

        return null;
    }

    private IReadOnlyList<WindowsHostName> SafeHostNames(NetworkOperatorTetheringClient client)
    {
        try
        {
            return client.HostNames;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "A tethering client reported no host names.");
            return Array.Empty<WindowsHostName>();
        }
    }

    private async Task ApplyAccessPointConfigurationAsync(
        NetworkOperatorTetheringManager manager,
        HotspotConfiguration wanted,
        CancellationToken cancellationToken)
    {
        NetworkOperatorTetheringAccessPointConfiguration current;
        try
        {
            current = manager.GetCurrentAccessPointConfiguration();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"The current access point configuration could not be read: {exception.Message}", exception);
        }

        // Reconfiguring bounces the radio, which drops any device already associated, so it is
        // only worth doing when something the user chose has actually changed.
        if (!NeedsReconfiguration(current, wanted)) return;

        var replacement = new NetworkOperatorTetheringAccessPointConfiguration
        {
            Ssid = wanted.Ssid,
            Passphrase = wanted.Passphrase,
        };

        ApplyBand(replacement, wanted.Band);

        try
        {
            await manager.ConfigureAccessPointAsync(replacement).AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetState(HotspotState.Failed);
            throw new InvalidOperationException(
                $"The access point could not be configured: {exception.Message}", exception);
        }
    }

    private bool NeedsReconfiguration(
        NetworkOperatorTetheringAccessPointConfiguration current,
        HotspotConfiguration wanted)
    {
        if (!string.Equals(current.Ssid, wanted.Ssid, StringComparison.Ordinal)) return true;
        if (!string.Equals(current.Passphrase, wanted.Passphrase, StringComparison.Ordinal)) return true;

        // A build that cannot report the band cannot be asked to change it either, so treat the
        // band as already correct.
        return TryReadBand(current, out var band) && band != Translate(wanted.Band);
    }

    private void ApplyBand(NetworkOperatorTetheringAccessPointConfiguration configuration, HotspotBand band)
    {
        var requested = Translate(band);
        if (requested == TetheringWiFiBand.Auto) return;

        try
        {
            if (configuration.IsBandSupported(requested))
            {
                configuration.Band = requested;
                return;
            }

            _logger.LogInformation("The radio does not offer {Band}; letting Windows choose.", band);
        }
        catch (Exception exception)
        {
            // Band selection arrived after the rest of the API. Older builds throw here, and the
            // access point still works — it just comes up on whichever band Windows prefers.
            _logger.LogDebug(exception, "Band selection is not supported on this build.");
        }
    }

    private bool TryReadBand(NetworkOperatorTetheringAccessPointConfiguration configuration, out TetheringWiFiBand band)
    {
        try
        {
            band = configuration.Band;
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Band selection is not supported on this build.");
            band = TetheringWiFiBand.Auto;
            return false;
        }
    }

    /// <summary>Returns the manager for the chosen uplink, creating or replacing it as needed.</summary>
    private NetworkOperatorTetheringManager AcquireManager(string? uplinkAdapterId)
    {
        var profile = ResolveProfile(uplinkAdapterId);
        if (profile is null)
        {
            SetState(HotspotState.Unavailable);
            throw new InvalidOperationException(
                "No internet connection was found to share. Connect the uplink adapter and try again.");
        }

        var adapterId = ReadAdapterId(profile);
        if (_manager is not null && adapterId != Guid.Empty && adapterId == _managerAdapterId) return _manager;

        try
        {
            _manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
            _managerAdapterId = adapterId;
            return _manager;
        }
        catch (Exception exception)
        {
            SetState(HotspotState.Unavailable);
            throw new InvalidOperationException(
                $"Mobile Hotspot is not available on this machine: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Finds the connection profile for the chosen uplink, falling back to whichever connection
    /// Windows currently considers to be the internet.
    /// </summary>
    /// <remarks>
    /// The fallback is what makes the common case work without configuration: on a laptop with one
    /// Ethernet cable plugged in, the internet connection profile is that cable.
    /// </remarks>
    private ConnectionProfile? ResolveProfile(string? uplinkAdapterId)
    {
        try
        {
            if (Guid.TryParse(uplinkAdapterId, out var wanted) && wanted != Guid.Empty)
            {
                foreach (var candidate in WindowsNetworkInformation.GetConnectionProfiles())
                {
                    if (ReadAdapterId(candidate) == wanted) return candidate;
                }

                _logger.LogInformation(
                    "Adapter {Adapter} has no connection profile; using the default internet connection.",
                    uplinkAdapterId);
            }

            return WindowsNetworkInformation.GetInternetConnectionProfile();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Connection profiles could not be enumerated.");
            return null;
        }
    }

    private Guid ReadAdapterId(ConnectionProfile profile)
    {
        try
        {
            // Profiles for connections that have gone away expose no adapter at all.
            return profile.NetworkAdapter?.NetworkAdapterId ?? Guid.Empty;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "A connection profile would not name its adapter.");
            return Guid.Empty;
        }
    }

    private string? ResolveHotspotAdapterId()
    {
        if (!_state.IsOn()) return null;

        var gateway = _subnet.GatewayAddress;
        string? fallback = null;

        foreach (var adapter in _adapters.GetAdapters())
        {
            if (!adapter.IsUp || !_subnet.Contains(adapter.Address)) continue;
            if (adapter.Address == gateway && adapter.IsWireless) return adapter.Id;

            // The gateway address is the strong signal; anything else on the private subnet is
            // only worth returning if nothing better turns up.
            fallback ??= adapter.Id;
        }

        return fallback;
    }

    /// <summary>Re-reads the live state after an operation and publishes whatever it finds.</summary>
    private void Settle(NetworkOperatorTetheringManager manager)
    {
        SetState(Translate(ReadOperationalState(manager), _state));
        if (_state.IsOn()) _hotspotAdapterId = ResolveHotspotAdapterId();
    }

    private void RefreshState()
    {
        if (_manager is not null) Settle(_manager);
    }

    private TetheringOperationalState ReadOperationalState(NetworkOperatorTetheringManager manager)
    {
        try
        {
            return manager.TetheringOperationalState;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "The tethering state could not be read.");
            return TetheringOperationalState.Unknown;
        }
    }

    private void SetState(HotspotState state)
    {
        if (_state == state) return;

        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private static HotspotState Translate(TetheringOperationalState state, HotspotState current) => state switch
    {
        TetheringOperationalState.On => HotspotState.Running,
        TetheringOperationalState.Off => HotspotState.Stopped,

        // Windows does not say which way a transition is heading, so keep whichever one we started.
        TetheringOperationalState.InTransition => current.IsBusy() ? current : HotspotState.Starting,
        _ => HotspotState.Unknown,
    };

    private static HotspotAvailability Translate(TetheringCapability capability) => capability switch
    {
        TetheringCapability.Enabled => HotspotAvailability.Available,
        TetheringCapability.DisabledByGroupPolicy => HotspotAvailability.BlockedByPolicy,
        TetheringCapability.DisabledByHardwareLimitation => HotspotAvailability.NoWifiAdapter,

        // Operator and SKU restrictions are both "somebody above you said no", which is what the
        // policy answer already means to the user.
        TetheringCapability.DisabledByOperator => HotspotAvailability.BlockedByPolicy,
        TetheringCapability.DisabledBySku => HotspotAvailability.BlockedByPolicy,
        TetheringCapability.DisabledByRequiredAppNotInstalled => HotspotAvailability.NotSupportedByDriver,
        TetheringCapability.DisabledBySystemCapability => HotspotAvailability.NotSupportedByDriver,
        _ => HotspotAvailability.Unknown,
    };

    private static TetheringWiFiBand Translate(HotspotBand band) => band switch
    {
        HotspotBand.TwoPointFourGigahertz => TetheringWiFiBand.TwoPointFourGigahertz,
        HotspotBand.FiveGigahertz => TetheringWiFiBand.FiveGigahertz,
        _ => TetheringWiFiBand.Auto,
    };

    private static HotspotBand Translate(TetheringWiFiBand band) => band switch
    {
        TetheringWiFiBand.TwoPointFourGigahertz => HotspotBand.TwoPointFourGigahertz,
        TetheringWiFiBand.FiveGigahertz => HotspotBand.FiveGigahertz,
        _ => HotspotBand.Auto,
    };

    /// <summary>Turns an operation status into something a tester can act on.</summary>
    private static string Explain(NetworkOperatorTetheringOperationResult result)
    {
        var reason = result.Status switch
        {
            TetheringOperationStatus.WiFiDeviceOff => "the Wi-Fi radio is switched off",
            TetheringOperationStatus.BluetoothDeviceOff => "the Bluetooth radio is switched off",
            TetheringOperationStatus.MobileBroadbandDeviceOff => "the mobile broadband radio is switched off",
            TetheringOperationStatus.OperationInProgress => "another tethering operation is still running",
            TetheringOperationStatus.NetworkLimitedConnectivity => "the uplink has no usable internet connection",
            TetheringOperationStatus.EntitlementCheckTimeout => "the tethering entitlement check timed out",
            TetheringOperationStatus.EntitlementCheckFailure => "this connection is not entitled to be shared",
            _ => $"Windows reported {result.Status}",
        };

        var detail = result.AdditionalErrorMessage;
        return string.IsNullOrWhiteSpace(detail)
            ? $"Mobile Hotspot failed because {reason}."
            : $"Mobile Hotspot failed because {reason}. {detail}";
    }
}
