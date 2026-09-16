using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Crapnet.Domain.Networking;

namespace Crapnet.Infrastructure.Network;

/// <summary>
/// Reads the IPv4 neighbour cache so a hardware address can be turned into a leased address.
/// </summary>
/// <remarks>
/// <para>
/// The Mobile Hotspot API reports associated clients by MAC only, while every rule Crapnet can
/// express is written against IPv4 addresses. The neighbour cache is the join between the two:
/// once a client has completed DHCP, the host has an entry for it.
/// </para>
/// <para>
/// This shells out to <c>arp -a</c> rather than calling <c>GetIpNetTable2</c>. The P/Invoke route
/// needs <c>MIB_IPNET_ROW2</c>, whose <c>SOCKADDR_INET</c> union and <c>NET_LUID</c> bitfield have
/// to be laid out exactly right or the table is read as garbage — a silent, hard-to-spot failure.
/// The table is only consulted when the client list refreshes, at human speed, so the cost of a
/// process launch is irrelevant and the parser's mistakes are at least visible.
/// </para>
/// </remarks>
public static class ArpTable
{
    /// <summary>
    /// Matches the address columns of an <c>arp -a</c> row. Anchored on shape rather than on the
    /// column headers, which are localised.
    /// </summary>
    private static readonly Regex EntryPattern = new(
        @"^\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+(?<mac>[0-9a-fA-F]{2}(?:[-:][0-9a-fA-F]{2}){5})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Returns the neighbour cache keyed by normalised MAC address, or an empty map.
    /// </summary>
    /// <remarks>
    /// An unreadable table is reported the same way as an empty one on purpose: the lookup only
    /// enriches the client list with an address, so a failure should cost the user a column, not
    /// the whole panel.
    /// </remarks>
    public static async Task<IReadOnlyDictionary<string, Ipv4Address>> ReadAsync(CancellationToken cancellationToken = default)
    {
        string output;
        try
        {
            output = await RunArpAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new Dictionary<string, Ipv4Address>(0);
        }

        return Parse(output);
    }

    /// <summary>
    /// Strips separators and casing from a hardware address so that <c>aa-bb-cc</c> and
    /// <c>AA:BB:CC</c> compare equal.
    /// </summary>
    public static string NormalizeMac(string? macAddress)
    {
        // 64 characters is far more than any separator style needs for six octets; anything
        // longer is not a hardware address and is rejected rather than stack-allocated.
        if (string.IsNullOrWhiteSpace(macAddress) || macAddress.Length > 64) return string.Empty;

        Span<char> buffer = stackalloc char[macAddress.Length];
        var length = 0;

        foreach (var character in macAddress)
        {
            if (!Uri.IsHexDigit(character)) continue;
            buffer[length++] = char.ToUpperInvariant(character);
        }

        return new string(buffer[..length]);
    }

    /// <summary>Parses <c>arp -a</c> output. Exposed for the sake of testing the row shapes.</summary>
    internal static IReadOnlyDictionary<string, Ipv4Address> Parse(string output)
    {
        var entries = new Dictionary<string, Ipv4Address>(StringComparer.Ordinal);

        foreach (var line in output.Split('\n'))
        {
            var match = EntryPattern.Match(line);
            if (!match.Success) continue;

            if (!Ipv4Address.TryParse(match.Groups["ip"].Value, out var address)) continue;

            var mac = NormalizeMac(match.Groups["mac"].Value);
            if (mac.Length != 12) continue;

            // The low bit of the first octet marks a group address, which covers the broadcast and
            // multicast rows Windows keeps permanently in the table. No client ever owns one.
            if (!byte.TryParse(mac.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var firstOctet)
                || (firstOctet & 1) != 0)
            {
                continue;
            }

            // First writer wins: a client seen on several interfaces is most likely to be on the
            // one listed first, and overwriting would make the result depend on row order anyway.
            entries.TryAdd(mac, address);
        }

        return entries;
    }

    private static async Task<string> RunArpAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "arp",
            Arguments = "-a",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start arp.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);

        try
        {
            // Both pipes are drained before waiting: a child that fills one it is not being read
            // from blocks forever, and we would wait on it just as long.
            var readOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var readError = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await readError.ConfigureAwait(false);
            return await readOutput.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout fired. Kill the child so it cannot outlive the request.
            TryKill(process);
            throw new TimeoutException("arp did not respond in time.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Nothing useful remains to be done about a process we cannot reach.
        }
    }
}
