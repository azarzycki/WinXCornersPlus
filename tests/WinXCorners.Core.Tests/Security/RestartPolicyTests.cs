using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

public class RestartPolicyTests
{
    private const RestartMethod Direct = RestartMethod.StartDirectly;
    private const RestartMethod Prompt = RestartMethod.ThroughElevationPrompt;
    private const RestartMethod Shell = RestartMethod.ThroughDesktopShell;
    private const RestartMethod No = RestartMethod.NotPossible;

    private const RestartRequest Same = RestartRequest.SameRights;
    private const RestartRequest Admin = RestartRequest.AsAdministrator;
    private const RestartRequest Without = RestartRequest.WithoutAdministrator;

    private const ElevationMode Normal = ElevationMode.NotElevated;
    private const ElevationMode Split = ElevationMode.ElevatedWithStandardShell;
    private const ElevationMode AllAdmin = ElevationMode.ElevatedWithoutStandardRights;

    private const InstallLocationKind Protected = InstallLocationKind.Protected;
    private const InstallLocationKind Writable = InstallLocationKind.UserWritable;
    private const InstallLocationKind Unknown = InstallLocationKind.Unknown;

    /// <summary>The whole table: every request, every elevation state, every install kind.</summary>
    [Theory]
    // Not elevated: nothing to protect. "As administrator" goes through the UAC prompt, which is real from normal rights.
    [InlineData(Same, Normal, Protected, Direct, false, false)]
    [InlineData(Same, Normal, Writable, Direct, false, false)]
    [InlineData(Same, Normal, Unknown, Direct, false, false)]
    [InlineData(Admin, Normal, Protected, Prompt, false, false)]
    [InlineData(Admin, Normal, Writable, Prompt, false, false)]
    [InlineData(Admin, Normal, Unknown, Prompt, false, false)]
    [InlineData(Without, Normal, Protected, Direct, true, false)]
    [InlineData(Without, Normal, Writable, Direct, true, false)]
    [InlineData(Without, Normal, Unknown, Direct, true, false)]
    // Elevated with a normal-rights shell: direct only from a protected folder; otherwise "Restart" drops the rights
    // (without --no-elevate, so "Always run as administrator" prompts for real) and "as administrator" is refused.
    [InlineData(Same, Split, Protected, Direct, false, false)]
    [InlineData(Same, Split, Writable, Shell, false, true)]
    [InlineData(Same, Split, Unknown, Shell, false, true)]
    [InlineData(Admin, Split, Protected, Direct, false, false)]
    [InlineData(Admin, Split, Writable, No, false, false)]
    [InlineData(Admin, Split, Unknown, No, false, false)]
    [InlineData(Without, Split, Protected, Shell, true, false)]
    [InlineData(Without, Split, Writable, Shell, true, false)]
    [InlineData(Without, Split, Unknown, Shell, true, false)]
    // No normal-rights context at all: no boundary to protect, so the restart is direct; dropping the rights is impossible.
    [InlineData(Same, AllAdmin, Protected, Direct, false, false)]
    [InlineData(Same, AllAdmin, Writable, Direct, false, false)]
    [InlineData(Same, AllAdmin, Unknown, Direct, false, false)]
    [InlineData(Admin, AllAdmin, Protected, Direct, false, false)]
    [InlineData(Admin, AllAdmin, Writable, Direct, false, false)]
    [InlineData(Admin, AllAdmin, Unknown, Direct, false, false)]
    [InlineData(Without, AllAdmin, Protected, No, false, false)]
    [InlineData(Without, AllAdmin, Writable, No, false, false)]
    [InlineData(Without, AllAdmin, Unknown, No, false, false)]
    public void Decide_covers_the_whole_table(
        RestartRequest request, ElevationMode mode, InstallLocationKind install, RestartMethod method, bool noElevate, bool drops)
    {
        var decision = RestartPolicy.Decide(request, mode, install);

        Assert.Equal(method, decision.Method);
        Assert.Equal(noElevate, decision.NoElevate);
        Assert.Equal(drops, decision.DropsAdministratorRights);
        Assert.Equal(method == No, decision.RefusalText is not null);
    }

    [Theory]
    [InlineData(Admin, Split, RestartPolicy.NoSilentElevatedRestartText)]
    [InlineData(Without, AllAdmin, RestartPolicy.NoStandardRightsText)]
    public void A_refusal_carries_its_reason(RestartRequest request, ElevationMode mode, string expected)
    {
        Assert.Equal(expected, RestartPolicy.Decide(request, mode, Writable).RefusalText);
    }

    private static IEnumerable<ElevationMode> AllModes() =>
        Enum.GetValues<ElevationMode>().Append((ElevationMode)77);

    private static IEnumerable<InstallLocationKind> AllKinds() =>
        Enum.GetValues<InstallLocationKind>().Append((InstallLocationKind)77);

    private static IEnumerable<RestartRequest> AllRequests() =>
        Enum.GetValues<RestartRequest>().Append((RestartRequest)77);

    [Fact]
    public void The_UAC_prompt_is_used_only_by_a_process_that_is_not_elevated()
    {
        // runas from an elevated process shows no prompt, so it would start the files elevated silently.
        foreach (var request in AllRequests())
        foreach (var mode in AllModes().Where(static m => m != Normal))
        foreach (var install in AllKinds())
        {
            Assert.NotEqual(Prompt, RestartPolicy.Decide(request, mode, install).Method);
        }
    }

    [Fact]
    public void With_a_normal_rights_desktop_an_elevated_session_never_starts_unprotected_files_with_its_own_token()
    {
        foreach (var request in AllRequests())
        foreach (var mode in new[] { Split, (ElevationMode)77 })
        foreach (var install in AllKinds().Where(static k => k != Protected))
        {
            var method = RestartPolicy.Decide(request, mode, install).Method;
            Assert.True(method is Shell or No, $"{request}/{mode}/{install} -> {method}");
        }
    }

    [Fact]
    public void An_undefined_elevation_state_is_treated_as_elevated_with_a_normal_rights_desktop()
    {
        foreach (var request in AllRequests())
        foreach (var install in AllKinds())
        {
            Assert.Equal(RestartPolicy.Decide(request, Split, install), RestartPolicy.Decide(request, (ElevationMode)77, install));
        }
    }

    [Fact]
    public void An_undefined_request_from_normal_rights_is_a_plain_restart()
    {
        Assert.Equal(RestartPolicy.Decide(Same, Normal, Writable), RestartPolicy.Decide((RestartRequest)77, Normal, Writable));
    }

    [Fact]
    public void Only_a_restart_that_drops_the_rights_skips_no_elevate_while_using_the_shell()
    {
        foreach (var request in AllRequests())
        foreach (var mode in AllModes())
        foreach (var install in AllKinds())
        {
            var decision = RestartPolicy.Decide(request, mode, install);
            if (decision.Method == Shell)
            {
                Assert.NotEqual(decision.NoElevate, decision.DropsAdministratorRights);
            }
            else
            {
                Assert.False(decision.DropsAdministratorRights);
            }
        }
    }

    [Fact]
    public void The_menu_says_when_Restart_drops_administrator_rights()
    {
        Assert.Equal("Restart without administrator rights", RestartPolicy.MenuText(RestartPolicy.Decide(Same, Split, Writable)));
        Assert.Equal("Restart", RestartPolicy.MenuText(RestartPolicy.Decide(Same, Split, Protected)));
        Assert.Equal("Restart", RestartPolicy.MenuText(RestartPolicy.Decide(Same, Normal, Writable)));
        Assert.Equal("Restart", RestartPolicy.MenuText(RestartPolicy.Decide(Same, AllAdmin, Writable)));
    }

    [Fact]
    public void The_log_line_explains_a_restart_without_administrator_rights()
    {
        var text = RestartPolicy.StartedText(RestartPolicy.Decide(Same, Split, Unknown));

        Assert.StartsWith("Restarting without administrator rights.", text, StringComparison.Ordinal);
        Assert.Contains(RestartPolicy.UnprotectedFolderText, text, StringComparison.Ordinal);
        Assert.Contains("Always run as administrator", text, StringComparison.Ordinal);
        Assert.Equal("Restarting (StartDirectly).", RestartPolicy.StartedText(RestartPolicy.Decide(Same, Split, Protected)));
    }

    [Fact]
    public void A_failed_restart_without_administrator_rights_says_why_it_was_not_elevated_instead()
    {
        var text = RestartPolicy.FailureText(RestartPolicy.Decide(Same, Split, Writable), "The desktop shell is not running.");

        Assert.Equal(
            "WinXCornersPlus could not restart without administrator rights: The desktop shell is not running. " +
            RestartPolicy.UnprotectedFolderText + " It keeps running.",
            text);
    }

    [Fact]
    public void A_refused_restart_reports_the_refusal_rather_than_the_platform_error()
    {
        var text = RestartPolicy.FailureText(RestartPolicy.Decide(Admin, Split, Writable), "ignored");

        Assert.Equal("WinXCornersPlus could not restart: " + RestartPolicy.NoSilentElevatedRestartText + " It keeps running.", text);
    }

    [Theory]
    [InlineData(null, "WinXCornersPlus could not restart: Unknown reason. It keeps running.")]
    [InlineData("", "WinXCornersPlus could not restart: Unknown reason. It keeps running.")]
    [InlineData(" . ", "WinXCornersPlus could not restart: Unknown reason. It keeps running.")]
    [InlineData("The administrator prompt was declined.", "WinXCornersPlus could not restart: The administrator prompt was declined. It keeps running.")]
    [InlineData("  Access is denied  ", "WinXCornersPlus could not restart: Access is denied. It keeps running.")]
    [InlineData("Access is denied...", "WinXCornersPlus could not restart: Access is denied. It keeps running.")]
    public void A_failure_message_has_exactly_one_full_stop_after_the_reason(string? error, string expected)
    {
        Assert.Equal(expected, RestartPolicy.FailureText(RestartPolicy.Decide(Admin, Normal, Writable), error));
    }

    [Fact]
    public void The_texts_reject_a_missing_decision()
    {
        Assert.Throws<ArgumentNullException>(() => RestartPolicy.MenuText(null!));
        Assert.Throws<ArgumentNullException>(() => RestartPolicy.StartedText(null!));
        Assert.Throws<ArgumentNullException>(() => RestartPolicy.FailureText(null!, "x"));
    }

    [Fact]
    public void The_not_possible_texts_name_their_causes()
    {
        Assert.Contains("User Account Control is off", RestartPolicy.NoStandardRightsText, StringComparison.Ordinal);
        Assert.Contains("built-in Administrator", RestartPolicy.NoStandardRightsText, StringComparison.Ordinal);
        Assert.Contains("already runs as administrator", RestartPolicy.NoSilentElevatedRestartText, StringComparison.Ordinal);
        Assert.Contains("without administrator rights can change", RestartPolicy.UnprotectedFolderText, StringComparison.Ordinal);
    }
}
