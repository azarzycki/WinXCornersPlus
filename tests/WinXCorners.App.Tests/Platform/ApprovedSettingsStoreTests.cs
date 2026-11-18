using Microsoft.Win32;
using WinXCorners.App.Tests.Infrastructure;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;

namespace WinXCorners.App.Tests.Platform;

/// <summary>The HKLM approval store, against a throw-away key under HKLM\SOFTWARE (never the app's real key).</summary>
public sealed class ApprovedSettingsStoreTests : IDisposable
{
    private const string TestRoot = @"SOFTWARE\WinXCornersPlus.Tests";
    private readonly string _keyPath = TestRoot + @"\" + Guid.NewGuid().ToString("N") + @"\ElevatedApprovals";

    public void Dispose()
    {
        if (!TestEnvironment.IsElevated)
        {
            return;
        }

        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        hive.DeleteSubKeyTree(TestRoot, throwOnMissingSubKey: false);
    }

    [Fact]
    public void Missing_key_reads_as_null()
    {
        Assert.Null(new ApprovedSettingsStore(_keyPath, "S-1-5-21-0-0-0-1001").Read());
    }

    [SkippableFact]
    public void Write_then_read_round_trips_and_overwrites()
    {
        Skip.IfNot(TestEnvironment.IsElevated, TestEnvironment.NotElevatedReason);
        var store = new ApprovedSettingsStore(_keyPath);

        Assert.True(store.TryWrite("first"));
        Assert.Equal("first", store.Read());

        Assert.True(store.TryWrite("second, with \"quotes\" and %PATH%"));
        Assert.Equal("second, with \"quotes\" and %PATH%", store.Read());
    }

    [SkippableFact]
    public void The_value_is_named_by_the_current_users_SID_in_the_64_bit_view()
    {
        Skip.IfNot(TestEnvironment.IsElevated, TestEnvironment.NotElevatedReason);
        var store = new ApprovedSettingsStore(_keyPath);
        Assert.True(store.TryWrite(SettingsSeal.CreateApproval(new ApplicationSettings())));

        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.OpenSubKey(_keyPath);
        Assert.NotNull(key);
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        Assert.Equal([sid], key.GetValueNames());
        Assert.Equal(RegistryValueKind.String, key.GetValueKind(sid));
    }

    [SkippableFact]
    public void Each_user_has_their_own_value()
    {
        Skip.IfNot(TestEnvironment.IsElevated, TestEnvironment.NotElevatedReason);
        var alice = new ApprovedSettingsStore(_keyPath, "S-1-5-21-1-2-3-1001");
        var bob = new ApprovedSettingsStore(_keyPath, "S-1-5-21-1-2-3-1002");

        Assert.True(alice.TryWrite("alice"));
        Assert.Null(bob.Read());
        Assert.True(bob.TryWrite("bob"));
        Assert.Equal("alice", alice.Read());

        Assert.True(alice.TryDelete());
        Assert.Null(alice.Read());
        Assert.Equal("bob", bob.Read());
    }

    [SkippableFact]
    public void A_non_string_value_reads_as_null()
    {
        Skip.IfNot(TestEnvironment.IsElevated, TestEnvironment.NotElevatedReason);
        const string sid = "S-1-5-21-1-2-3-1003";
        using (var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var key = hive.CreateSubKey(_keyPath))
        {
            key.SetValue(sid, 42, RegistryValueKind.DWord);
        }

        Assert.Null(new ApprovedSettingsStore(_keyPath, sid).Read());
    }

    [SkippableFact]
    public void TryDelete_removes_the_value_and_the_empty_key()
    {
        Skip.IfNot(TestEnvironment.IsElevated, TestEnvironment.NotElevatedReason);
        var store = new ApprovedSettingsStore(_keyPath);
        Assert.True(store.TryWrite("approval"));

        Assert.True(store.TryDelete());

        Assert.Null(store.Read());
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.OpenSubKey(_keyPath);
        Assert.Null(key);
        Assert.True(store.TryDelete(), "Deleting what is already gone succeeds.");
    }

    [SkippableFact]
    public void The_key_inherits_an_ACL_that_lets_ordinary_users_read_but_not_write()
    {
        Skip.IfNot(TestEnvironment.IsElevated, TestEnvironment.NotElevatedReason);
        Assert.True(new ApprovedSettingsStore(_keyPath).TryWrite("approval"));

        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.OpenSubKey(_keyPath)!;
        var users = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);
        var userRights = key.GetAccessControl()
            .GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<System.Security.AccessControl.RegistryAccessRule>()
            .Where(rule => rule.IdentityReference == users && rule.AccessControlType == System.Security.AccessControl.AccessControlType.Allow)
            .Aggregate((System.Security.AccessControl.RegistryRights)0, (rights, rule) => rights | rule.RegistryRights);

        const System.Security.AccessControl.RegistryRights writeRights =
            System.Security.AccessControl.RegistryRights.SetValue | System.Security.AccessControl.RegistryRights.CreateSubKey |
            System.Security.AccessControl.RegistryRights.Delete | System.Security.AccessControl.RegistryRights.ChangePermissions |
            System.Security.AccessControl.RegistryRights.TakeOwnership;
        Assert.Equal((System.Security.AccessControl.RegistryRights)0, userRights & writeRights);
    }
}
