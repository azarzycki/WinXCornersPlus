using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WinXCorners.App;

/// <summary>
/// Reads ACLs to tell whether programs running without administrator rights could change the app's files (and so
/// replace the app's code under an elevated instance; review S1, V2). Feeds Core's InstallLocationAssessor.
/// </summary>
/// <remarks>
/// Three things are checked, because any one of them is enough to replace the code that runs elevated:
/// <list type="bullet">
/// <item>the app folder itself: rights to add, replace or delete files in it, or to take control of it;</item>
/// <item>the executable and its DLLs: rights to write, delete or take control of each file;</item>
/// <item>every folder above it: rights that let the holder delete or rename the folder below (delete-child on the
/// parent, delete on the folder, modify or full control, change permissions, take ownership), since a renamed folder
/// can be replaced by a new one. The ordinary "create folders" right that everyone has on a drive root is not such a
/// right and is not counted.</item>
/// </list>
/// Groups are matched by SID (<see cref="WellKnownSidType"/>), never by localized account name. Deny rules are ignored,
/// which can only over-report (the safe direction for a warning).
/// </remarks>
internal static class InstallLocationProbe
{
    // GENERIC_ALL / GENERIC_WRITE are not FileSystemRights members but do appear in (inherit-only) ACEs.
    private const int GenericAll = 0x10000000;
    private const int GenericWrite = 0x40000000;

    /// <summary>
    /// Rights on the app folder that let the holder add, replace or delete files, or take control of the folder. Plain
    /// WriteAttributes / WriteExtendedAttributes are not enough to plant code and are left out; Write, Modify and
    /// FullControl all include WriteData, so they are covered.
    /// </summary>
    private const FileSystemRights FolderWriteRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership | (FileSystemRights)GenericAll | (FileSystemRights)GenericWrite;

    /// <summary>Rights on one of the app's files that let the holder change, replace or take control of it.</summary>
    private const FileSystemRights FileWriteRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | (FileSystemRights)GenericAll | (FileSystemRights)GenericWrite;

    /// <summary>
    /// Rights on a folder above the app folder that let the holder remove or rename what is below it, or take the
    /// folder over. AppendData (create folders) and WriteData (create files) are deliberately absent: everyone has
    /// "create folders" on a drive root, and neither right touches an existing folder.
    /// </summary>
    private const FileSystemRights AncestorReplaceRights =
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete | FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership | (FileSystemRights)GenericAll;

    /// <summary>
    /// True when a program without administrator rights could change the app folder, one of <paramref name="files"/>,
    /// or a folder above it (see the remarks); false when none could; null when an ACL that matters cannot be read.
    /// </summary>
    /// <param name="directory">The app folder.</param>
    /// <param name="files">The executable and its DLLs; blank or missing entries are skipped.</param>
    internal static bool? IsWritableByNonAdministrators(string directory, IEnumerable<string?>? files = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var probe = new Probe(identity.User);

            var folder = new DirectoryInfo(directory);
            // Inherit-only rules count for the app folder: they decide who may overwrite the files already in it.
            var folderResult = probe.Check(folder.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
                FolderWriteRights, includeInheritOnly: true);
            if (folderResult)
            {
                return true;
            }

            foreach (var path in (files ?? []).Where(static f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var file = new FileInfo(path!);
                if (file.Exists && probe.Check(file.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
                        FileWriteRights, includeInheritOnly: false))
                {
                    return true;
                }
            }

            for (var parent = folder.Parent; parent is not null; parent = parent.Parent)
            {
                if (probe.Check(parent.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
                        AncestorReplaceRights, includeInheritOnly: false))
                {
                    return true;
                }
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return null; // Includes PrivilegeNotHeldException.
        }
        catch (IOException)
        {
            return null; // Includes DirectoryNotFoundException.
        }
        catch (ArgumentException)
        {
            return null; // Malformed path.
        }
        catch (SecurityException)
        {
            return null;
        }
        catch (SystemException ex) when (ex is IdentityNotMappedException or PlatformNotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The non-administrator principals and the matching rule.</summary>
    private sealed class Probe
    {
        private readonly SecurityIdentifier? _currentUser;
        private readonly List<SecurityIdentifier> _nonAdministrators;
        private readonly SecurityIdentifier _administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

        internal Probe(SecurityIdentifier? currentUser)
        {
            _currentUser = currentUser;
            _nonAdministrators =
            [
                new(WellKnownSidType.WorldSid, null),
                new(WellKnownSidType.AuthenticatedUserSid, null),
                new(WellKnownSidType.BuiltinUsersSid, null),
                new(WellKnownSidType.InteractiveSid, null)
            ];
            if (currentUser is not null)
            {
                _nonAdministrators.Add(currentUser);
            }
        }

        /// <summary>
        /// True when an Allow rule for a non-administrator grants one of <paramref name="rights"/>, or the current user
        /// owns the object (an owner can always rewrite the DACL; objects created by an elevated installer are owned
        /// by Administrators, which is fine).
        /// </summary>
        internal bool Check(FileSystemSecurity security, FileSystemRights rights, bool includeInheritOnly)
        {
            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow &&
                    (includeInheritOnly || (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0) &&
                    rule.IdentityReference is SecurityIdentifier sid &&
                    _nonAdministrators.Contains(sid) &&
                    (rule.FileSystemRights & rights) != 0)
                {
                    return true;
                }
            }

            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            return owner is not null && _currentUser is not null && owner == _currentUser && owner != _administrators;
        }
    }
}
