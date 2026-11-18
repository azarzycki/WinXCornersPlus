using System.Security.AccessControl;
using System.Security.Principal;
using WinXCorners.App.Tests.Infrastructure;
using WinXCorners.Core.Security;
using Xunit.Abstractions;

namespace WinXCorners.App.Tests.Platform;

/// <summary>The ACL probe and Core's assessor on real NTFS folders.</summary>
public sealed class InstallLocationProbeTests
{
    private readonly ITestOutputHelper _output;

    public InstallLocationProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    [Fact]
    public void A_folder_in_temp_is_writable_by_non_administrators()
    {
        using var temp = new TempDirectory();
        Assert.True(InstallLocationProbe.IsWritableByNonAdministrators(temp.Path));
    }

    [Fact]
    public void System32_is_not_writable_by_non_administrators()
    {
        Assert.False(InstallLocationProbe.IsWritableByNonAdministrators(Environment.SystemDirectory));
    }

    [Fact]
    public void Program_Files_is_not_writable_by_non_administrators()
    {
        Assert.False(InstallLocationProbe.IsWritableByNonAdministrators(ProgramFiles));
    }

    [Fact]
    public void A_missing_folder_is_unknown()
    {
        using var temp = new TempDirectory();
        Assert.Null(InstallLocationProbe.IsWritableByNonAdministrators(Path.Combine(temp.Path, "missing")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\bad\0path")]
    [InlineData("\\\\?\\")]
    public void Malformed_paths_are_unknown_and_do_not_throw(string path)
    {
        Assert.Null(InstallLocationProbe.IsWritableByNonAdministrators(path));
    }

    [Fact]
    public void Assessor_end_to_end_temp_folder_is_user_writable()
    {
        using var temp = new TempDirectory();
        var probe = InstallLocationProbe.IsWritableByNonAdministrators(temp.Path);
        var assessment = InstallLocationAssessor.Assess(temp.Path, SystemDirectoriesProvider.Get().ProtectedRoots, probe);
        Assert.Equal(InstallLocationKind.UserWritable, assessment.Kind);
    }

    [Fact]
    public void Assessor_end_to_end_system32_and_program_files_are_protected()
    {
        var roots = SystemDirectoriesProvider.Get().ProtectedRoots;
        foreach (var folder in new[] { Environment.SystemDirectory, ProgramFiles })
        {
            var assessment = InstallLocationAssessor.Assess(folder, roots, InstallLocationProbe.IsWritableByNonAdministrators(folder));
            Assert.Equal(InstallLocationKind.Protected, assessment.Kind);
        }
    }

    [Fact]
    public void Assessor_end_to_end_missing_folder_is_unknown()
    {
        using var temp = new TempDirectory();
        var missing = Path.Combine(temp.Path, "missing");
        var assessment = InstallLocationAssessor.Assess(missing, SystemDirectoriesProvider.Get().ProtectedRoots,
            InstallLocationProbe.IsWritableByNonAdministrators(missing));
        Assert.Equal(InstallLocationKind.Unknown, assessment.Kind);
    }

    [Fact]
    public void System_directories_are_absolute_and_include_the_protected_roots()
    {
        var directories = SystemDirectoriesProvider.Get();
        Assert.True(Path.IsPathFullyQualified(directories.Windows));
        Assert.True(Path.IsPathFullyQualified(directories.System32));
        Assert.True(Path.IsPathFullyQualified(directories.UserProfile));
        Assert.Contains(directories.ProtectedRoots, root => string.Equals(root, ProgramFiles, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(directories.ProtectedRoots, root => string.Equals(root, directories.Windows, StringComparison.OrdinalIgnoreCase));
    }

    // ---- review V2: the files, and the folders above ------------------------------------------------------

    [Fact]
    public void System32_and_Program_Files_with_real_files_and_all_their_parents_are_not_writable()
    {
        var system32Files = new[] { Path.Combine(Environment.SystemDirectory, "notepad.exe"), Path.Combine(Environment.SystemDirectory, "kernel32.dll") };
        Assert.False(InstallLocationProbe.IsWritableByNonAdministrators(Environment.SystemDirectory, system32Files));

        // A real program under Program Files, if the image has one with a DLL beside it; otherwise the folder alone.
        var dotnet = Path.Combine(ProgramFiles, "dotnet");
        var files = Directory.Exists(dotnet) ? Directory.GetFiles(dotnet, "*.exe").Concat(Directory.GetFiles(dotnet, "*.dll")).Take(4).ToArray() : [];
        _output.WriteLine($"Program Files probe files: {string.Join(", ", files)}");
        Assert.False(InstallLocationProbe.IsWritableByNonAdministrators(Directory.Exists(dotnet) ? dotnet : ProgramFiles, files));
    }

    [SkippableFact]
    public void A_locked_down_folder_inside_a_user_writable_parent_is_writable()
    {
        Skip.IfNot(TestEnvironment.IsElevated, "Making Administrators the owner of the test folder needs administrator rights.");
        using var temp = new TempDirectory();
        var locked = Path.Combine(temp.Path, "locked");
        var folder = Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "app.exe"), "x");
        LockDown(folder);
        try
        {
            var rules = folder.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            _output.WriteLine(string.Join(Environment.NewLine, rules.Select(static r => $"{r.IdentityReference} {r.AccessControlType} {r.FileSystemRights}")));

            // Nothing on the folder itself lets a normal user in, but the parent lets the user rename it away and put
            // another folder in its place.
            Assert.True(InstallLocationProbe.IsWritableByNonAdministrators(locked, [Path.Combine(locked, "app.exe")]));
        }
        finally
        {
            Unlock(folder);
        }
    }

    /// <summary>
    /// Under Program Files (admin-only all the way up) a new folder is protected; one file in it that normal users may
    /// write makes the install writable, because that file is part of what runs elevated.
    /// </summary>
    [SkippableFact]
    public void A_writable_executable_or_dll_in_a_protected_folder_makes_it_writable()
    {
        Skip.IfNot(TestEnvironment.IsElevated, "Creating a folder under Program Files needs administrator rights.");
        var folder = Directory.CreateDirectory(Path.Combine(ProgramFiles, "WinXCornersPlus.ProbeTest." + Guid.NewGuid().ToString("N")));
        try
        {
            var exe = Path.Combine(folder.FullName, "app.exe");
            var dll = Path.Combine(folder.FullName, "app.dll");
            File.WriteAllText(exe, "x");
            File.WriteAllText(dll, "x");

            Assert.False(InstallLocationProbe.IsWritableByNonAdministrators(folder.FullName, [exe, dll, null, "", Path.Combine(folder.FullName, "missing.dll")]));

            var file = new FileInfo(dll);
            var security = file.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify, AccessControlType.Allow));
            file.SetAccessControl(security);

            Assert.True(InstallLocationProbe.IsWritableByNonAdministrators(folder.FullName, [exe, dll]));
            Assert.False(InstallLocationProbe.IsWritableByNonAdministrators(folder.FullName, [exe]));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_running_tests_own_files_are_reported_writable()
    {
        // The test output folder is under the user's profile or the runner's work folder: never protected.
        Assert.True(InstallLocationProbe.IsWritableByNonAdministrators(AppContext.BaseDirectory,
            [typeof(InstallLocationProbe).Assembly.Location, Environment.ProcessPath]));
    }

    /// <summary>Only Administrators and SYSTEM, owned by Administrators, nothing inherited.</summary>
    private static void LockDown(DirectoryInfo folder)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        folder.SetAccessControl(security);
    }

    private static void Unlock(DirectoryInfo folder)
    {
        var security = folder.GetAccessControl();
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
        folder.SetAccessControl(security);
    }
}
