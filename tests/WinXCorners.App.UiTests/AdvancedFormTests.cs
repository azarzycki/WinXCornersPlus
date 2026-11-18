using WinXCorners.App.UiTests.Infrastructure;
using WinXCorners.Core.Actions;
using WinXCorners.Core.HotCorners;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;

namespace WinXCorners.App.UiTests;

public sealed class AdvancedFormTests
{
    [Fact]
    public void Loading_RiskySettings_PromptsNothing()
    {
        var settings = FilledIn();
        settings.AlwaysRunAsAdministrator = true;
        settings.AlwaysHideTrayIcon = true;
        settings.CustomCommands[0].LaunchHidden = true;
        using var host = new FakeAppHost(settings);
        var recorder = new Recorder();

        Ui.Run(() =>
        {
            using var form = Open(host, recorder);
            Find.Control<Button>(form, "commandTab2").PerformClick();
            Find.Control<Button>(form, "commandTab1").PerformClick();
            Find.Control<Button>(form, "advancedTab").PerformClick();
            Ui.Pump(50);

            Assert.Empty(recorder.Prompts);
            Assert.True(Find.Control<CheckBox>(form, "alwaysAdminCheckBox").Checked);
            Assert.True(Find.Control<CheckBox>(form, "hideTrayCheckBox").Checked);
            Assert.True(Find.Control<CheckBox>(form, "launchHiddenCheckBox").Checked);
            Assert.False(form.HasPendingChanges);
        });
    }

    [Theory]
    [InlineData("alwaysAdminCheckBox")]
    [InlineData("enableCustomCommandsCheckBox")]
    [InlineData("hideTrayCheckBox")]
    [InlineData("launchHiddenCheckBox")]
    public void TurningOnARiskySwitch_AsksOnceWithCoreItems_AndCancelRevertsWithoutASecondPrompt(string checkBoxName)
    {
        using var host = new FakeAppHost();
        host.Install = FakeAppHost.UserWritableInstall();
        var recorder = new Recorder { Answer = false };

        Ui.Run(() =>
        {
            using var form = Open(host, recorder);
            Find.Control<Button>(form, "advancedTab").PerformClick();
            var box = Find.Control<CheckBox>(form, checkBoxName);

            box.Checked = true;
            Assert.Single(recorder.Prompts);
            Assert.False(box.Checked);
            Assert.Equal(ExpectedIds(checkBoxName, host), recorder.Prompts[0].Items.Select(static i => i.Id));
            Assert.Equal(AdvancedForm.WindowTitle, recorder.Prompts[0].Title);
            Assert.Equal("Turn on", recorder.Prompts[0].ConfirmText);

            recorder.Answer = true;
            box.Checked = true;
            Assert.Equal(2, recorder.Prompts.Count);
            Assert.True(box.Checked);

            box.Checked = false;
            Assert.Equal(2, recorder.Prompts.Count);
        });
    }

    [Fact]
    public void InvalidCustomCommand_BlocksApplyAndOk_WithAMessageNamingTheCommand()
    {
        using var host = new FakeAppHost();
        var recorder = new Recorder();
        var changes = 0;
        host.Settings.Changed += _ => changes++;

        Ui.Run(() =>
        {
            using var form = Open(host, recorder);
            Find.Control<Button>(form, "commandTab2").PerformClick();
            Find.Control<TextBox>(form, "commandTextBox").Text = "!_win+nosuchkey";
            Find.Control<Button>(form, "commandTab1").PerformClick();

            Find.Control<Button>(form, "applyButton").PerformClick();
            Assert.Single(recorder.Messages);
            Assert.Contains("Command 2", recorder.Messages[0].Text, StringComparison.Ordinal);
            Assert.Equal(0, changes);
            Assert.Equal("!_win+nosuchkey", Find.Control<TextBox>(form, "commandTextBox").Text); // tab 2 selected again

            Find.Control<Button>(form, "okButton").PerformClick();
            Ui.Pump(50);
            Assert.Equal(2, recorder.Messages.Count);
            Assert.False(form.IsDisposed);
            Assert.True(form.Visible);
            Assert.Equal(0, changes);
            Assert.Equal(string.Empty, host.Settings.Current.CustomCommands[1].Command);
        });
    }

    [Fact]
    public void Ok_AppliesExactlyOnce_SavesAndCloses()
    {
        using var host = new FakeAppHost();
        var recorder = new Recorder();
        var changes = 0;
        host.Settings.Changed += _ => changes++;

        Ui.Run(() =>
        {
            var form = Open(host, recorder);
            Find.Control<CheckBox>(form, "showCountdownCheckBox").Checked = true;
            Find.Control<Button>(form, "okButton").PerformClick();
            Ui.Pump(50);

            Assert.Equal(1, changes);
            Assert.True(host.Settings.Current.ShowCountdown);
            Assert.True(form.IsDisposed);
            Assert.True(File.Exists(host.Store.Paths.PrimaryPath));
            Assert.Empty(host.StartupChanges);
            Assert.Empty(recorder.Messages);
        });
    }

    [Theory]
    [InlineData("escape")]
    [InlineData("cancel")]
    public void CancelAndEsc_DiscardEdits(string how)
    {
        using var host = new FakeAppHost();
        var changes = 0;
        host.Settings.Changed += _ => changes++;

        Ui.Run(() =>
        {
            var form = Open(host, new Recorder());
            Find.Control<CheckBox>(form, "showCountdownCheckBox").Checked = true;
            Find.Control<TextBox>(form, "commandNameTextBox").Text = "Edited";
            if (how == "escape")
            {
                Ui.PressKey(form.ActiveControl ?? form, Keys.Escape);
            }
            else
            {
                Find.Control<Button>(form, "cancelButton").PerformClick();
            }

            Ui.Pump(50);
            Assert.True(form.IsDisposed);
            Assert.Equal(0, changes);
            Assert.False(host.Settings.Current.ShowCountdown);
            Assert.Equal(string.Empty, host.Settings.Current.CustomCommands[0].Name);
        });
    }

    [Fact]
    public void OutOfRangeValues_DoNotThrow_AndShowClamped()
    {
        using var host = new FakeAppHost();
        // Bypasses the session's sanitizer on purpose: the window must cope with whatever it is given (review B3).
        host.Settings.Current.GlobalDelayIndex = 99;
        host.Settings.Current.TopLeftDelayIndex = -5;
        host.Settings.Current.HotCornerModifierKey = (HotCornerModifierKey)42;
        host.Settings.Current.FlyoutAnimationDirection = (FlyoutAnimationDirection)9;

        Ui.Run(() =>
        {
            using var form = Open(host, new Recorder());
            var global = Find.Control<ComboBox>(form, "globalDelayComboBox");
            Assert.Equal(global.Items.Count - 1, global.SelectedIndex);
            Assert.Equal(0, Find.Control<ComboBox>(form, "topLeftDelayComboBox").SelectedIndex);
            Assert.Equal("None", Find.Control<ComboBox>(form, "modifierKeyComboBox").Text);
            Assert.Equal("Bottom", Find.Control<ComboBox>(form, "flyoutDirectionComboBox").Text);
        });
    }

    [Fact]
    public void LaunchHidden_RoundTripsPerCommand()
    {
        var settings = FilledIn();
        settings.CustomCommands[0].LaunchHidden = true;
        settings.CustomCommands[1].Command = "calc.exe";
        using var host = new FakeAppHost(settings);
        var recorder = new Recorder { Answer = true };

        Ui.Run(() =>
        {
            var form = Open(host, recorder);
            var hidden = Find.Control<CheckBox>(form, "launchHiddenCheckBox");
            Assert.True(hidden.Checked);

            Find.Control<Button>(form, "commandTab2").PerformClick();
            Assert.False(hidden.Checked);
            hidden.Checked = true;
            Assert.Single(recorder.Prompts);

            Find.Control<Button>(form, "commandTab1").PerformClick();
            Assert.True(hidden.Checked);
            Find.Control<Button>(form, "commandTab3").PerformClick();
            Assert.False(hidden.Checked);

            Find.Control<Button>(form, "okButton").PerformClick();
            Ui.Pump(50);
            Assert.True(form.IsDisposed);
            Assert.True(host.Settings.Current.CustomCommands[0].LaunchHidden);
            Assert.True(host.Settings.Current.CustomCommands[1].LaunchHidden);
            Assert.False(host.Settings.Current.CustomCommands[2].LaunchHidden);
        });

        // And back from disk.
        var reloaded = host.Store.Load().Settings;
        Assert.True(reloaded.CustomCommands[0].LaunchHidden);
        Assert.True(reloaded.CustomCommands[1].LaunchHidden);
    }

    [Fact]
    public void RunAtStartup_IsChangedOnlyWhenTheBoxChanged()
    {
        using var host = new FakeAppHost();

        Ui.Run(() =>
        {
            var form = Open(host, new Recorder());
            Find.Control<CheckBox>(form, "showCountdownCheckBox").Checked = true;
            Find.Control<Button>(form, "applyButton").PerformClick();
            Assert.Empty(host.StartupChanges);

            Find.Control<CheckBox>(form, "startWithWindowsCheckBox").Checked = true;
            Find.Control<Button>(form, "okButton").PerformClick();
            Ui.Pump(50);
            Assert.Equal([true], host.StartupChanges);
            Assert.True(host.StartupEnabled);
            Assert.True(form.IsDisposed);
        });
    }

    [Fact]
    public void RunAtStartupFailure_IsShown_AndTheBoxIsResynced()
    {
        using var host = new FakeAppHost();
        host.StartupFailure = "Access is denied";
        var recorder = new Recorder();

        Ui.Run(() =>
        {
            using var form = Open(host, recorder);
            var box = Find.Control<CheckBox>(form, "startWithWindowsCheckBox");
            box.Checked = true;
            Find.Control<Button>(form, "applyButton").PerformClick();

            Assert.Equal([true], host.StartupChanges);
            Assert.Single(recorder.Messages);
            Assert.Contains("Access is denied", recorder.Messages[0].Text, StringComparison.Ordinal);
            Assert.False(box.Checked);
            Assert.False(host.Settings.Current.StartWithWindows);
            Assert.False(form.HasPendingChanges);
        });
    }

    [Fact]
    public void FooterLinks_OpenTheReleasesPage_AndTheSecurityPageOnce()
    {
        using var host = new FakeAppHost();

        Ui.Run(() =>
        {
            using var form = Open(host, new Recorder());
            Find.ClickLink(Find.Control<LinkLabel>(form, "updatesLink"));
            Assert.Equal([AdvancedForm.ReleasesUrl], host.OpenedLinks);
            Assert.StartsWith("https://", host.OpenedLinks[0], StringComparison.Ordinal);

            var security = Find.Control<LinkLabel>(form, "securityLink");
            Find.ClickLink(security);
            Ui.Pump(100);
            var first = form.SecurityInfoWindow;
            Assert.NotNull(first);
            Assert.True(first.Visible);

            Find.ClickLink(security);
            Ui.Pump(100);
            Assert.Same(first, form.SecurityInfoWindow);
            Assert.Single(Application.OpenForms.OfType<SecurityInfoForm>());
        });
    }

    [Theory]
    [InlineData("delays", Theme.Light)]
    [InlineData("delays", Theme.Dark)]
    [InlineData("advanced", Theme.Light)]
    [InlineData("advanced", Theme.Dark)]
    public void Screenshots_WithACustomCommandFilledIn(string tab, Theme theme)
    {
        using var themeScope = ThemeScope.Use(theme);
        using var host = new FakeAppHost(FilledIn());

        Ui.Run(() =>
        {
            using var form = Open(host, new Recorder());
            Find.Control<Button>(form, tab == "delays" ? "delaysTab" : "advancedTab").PerformClick();
            Ui.Pump(50);
            LayoutAssert.IsSound(form);
            Screenshot.Capture(form, $"settings-{tab}-{theme.ToString().ToLowerInvariant()}");
        });
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Screenshots_AtHigherScale_HaveASoundLayout(double factor)
    {
        using var themeScope = ThemeScope.Use(Theme.Light);
        using var host = new FakeAppHost(FilledIn());

        Ui.Run(() =>
        {
            var form = Open(host, new Recorder());
            var simulation = DpiSimulation.Apply(form, form.Scaling, factor);
            try
            {
                var percent = (int)Math.Round(factor * 100);
                foreach (var tab in new[] { "delays", "advanced" })
                {
                    Find.Control<Button>(form, tab + "Tab").PerformClick();
                    Ui.Pump(50);
                    LayoutAssert.IsSound(form);
                    Screenshot.Capture(form, $"settings-{tab}-{percent}pct");
                }
            }
            finally
            {
                form.Dispose();
                simulation.Dispose();
            }
        });
    }

    internal static AdvancedForm Open(FakeAppHost host, Recorder recorder)
    {
        var form = new AdvancedForm(host,
            (_, title, heading, items, confirmText) =>
            {
                recorder.Prompts.Add((title, heading, items, confirmText));
                return recorder.Answer;
            },
            (title, text) => recorder.Messages.Add((title, text)));
        form.StartPosition = FormStartPosition.Manual;
        form.Location = Screenshot.DefaultLocation;
        form.Show();
        Ui.Pump(100);
        return form;
    }

    /// <summary>Realistic settings: two corners changed, delays on, and a custom command with arguments.</summary>
    internal static ApplicationSettings FilledIn()
    {
        var settings = new ApplicationSettings
        {
            EnableCustomCommands = true,
            TopLeftDelayEnabled = true,
            BottomRightDelayEnabled = true,
            BottomRightDelayIndex = 5,
            ShowCountdown = true,
            BottomRightActionId = ActionCatalog.CustomId(0)
        };
        settings.CustomCommands[0].Name = "Daily notes";
        settings.CustomCommands[0].Command = @"C:\Windows\System32\notepad.exe";
        settings.CustomCommands[0].Parameters = @"""C:\Users\Test User\Documents\Notes\today.txt""";
        return settings;
    }

    private static IEnumerable<string> ExpectedIds(string checkBoxName, FakeAppHost host)
    {
        var edited = host.Settings.Snapshot();
        var context = host.GetSecurityContext();
        return checkBoxName switch
        {
            "alwaysAdminCheckBox" => SecurityPosture.DescribeElevation(edited, context.Install, everyStart: true).Select(static i => i.Id),
            "enableCustomCommandsCheckBox" => [SecurityPosture.Ids.CustomCommandsEnabled],
            "hideTrayCheckBox" => [SecurityPosture.Ids.TrayIconHidden],
            _ => [SecurityPosture.Ids.CommandLaunchesHidden]
        };
    }

    internal sealed class Recorder
    {
        internal bool Answer { get; set; }

        internal List<(string Title, string Heading, IReadOnlyList<RiskItem> Items, string ConfirmText)> Prompts { get; } = [];

        internal List<(string Title, string Text)> Messages { get; } = [];
    }
}
