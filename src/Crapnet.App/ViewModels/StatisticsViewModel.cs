using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Crapnet.Application.Engine;

namespace Crapnet.App.ViewModels;

/// <summary>
/// The footer read-out, plus the rolling throughput window the sparkline draws.
/// </summary>
/// <remarks>
/// Samples are stored in two fixed arrays that are shifted in place; each update hands the
/// sparkline a fresh copy so the control never renders a buffer that is being written to. At four
/// samples a second the 120-slot window is half a minute of history, which is enough to see a
/// bandwidth cap bite without turning the trace into noise.
/// </remarks>
public sealed class StatisticsViewModel : ObservableObject
{
    /// <summary>Sample count, chosen to fill the footer's sparkline at roughly one point per pixel.</summary>
    public const int WindowLength = 120;

    private static readonly double[] Empty = new double[WindowLength];

    private readonly double[] _uplink = new double[WindowLength];
    private readonly double[] _downlink = new double[WindowLength];

    private IReadOnlyList<double> _uplinkSamples = Empty;
    private IReadOnlyList<double> _downlinkSamples = Empty;
    private double _scale = 1d;
    private string _uplinkText = "0 b/s";
    private string _downlinkText = "0 b/s";
    private string _dropText = "0%";
    private string _queueText = "0";
    private string _uptimeText = "0:00";
    private long _packetsSeen;
    private long _packetsMatched;

    public IReadOnlyList<double> UplinkSamples
    {
        get => _uplinkSamples;
        private set => SetProperty(ref _uplinkSamples, value);
    }

    public IReadOnlyList<double> DownlinkSamples
    {
        get => _downlinkSamples;
        private set => SetProperty(ref _downlinkSamples, value);
    }

    /// <summary>Shared ceiling so both traces are drawn against the same scale.</summary>
    public double Scale
    {
        get => _scale;
        private set => SetProperty(ref _scale, value);
    }

    public string UplinkText
    {
        get => _uplinkText;
        private set => SetProperty(ref _uplinkText, value);
    }

    public string DownlinkText
    {
        get => _downlinkText;
        private set => SetProperty(ref _downlinkText, value);
    }

    public string DropText
    {
        get => _dropText;
        private set => SetProperty(ref _dropText, value);
    }

    public string QueueText
    {
        get => _queueText;
        private set => SetProperty(ref _queueText, value);
    }

    public string UptimeText
    {
        get => _uptimeText;
        private set => SetProperty(ref _uptimeText, value);
    }

    public long PacketsSeen
    {
        get => _packetsSeen;
        private set => SetProperty(ref _packetsSeen, value);
    }

    public long PacketsMatched
    {
        get => _packetsMatched;
        private set => SetProperty(ref _packetsMatched, value);
    }

    public void Update(EngineStatistics stats)
    {
        Push(_uplink, stats.UplinkBitsPerSecond);
        Push(_downlink, stats.DownlinkBitsPerSecond);

        UplinkSamples = (double[])_uplink.Clone();
        DownlinkSamples = (double[])_downlink.Clone();

        var peak = 0d;
        for (var i = 0; i < WindowLength; i++)
        {
            peak = Math.Max(peak, Math.Max(_uplink[i], _downlink[i]));
        }

        // A floor keeps an idle link from magnifying a few stray bits into a full-height trace.
        Scale = Math.Max(peak, 64_000d);

        UplinkText = FormatRate(stats.UplinkBitsPerSecond);
        DownlinkText = FormatRate(stats.DownlinkBitsPerSecond);
        DropText = stats.DropRate.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        QueueText = stats.QueueDepth.ToString(CultureInfo.InvariantCulture);
        UptimeText = FormatUptime(stats.Uptime);
        PacketsSeen = stats.PacketsSeen;
        PacketsMatched = stats.PacketsMatched;
    }

    /// <summary>Clears the trace when a session ends, so the next one starts from a blank window.</summary>
    public void Reset()
    {
        Array.Clear(_uplink);
        Array.Clear(_downlink);
        Update(default);
    }

    private static void Push(double[] window, double sample)
    {
        Array.Copy(window, 1, window, 0, WindowLength - 1);
        window[WindowLength - 1] = double.IsFinite(sample) && sample > 0d ? sample : 0d;
    }

    private static string FormatRate(double bitsPerSecond) => bitsPerSecond switch
    {
        >= 1_000_000_000d => (bitsPerSecond / 1_000_000_000d).ToString("0.0", CultureInfo.InvariantCulture) + " Gb/s",
        >= 1_000_000d => (bitsPerSecond / 1_000_000d).ToString("0.0", CultureInfo.InvariantCulture) + " Mb/s",
        >= 1_000d => (bitsPerSecond / 1_000d).ToString("0.0", CultureInfo.InvariantCulture) + " kb/s",
        > 0d => bitsPerSecond.ToString("0", CultureInfo.InvariantCulture) + " b/s",
        _ => "0 b/s",
    };

    private static string FormatUptime(TimeSpan uptime)
        => uptime >= TimeSpan.FromHours(1)
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)uptime.TotalHours}:{uptime.Minutes:00}:{uptime.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{uptime.Minutes}:{uptime.Seconds:00}");
}
