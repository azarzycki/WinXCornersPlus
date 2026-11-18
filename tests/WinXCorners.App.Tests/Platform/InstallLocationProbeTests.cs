using WinXCorners.App.Tests.Infrastructure;
using WinXCorners.Core.Security;

namespace WinXCorners.App.Tests.Platform;

/// <summary>The ACL probe and Core's assessor on real NTFS folders.</summary>
public sealed class InstallLocationProbeTests
{
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
}
