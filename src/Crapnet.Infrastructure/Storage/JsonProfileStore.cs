using System.Text;
using System.Text.Json;
using Crapnet.Application.Abstractions;
using Crapnet.Domain.Rules;
using Crapnet.Infrastructure.Storage.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crapnet.Infrastructure.Storage;

/// <summary>
/// Persists settings and profiles as JSON under <c>%LOCALAPPDATA%\Crapnet</c>.
/// </summary>
/// <remarks>
/// <para>
/// Layout is <c>settings.json</c> plus one file per profile in <c>profiles\</c>. One file per
/// profile rather than one big document so that a corrupted profile costs the user that profile
/// and nothing else, and so profiles can be copied between machines by hand.
/// </para>
/// <para>
/// <c>%LOCALAPPDATA%</c> rather than roaming: profiles reference adapter GUIDs, which are
/// meaningless on another machine.
/// </para>
/// </remarks>
public sealed class JsonProfileStore : IProfileStore
{
    private const string SettingsFileName = "settings.json";
    private const string ProfilesDirectoryName = "profiles";
    private const string ProfileExtension = ".json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // The files are meant to be legible and hand-editable, so accept the shapes a human
        // produces: any casing, trailing commas, and comments explaining a tweak.
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly string _rootDirectory;
    private readonly ILogger<JsonProfileStore> _logger;

    /// <summary>Creates a store rooted at the per-user application data directory.</summary>
    public JsonProfileStore(ILogger<JsonProfileStore>? logger = null)
        : this(DefaultRootDirectory(), logger)
    {
    }

    /// <summary>Creates a store rooted at an explicit directory, which the tests rely on.</summary>
    public JsonProfileStore(string rootDirectory, ILogger<JsonProfileStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        _rootDirectory = rootDirectory;
        _logger = logger ?? NullLogger<JsonProfileStore>.Instance;
    }

    /// <summary>The directory the store reads and writes, exposed so the UI can offer to open it.</summary>
    public string RootDirectory => _rootDirectory;

    private string SettingsPath => Path.Combine(_rootDirectory, SettingsFileName);

    private string ProfilesDirectory => Path.Combine(_rootDirectory, ProfilesDirectoryName);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Profile>> LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        var directory = ProfilesDirectory;
        if (!Directory.Exists(directory)) return Array.Empty<Profile>();

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*" + ProfileExtension);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Could not list profiles in {Directory}.", directory);
            return Array.Empty<Profile>();
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Could not list profiles in {Directory}.", directory);
            return Array.Empty<Profile>();
        }

        // Stable order so the profile list does not shuffle between runs.
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        var profiles = new List<Profile>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dto = await ReadAsync<ProfileDto>(file, cancellationToken).ConfigureAwait(false);

            // A file we could not read at all is skipped rather than replaced by an empty
            // placeholder: the user may still want to rescue it by hand.
            if (dto is null) continue;

            profiles.Add(PersistenceMapping.ToDomain(dto, Path.GetFileNameWithoutExtension(file)));
        }

        return profiles;
    }

    /// <inheritdoc />
    public async Task SaveProfileAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Directory.CreateDirectory(ProfilesDirectory);
        await WriteAsync(ProfilePath(profile.Name), PersistenceMapping.ToDto(profile), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            File.Delete(ProfilePath(name));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not delete profile {Profile}.", name);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        var dto = await ReadAsync<SettingsDto>(SettingsPath, cancellationToken).ConfigureAwait(false);
        return PersistenceMapping.ToDomain(dto);
    }

    /// <inheritdoc />
    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(_rootDirectory);
        return WriteAsync(SettingsPath, PersistenceMapping.ToDto(settings), cancellationToken);
    }

    private static string DefaultRootDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Crapnet");

    private string ProfilePath(string name) => Path.Combine(ProfilesDirectory, ToFileName(name) + ProfileExtension);

    /// <summary>
    /// Turns a profile name into a file name that is safe to combine with a directory.
    /// </summary>
    /// <remarks>
    /// Profile names are free text, so they can contain path separators, reserved characters, or
    /// be nothing but dots. Every character outside a conservative allow-list is replaced, which
    /// also rules out any attempt to escape the profiles directory. The authoritative name lives
    /// inside the file, so two names that collapse to the same file name are a nuisance rather
    /// than data loss — the loaded profile still reports the name it was saved with.
    /// </remarks>
    private static string ToFileName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or ' '
                ? character
                : '_');
        }

        var sanitised = builder.ToString().Trim();
        return sanitised.Length == 0 ? "profile" : sanitised;
    }

    private async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);

            return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Falling back to defaults beats refusing to start over a file the user cannot see.
            _logger.LogWarning(exception, "Ignoring unreadable file {Path}.", path);
            return null;
        }
    }

    /// <summary>
    /// Writes a document by replacing the target in one step.
    /// </summary>
    /// <remarks>
    /// Serialising into memory first means a failure to render the document never truncates the
    /// previous one, and moving a fully written temporary file over the target means a crash
    /// mid-save leaves either the old file or the new one, never a half of each.
    /// </remarks>
    private async Task WriteAsync<T>(string path, T document, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(document, SerializerOptions);

        var temporaryPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(exception, "Could not remove temporary file {Path}.", path);
        }
    }
}
