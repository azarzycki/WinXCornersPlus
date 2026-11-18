using System.Globalization;
using System.Text;
using System.Text.Json;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Settings;

public enum SettingsLoadStatus
{
    /// <summary>The primary file was read (and possibly corrected; see <see cref="SettingsLoadResult.Corrections"/>).</summary>
    Loaded,

    /// <summary>No usable file existed; defaults were returned and nothing was written.</summary>
    CreatedDefaults,

    /// <summary>The legacy file was read and copied to the primary location (best effort). The legacy file is untouched.</summary>
    MigratedFromLegacy,

    /// <summary>The primary file was not valid settings; it was moved aside (see <see cref="SettingsLoadResult.BackupPath"/>) and defaults returned.</summary>
    RecoveredFromCorruptFile,

    /// <summary>The primary file exists but could not be read; defaults were returned and the file left alone.</summary>
    ReadFailed
}

/// <param name="Settings">Sanitized settings, always usable.</param>
/// <param name="Corrections">What the sanitizer changed in the loaded file; empty for defaults.</param>
/// <param name="BackupPath">Where an unreadable-as-settings file was moved to, if it was.</param>
/// <param name="Error">Human-readable description of any problem, also for statuses that still succeeded.</param>
public sealed record SettingsLoadResult(
    ApplicationSettings Settings,
    SettingsLoadStatus Status,
    IReadOnlyList<string> Corrections,
    string? BackupPath,
    string? Error);

/// <param name="Succeeded">The file on disk now holds the given settings.</param>
/// <param name="Written">The file was actually rewritten (false when content was already identical).</param>
public sealed record SettingsSaveResult(bool Succeeded, bool Written, string? Error);

/// <summary>
/// Reads and writes settings.json. Never throws for I/O or content problems; every outcome is reported in
/// the result. Not thread-safe: use from one thread (in practice through <see cref="SettingsSession"/>).
/// </summary>
/// <remarks>
/// The guiding rule is that a user's file is never destroyed: a file that cannot be parsed is moved to a
/// timestamped backup before anything replaces it, and a file that cannot be read (or backed up) blocks
/// saving for the rest of the session instead of being overwritten with defaults.
/// </remarks>
public sealed class SettingsStore
{
    /// <summary>Files larger than this are treated as corrupt; a real settings file is a few KiB.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly TimeProvider _timeProvider;

    // Set when the primary file holds data we could not read; saving would destroy it.
    private string? _saveBlockedReason;

    // Set when the primary file is corrupt but moving it aside failed; Save retries the move first.
    private bool _corruptBackupPending;

    public SettingsStore(SettingsPaths paths, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.PrimaryPath);
        Paths = paths;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public SettingsPaths Paths { get; }

    /// <summary>Loads settings, migrating or recovering as described by <see cref="SettingsLoadStatus"/>.</summary>
    public SettingsLoadResult Load()
    {
        _saveBlockedReason = null;
        _corruptBackupPending = false;

        var primary = ReadSettingsFile(Paths.PrimaryPath);
        switch (primary.Kind)
        {
            case ReadKind.Parsed:
                var corrections = SettingsSanitizer.Sanitize(primary.Settings!);
                return new SettingsLoadResult(primary.Settings!, SettingsLoadStatus.Loaded, corrections, null, null);

            case ReadKind.Unreadable:
                _saveBlockedReason = $"The settings file could not be read at startup ({primary.Error}); it is not overwritten so that it is not lost.";
                return Defaults(SettingsLoadStatus.ReadFailed, $"Could not read {Paths.PrimaryPath}: {primary.Error}");

            case ReadKind.Invalid:
                return RecoverFromCorruptFile(primary.Error!);

            default:
                return MigrateFromLegacy();
        }
    }

    /// <summary>
    /// Writes a sanitized copy of <paramref name="settings"/> (the argument is never modified) by writing a
    /// temp file in the same folder, flushing it to disk and renaming it over the target, so a crash or
    /// power loss leaves either the old or the new file, never a torn one. Skips the write only when the file
    /// on disk already holds exactly the bytes that would be written.
    /// </summary>
    public SettingsSaveResult Save(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var copy = settings.Clone();
        SettingsSanitizer.Sanitize(copy);
        var json = SettingsSerializer.Serialize(copy);

        if (_saveBlockedReason is not null)
        {
            return new SettingsSaveResult(false, false, _saveBlockedReason);
        }

        if (_corruptBackupPending)
        {
            if (File.Exists(Paths.PrimaryPath) && !TryBackupCorruptFile(out _, out var backupError))
            {
                return new SettingsSaveResult(false, false,
                    $"The unreadable settings file could not be backed up ({backupError}); it is not overwritten so that it is not lost.");
            }

            _corruptBackupPending = false;
        }

        // Skip the write only when the file on disk already holds exactly these bytes. Comparing with what this
        // store last wrote would not do: another program may have changed the file since, and reporting success
        // would then leave its content in place.
        var bytes = StrictUtf8.GetBytes(json);
        if (FileHasContent(Paths.PrimaryPath, bytes))
        {
            return new SettingsSaveResult(true, false, null);
        }

        var error = WriteAtomically(bytes);
        return error is null
            ? new SettingsSaveResult(true, true, null)
            : new SettingsSaveResult(false, false, error);
    }

    private SettingsLoadResult RecoverFromCorruptFile(string parseError)
    {
        var problem = $"{Paths.PrimaryPath} is not valid settings: {parseError}";
        if (TryBackupCorruptFile(out var backupPath, out var backupError))
        {
            return Defaults(SettingsLoadStatus.RecoveredFromCorruptFile, $"{problem} It was moved to {backupPath}.", backupPath);
        }

        _corruptBackupPending = true;
        return Defaults(SettingsLoadStatus.RecoveredFromCorruptFile,
            $"{problem} It could not be moved aside ({backupError}) and was left in place; it will not be overwritten.");
    }

    private SettingsLoadResult MigrateFromLegacy()
    {
        var legacyPath = Paths.LegacyPath;
        if (string.IsNullOrWhiteSpace(legacyPath) || string.Equals(legacyPath, Paths.PrimaryPath, StringComparison.Ordinal))
        {
            return Defaults(SettingsLoadStatus.CreatedDefaults, null);
        }

        var legacy = ReadSettingsFile(legacyPath);
        switch (legacy.Kind)
        {
            case ReadKind.Parsed:
                var corrections = SettingsSanitizer.Sanitize(legacy.Settings!);
                var writeError = WriteAtomically(StrictUtf8.GetBytes(SettingsSerializer.Serialize(legacy.Settings!)));
                var error = writeError is null ? null : $"Migrated settings could not be written to {Paths.PrimaryPath}: {writeError}";
                return new SettingsLoadResult(legacy.Settings!, SettingsLoadStatus.MigratedFromLegacy, corrections, null, error);

            case ReadKind.Missing:
                return Defaults(SettingsLoadStatus.CreatedDefaults, null);

            default:
                // The legacy file lives in the program folder and is never written or moved by this version.
                return Defaults(SettingsLoadStatus.CreatedDefaults, $"The old settings file {legacyPath} could not be used and was ignored: {legacy.Error}");
        }
    }

    private static SettingsLoadResult Defaults(SettingsLoadStatus status, string? error, string? backupPath = null)
    {
        return new SettingsLoadResult(ApplicationSettings.CreateDefault(), status, [], backupPath, error);
    }

    private static ReadOutcome ReadSettingsFile(string path)
    {
        if (!File.Exists(path))
        {
            return new ReadOutcome(ReadKind.Missing);
        }

        byte[] buffer;
        int length;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Read at most one byte past the limit: enough to know it is too big without trusting Length.
            buffer = new byte[(int)Math.Min(stream.Length, MaxFileBytes) + 1];
            length = 0;
            int read;
            while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0)
            {
                length += read;
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new ReadOutcome(ReadKind.Missing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ReadOutcome(ReadKind.Unreadable, Error: ex.Message);
        }

        if (length > MaxFileBytes)
        {
            return new ReadOutcome(ReadKind.Invalid,
                Error: string.Create(CultureInfo.InvariantCulture, $"the file is larger than the {MaxFileBytes}-byte limit."));
        }

        try
        {
            var text = StrictUtf8.GetString(buffer, 0, length);
            var settings = SettingsSerializer.Deserialize(text);
            return new ReadOutcome(ReadKind.Parsed, settings);
        }
        catch (DecoderFallbackException)
        {
            return new ReadOutcome(ReadKind.Invalid, Error: "the file is not valid UTF-8 text.");
        }
        catch (JsonException ex)
        {
            return new ReadOutcome(ReadKind.Invalid, Error: ex.Message);
        }
    }

    /// <summary>Moves the primary file to a fresh settings.corrupt-yyyyMMdd-HHmmss[-N].json; never replaces an existing file.</summary>
    private bool TryBackupCorruptFile(out string? backupPath, out string? error)
    {
        var primaryPath = Paths.PrimaryPath;
        var directory = Path.GetDirectoryName(primaryPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(primaryPath);
        var extension = Path.GetExtension(primaryPath);
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        for (var attempt = 1; attempt <= 1000; attempt++)
        {
            var suffix = attempt == 1 ? string.Empty : "-" + attempt.ToString(CultureInfo.InvariantCulture);
            var candidate = Path.Combine(directory, $"{name}.corrupt-{stamp}{suffix}{extension}");
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                continue;
            }

            try
            {
                File.Move(primaryPath, candidate, overwrite: false);
                backupPath = candidate;
                error = null;
                return true;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Someone created the name between our check and the move; try the next one.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                backupPath = null;
                error = ex.Message;
                return false;
            }
        }

        backupPath = null;
        error = "no unused backup file name was available.";
        return false;
    }

    /// <summary>Returns null on success, otherwise the error. Leaves no temp file behind either way.</summary>
    private string? WriteAtomically(byte[] content)
    {
        var targetPath = Paths.PrimaryPath;
        string? tempPath = null;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(directory, $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, targetPath, overwrite: true);
            tempPath = null;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var cleanupError = tempPath is null ? null : TryDelete(tempPath);
            return cleanupError is null ? ex.Message : $"{ex.Message} (the temporary file {tempPath} could not be removed: {cleanupError})";
        }
    }

    /// <summary>True when <paramref name="path"/> exists and its bytes equal <paramref name="expected"/>; false when it cannot be read.</summary>
    private static bool FileHasContent(string path, byte[] expected)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length != expected.Length)
            {
                return false;
            }

            var actual = new byte[expected.Length];
            stream.ReadExactly(actual);
            return actual.AsSpan().SequenceEqual(expected);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Missing, locked or shrinking under us: write, and let the write report any real problem.
            return false;
        }
    }

    private static string? TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private enum ReadKind
    {
        Missing,
        Parsed,
        Unreadable,
        Invalid
    }

    private sealed record ReadOutcome(ReadKind Kind, ApplicationSettings? Settings = null, string? Error = null);
}
