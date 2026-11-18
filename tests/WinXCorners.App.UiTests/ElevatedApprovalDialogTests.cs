using WinXCorners.App.UiTests.Infrastructure;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;

namespace WinXCorners.App.UiTests;

public sealed class ElevatedApprovalDialogTests
{
    private const string LongParameters =
        "-NoProfile -ExecutionPolicy Bypass -File \"C:\\Users\\Test User\\Documents\\Scripts\\Rotate-Wallpaper-And-Clean-Downloads.ps1\" -Days 30 -Verbose";

    [Theory]
    [InlineData(Theme.Light)]
    [InlineData(Theme.Dark)]
    public void FirstRun_ShowsEverySummaryLineAndNoChangeSection(Theme theme)
    {
        var settings = Approved();
        var seal = SettingsSeal.Check(settings, storedApproval: null);
        Assert.Equal(SealStatus.NeverApproved, seal.Status);
        var risks = SecurityPosture.DescribeElevation(settings, FakeAppHost.ProtectedInstall(), everyStart: true);

        using var themeScope = ThemeScope.Use(theme);
        Ui.Run(() =>
        {
            using var dialog = Show(settings, seal, risks);
            Assert.Equal(ElevatedApprovalDialog.FirstRunHeading, Find.Control<Label>(dialog, "headingLabel").Text);
            Assert.Null(Find.Optional<TextBox>(dialog, "whatChangedBox"));
            AssertShowsInFull(Find.Control<TextBox>(dialog, "whatWillRunBox"), SettingsSeal.Summarize(settings));
            AssertCommon(dialog, risks);
            Screenshot.Capture(dialog, $"approval-first-run-{theme.ToString().ToLowerInvariant()}");
        });
    }

    [Theory]
    [InlineData(Theme.Light)]
    [InlineData(Theme.Dark)]
    public void Changed_ShowsEveryDifferenceAndSummaryLineUntruncated(Theme theme)
    {
        var approved = Approved();
        var current = Changed();
        var seal = SettingsSeal.Check(current, SettingsSeal.CreateApproval(approved));
        Assert.Equal(SealStatus.Changed, seal.Status);
        Assert.True(seal.Differences.Count >= 3, "The scenario should produce several difference lines.");
        var risks = SecurityPosture.DescribeElevation(current, FakeAppHost.ProtectedInstall(), everyStart: true);

        using var themeScope = ThemeScope.Use(theme);
        Ui.Run(() =>
        {
            using var dialog = Show(current, seal, risks);
            Assert.Equal(ElevatedApprovalDialog.ChangedHeading, Find.Control<Label>(dialog, "headingLabel").Text);
            AssertShowsInFull(Find.Control<TextBox>(dialog, "whatChangedBox"), seal.Differences);
            AssertShowsInFull(Find.Control<TextBox>(dialog, "whatWillRunBox"), SettingsSeal.Summarize(current));
            Assert.Contains(seal.Differences, static line => line.Contains("Rotate-Wallpaper-And-Clean-Downloads.ps1", StringComparison.Ordinal));
            AssertCommon(dialog, risks);
            Screenshot.Capture(dialog, $"approval-changed-{theme.ToString().ToLowerInvariant()}");

            var scroller = Find.Control<Panel>(dialog, "approvalScroller");
            if (scroller.AutoScroll)
            {
                scroller.AutoScrollPosition = new Point(0, scroller.DisplayRectangle.Height);
                Ui.Pump(50);
                Screenshot.Capture(dialog, $"approval-changed-{theme.ToString().ToLowerInvariant()}-end");
            }
        });
    }

    [Fact]
    public void UnreadableApproval_IsPresentedAsAChange()
    {
        var settings = Approved();
        var seal = SettingsSeal.Check(settings, "not an approval");
        Assert.Equal(SealStatus.Unreadable, seal.Status);

        Ui.Run(() =>
        {
            using var dialog = Show(settings, seal, []);
            Assert.Equal(ElevatedApprovalDialog.ChangedHeading, Find.Control<Label>(dialog, "headingLabel").Text);
            AssertShowsInFull(Find.Control<TextBox>(dialog, "whatChangedBox"), [SettingsSeal.UnreadableDifference]);
            LayoutAssert.IsSound(dialog);
        });
    }

    [Theory]
    [InlineData("escape")]
    [InlineData("enter")]
    public void Keys_MeanDoNotRunAsAdministrator(string key)
    {
        var settings = Approved();
        Ui.Run(() =>
        {
            using var dialog = Show(settings, SettingsSeal.Check(settings, null), []);
            var cancel = Find.Control<Button>(dialog, "cancelButton");
            Ui.PressKey(cancel, key == "escape" ? Keys.Escape : Keys.Enter);
            Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
        });
    }

    private static Form Show(ApplicationSettings settings, SealCheck seal, IReadOnlyList<RiskItem> risks)
    {
        var dialog = ElevatedApprovalDialog.Create(settings, seal, risks);
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(20, 10);
        dialog.Show();
        Ui.Pump(100);
        return dialog;
    }

    private static void AssertCommon(Form dialog, IReadOnlyList<RiskItem> risks)
    {
        var cancel = Find.Control<Button>(dialog, "cancelButton");
        var confirm = Find.Control<Button>(dialog, "confirmButton");
        Assert.Equal("Don't run as administrator", cancel.Text);
        Assert.Equal("Approve and continue", confirm.Text);
        Assert.Same(cancel, dialog.AcceptButton);
        Assert.Same(cancel, dialog.CancelButton);
        Assert.Same(cancel, dialog.ActiveControl);
        Assert.Equal(ElevatedApprovalDialog.WhyAskedText, Find.Control<Label>(dialog, "whyAskedLabel").Text);
        for (var i = 0; i < risks.Count; i++)
        {
            Assert.Equal(risks[i].Title, Find.Control<Label>(dialog, "riskTitle" + i).Text);
            Assert.Equal(risks[i].Detail, Find.Control<Label>(dialog, "riskDetail" + i).Text);
        }

        Assert.True(dialog.Height <= Screen.FromControl(dialog).WorkingArea.Height,
            $"The dialog ({dialog.Height} px) is taller than the working area.");
        LayoutAssert.IsSound(dialog);
    }

    /// <summary>The box holds exactly these lines, and either shows them all or offers a scroll bar.</summary>
    private static void AssertShowsInFull(TextBox box, IReadOnlyList<string> lines)
    {
        Assert.Equal(string.Join(Environment.NewLine, lines), box.Text);
        foreach (var line in lines)
        {
            Assert.Contains(line, box.Text, StringComparison.Ordinal);
        }

        var lastLine = box.GetLineFromCharIndex(Math.Max(0, box.TextLength - 1));
        var lastLineTop = box.GetPositionFromCharIndex(Math.Max(0, box.TextLength - 1)).Y;
        var fits = lastLineTop + box.Font.Height <= box.ClientSize.Height;
        // The lists never scroll on their own (only the dialog body does), so the last line must be inside the box.
        Assert.Equal(ScrollBars.None, box.ScrollBars);
        Assert.True(fits, $"{box.Name}: {lastLine + 1} wrapped lines do not fit in {box.ClientSize.Height} px.");
    }

    /// <summary>What the user approved last time: two corners and one plain custom command.</summary>
    private static ApplicationSettings Approved()
    {
        var settings = new ApplicationSettings
        {
            EnableCustomCommands = true,
            TopLeftActionId = ActionIds.FileExplorer,
            BottomRightActionId = ActionCatalog.CustomId(0)
        };
        settings.CustomCommands[0].Name = "Notes";
        settings.CustomCommands[0].Command = @"C:\Windows\System32\notepad.exe";
        return settings;
    }

    /// <summary>
    /// What a program running as the user might have changed: a new hidden PowerShell command on a corner, the
    /// first command repointed, the modifier key removed and the tray icon hidden.
    /// </summary>
    private static ApplicationSettings Changed()
    {
        var settings = Approved();
        settings.TopRightActionId = ActionCatalog.CustomId(1);
        settings.CustomCommands[0].Command = @"C:\Users\Test User\AppData\Local\Temp\notepad.exe";
        settings.CustomCommands[1].Name = "Tidy up";
        settings.CustomCommands[1].Command = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
        settings.CustomCommands[1].Parameters = LongParameters;
        settings.CustomCommands[1].LaunchHidden = true;
        settings.AlwaysHideTrayIcon = true;
        return settings;
    }
}
