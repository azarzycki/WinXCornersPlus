using System.Security;
using System.Security.Principal;
using Microsoft.Win32;
using WinXCorners.Core.Security;

namespace WinXCorners.App;

/// <summary>
/// Keeps the elevated-session approval (Core's settings seal, brief design point 2) in
/// <c>HKEY_LOCAL_MACHINE\SOFTWARE\WinXCornersPlus\ElevatedApprovals</c>, one REG_SZ value per user, named by SID.
/// </summary>
/// <remarks>
/// Why HKLM: HKLM\SOFTWARE grants ordinary users read access only, so only an administrator (in practice the elevated
/// instance itself) can create or change the approval. A program running with the user's normal rights can edit
/// settings.json but cannot forge the matching approval, so its edit is noticed on the next elevated start.
/// The 64-bit view is used explicitly so a 32-bit build would not read a different (redirected) key.
/// </remarks>
internal sealed class ApprovedSettingsStore : IApprovedSettingsStore
{
    internal const string DefaultKeyPath = @"SOFTWARE\WinXCornersPlus\ElevatedApprovals";

    private readonly string _keyPath;
    private readonly string? _valueName;

    /// <param name="keyPath">Key under HKLM; tests pass a throw-away path.</param>
    /// <param name="userSid">Value name; defaults to the current user's SID.</param>
    internal ApprovedSettingsStore(string keyPath = DefaultKeyPath, string? userSid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        _keyPath = keyPath;
        _valueName = userSid ?? CurrentUserSid();
    }

    /// <summary>The stored approval, or null when there is none, it is not a string, or it cannot be read.</summary>
    public string? Read()
    {
        if (_valueName is null)
        {
            return null;
        }

        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(_keyPath, writable: false);
            return key?.GetValue(_valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        catch (SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Stores <paramref name="approval"/>; false when not elevated or the registry refuses.</summary>
    public bool TryWrite(string approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        if (_valueName is null)
        {
            return false;
        }

        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            // The new key inherits HKLM\SOFTWARE's ACL: Users read, Administrators/SYSTEM full control.
            using var key = hive.CreateSubKey(_keyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue(_valueName, approval, RegistryValueKind.String);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Removes this user's value, and the key when it is left empty. For tests and clean-up; needs admin.</summary>
    internal bool TryDelete()
    {
        if (_valueName is null)
        {
            return false;
        }

        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using (var key = hive.OpenSubKey(_keyPath, writable: true))
            {
                if (key is null)
                {
                    return true;
                }

                key.DeleteValue(_valueName, throwOnMissingValue: false);
                if (key.ValueCount > 0 || key.SubKeyCount > 0)
                {
                    return true;
                }
            }

            hive.DeleteSubKey(_keyPath, throwOnMissingSubKey: false);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false; // The key gained a subkey between the check and the delete.
        }
    }

    private static string? CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value;
    }
}
