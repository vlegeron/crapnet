using Crapnet.App.ViewModels;
using Crapnet.App.Views;
using Crapnet.Application.Abstractions;
using Crapnet.Application.Engine;
using Crapnet.Application.UseCases;
using Crapnet.Infrastructure.Capture;
using Crapnet.Infrastructure.Hotspot;
using Crapnet.Infrastructure.Network;
using Crapnet.Infrastructure.Sharing;
using Crapnet.Infrastructure.Storage;
// The clock, randomness and privilege adapters live under SystemServices rather than System: a
// namespace called Crapnet.Infrastructure.System would shadow the global System namespace.
using Crapnet.Infrastructure.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace Crapnet.App.Composition;

/// <summary>
/// Wires the application's ports to their Windows adapters.
/// </summary>
/// <remarks>
/// This is the only file that knows both halves of the solution. Everything is a singleton: the
/// capture driver, the access point and the sharing link are all machine-wide resources, and a
/// second instance of any of them would fight the first for the same hardware.
/// </remarks>
public static class CompositionRoot
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IRandomSource, DefaultRandomSource>();
        services.AddSingleton<IPrivilegeProbe, WindowsPrivilegeProbe>();
        services.AddSingleton<ICaptureDriverProbe, WinDivertDriverProbe>();

        services.AddSingleton<IPacketGateway, WinDivertPacketGateway>();
        services.AddSingleton<IHotspotController, WindowsHotspotController>();
        services.AddSingleton<IInternetSharingController, IcsSharingController>();
        services.AddSingleton<INetworkAdapterProvider, SystemNetworkAdapterProvider>();
        services.AddSingleton<IProfileStore, JsonProfileStore>();

        services.AddSingleton<IImpairmentEngine, ImpairmentEngine>();
        services.AddSingleton<ISessionOrchestrator, SessionOrchestrator>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
