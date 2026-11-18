using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WinXCorners.App;

/// <summary>
/// Reads a folder's ACL to tell whether programs running without administrator rights could change the files in it
/// (and so replace the app's DLLs under an elevated instance; review S1). Feeds Core's InstallLocationAssessor.
/// </summary>
internal static class InstallLocationProbe
{
    // GENERIC_ALL / GENERIC_WRITE are not FileSystemRights members but do appear in (inherit-only) ACEs.
    private const int GenericAll = 0x10000000;
    private const int GenericWrite = 0x40000000;

    /// <summary>
    /// Rights that let the holder add, replace or delete files, or take control of the folder. Plain
    /// WriteAttributes / WriteExtendedAttributes are not enough to plant code and are left out; Write, Modify and
    /// FullControl all include WriteData, so they are covered.
    /// </summary>
    private const FileSystemRights WriteTypeRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership | (FileSystemRights)GenericAll | (FileSystemRights)GenericWrite;

    /// <summary>
    /// True when an Allow rule for Everyone, Authenticated Users, BUILTIN\Users, INTERACTIVE or the current user grants
    /// a write-type right, or the current user owns the folder; false when none does; null when the ACL cannot be read.
    /// </summary>
    /// <remarks>
    /// Inherit-only rules are counted too: they decide who may overwrite the files already in the folder. Deny rules
    /// are ignored, which can only over-report (the safe direction for a warning). Groups are matched by SID
    /// (<see cref="WellKnownSidType"/>), never by localized account name.
    /// </remarks>
    internal static bool? IsWritableByNonAdministrators(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var currentUser = identity.User;
            var nonAdministrators = new List<SecurityIdentifier>
            {
                new(WellKnownSidType.WorldSid, null),
                new(WellKnownSidType.AuthenticatedUserSid, null),
                new(WellKnownSidType.BuiltinUsersSid, null),
                new(WellKnownSidType.InteractiveSid, null)
            };
            if (currentUser is not null)
            {
                nonAdministrators.Add(currentUser);
            }

            var security = new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow &&
                    rule.IdentityReference is SecurityIdentifier sid &&
                    nonAdministrators.Contains(sid) &&
                    (rule.FileSystemRights & WriteTypeRights) != 0)
                {
                    return true;
                }
            }

            // An owner can always rewrite the DACL. Folders created by an elevated installer are owned by
            // Administrators, which is fine; a folder owned by the user (e.g. unzipped into Downloads) is not.
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            return owner is not null && currentUser is not null && owner == currentUser && owner != administrators;
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
}
