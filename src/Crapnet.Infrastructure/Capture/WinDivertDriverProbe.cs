using Crapnet.Application.Abstractions;

namespace Crapnet.Infrastructure.Capture;

/// <summary>
/// Checks that the WinDivert user-mode DLL and its kernel driver are where the loader will find
/// them.
/// </summary>
/// <remarks>
/// <para>
/// WinDivert is vendored under <c>third_party/windivert</c> and copied next to the executable by
/// the build, so the files should always be there. They can still go missing — a partial copy of
/// the output folder, or anti-virus quarantining the driver. Catching that here turns what would
/// otherwise be a <c>DllNotFoundException</c> deep inside the first capture attempt into a message
/// that says what to do.
/// </para>
/// <para>
/// This only answers "are the files present". Whether the driver will actually load — signature
/// policy, elevation, a security product blocking it — is not knowable without opening a handle,
/// and those failures are reported by <see cref="WinDivertNative.DescribeOpenError"/> instead.
/// </para>
/// </remarks>
public sealed class WinDivertDriverProbe : ICaptureDriverProbe
{
    /// <summary>The user-mode library that hosts the P/Invoke entry points.</summary>
    public const string LibraryFileName = "WinDivert.dll";

    /// <summary>The kernel driver the library installs on first open. Crapnet is 64-bit only.</summary>
    public const string DriverFileName = "WinDivert64.sys";

    private readonly string _directory;

    /// <summary>Probes the directory the application was loaded from.</summary>
    public WinDivertDriverProbe()
        : this(AppContext.BaseDirectory)
    {
    }

    /// <summary>Probes an explicit directory, which is what makes this testable off Windows.</summary>
    /// <param name="directory">The directory expected to hold the WinDivert binaries.</param>
    public WinDivertDriverProbe(string directory)
        => _directory = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <inheritdoc />
    public string? Diagnose()
    {
        var missing = new List<string>(2);

        if (!File.Exists(Path.Combine(_directory, LibraryFileName))) missing.Add(LibraryFileName);
        if (!File.Exists(Path.Combine(_directory, DriverFileName))) missing.Add(DriverFileName);

        if (missing.Count == 0) return null;

        return $"Packet capture is unavailable: {string.Join(" and ", missing)} " +
               $"{(missing.Count == 1 ? "is" : "are")} missing from '{_directory}'. " +
               "Rebuild Crapnet or re-extract the release archive, and check that anti-virus has not quarantined them.";
    }
}
