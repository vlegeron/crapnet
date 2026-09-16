using Crapnet.App.ViewModels;
using Crapnet.Application.Abstractions;
using Crapnet.Application.Engine;
using Crapnet.Application.UseCases;
using Crapnet.Domain.Hotspot;
using Crapnet.Domain.Networking;
using Crapnet.Domain.Profiles;
using Crapnet.Domain.Rules;

namespace Crapnet.App.DesignData;

/// <summary>
/// A fully populated window state for the XAML previewer.
/// </summary>
/// <remarks>
/// The previewer has no container and no Windows underneath it, so the ports are stubbed here with
/// plausible data: a running session, some traffic in the sparkline, a tethered phone and one
/// preflight complaint, which between them exercise every part of the chrome.
/// </remarks>
public static class DesignTimeData
{
    private static MainWindowViewModel? _mainWindow;

    public static MainWindowViewModel MainWindow => _mainWindow ??= Build();

    private static MainWindowViewModel Build()
    {
        var orchestrator = new FakeOrchestrator();
        var viewModel = new MainWindowViewModel(orchestrator, new FakeProfileStore(), new FakeAdapters());

        viewModel.Hotspot.SetAdapters(FakeAdapters.Sample);
        foreach (var profile in BuiltInProfiles.All)
        {
            viewModel.Profiles.Add(new ProfileEntry(profile.Name, profile, true));
        }

        viewModel.PreflightIssues.Add("Not running as administrator.");
        viewModel.HasPreflightIssues = true;

        viewModel.Clients.Add(new ClientViewModel(new TetheredClient
        {
            MacAddress = "3C:5A:B4:11:9F:02",
            Address = new Ipv4Address(192, 168, 137, 42),
            HostName = "Pixel-8",
            LastSeen = DateTimeOffset.UtcNow,
        }));
        viewModel.ClientCount = viewModel.Clients.Count;

        // A ramp rather than a flat line, so the sparkline shows its shape at design time.
        for (var i = 0; i < StatisticsViewModel.WindowLength; i++)
        {
            var phase = i / 12d;
            viewModel.Stats.Update(new EngineStatistics
            {
                PacketsSeen = 148_320 + (i * 97),
                PacketsMatched = 96_104 + (i * 61),
                PacketsDropped = 3_118 + i,
                QueueDepth = 14,
                UplinkBitsPerSecond = 900_000d + (Math.Sin(phase) * 380_000d),
                DownlinkBitsPerSecond = 4_600_000d + (Math.Cos(phase * 0.7d) * 1_500_000d),
                Uptime = TimeSpan.FromSeconds(372),
            });
        }

        return viewModel;
    }

    private sealed class FakeOrchestrator : ISessionOrchestrator
    {
        public SessionStatus Status { get; } = new()
        {
            Hotspot = HotspotState.Running,
            Engine = EngineState.Running,
            SharingEnabled = true,
        };

        public event EventHandler<SessionStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public bool Bypass { get; set; }

        public Task<StartResult> StartAsync(StartRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(StartResult.Ok);

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ApplyProfile(Profile profile)
        {
        }

        public EngineStatistics Snapshot() => default;

        public Task<IReadOnlyList<TetheredClient>> GetClientsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TetheredClient>>([]);

        public IReadOnlyList<string> Preflight() => ["Not running as administrator."];

        public void Dispose()
        {
        }
    }

    private sealed class FakeProfileStore : IProfileStore
    {
        public Task<IReadOnlyList<Profile>> LoadProfilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Profile>>([]);

        public Task SaveProfileAsync(Profile profile, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteProfileAsync(string name, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(AppSettings.Default);

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeAdapters : INetworkAdapterProvider
    {
        public static readonly IReadOnlyList<NetworkAdapter> Sample =
        [
            new() { Id = "eth0", Name = "Ethernet", Description = "Realtek Gaming 2.5GbE", IsUp = true, HasInternet = true },
            new() { Id = "wlan0", Name = "Wi-Fi", Description = "Intel AX211", IsUp = true, IsWireless = true },
        ];

        public IReadOnlyList<NetworkAdapter> GetAdapters() => Sample;

        public NetworkAdapter? FindById(string adapterId)
            => Sample.FirstOrDefault(adapter => adapter.Id == adapterId);

        public Ipv4Subnet? GetSubnet(string adapterId) => Ipv4Subnet.IcsDefault;
    }
}
