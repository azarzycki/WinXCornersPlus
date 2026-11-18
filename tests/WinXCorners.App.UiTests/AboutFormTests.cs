using WinXCorners.App.UiTests.Infrastructure;

namespace WinXCorners.App.UiTests;

public sealed class AboutFormTests
{
    [Theory]
    [InlineData(Theme.Light)]
    [InlineData(Theme.Dark)]
    public void ShowsVersionStatusAndPath_AndLinksGoThroughTheHost(Theme theme)
    {
        using var themeScope = ThemeScope.Use(theme);
        using var host = new FakeAppHost { IsElevated = theme == Theme.Dark };

        Ui.Run(() =>
        {
            using var form = Open(host);
            Assert.Equal("Version: " + host.VersionText, Find.Control<Label>(form, "versionLabel").Text);
            Assert.Equal($"Running as administrator: {(host.IsElevated ? "Yes" : "No")}", Find.Control<Label>(form, "elevatedLabel").Text);
            var path = Find.Control<Label>(form, "settingsPathLabel");
            Assert.Equal(host.SettingsPath, path.Text);
            Assert.Equal(host.SettingsPath, form.PathToolTip.GetToolTip(path));

            Find.ClickLink(Find.Control<LinkLabel>(form, "asteskiLink"));
            Find.ClickLink(Find.Control<LinkLabel>(form, "vhanlaLink"));
            Assert.Equal([AboutForm.AsteskiUrl, AboutForm.VhanlaUrl], host.OpenedLinks);

            LayoutAssert.IsSound(form);
            Screenshot.Capture(form, $"about-{theme.ToString().ToLowerInvariant()}");
        });
    }

    [Fact]
    public void AVeryLongPath_IsShortenedInTheMiddle_AndAvailableInFullInTheTooltip()
    {
        using var themeScope = ThemeScope.Use(Theme.Light);
        using var host = new FakeAppHost
        {
            SettingsPath = @"C:\Users\A User With A Rather Long Name\AppData\Roaming\Some Sync Folder\Nested\Deeper\WinXCornersPlus\settings.json"
        };

        Ui.Run(() =>
        {
            using var form = Open(host);
            var path = Find.Control<Label>(form, "settingsPathLabel");
            Assert.Equal(host.SettingsPath, form.PathToolTip.GetToolTip(path));
            Assert.Equal("Settings file " + host.SettingsPath, path.AccessibleName);

            var full = TextRenderer.MeasureText(path.Text, path.Font);
            Assert.True(full.Width > path.ClientSize.Width, "The scenario needs a path wider than the label.");
            Assert.True(path.Right <= form.ClientSize.Width, "The path label runs off the window.");
            LayoutAssert.IsSound(form);
            Screenshot.Capture(form, "about-long-path");
        });
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void AtHigherScale_TheLayoutIsSound(double factor)
    {
        using var themeScope = ThemeScope.Use(Theme.Light);
        using var host = new FakeAppHost();

        Ui.Run(() =>
        {
            var form = Open(host);
            var simulation = DpiSimulation.Apply(form, form.Scaling, factor);
            try
            {
                LayoutAssert.IsSound(form);
                Screenshot.Capture(form, $"about-{(int)Math.Round(factor * 100)}pct");
            }
            finally
            {
                form.Dispose();
                simulation.Dispose();
            }
        });
    }

    private static AboutForm Open(FakeAppHost host)
    {
        var form = new AboutForm(host) { StartPosition = FormStartPosition.Manual, Location = Screenshot.DefaultLocation };
        form.Show();
        Ui.Pump(100);
        return form;
    }
}
