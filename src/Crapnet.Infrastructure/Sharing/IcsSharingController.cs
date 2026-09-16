using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Crapnet.Application.Abstractions;
using Crapnet.Domain.Hotspot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crapnet.Infrastructure.Sharing;

/// <summary>
/// Drives classic Internet Connection Sharing through the <c>HNetCfg</c> COM component.
/// </summary>
/// <remarks>
/// <para>
/// This is the fallback path, not the normal one. Windows Mobile Hotspot is built around sharing
/// the connection profile it is handed, so on the primary path
/// (<c>WindowsHotspotController</c>) the uplink is already shared by the time the access point is
/// up and this controller is never called —
/// <see cref="IHotspotController.SharesUplinkAutomatically"/> reports exactly that. ICS matters
/// when Mobile Hotspot is unavailable and the access point comes from somewhere else: a hosted
/// network, a second Wi-Fi adapter, or a cabled test rig.
/// </para>
/// <para>
/// Windows permits a single share at a time, machine-wide, and setting one up while another is
/// live fails in unhelpful ways. Every entry point here therefore reads the current state first
/// and tears down whatever it finds.
/// </para>
/// <para>
/// All of this needs elevation; without it the calls fail with access-denied HRESULTs.
/// </para>
/// </remarks>
public sealed class IcsSharingController : IInternetSharingController
{
    private readonly ILogger<IcsSharingController> _logger;
    private readonly object _gate = new();

    private bool? _isSupported;

    public IcsSharingController(ILogger<IcsSharingController>? logger = null)
        => _logger = logger ?? NullLogger<IcsSharingController>.Instance;

    /// <inheritdoc />
    /// <remarks>
    /// Probed once and remembered. The sharing component is either installed on this machine or
    /// it is not, and re-creating the COM object per query would put an RPC round trip behind a
    /// property the UI reads while painting.
    /// </remarks>
    public bool IsSupported
    {
        get
        {
            lock (_gate)
            {
                return _isSupported ??= Probe();
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>Returns <see cref="SharingState.Disabled"/> when the component cannot be reached:
    /// a UI that cannot read the state should show "off", not an error dialog.</remarks>
    public SharingState GetState()
    {
        lock (_gate)
        {
            try
            {
                return ReadState();
            }
            catch (Exception exception) when (IsComFailure(exception))
            {
                _logger.LogWarning(exception, "Could not read the Internet Connection Sharing state.");
                return SharingState.Disabled;
            }
        }
    }

    /// <inheritdoc />
    public void Enable(string publicAdapterId, string privateAdapterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicAdapterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateAdapterId);

        if (string.Equals(publicAdapterId, privateAdapterId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An adapter cannot share its connection with itself.", nameof(privateAdapterId));

        lock (_gate)
        {
            using var scope = new ComScope();
            var manager = CreateManager(scope);

            var connections = Enumerate(manager, scope);
            var uplink = Find(connections, publicAdapterId)
                ?? throw new InvalidOperationException($"Adapter {publicAdapterId} is not known to Internet Connection Sharing.");
            var hotspot = Find(connections, privateAdapterId)
                ?? throw new InvalidOperationException($"Adapter {privateAdapterId} is not known to Internet Connection Sharing.");

            DisableAll(manager, connections, scope);

            // Public first: the private side is only meaningful once something is sharing to it.
            uplink.Configuration(manager, scope).EnableSharing(SharingConnectionType.Public);
            hotspot.Configuration(manager, scope).EnableSharing(SharingConnectionType.Private);

            _logger.LogInformation(
                "Sharing {Public} with {Private}.", uplink.Name, hotspot.Name);
        }
    }

    /// <inheritdoc />
    public void Disable()
    {
        lock (_gate)
        {
            try
            {
                using var scope = new ComScope();
                var manager = CreateManager(scope);
                DisableAll(manager, Enumerate(manager, scope), scope);
            }
            catch (Exception exception) when (IsComFailure(exception))
            {
                // Stopping a session must not fail because sharing was already gone.
                _logger.LogWarning(exception, "Could not tear down Internet Connection Sharing.");
            }
        }
    }

    private bool Probe()
    {
        try
        {
            using var scope = new ComScope();
            return CreateManager(scope).GetSharingInstalled();
        }
        catch (Exception exception) when (IsComFailure(exception))
        {
            _logger.LogInformation(exception, "Internet Connection Sharing is not available on this machine.");
            return false;
        }
    }

    private SharingState ReadState()
    {
        using var scope = new ComScope();
        var manager = CreateManager(scope);

        string? publicAdapterId = null;
        string? privateAdapterId = null;

        foreach (var connection in Enumerate(manager, scope))
        {
            var configuration = connection.Configuration(manager, scope);
            if (!configuration.GetSharingEnabled()) continue;

            switch (configuration.GetSharingConnectionType())
            {
                case SharingConnectionType.Public:
                    publicAdapterId = connection.AdapterId;
                    break;
                case SharingConnectionType.Private:
                    privateAdapterId = connection.AdapterId;
                    break;
            }
        }

        return new SharingState
        {
            IsEnabled = publicAdapterId is not null || privateAdapterId is not null,
            PublicAdapterId = publicAdapterId,
            PrivateAdapterId = privateAdapterId,
        };
    }

    private static void DisableAll(INetSharingManager manager, IReadOnlyList<Connection> connections, ComScope scope)
    {
        foreach (var connection in connections)
        {
            var configuration = connection.Configuration(manager, scope);
            if (configuration.GetSharingEnabled()) configuration.DisableSharing();
        }
    }

    private static Connection? Find(IReadOnlyList<Connection> connections, string adapterId)
    {
        foreach (var connection in connections)
        {
            if (SameAdapter(connection.AdapterId, adapterId)) return connection;
        }

        return null;
    }

    /// <summary>
    /// Compares adapter ids as GUIDs rather than as text, because the braces and the casing differ
    /// between what the sharing component reports and what <c>NetworkInterface.Id</c> gives us.
    /// </summary>
    private static bool SameAdapter(string? left, string? right)
    {
        if (Guid.TryParse(left, out var leftId) && Guid.TryParse(right, out var rightId))
            return leftId == rightId;

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static INetSharingManager CreateManager(ComScope scope)
    {
        var type = Type.GetTypeFromCLSID(NetSharingInterop.NetSharingManagerClsid)
            ?? throw new InvalidOperationException("The Internet Connection Sharing component is not registered.");

        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("The Internet Connection Sharing component could not be created.");

        scope.Track(instance);

        return instance as INetSharingManager
            ?? throw new InvalidOperationException("The Internet Connection Sharing component does not implement INetSharingManager.");
    }

    /// <summary>Materialises the connection list, so the enumerator is not held open across work.</summary>
    private static IReadOnlyList<Connection> Enumerate(INetSharingManager manager, ComScope scope)
    {
        var collection = manager.GetEnumEveryConnection();
        scope.Track(collection);

        var enumerator = collection.GetNewEnum() as IEnumVARIANT
            ?? throw new InvalidOperationException("The connection collection did not return an enumerator.");
        scope.Track(enumerator);

        var connections = new List<Connection>();
        var buffer = new object[1];

        // Next returns S_FALSE once the collection runs dry; anything other than S_OK ends it.
        while (enumerator.Next(1, buffer, IntPtr.Zero) == 0)
        {
            var item = buffer[0];
            if (item is null) continue;

            // Tracked as the object the enumerator handed back rather than as the interface it is
            // cast to: casting a wrapper does not take a second reference, so releasing it twice
            // would free a connection the collection is still using.
            scope.Track(item);

            if (item is not INetConnection connection) continue;

            var properties = manager.GetNetConnectionProps(connection);
            scope.Track(properties);

            connections.Add(new Connection(connection, properties.GetGuid(), properties.GetName()));
        }

        return connections;
    }

    /// <summary>True for the failures that mean "the machine will not do this", not "we asked wrongly".</summary>
    private static bool IsComFailure(Exception exception)
        => exception is COMException or InvalidCastException or NotSupportedException
            or PlatformNotSupportedException or InvalidOperationException or UnauthorizedAccessException;

    private sealed record Connection(INetConnection Handle, string AdapterId, string Name)
    {
        public INetSharingConfiguration Configuration(INetSharingManager manager, ComScope scope)
        {
            var configuration = manager.GetConfigurationForConnection(Handle);
            scope.Track(configuration);
            return configuration;
        }
    }

    /// <summary>
    /// Releases every runtime callable wrapper handed out during one operation.
    /// </summary>
    /// <remarks>
    /// Left to the finaliser, these wrappers keep the sharing component alive on a thread it did
    /// not expect, and the component holds the configuration database open while it lives.
    /// Releasing in reverse order mirrors acquisition, so a child is always let go before the
    /// object that produced it.
    /// </remarks>
    private sealed class ComScope : IDisposable
    {
        private readonly List<object> _objects = [];

        public void Track(object comObject) => _objects.Add(comObject);

        public void Dispose()
        {
            for (var i = _objects.Count - 1; i >= 0; i--)
            {
                var comObject = _objects[i];
                if (!Marshal.IsComObject(comObject)) continue;

                try
                {
                    Marshal.ReleaseComObject(comObject);
                }
                catch (Exception)
                {
                    // A wrapper that is already gone is not worth failing the operation over.
                }
            }

            _objects.Clear();
        }
    }
}
