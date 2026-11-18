namespace WinXCorners.Core.Settings;

/// <summary>
/// What a <see cref="SettingsStore"/> may do on disk. Decided once from whether the process is elevated
/// (<see cref="For"/>), so the rule is in one tested place instead of in every caller.
/// </summary>
/// <remarks>
/// The settings folder lives under the user's profile, where any program running as the user can replace files and
/// folders with links (junctions, object-manager symbolic links, hard links). A normal-rights process following such
/// a link can only reach what the user could reach anyway. An elevated process following one would move, create or
/// overwrite files anywhere with the administrator's token (review V2 D1). So an elevated store does the minimum:
/// it reads and writes the one settings file in place, through handles the app has verified are not redirected, and
/// never moves, renames, backs up or migrates anything. Whatever it cannot read safely it leaves untouched for a
/// normal-rights session to deal with.
/// </remarks>
/// <param name="IsElevatedSession">The policy is for an elevated process.</param>
/// <param name="MoveUnreadableFileAside">An invalid settings file is renamed to a timestamped backup before it is replaced.</param>
/// <param name="MigrateLegacyFile">The pre-1.5 file beside the executable is read and copied to the new location.</param>
/// <param name="WriteThroughTemporaryFile">Saves write a temp file and rename it over settings.json (atomic).</param>
/// <param name="RequireVerifiedFileAccess">Every read and write goes through an <see cref="IVerifiedSettingsFile"/>.</param>
public sealed record SettingsStorePolicy(
    bool IsElevatedSession,
    bool MoveUnreadableFileAside,
    bool MigrateLegacyFile,
    bool WriteThroughTemporaryFile,
    bool RequireVerifiedFileAccess)
{
    /// <summary>A normal-rights session: recover, migrate and save atomically, as before.</summary>
    public static SettingsStorePolicy Normal { get; } = new(false, true, true, true, false);

    /// <summary>An elevated session: verified, in-place access to settings.json only; nothing is moved or created elsewhere.</summary>
    public static SettingsStorePolicy Elevated { get; } = new(true, false, false, false, true);

    public static SettingsStorePolicy For(bool isElevated) => isElevated ? Elevated : Normal;
}

/// <summary>How a verified read ended.</summary>
public enum VerifiedReadStatus
{
    /// <summary>The file (or its folder) does not exist.</summary>
    Missing,

    /// <summary>The bytes were read through a verified handle.</summary>
    Read,

    /// <summary>
    /// The folder or the file is redirected (a reparse point, a hard link, or a handle whose final path is not the
    /// expected one). Nothing was read and nothing may be written.
    /// </summary>
    Refused,

    /// <summary>An I/O error (sharing violation, access denied): possibly transient.</summary>
    Failed
}

/// <param name="Content">The bytes read (at most the requested limit); null unless <see cref="VerifiedReadStatus.Read"/>.</param>
/// <param name="Error">What went wrong, for <see cref="VerifiedReadStatus.Refused"/> and <see cref="VerifiedReadStatus.Failed"/>.</param>
public sealed record VerifiedRead(VerifiedReadStatus Status, byte[]? Content = null, string? Error = null);

/// <summary>
/// Access to settings.json for an elevated store. The app implements it with handle-based checks (it cannot be done
/// in portable code): the folder and the file are opened without following reparse points, each handle's final path
/// must be the expected one, the file must have a single link, and files are created only relative to a verified
/// folder handle, so nothing is ever created or written through a redirected path.
/// </summary>
public interface IVerifiedSettingsFile
{
    /// <summary>Reads at most <paramref name="maxBytes"/> bytes of settings.json. Never throws for I/O problems.</summary>
    VerifiedRead Read(int maxBytes);

    /// <summary>
    /// Replaces the content of settings.json in place (creating the folder and the file if they do not exist), after
    /// the same checks as <see cref="Read"/>. Returns null on success, otherwise why nothing (or, after a failure part
    /// way through the write, not everything) was written. Never throws for I/O problems.
    /// </summary>
    string? Write(byte[] content);
}
