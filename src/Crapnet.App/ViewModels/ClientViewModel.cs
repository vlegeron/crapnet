using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Networking;

namespace Crapnet.App.ViewModels;

/// <summary>
/// A device currently associated with the hotspot, and the one action it offers: narrow the
/// selected rule down to this device's address.
/// </summary>
/// <remarks>
/// The action lives on the client rather than on the window so that the flyout's item template can
/// bind to it directly, without reaching back up the visual tree for an ancestor's command.
/// </remarks>
public sealed partial class ClientViewModel : ObservableObject
{
    private readonly Action<ClientViewModel>? _select;

    public ClientViewModel(TetheredClient client, Action<ClientViewModel>? select = null)
    {
        _select = select;
        DisplayName = client.DisplayName;
        MacAddress = client.MacAddress;
        Address = client.Address;
        HasAddress = client.HasAddress;
        AddressText = client.HasAddress ? client.Address.ToString() : "—";
    }

    public string DisplayName { get; }

    public string MacAddress { get; }

    public Ipv4Address Address { get; }

    public bool HasAddress { get; }

    /// <summary>The leased address, or an em dash while DHCP has not finished.</summary>
    public string AddressText { get; }

    [RelayCommand(CanExecute = nameof(HasAddress))]
    private void Use() => _select?.Invoke(this);
}
