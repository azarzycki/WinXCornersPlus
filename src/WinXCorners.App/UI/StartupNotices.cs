using WinXCorners.Core.Security;
using WinXCorners.Core.Settings;

namespace WinXCorners.App;

/// <summary>
/// The plain-language notices shown (tray balloon + log) after start when settings were recovered, unreadable or
/// migrated, or when Run at startup points at another copy of the app.
/// </summary>
/// <remarks>Pure text building; a candidate for Core (it only needs Core types).</remarks>
internal static class StartupNotices
{
    /// <summary>Balloon text is limited by Windows to 255 characters; the log keeps the full notices.</summary>
    internal const int MaxBalloonTextLength = 250;

    /// <param name="load">How settings were loaded.</param>
    /// <param name="settingsPath">Where settings.json lives (asked only for a migration).</param>
    /// <param name="staleStartupEntry">The program a stale Run entry starts, or null.</param>
    internal static IReadOnlyList<string> Build(SettingsLoadResult load, Func<string> settingsPath, string? staleStartupEntry)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(settingsPath);

        var notices = new List<string>();
        switch (load.Status)
        {
            case SettingsLoadStatus.RecoveredFromCorruptFile:
                notices.Add(load.BackupPath is null
                    ? "The settings file could not be understood, so default settings are in use. The file was left in place and will not be overwritten."
                    : $"The settings file could not be understood, so default settings are in use. The old file was kept as {DisplayText.ForDisplay(Path.GetFileName(load.BackupPath))}.");
                break;
            case SettingsLoadStatus.ReadFailed:
                notices.Add("The settings file could not be read, so default settings are in use. The file was left unchanged, " +
                            "and changes will not be saved until WinXCornersPlus is started again.");
                break;
            case SettingsLoadStatus.MigratedFromLegacy:
                notices.Add($"Settings were moved to {DisplayText.ForDisplay(settingsPath())}. The old file next to the program was left in place.");
                break;
        }

        if (!string.IsNullOrWhiteSpace(staleStartupEntry))
        {
            notices.Add($"Run at startup starts another copy of WinXCornersPlus ({DisplayText.ForDisplay(staleStartupEntry)}). " +
                        "Turn Run at startup off and on again in Settings to use this copy.");
        }

        return notices;
    }

    /// <summary>All notices in one balloon, shortened with an ellipsis when too long.</summary>
    internal static string ForBalloon(IReadOnlyList<string> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        var text = string.Join(" ", notices);
        return text.Length <= MaxBalloonTextLength ? text : text[..(MaxBalloonTextLength - 1)] + DisplayText.Ellipsis;
    }
}
