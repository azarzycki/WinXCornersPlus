using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

public class RestartPolicyTests
{
    private const RestartMethod Direct = RestartMethod.StartDirectly;
    private const RestartMethod Prompt = RestartMethod.ThroughElevationPrompt;
    private const RestartMethod Shell = RestartMethod.ThroughDesktopShell;
    private const RestartMethod No = RestartMethod.NotPossible;

    /// <summary>The whole table: every request, every elevation state, every install kind.</summary>
    [Theory]
    // Not elevated: nothing to protect; "as administrator" always goes through the UAC prompt.
    [InlineData(RestartRequest.SameRights, ElevationMode.NotElevated, InstallLocationKind.Protected, Direct)]
    [InlineData(RestartRequest.SameRights, ElevationMode.NotElevated, InstallLocationKind.UserWritable, Direct)]
    [InlineData(RestartRequest.SameRights, ElevationMode.NotElevated, InstallLocationKind.Unknown, Direct)]
    [InlineData(RestartRequest.AsAdministrator, ElevationMode.NotElevated, InstallLocationKind.Protected, Prompt)]
    [InlineData(RestartRequest.AsAdministrator, ElevationMode.NotElevated, InstallLocationKind.UserWritable, Prompt)]
    [InlineData(RestartRequest.AsAdministrator, ElevationMode.NotElevated, InstallLocationKind.Unknown, Prompt)]
    [InlineData(RestartRequest.WithoutAdministrator, ElevationMode.NotElevated, InstallLocationKind.Protected, Direct)]
    [InlineData(RestartRequest.WithoutAdministrator, ElevationMode.NotElevated, InstallLocationKind.UserWritable, Direct)]
    [InlineData(RestartRequest.WithoutAdministrator, ElevationMode.NotElevated, InstallLocationKind.Unknown, Direct)]
    // Elevated with a normal-rights shell: a direct elevated restart only from a protected folder (review V2 D5).
    [InlineData(RestartRequest.SameRights, ElevationMode.ElevatedWithStandardShell, InstallLocationKind.Protected, Direct)]
    [InlineData(RestartRequest.SameRights, ElevationMode.ElevatedWithStandardShell, InstallLocationKind.UserWritable, Prompt)]
    [InlineData(RestartRequest.SameRights, ElevationMode.ElevatedWithStandardShell, InstallLocationKind.Unknown, Prompt)]
    [InlineData(RestartRequest.AsAdministrator, ElevationMode.ElevatedWithStandardShell, InstallLocationKind.Protected, Direct)]
    [InlineData(RestartRequest.AsAdministrator, ElevationMode.ElevatedWithStandardShell, InstallLocationKind.UserWritable, Prompt)]
    [InlineData(RestartRequest.AsAdministrator, ElevationMode.ElevatedWithStandardShell, InstallLocationKind.Unknown, Prompt)]
    [InlineData(RestartRequest.WithoutAdministrator, ElevationMode.ElevatedWithStandardShell, InstallLocationKind.Protected, Shell)]
    [InlineData(RestartRequest.WithoutAdministrator, ElevationMode.ElevatedWithStandardShell, InstallLocationKind.UserWritable, Shell)]
    [InlineData(RestartRequest.WithoutAdministrator, ElevationMode.ElevatedWithStandardShell, InstallLocationKind.Unknown, Shell)]
    // Elevated with no normal-rights context: the same rule for an elevated restart; no way to drop the rights.
    [InlineData(RestartRequest.SameRights, ElevationMode.ElevatedWithoutStandardRights, InstallLocationKind.Protected, Direct)]
    [InlineData(RestartRequest.SameRights, ElevationMode.ElevatedWithoutStandardRights, InstallLocationKind.UserWritable, Prompt)]
    [InlineData(RestartRequest.SameRights, ElevationMode.ElevatedWithoutStandardRights, InstallLocationKind.Unknown, Prompt)]
    [InlineData(RestartRequest.AsAdministrator, ElevationMode.ElevatedWithoutStandardRights, InstallLocationKind.Protected, Direct)]
    [InlineData(RestartRequest.AsAdministrator, ElevationMode.ElevatedWithoutStandardRights, InstallLocationKind.UserWritable, Prompt)]
    [InlineData(RestartRequest.AsAdministrator, ElevationMode.ElevatedWithoutStandardRights, InstallLocationKind.Unknown, Prompt)]
    [InlineData(RestartRequest.WithoutAdministrator, ElevationMode.ElevatedWithoutStandardRights, InstallLocationKind.Protected, No)]
    [InlineData(RestartRequest.WithoutAdministrator, ElevationMode.ElevatedWithoutStandardRights, InstallLocationKind.UserWritable, No)]
    [InlineData(RestartRequest.WithoutAdministrator, ElevationMode.ElevatedWithoutStandardRights, InstallLocationKind.Unknown, No)]
    public void Decide_covers_the_whole_table(RestartRequest request, ElevationMode mode, InstallLocationKind install, RestartMethod expected)
    {
        Assert.Equal(expected, RestartPolicy.Decide(request, mode, install));
    }

    [Fact]
    public void An_elevated_session_never_restarts_directly_from_a_folder_that_is_not_protected()
    {
        foreach (var request in Enum.GetValues<RestartRequest>())
        foreach (var mode in new[] { ElevationMode.ElevatedWithStandardShell, ElevationMode.ElevatedWithoutStandardRights })
        foreach (var install in new[] { InstallLocationKind.UserWritable, InstallLocationKind.Unknown, (InstallLocationKind)77 })
        {
            Assert.NotEqual(RestartMethod.StartDirectly, RestartPolicy.Decide(request, mode, install));
        }
    }

    [Fact]
    public void The_not_possible_text_names_both_causes()
    {
        Assert.Contains("User Account Control is off", RestartPolicy.NoStandardRightsText, StringComparison.Ordinal);
        Assert.Contains("built-in Administrator", RestartPolicy.NoStandardRightsText, StringComparison.Ordinal);
    }
}
