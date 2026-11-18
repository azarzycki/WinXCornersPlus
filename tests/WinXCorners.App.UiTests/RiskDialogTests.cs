using WinXCorners.App.UiTests.Infrastructure;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;

namespace WinXCorners.App.UiTests;

public sealed class RiskDialogTests
{
    public static TheoryData<string, Theme> ScenariosAndThemes()
    {
        var data = new TheoryData<string, Theme>();
        foreach (var scenario in Scenarios.Keys)
        {
            data.Add(scenario, Theme.Light);
            data.Add(scenario, Theme.Dark);
        }

        return data;
    }

    /// <summary>The prompts the Settings window shows, with the items Core produces for them.</summary>
    private static readonly Dictionary<string, (string Heading, Func<IReadOnlyList<RiskItem>> Items)> Scenarios = new()
    {
        // The longest text: elevation at every start from a folder the user can write to, custom commands on.
        ["always-admin-user-writable"] = ("Before WinXCornersPlus runs as administrator", () =>
            SecurityPosture.DescribeElevation(new ApplicationSettings { EnableCustomCommands = true },
                FakeAppHost.UserWritableInstall(), everyStart: true)),
        ["custom-commands"] = ("Before you turn on custom commands", () =>
            [SecurityPosture.ForSetting(nameof(ApplicationSettings.EnableCustomCommands), new ApplicationSettings(), Context(startup: false))!]),
        ["hidden-tray-icon"] = ("Before you hide the tray icon", () =>
            [SecurityPosture.ForSetting(nameof(ApplicationSettings.AlwaysHideTrayIcon), new ApplicationSettings(), Context(startup: true))!]),
        ["hidden-command-window"] = ("Before this command runs without a window", () =>
            [SecurityPosture.ForSetting(SecurityPosture.LaunchHiddenSetting, new ApplicationSettings(), Context(startup: false))!])
    };

    [Theory]
    [MemberData(nameof(ScenariosAndThemes))]
    public void Confirm_ShowsEveryItemVerbatim_WithCancelAsDefault(string scenario, Theme theme)
    {
        var (heading, describe) = Scenarios[scenario];
        var items = describe();
        Assert.NotEmpty(items);

        using var themeScope = ThemeScope.Use(theme);
        Ui.Run(() =>
        {
            using var dialog = RiskDialog.CreateConfirm(AdvancedForm.WindowTitle, heading, items, "Turn on");
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = Screenshot.DefaultLocation;
            dialog.Show();
            Ui.Pump(100);

            var cancel = Find.Control<Button>(dialog, "cancelButton");
            var confirm = Find.Control<Button>(dialog, "confirmButton");
            Assert.Same(cancel, dialog.AcceptButton);
            Assert.Same(cancel, dialog.CancelButton);
            Assert.Same(cancel, dialog.ActiveControl);
            if (Form.ActiveForm == dialog)
            {
                Assert.True(cancel.Focused);
            }

            Assert.Equal("Turn on", confirm.Text);
            Assert.Equal(heading, Find.Control<Label>(dialog, "headingLabel").Text);
            for (var i = 0; i < items.Count; i++)
            {
                Assert.Equal(items[i].Title, Find.Control<Label>(dialog, "riskTitle" + i).Text);
                Assert.Equal(items[i].Detail, Find.Control<Label>(dialog, "riskDetail" + i).Text);
            }

            Assert.Null(Find.Optional<Label>(dialog, "riskTitle" + items.Count));
            LayoutAssert.IsSound(dialog);
            Screenshot.Capture(dialog, $"risk-{scenario}-{theme.ToString().ToLowerInvariant()}");
        });
    }

    [Theory]
    [InlineData("escape")]
    [InlineData("enter")]
    [InlineData("close")]
    public void Confirm_EscEnterAndClose_MeanNo(string how)
    {
        Ui.Run(() =>
        {
            using var dialog = RiskDialog.CreateConfirm("t", "h", Scenarios["custom-commands"].Items(), "Turn on");
            dialog.Show();
            Ui.Pump(100);
            var cancel = Find.Control<Button>(dialog, "cancelButton");

            switch (how)
            {
                case "escape":
                    Ui.PressKey(cancel, Keys.Escape);
                    Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
                    break;
                case "enter":
                    Ui.PressKey(cancel, Keys.Enter);
                    Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
                    break;
                default:
                    dialog.Close();
                    Ui.Pump(50);
                    Assert.NotEqual(DialogResult.OK, dialog.DialogResult);
                    break;
            }
        });
    }

    [Fact]
    public void Confirm_OnlyAnExplicitClickOnTheConfirmButtonMeansYes()
    {
        Ui.Run(() =>
        {
            using var dialog = RiskDialog.CreateConfirm("t", "h", Scenarios["custom-commands"].Items(), "Turn on");
            dialog.Show();
            Ui.Pump(100);
            Assert.Equal(DialogResult.None, dialog.DialogResult);

            var confirm = Find.Control<Button>(dialog, "confirmButton");
            Assert.False(confirm.Focused);
            Assert.NotSame(confirm, dialog.AcceptButton);
            confirm.PerformClick();
            Assert.Equal(DialogResult.OK, dialog.DialogResult);
        });
    }

    [Fact]
    public void ZeroItems_ShowNothing()
    {
        Ui.Run(() =>
        {
            Assert.True(RiskDialog.Confirm(null, "t", "h", [], "Turn on"));
            RiskDialog.Inform(null, "t", "h", []);
            Assert.Empty(Application.OpenForms.Cast<Form>());
        });
    }

    [Fact]
    public void Inform_HasOnlyACloseButton()
    {
        Ui.Run(() =>
        {
            using var dialog = RiskDialog.CreateInform("t", "Things to know", Scenarios["hidden-tray-icon"].Items());
            dialog.Show();
            Ui.Pump(100);
            Assert.Null(Find.Optional<Button>(dialog, "confirmButton"));
            Assert.Equal("Close", Find.Control<Button>(dialog, "cancelButton").Text);
            LayoutAssert.IsSound(dialog);
        });
    }

    [Fact]
    public void ALongList_ScrollsInsteadOfGrowingPastTheScreen()
    {
        var many = Enumerable.Range(1, 25)
            .Select(static i => new RiskItem("id-" + i, RiskSeverity.Caution, $"Item {i}",
                string.Concat(Enumerable.Repeat("A detail that wraps over more than one line in the dialog. ", 3))))
            .ToList();

        using var themeScope = ThemeScope.Use(Theme.Light);
        Ui.Run(() =>
        {
            using var dialog = RiskDialog.CreateConfirm("t", "A long list", many, "Turn on");
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(10, 10);
            dialog.Show();
            Ui.Pump(100);

            var scroller = Find.Control<Panel>(dialog, "riskScroller");
            var workingArea = Screen.FromControl(dialog).WorkingArea;
            Assert.True(scroller.AutoScroll, "The list should scroll.");
            Assert.True(scroller.Height <= workingArea.Height * 0.7 + 1, $"List height {scroller.Height} exceeds 70% of {workingArea.Height}.");
            Assert.True(dialog.Height <= workingArea.Height, $"Dialog height {dialog.Height} exceeds the working area {workingArea.Height}.");
            Assert.True(scroller.VerticalScroll.Visible, "The vertical scroll bar should be visible.");
            LayoutAssert.IsSound(dialog);
            Screenshot.Capture(dialog, "risk-long-list-light");
        });
    }

    private static SecurityContext Context(bool startup) =>
        new(false, FakeAppHost.ProtectedInstall(), startup, @"C:\Users\Test User\AppData\Roaming\WinXCornersPlus\settings.json");
}
