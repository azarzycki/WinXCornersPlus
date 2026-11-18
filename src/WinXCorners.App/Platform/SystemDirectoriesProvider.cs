using WinXCorners.Core.Platform;

namespace WinXCorners.App;

/// <summary>Reads the well-known folders Core reasons about from the OS (known-folder APIs, not environment variables).</summary>
internal static class SystemDirectoriesProvider
{
    internal static SystemDirectories Get()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        // Why not %ProgramFiles% etc.: environment variables are inherited from whoever started us and can be set
        // by any program running as the user; the known-folder lookups behind GetFolderPath cannot.
        string[] candidates =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            windows
        ];

        var protectedRoots = candidates
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SystemDirectories(
            Windows: windows,
            System32: Environment.SystemDirectory,
            UserProfile: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ProtectedRoots: protectedRoots);
    }
}
