using Crapnet.Domain.Networking;
using Crapnet.Domain.Rules;

namespace Crapnet.App.ViewModels;

/// <summary>A picker entry: the value the rule stores and the glyph or word shown for it.</summary>
public sealed record DirectionOption(DirectionFilter Value, string Glyph);

/// <summary>A picker entry for the protocol strip.</summary>
public sealed record ProtocolOption(ProtocolFilter Value, string Label);

/// <summary>
/// The match strip: which way, which protocol, whose address and which ports.
/// </summary>
/// <remarks>
/// Address and port boxes hold raw text and are parsed on every keystroke. The parsed value is
/// only replaced when parsing succeeds, so the rule the engine is running stays coherent while
/// the user is halfway through typing an address.
/// </remarks>
public sealed class MatchCriteriaViewModel : EditableViewModel
{
    /// <summary>Shared so that selection can be compared by reference across rules.</summary>
    public static readonly IReadOnlyList<DirectionOption> Directions =
    [
        new(DirectionFilter.Both, "↕"),
        new(DirectionFilter.Uplink, "↑"),
        new(DirectionFilter.Downlink, "↓"),
    ];

    public static readonly IReadOnlyList<ProtocolOption> Protocols =
    [
        new(ProtocolFilter.Any, "ANY"),
        new(ProtocolFilter.Tcp, "TCP"),
        new(ProtocolFilter.Udp, "UDP"),
        new(ProtocolFilter.Icmp, "ICMP"),
    ];

    private DirectionOption _direction;
    private ProtocolOption _protocol;

    public MatchCriteriaViewModel(MatchCriteria criteria)
    {
        _direction = Directions.First(option => option.Value == criteria.Direction);
        _protocol = Protocols.First(option => option.Value == criteria.Protocol);

        DeviceAddresses = new SelectorField<Ipv4Selector>(criteria.DeviceAddresses.Text, Ipv4Selector.Any, Ipv4Selector.TryParse);
        RemoteAddresses = new SelectorField<Ipv4Selector>(criteria.RemoteAddresses.Text, Ipv4Selector.Any, Ipv4Selector.TryParse);
        DevicePorts = new SelectorField<PortSelector>(criteria.DevicePorts.Text, PortSelector.Any, PortSelector.TryParse);
        RemotePorts = new SelectorField<PortSelector>(criteria.RemotePorts.Text, PortSelector.Any, PortSelector.TryParse);

        Track(DeviceAddresses);
        Track(RemoteAddresses);
        Track(DevicePorts);
        Track(RemotePorts);
    }

    public IReadOnlyList<DirectionOption> DirectionOptions => Directions;

    public IReadOnlyList<ProtocolOption> ProtocolOptions => Protocols;

    public DirectionOption Direction
    {
        get => _direction;
        set => SetProperty(ref _direction, value);
    }

    public ProtocolOption Protocol
    {
        get => _protocol;
        set => SetProperty(ref _protocol, value);
    }

    public SelectorField<Ipv4Selector> DeviceAddresses { get; }

    public SelectorField<Ipv4Selector> RemoteAddresses { get; }

    public SelectorField<PortSelector> DevicePorts { get; }

    public SelectorField<PortSelector> RemotePorts { get; }

    /// <summary>False while any box holds something that will not parse.</summary>
    public bool IsValid
        => !DeviceAddresses.HasError && !RemoteAddresses.HasError && !DevicePorts.HasError && !RemotePorts.HasError;

    /// <summary>Narrows the rule to one tethered client, used when a client is picked from the list.</summary>
    public void RestrictToDevice(Ipv4Address address) => DeviceAddresses.Text = address.ToString();

    public MatchCriteria ToCriteria() => new()
    {
        Direction = Direction.Value,
        Protocol = Protocol.Value,
        DeviceAddresses = DeviceAddresses.Value,
        RemoteAddresses = RemoteAddresses.Value,
        DevicePorts = DevicePorts.Value,
        RemotePorts = RemotePorts.Value,
    };

    protected override void OnEdited() => OnPropertyChanged(nameof(IsValid));
}
