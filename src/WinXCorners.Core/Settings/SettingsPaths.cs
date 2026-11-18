namespace WinXCorners.Core.Settings;

/// <summary>Where settings are stored.</summary>
/// <param name="PrimaryPath">The file read and written by this version.</param>
/// <param name="LegacyPath">The pre-1.5 location beside the executable, read once for migration and never written; <c>null</c> to skip migration.</param>
public sealed record SettingsPaths(string PrimaryPath, string? LegacyPath)
{
    public const string FolderName = "WinXCornersPlus";
    public const string FileName = "settings.json";

    /// <summary>
    /// Primary: <c>&lt;roaming&gt;\WinXCornersPlus\settings.json</c> (per user, outside the program folder).
    /// Legacy: <c>&lt;appBase&gt;\settings.json</c>, where v1.4.0 and earlier kept it.
    /// </summary>
    public static SettingsPaths ForApp(string roamingAppDataDirectory, string appBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roamingAppDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(appBaseDirectory);
        return new SettingsPaths(
            Path.Combine(roamingAppDataDirectory, FolderName, FileName),
            Path.Combine(appBaseDirectory, FileName));
    }
}
