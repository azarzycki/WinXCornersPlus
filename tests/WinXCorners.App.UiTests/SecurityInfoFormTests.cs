using WinXCorners.App.UiTests.Infrastructure;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;

namespace WinXCorners.App.UiTests;

public sealed class SecurityInfoFormTests
{
    [Theory]
    [InlineData(Theme.Light)]
    [InlineData(Theme.Dark)]
    public void NothingToReport_ShowsTheReassuringSentence(Theme theme)
    {
        using var themeScope = ThemeScope.Use(theme);
        using var host = new FakeAppHost();

        Ui.Run(() =>
        {
            using var form = Open(host);
            Assert.Empty(SecurityPosture.Evaluate(host.Settings.Current, host.GetSecurityContext()));
            Assert.Equal(SecurityInfoForm.NothingToReportText, Find.Control<Label>(form, "nothingToReportLabel").Text);
            Assert.Null(Find.Optional<TableLayoutPanel>(form, "riskList"));
            Assert.Equal("Running as administrator: No", Find.Control<Label>(form, "elevatedLabel").Text);
            Assert.Equal(FakeAppHost.ProtectedFolder, Find.Control<TextBox>(form, "programFolderBox").Text);
            Assert.Equal(host.SettingsPath, Find.Control<TextBox>(form, "settingsPathBox").Text);

            // Regression: the sentence was below the fold, under the facts; what needs attention now comes first.
            var label = Find.Control<Label>(form, "nothingToReportLabel");
            var scroller = Find.Control<Panel>(form, "securityScroller");
            var top = scroller.PointToClient(label.PointToScreen(Point.Empty)).Y;
            Assert.True(top + label.Height <= scroller.ClientSize.Height, "The nothing-to-report sentence needs scrolling to be seen.");
            LayoutAssert.IsSound(form);
            Screenshot.Capture(form, $"security-nothing-{theme.ToString().ToLowerInvariant()}");
        });
    }

    [Theory]
    [InlineData(Theme.Light)]
    [InlineData(Theme.Dark)]
    public void SeveralRisks_AreAllListedInCoresWords(Theme theme)
    {
        var settings = new ApplicationSettings
        {
            AlwaysRunAsAdministrator = true,
            EnableCustomCommands = true,
            AlwaysHideTrayIcon = true
        };
        settings.CustomCommands[0].Command = "notepad.exe";
        settings.CustomCommands[0].LaunchHidden = true;
        using var themeScope = ThemeScope.Use(theme);
        using var host = new FakeAppHost(settings)
        {
            IsElevated = true,
            StartupEnabled = true,
            Install = FakeAppHost.UserWritableInstall()
        };

        Ui.Run(() =>
        {
            using var form = Open(host);
            var risks = SecurityPosture.Evaluate(host.Settings.Current, host.GetSecurityContext());
            Assert.True(risks.Count >= 6, $"Expected several risks, got {risks.Count}.");
            for (var i = 0; i < risks.Count; i++)
            {
                Assert.Equal(risks[i].Title, Find.Control<Label>(form, "riskTitle" + i).Text);
                Assert.Equal(risks[i].Detail, Find.Control<Label>(form, "riskDetail" + i).Text);
            }

            Assert.Null(Find.Optional<Label>(form, "nothingToReportLabel"));
            Assert.Equal("Running as administrator: Yes", Find.Control<Label>(form, "elevatedLabel").Text);

            // Regression: a long path was cut off in a one-line box; the boxes now wrap and fit their text.
            foreach (var name in new[] { "programFolderBox", "settingsPathBox" })
            {
                var box = Find.Control<TextBox>(form, name);
                var last = box.GetPositionFromCharIndex(box.TextLength - 1);
                Assert.True(last.Y + box.Font.Height <= box.ClientSize.Height && last.X < box.ClientSize.Width,
                    $"{name}: the end of '{box.Text}' is outside the box ({last}, box {box.ClientSize}).");
            }

            LayoutAssert.IsSound(form);
            Screenshot.Capture(form, $"security-risks-{theme.ToString().ToLowerInvariant()}");

            // Scrolled to the end, so the screenshot shows the last items too.
            var scroller = Find.Control<Panel>(form, "securityScroller");
            scroller.AutoScrollPosition = new Point(0, scroller.DisplayRectangle.Height);
            Ui.Pump(50);
            Screenshot.Capture(form, $"security-risks-{theme.ToString().ToLowerInvariant()}-end");
        });
    }

    [Fact]
    public void SettingsChanged_RebuildsFromAFreshContext()
    {
        using var host = new FakeAppHost();

        Ui.Run(() =>
        {
            using var form = Open(host);
            Assert.NotNull(Find.Optional<Label>(form, "nothingToReportLabel"));
            var requests = host.SecurityContextRequests;

            host.StartupEnabled = true;
            host.Settings.Update(static s => s.AlwaysHideTrayIcon = true);
            Ui.Pump(50);

            Assert.True(host.SecurityContextRequests > requests, "The page did not ask for a fresh context.");
            Assert.Null(Find.Optional<Label>(form, "nothingToReportLabel"));
            Assert.Equal("Starts with Windows: Yes", Find.Control<Label>(form, "startsWithWindowsLabel").Text);
            var titles = Find.Descendants(form).OfType<Label>().Where(static l => l.Name.StartsWith("riskTitle", StringComparison.Ordinal))
                .Select(static l => l.Text).ToList();
            var expected = SecurityPosture.Evaluate(host.Settings.Current, host.GetSecurityContext()).Select(static r => r.Title);
            Assert.Equal(expected, titles);
            LayoutAssert.IsSound(form);
        });
    }

    [Fact]
    public void OpenSettingsFolder_CallsTheHost_AndCloseCloses()
    {
        using var host = new FakeAppHost();

        Ui.Run(() =>
        {
            var form = Open(host);
            Find.Control<Button>(form, "openSettingsFolderButton").PerformClick();
            Assert.Equal(1, host.OpenSettingsFolderCalls);
            Find.Control<Button>(form, "closeButton").PerformClick();
            Ui.Pump(50);
            Assert.True(form.IsDisposed);
        });
    }

    [Fact]
    public void Dispose_UnsubscribesFromSettingsChanges()
    {
        using var host = new FakeAppHost();
        Ui.Run(() =>
        {
            var form = Open(host);
            form.Dispose();
        });

        var handlers = typeof(WinXCorners.Core.Settings.SettingsSession)
            .GetField("Changed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(host.Settings);
        Assert.Null(handlers);
    }

    private static SecurityInfoForm Open(FakeAppHost host)
    {
        var form = new SecurityInfoForm(host) { StartPosition = FormStartPosition.Manual, Location = Screenshot.DefaultLocation };
        form.Show();
        Ui.Pump(150);
        return form;
    }
}
