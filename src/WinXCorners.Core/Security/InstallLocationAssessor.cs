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
/// <remarks>
/// Fails closed. Only a real permission check can show a folder is protected: a folder name proves nothing (C:\Windows
/// holds folders any user can write to, such as Temp, Tasks and System32\spool\drivers\color; Program Files can have
/// loosened ACLs). So the result is <see cref="InstallLocationKind.Protected"/> only when the ACL probe says
/// non-administrators cannot write there and the folder is local. A network folder is
/// <see cref="InstallLocationKind.UserWritable"/> whatever the probe says, because a local check says nothing about who
/// can change the share on the server.
/// </remarks>
public static class InstallLocationAssessor
{
    /// <param name="appDirectory">The folder holding the app's executable.</param>
    /// <param name="protectedRoots">
    /// Folders that are normally admin-only (Program Files, Windows). Only used to word the explanation when the
    /// permissions could not be read; never enough on their own to call a folder protected. Blank or relative
    /// entries are ignored.
    /// </param>
    /// <param name="writableByNonAdministrators">
    /// The result of the app's ACL probe, or null when it could not do one (the result is then
    /// <see cref="InstallLocationKind.Unknown"/> for a local folder).
    /// </param>
    public static InstallLocationAssessment Assess(string? appDirectory, IEnumerable<string?>? protectedRoots,
        bool? writableByNonAdministrators = null)
    {
        var directory = WindowsPath.Normalize(appDirectory);
        var shown = DisplayText.ForDisplay(directory);

        if (writableByNonAdministrators == true)
        {
            return new InstallLocationAssessment(InstallLocationKind.UserWritable, directory,
                $"Programs running under your account can change the files in {Describe(shown)} without administrator approval.");
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

        if (writableByNonAdministrators == false)
        {
            return new InstallLocationAssessment(InstallLocationKind.Protected, directory,
                $"Only administrators can change the files in {shown}.");
        }

        var looksProtected = (protectedRoots ?? [])
            .Where(static root => WindowsPath.IsFullyQualified(root))
            .Any(root => WindowsPath.IsUnder(directory, root));
        var hint = looksProtected
            ? " It is inside a folder that is normally admin-only, but some folders there can be changed by any program."
            : string.Empty;
        return new InstallLocationAssessment(InstallLocationKind.Unknown, directory,
            $"The permissions of {shown} could not be read, so the app cannot tell whether other programs can change its files.{hint}");
    }

    private static string Describe(string shown) => shown.Length == 0 ? "the app's folder" : shown;
}
