using WinXCorners.Core.Model;
using WinXCorners.Core.Security;
using Ids = WinXCorners.Core.Security.SecurityPosture.Ids;

namespace WinXCorners.Core.Tests.Security;

public class SecurityPostureTests
{
    private const string SettingsPath = @"C:\Users\alex\AppData\Roaming\WinXCornersPlus\settings.json";

    private static readonly InstallLocationAssessment[] Installs =
        [TestData.ProtectedInstall, TestData.WritableInstall, TestData.UnknownInstall];

    private static SecurityContext Context(bool elevated = false, InstallLocationAssessment? install = null, bool startup = false) =>
        new(elevated, install ?? TestData.ProtectedInstall, startup, SettingsPath);

    /// <summary>Every combination of the inputs Evaluate looks at.</summary>
    private static IEnumerable<(ApplicationSettings Settings, SecurityContext Context)> AllCombinations()
    {
        foreach (var (elevated, everythingElevated) in new[] { (false, false), (true, false), (true, true) })
        foreach (var install in Installs)
        foreach (var startup in new[] { false, true })
        foreach (var admin in new[] { false, true })
        foreach (var custom in new[] { false, true })
        foreach (var hiddenCommand in new[] { false, true })
        foreach (var hideTray in new[] { false, true })
        {
            var settings = ApplicationSettings.CreateDefault();
            settings.AlwaysRunAsAdministrator = admin;
            settings.EnableCustomCommands = custom;
            settings.AlwaysHideTrayIcon = hideTray;
            settings.CustomCommands[0].Command = "notepad.exe";
            settings.CustomCommands[2] = new CustomCommandSettings { Command = "cmd.exe", Parameters = "/c x", LaunchHidden = hiddenCommand };
            yield return (settings, new SecurityContext(elevated, install, startup, SettingsPath, everythingElevated));
        }
    }

    [Fact]
    public void Evaluate_EachItemAppearsExactlyWhenItsConditionHolds()
    {
        foreach (var (settings, context) in AllCombinations())
        {
            var ids = SecurityPosture.Evaluate(settings, context).Select(static i => i.Id).ToHashSet();
            var notProtected = context.Install.Kind != InstallLocationKind.Protected;
            var expected = new Dictionary<string, bool>
            {
                [Ids.RunningElevated] = context.IsElevated,
                [Ids.LaunchesWithAdministratorRights] = context.Elevation == ElevationMode.ElevatedWithoutStandardRights,
                [Ids.ElevatedFromUserWritableFolder] = context.IsElevated && notProtected,
                [Ids.AlwaysRunAsAdministrator] = settings.AlwaysRunAsAdministrator,
                [Ids.CustomCommandsEnabled] = settings.EnableCustomCommands,
                [Ids.CommandLaunchesHidden] = settings.EnableCustomCommands && settings.CustomCommands[2].LaunchHidden,
                [Ids.TrayIconHidden] = settings.AlwaysHideTrayIcon,
                [Ids.RunsAtStartup] = context.StartsWithWindows
            };

            foreach (var (id, present) in expected)
            {
                Assert.True(present == ids.Contains(id), $"{id} expected {present} for {context} admin={settings.AlwaysRunAsAdministrator}");
            }

            Assert.Equal(expected.Count(static e => e.Value), ids.Count);
        }
    }

    [Fact]
    public void Evaluate_NothingForDefaultsWhenNotElevated()
    {
        Assert.Empty(SecurityPosture.Evaluate(ApplicationSettings.CreateDefault(), Context()));
    }

    [Fact]
    public void Evaluate_OrdersMostSevereFirst()
    {
        foreach (var (settings, context) in AllCombinations())
        {
            var severities = SecurityPosture.Evaluate(settings, context).Select(static i => (int)i.Severity).ToArray();
            Assert.Equal(severities.OrderByDescending(static s => s), severities);
        }

        var all = AllItems(TestData.WritableInstall);
        Assert.Equal(Ids.ElevatedFromUserWritableFolder, all[0].Id);
        Assert.Equal(RiskSeverity.Warning, all[0].Severity);
    }

    [Fact]
    public void Evaluate_SeverityDependsOnInstallAndStartup()
    {
        Assert.Equal(RiskSeverity.Caution, Find(AllItems(TestData.ProtectedInstall), Ids.AlwaysRunAsAdministrator).Severity);
        Assert.Equal(RiskSeverity.Warning, Find(AllItems(TestData.WritableInstall), Ids.AlwaysRunAsAdministrator).Severity);
        Assert.Equal(RiskSeverity.Warning, Find(AllItems(TestData.UnknownInstall), Ids.AlwaysRunAsAdministrator).Severity);

        var settings = ApplicationSettings.CreateDefault();
        settings.AlwaysHideTrayIcon = true;
        Assert.Equal(RiskSeverity.Info, Find(SecurityPosture.Evaluate(settings, Context(startup: false)), Ids.TrayIconHidden).Severity);
        Assert.Equal(RiskSeverity.Caution, Find(SecurityPosture.Evaluate(settings, Context(startup: true)), Ids.TrayIconHidden).Severity);
    }

    [Theory]
    [InlineData(0, "No custom commands are set up yet.")]
    [InlineData(1, "1 custom command is set up.")]
    [InlineData(4, "4 custom commands are set up.")]
    public void Evaluate_CustomCommandsCountsConfiguredCommands(int configured, string expectedStart)
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.EnableCustomCommands = true;
        for (var i = 0; i < configured; i++)
        {
            settings.CustomCommands[i].Command = "app" + i;
        }

        var item = Find(SecurityPosture.Evaluate(settings, Context()), Ids.CustomCommandsEnabled);
        Assert.StartsWith(expectedStart, item.Detail, StringComparison.Ordinal);
        Assert.Contains("settings file", item.Detail, StringComparison.Ordinal);
        Assert.Contains("pointer touches a corner", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_HiddenItemNamesTheHiddenCommandsAndIgnoresEmptySlots()
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.EnableCustomCommands = true;
        settings.CustomCommands[0] = new CustomCommandSettings { Command = "a", LaunchHidden = true };
        settings.CustomCommands[1] = new CustomCommandSettings { Command = "  ", LaunchHidden = true };
        settings.CustomCommands[3] = new CustomCommandSettings { Command = "b", LaunchHidden = true };
        var item = Find(SecurityPosture.Evaluate(settings, Context()), Ids.CommandLaunchesHidden);
        Assert.StartsWith("Commands 1, 4 start", item.Detail, StringComparison.Ordinal);

        settings.CustomCommands[3].LaunchHidden = false;
        item = Find(SecurityPosture.Evaluate(settings, Context()), Ids.CommandLaunchesHidden);
        Assert.StartsWith("Command 1 starts", item.Detail, StringComparison.Ordinal);
        Assert.Contains("Task Manager", item.Detail, StringComparison.Ordinal);
    }

    /// <summary>Hotkey, conditional and invalid commands open no window, so the hidden-window risk does not apply.</summary>
    [Theory]
    [InlineData("!win+d", false)]
    [InlineData("#[Notepad]:(ctrl+s)", false)]
    [InlineData("!nosuchkey", false)]
    [InlineData("calc\u0001.exe", false)]
    [InlineData("notepad.exe", true)]
    [InlineData("  https://example.com  ", true)]
    public void Evaluate_HiddenWindowRiskOnlyForCommandsThatLaunchAProgram(string command, bool reported)
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.EnableCustomCommands = true;
        settings.CustomCommands[2] = new CustomCommandSettings { Command = command, LaunchHidden = true };
        var ids = SecurityPosture.Evaluate(settings, Context()).Select(static i => i.Id);
        Assert.Equal(reported, ids.Contains(Ids.CommandLaunchesHidden));
    }

    [Theory]
    [InlineData(false, "To stop, exit the app and start it normally.")]
    [InlineData(true, "To stop, turn off Always run as administrator, then exit the app and start it again.")]
    public void Evaluate_RunningElevatedSaysHowToStopForTheCurrentSettings(bool alwaysElevated, string advice)
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.AlwaysRunAsAdministrator = alwaysElevated;
        var context = new SecurityContext(true, TestData.ProtectedInstall, false, "x");
        var item = Find(SecurityPosture.Evaluate(settings, context), Ids.RunningElevated);
        Assert.EndsWith(advice, item.Detail, StringComparison.Ordinal);
        Assert.InRange(item.Detail.Length, 1, SecurityPosture.MaxDetailLength);
    }

    [Fact]
    public void Evaluate_ToleratesUnsanitizedSettings()
    {
        var settings = new ApplicationSettings { EnableCustomCommands = true, CustomCommands = [null!, new CustomCommandSettings { Command = null!, LaunchHidden = true }] };
        var ids = SecurityPosture.Evaluate(settings, Context()).Select(static i => i.Id).ToArray();
        Assert.Equal([Ids.CustomCommandsEnabled], ids);
        settings.CustomCommands = null!;
        Assert.Single(SecurityPosture.Evaluate(settings, Context()));
    }

    [Fact]
    public void TrayIconHidden_ExplainsHowToGetBack()
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.AlwaysHideTrayIcon = true;
        var item = Find(SecurityPosture.Evaluate(settings, Context()), Ids.TrayIconHidden);
        Assert.Contains("start WinXCornersPlus again", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeElevation_CoversTheRequiredPoints()
    {
        foreach (var install in Installs)
        foreach (var everyStart in new[] { false, true })
        foreach (var custom in new[] { false, true })
        {
            var settings = ApplicationSettings.CreateDefault();
            settings.EnableCustomCommands = custom;
            var items = SecurityPosture.DescribeElevation(settings, install, everyStart);
            var ids = items.Select(static i => i.Id).ToList();

            Assert.Contains(Ids.ElevationControlsElevatedWindows, ids);
            Assert.Contains(Ids.ElevationLaunchesWithoutAdmin, ids);
            Assert.Contains(Ids.ElevationSettingsApproval, ids);
            Assert.Equal(install.Kind != InstallLocationKind.Protected, ids.Contains(Ids.ElevationUserWritableInstall));
            Assert.Equal(everyStart, ids.Contains(Ids.ElevationPromptEveryStart));
            Assert.Equal(ids.Distinct().Count(), ids.Count);

            var severities = items.Select(static i => (int)i.Severity).ToArray();
            Assert.Equal(severities.OrderByDescending(static s => s), severities);

            Assert.Contains("without administrator rights", Find(items, Ids.ElevationLaunchesWithoutAdmin).Detail, StringComparison.Ordinal);
            Assert.Contains("approve", Find(items, Ids.ElevationSettingsApproval).Detail, StringComparison.Ordinal);
            Assert.Equal(custom, Find(items, Ids.ElevationControlsElevatedWindows).Detail.Contains("custom hotkey", StringComparison.Ordinal));
            if (install.Kind != InstallLocationKind.Protected)
            {
                var warning = Find(items, Ids.ElevationUserWritableInstall);
                Assert.Equal(RiskSeverity.Warning, warning.Severity);
                Assert.Contains("Program Files", warning.Detail, StringComparison.Ordinal);
                Assert.Equal(warning, items[0]);
            }
        }
    }

    [Fact]
    public void Facts_StateWhatTheAppDoes()
    {
        var facts = SecurityPosture.Facts(Context(startup: true, elevated: true));
        Assert.Contains(facts, f => f.Contains("never records keystrokes", StringComparison.Ordinal));
        Assert.Contains(facts, f => f.Contains("no network connections", StringComparison.Ordinal));
        Assert.Contains(facts, f => f.Contains(SettingsPath, StringComparison.Ordinal));
        Assert.Contains(facts, f => f.Contains("Run entry", StringComparison.Ordinal) && f.Contains("Run at startup is on", StringComparison.Ordinal));
        Assert.Contains(facts, f => f.Contains("Unknown publisher", StringComparison.Ordinal));
        Assert.Contains(facts, f => f.Contains("running as administrator", StringComparison.Ordinal));

        var off = SecurityPosture.Facts(Context());
        Assert.Contains(off, f => f.Contains("currently is not", StringComparison.Ordinal));
        Assert.Contains(off, f => f.Contains("without administrator rights", StringComparison.Ordinal));
    }

    [Fact]
    public void Facts_HandlesMissingOrHostileSettingsPath()
    {
        Assert.Contains(SecurityPosture.Facts(Context() with { SettingsPath = "" }), f => f.Contains("an unknown location", StringComparison.Ordinal));
        var hostile = SecurityPosture.Facts(Context() with { SettingsPath = "C:\\x\nIt never does anything." });
        Assert.All(hostile, f => Assert.DoesNotContain('\n', f));
    }

    [Theory]
    [InlineData(nameof(ApplicationSettings.AlwaysRunAsAdministrator), Ids.AlwaysRunAsAdministrator)]
    [InlineData(nameof(ApplicationSettings.EnableCustomCommands), Ids.CustomCommandsEnabled)]
    [InlineData(nameof(ApplicationSettings.AlwaysHideTrayIcon), Ids.TrayIconHidden)]
    [InlineData("LaunchHidden", Ids.CommandLaunchesHidden)]
    public void ForSetting_ReturnsTheItemForEachRiskySwitchEvenBeforeItIsSaved(string setting, string expectedId)
    {
        foreach (var install in Installs)
        {
            var item = SecurityPosture.ForSetting(setting, ApplicationSettings.CreateDefault(), Context(install: install));
            Assert.NotNull(item);
            Assert.Equal(expectedId, item.Id);
        }
    }

    [Fact]
    public void ForSetting_AdminWarnsWhenInstallIsNotProtected()
    {
        var name = nameof(ApplicationSettings.AlwaysRunAsAdministrator);
        Assert.Equal(RiskSeverity.Caution, SecurityPosture.ForSetting(name, new(), Context())!.Severity);
        var writable = SecurityPosture.ForSetting(name, new(), Context(install: TestData.WritableInstall))!;
        Assert.Equal(RiskSeverity.Warning, writable.Severity);
        Assert.Contains("Program Files", writable.Detail, StringComparison.Ordinal);
        Assert.Contains("UAC prompt", writable.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("StartWithWindows")]
    [InlineData("alwaysrunasadministrator")]
    [InlineData("Unknown")]
    public void ForSetting_ReturnsNullForOtherNames(string? name)
    {
        Assert.Null(SecurityPosture.ForSetting(name, new(), Context()));
    }

    /// <summary>
    /// Review V2 D4: "programs it opens never get administrator rights" must not be claimed where it is false. With no
    /// normal-rights context on the account, every text that talks about launches says plainly that they get them.
    /// </summary>
    [Fact]
    public void Without_a_standard_rights_context_no_text_claims_launches_lack_administrator_rights()
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.AlwaysRunAsAdministrator = true;
        settings.EnableCustomCommands = true;
        var context = new SecurityContext(true, TestData.ProtectedInstall, false, SettingsPath, EverythingRunsElevated: true);
        Assert.Equal(ElevationMode.ElevatedWithoutStandardRights, context.Elevation);

        var evaluated = SecurityPosture.Evaluate(settings, context);
        var described = SecurityPosture.DescribeElevation(settings, context.Install, everyStart: true, context.Elevation);
        var forSetting = SecurityPosture.ForSetting(nameof(ApplicationSettings.AlwaysRunAsAdministrator), settings, context)!;
        var facts = SecurityPosture.Facts(context);
        var allText = evaluated.Concat(described).Append(forSetting).Select(static i => i.Title + " " + i.Detail).Concat(facts).ToList();

        Assert.DoesNotContain(allText, t => t.Contains("start without administrator rights", StringComparison.Ordinal) ||
                                           t.Contains("started without administrator rights", StringComparison.Ordinal) ||
                                           t.Contains("do not get admin rights", StringComparison.Ordinal));
        var warning = Find(evaluated, Ids.LaunchesWithAdministratorRights);
        Assert.Equal(RiskSeverity.Warning, warning.Severity);
        Assert.Contains("User Account Control is off", warning.Detail, StringComparison.Ordinal);
        Assert.Contains("built-in Administrator account", warning.Detail, StringComparison.Ordinal);
        Assert.Equal(warning, Find(described, Ids.LaunchesWithAdministratorRights));
        Assert.DoesNotContain(described, static i => i.Id == Ids.ElevationLaunchesWithoutAdmin);
        Assert.Contains("get administrator rights too", Find(evaluated, Ids.RunningElevated).Detail, StringComparison.Ordinal);
        Assert.Contains("get administrator rights too", forSetting.Detail, StringComparison.Ordinal);
        Assert.Contains(facts, static f => f.Contains("get administrator rights too", StringComparison.Ordinal));
    }

    [Fact]
    public void With_a_standard_shell_the_text_says_launches_fail_rather_than_get_administrator_rights()
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.AlwaysRunAsAdministrator = true;
        var context = new SecurityContext(true, TestData.ProtectedInstall, false, SettingsPath);
        Assert.Equal(ElevationMode.ElevatedWithStandardShell, context.Elevation);

        Assert.Contains("or not at all", Find(SecurityPosture.Evaluate(settings, context), Ids.RunningElevated).Detail, StringComparison.Ordinal);
        Assert.Contains("or not at all", Find(SecurityPosture.Evaluate(settings, context), Ids.AlwaysRunAsAdministrator).Detail, StringComparison.Ordinal);
        Assert.Contains("not opened at all",
            Find(SecurityPosture.DescribeElevation(settings, context.Install, everyStart: false), Ids.ElevationLaunchesWithoutAdmin).Detail,
            StringComparison.Ordinal);
        Assert.Contains(SecurityPosture.Facts(context), static f => f.Contains("or not at all", StringComparison.Ordinal));
        Assert.DoesNotContain(SecurityPosture.Evaluate(settings, context), static i => i.Id == Ids.LaunchesWithAdministratorRights);
    }

    [Fact]
    public void A_normal_rights_session_describes_the_switch_for_the_session_it_would_start()
    {
        // Before the UAC prompt the session is not elevated; the text is about the elevated session to come, which has
        // a normal-rights shell (UAC is on, or there would be no prompt).
        var context = Context();
        var item = SecurityPosture.ForSetting(nameof(ApplicationSettings.AlwaysRunAsAdministrator), new ApplicationSettings(), context)!;

        Assert.Contains("or not at all", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AllText_IsWithinLimitsHasNoExclamationMarksAndUsesUniqueKebabCaseIds()
    {
        var items = new List<RiskItem>();
        foreach (var (settings, context) in AllCombinations())
        {
            var evaluated = SecurityPosture.Evaluate(settings, context);
            Assert.Equal(evaluated.Count, evaluated.Select(static i => i.Id).Distinct().Count());
            items.AddRange(evaluated);
            items.AddRange(SecurityPosture.DescribeElevation(settings, context.Install, everyStart: true, context.Elevation.AfterElevation()));
            items.AddRange(new[] { "AlwaysRunAsAdministrator", "EnableCustomCommands", "AlwaysHideTrayIcon", "LaunchHidden" }
                .Select(name => SecurityPosture.ForSetting(name, settings, context)!));
            Assert.All(SecurityPosture.Facts(context), fact => Assert.DoesNotContain('!', fact));
        }

        foreach (var item in items)
        {
            Assert.InRange(item.Title.Length, 1, SecurityPosture.MaxTitleLength);
            Assert.InRange(item.Detail.Length, 1, SecurityPosture.MaxDetailLength);
            Assert.DoesNotContain('!', item.Title + item.Detail);
            Assert.DoesNotContain("may be unsafe", item.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.Matches("^[a-z]+(-[a-z]+)*$", item.Id);
        }

        // Same id always means the same title, so the UI can key on ids.
        foreach (var group in items.GroupBy(static i => i.Id))
        {
            Assert.Single(group.Select(static i => i.Title).Distinct());
        }

        var allIds = typeof(Ids).GetFields().Select(static f => (string)f.GetRawConstantValue()!).ToArray();
        Assert.Equal(allIds.Length, allIds.Distinct().Count());
        Assert.Equal(allIds.Order(), items.Select(static i => i.Id).Distinct().Order());
    }

    [Fact]
    public void Ids_AreStable()
    {
        Assert.Equal("running-elevated", Ids.RunningElevated);
        Assert.Equal("elevated-from-user-writable-folder", Ids.ElevatedFromUserWritableFolder);
        Assert.Equal("always-run-as-administrator", Ids.AlwaysRunAsAdministrator);
        Assert.Equal("custom-commands-enabled", Ids.CustomCommandsEnabled);
        Assert.Equal("command-launches-hidden", Ids.CommandLaunchesHidden);
        Assert.Equal("tray-icon-hidden", Ids.TrayIconHidden);
        Assert.Equal("runs-at-startup", Ids.RunsAtStartup);
        Assert.Equal("elevation-controls-elevated-windows", Ids.ElevationControlsElevatedWindows);
        Assert.Equal("elevation-launches-without-admin", Ids.ElevationLaunchesWithoutAdmin);
        Assert.Equal("elevation-settings-approval", Ids.ElevationSettingsApproval);
        Assert.Equal("elevation-user-writable-install", Ids.ElevationUserWritableInstall);
        Assert.Equal("elevation-prompt-every-start", Ids.ElevationPromptEveryStart);
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => SecurityPosture.Evaluate(null!, Context()));
        Assert.Throws<ArgumentNullException>(() => SecurityPosture.Evaluate(new(), null!));
        Assert.Throws<ArgumentNullException>(() => SecurityPosture.DescribeElevation(null!, TestData.ProtectedInstall, false));
        Assert.Throws<ArgumentNullException>(() => SecurityPosture.DescribeElevation(new(), null!, false));
        Assert.Throws<ArgumentNullException>(() => SecurityPosture.Facts(null!));
        Assert.Throws<ArgumentNullException>(() => SecurityPosture.ForSetting("x", null!, Context()));
        Assert.Throws<ArgumentNullException>(() => SecurityPosture.ForSetting("x", new(), null!));
    }

    private static IReadOnlyList<RiskItem> AllItems(InstallLocationAssessment install)
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.AlwaysRunAsAdministrator = true;
        settings.EnableCustomCommands = true;
        settings.AlwaysHideTrayIcon = true;
        settings.CustomCommands[0] = new CustomCommandSettings { Command = "x", LaunchHidden = true };
        return SecurityPosture.Evaluate(settings, Context(elevated: true, install: install, startup: true));
    }

    private static RiskItem Find(IEnumerable<RiskItem> items, string id) => Assert.Single(items, i => i.Id == id);
}
