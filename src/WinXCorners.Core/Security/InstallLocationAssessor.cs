using WinXCorners.Core.Platform;

namespace WinXCorners.Core.Security;

public enum InstallLocationKind
{
    /// <summary>Only administrators can change the program files.</summary>
    Protected,
    /// <summary>Programs running as the user (or other machines, for a network path) can change the program files.</summary>
    UserWritable,
    /// <summary>The install folder is not known.</summary>
    Unknown
}

/// <param name="Directory">The normalized install folder, or empty when unknown.</param>
/// <param name="Explanation">One plain sentence for the UI.</param>
public sealed record InstallLocationAssessment(InstallLocationKind Kind, string Directory, string Explanation);

/// <summary>
/// Decides whether the folder the app runs from is protected. This matters because an elevated start runs whatever
/// code sits in that folder: if a normal program can replace the files, approving the UAC prompt hands it admin rights.
/// </summary>
public static class InstallLocationAssessor
{
    /// <param name="appDirectory">The folder holding the app's executable.</param>
    /// <param name="protectedRoots">Folders only administrators can change; blank or relative entries are ignored.</param>
    /// <param name="writableByNonAdministrators">
    /// The result of the app's ACL probe, when it could do one. A real permission check beats the folder-name
    /// heuristic in both directions (Program Files can have loosened ACLs; a custom folder can be locked down).
    /// </param>
    public static InstallLocationAssessment Assess(string? appDirectory, IEnumerable<string?>? protectedRoots,
        bool? writableByNonAdministrators = null)
    {
        var directory = WindowsPath.Normalize(appDirectory);
        var shown = DisplayText.ForDisplay(directory);

        if (writableByNonAdministrators is bool writable)
        {
            return writable
                ? new InstallLocationAssessment(InstallLocationKind.UserWritable, directory,
                    $"Programs running under your account can change the files in {Describe(shown)} without administrator approval.")
                : new InstallLocationAssessment(InstallLocationKind.Protected, directory,
                    $"Only administrators can change the files in {Describe(shown)}.");
        }

        if (directory.Length == 0)
        {
            return new InstallLocationAssessment(InstallLocationKind.Unknown, directory,
                "The app could not determine its install folder, so it cannot tell who can change its files.");
        }

        if (WindowsPath.IsUnc(directory))
        {
            return new InstallLocationAssessment(InstallLocationKind.UserWritable, directory,
                $"The app runs from the network folder {shown}, which other computers and accounts may be able to change.");
        }

        if (!WindowsPath.IsFullyQualified(directory))
        {
            return new InstallLocationAssessment(InstallLocationKind.UserWritable, directory,
                $"The install folder {shown} is not a full path, so it is treated as changeable by any program.");
        }

        var isProtected = (protectedRoots ?? [])
            .Where(static root => WindowsPath.IsFullyQualified(root))
            .Any(root => WindowsPath.IsUnder(directory, root));

        return isProtected
            ? new InstallLocationAssessment(InstallLocationKind.Protected, directory,
                $"The app is installed in {shown}, a folder that only administrators can change.")
            : new InstallLocationAssessment(InstallLocationKind.UserWritable, directory,
                $"The app is installed in {shown}, a folder that programs running under your account can change without administrator approval.");
    }

    private static string Describe(string shown) => shown.Length == 0 ? "the app's folder" : shown;
}
