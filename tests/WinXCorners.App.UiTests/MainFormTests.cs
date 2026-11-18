using System.Runtime.InteropServices;
using WinXCorners.App.UiTests.Infrastructure;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Layout;
using WinXCorners.Core.Model;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;
using WinXCorners.Core.Settings;

namespace WinXCorners.App.UiTests;

/// <summary>
/// The hidden main window with the real <see cref="AppHost"/> (temp settings folder, in-memory approvals, a throw-away
/// Run value name, never elevated). Every corner is set to "none", so the hot-corner timer can never launch anything,
/// and the restart, balloon and tray-menu seams are replaced.
/// </summary>
public sealed class MainFormTests
{
    [Fact]
    public void Constructing_ShowsNothing_AndPromptsNothing()
    {
        using var env = new MainFormEnvironment();
        Ui.Run(() =>
        {
            using var form = env.CreateForm();
            Ui.Pump(100);
            Assert.False(form.Visible);
            Assert.False(form.IsStarted);
            Assert.False(form.TrayIcon.Visible);
            Assert.DoesNotContain(Application.OpenForms.Cast<Form>(), static f => f.Visible);
            Assert.Empty(env.Confirmations);
            Assert.Empty(env.Balloons);
        });
    }

    [Theory]
    [InlineData(Theme.Light)]
    [InlineData(Theme.Dark)]
    public void Flyout_OpensAtCoresAnchor_AndCloses(Theme theme)
    {
        using var themeScope = ThemeScope.Use(theme);
        using var env = new MainFormEnvironment();
        Ui.Run(() =>
        {
            using var form = env.CreateForm();
            form.Flyout.Open();
            Ui.Pump(600);
            Assert.True(form.IsFlyoutOpen);
            Assert.False(form.Flyout.IsAnimating, "The opening animation should be over after 600 ms.");
            Assert.Equal(ExpectedAnchor(form.Flyout.Size), form.Flyout.Location);
            Assert.Equal(1.0, form.Flyout.Opacity, 3);
            LayoutAssert.IsSound(form.Flyout);
            Screenshot.Capture(form.Flyout, $"flyout-{theme.ToString().ToLowerInvariant()}");

            form.Flyout.Dismiss();
            Ui.Pump(600);
            Assert.False(form.IsFlyoutOpen);
            Assert.False(form.Flyout.IsAnimating);
        });
    }

    [Fact]
    public void PickingACornerAction_UpdatesTheButton_AndIsSavedAfterTheDebounce()
    {
        using var env = new MainFormEnvironment();
        Ui.Run(() =>
        {
            using var form = env.CreateForm();
            IReadOnlyList<CornerMenuItem>? offered = null;
            form.Flyout.ShowCornerMenu = (_, items) =>
            {
                offered = items;
                return ActionIds.TaskManager;
            };

            form.Flyout.OpenCornerMenu(HotCornerArea.TopLeft);
            Assert.NotNull(offered);
            Assert.Contains(offered, static item => item.ActionId == ActionIds.TaskManager);
            Assert.Equal(ActionIds.TaskManager, env.Host.Settings.Current.TopLeftActionId);
            Assert.Equal(ActionCatalog.GetButtonText(env.Host.Settings.Current, ActionIds.TaskManager),
                form.Flyout.GetCornerButton(HotCornerArea.TopLeft).Text);

            Assert.True(Ui.PumpUntil(() => env.LoadFromDisk()?.TopLeftActionId == ActionIds.TaskManager, 2000),
                "The pick was not saved to disk after the debounce.");
        });
    }

    [Fact]
    public void TrayToggle_IsSaved()
    {
        using var env = new MainFormEnvironment();
        Ui.Run(() =>
        {
            using var form = env.CreateForm();
            Assert.True(env.Host.Settings.Current.HotCornersEnabled);
            form.HandleTrayCommandForTest(Win32PopupMenu.TrayCommandToggleHotCorners);
            Assert.False(env.Host.Settings.Current.HotCornersEnabled);
            Assert.True(Ui.PumpUntil(() => env.LoadFromDisk()?.HotCornersEnabled == false, 2000),
                "The toggle was not saved to disk.");
        });
    }

    [Fact]
    public void HideTray_ThenASecondLaunch_ShowsSettingsAndTheTrayIconAgain()
    {
        using var env = new MainFormEnvironment();
        Ui.Run(() =>
        {
            using var form = env.CreateForm();
            form.Start();
            Ui.Pump(50);
            Assert.True(form.TrayIcon.Visible);

            form.HandleTrayCommandForTest(Win32PopupMenu.TrayCommandHideTray);
            Assert.False(form.TrayIcon.Visible);

            form.ShowSettingsFromExternalRequest();
            Ui.Pump(100);
            Assert.True(form.TrayIcon.Visible);
            Assert.NotNull(form.SettingsWindow);
            Assert.True(form.SettingsWindow.Visible);
        });
    }

    [Fact]
    public void ChildWindows_OpenOnce_AndAreReused()
    {
        using var env = new MainFormEnvironment();
        Ui.Run(() =>
        {
            using var form = env.CreateForm();
            OpenAllWindows(form);
            var settings = form.SettingsWindow;
            var log = form.LogWindow;
            var about = form.AboutWindow;
            var security = form.SecurityWindow;
            OpenAllWindows(form);

            Assert.Same(settings, form.SettingsWindow);
            Assert.Same(log, form.LogWindow);
            Assert.Same(about, form.AboutWindow);
            Assert.Same(security, form.SecurityWindow);
            Assert.Single(Application.OpenForms.OfType<AdvancedForm>());
            Assert.Single(Application.OpenForms.OfType<LogWindowForm>());
            Assert.Single(Application.OpenForms.OfType<AboutForm>());
            Assert.Single(Application.OpenForms.OfType<SecurityInfoForm>());

            // The log window is hidden by the user, then reopened: still the same window.
            log!.Close();
            Ui.Pump(50);
            form.HandleTrayCommandForTest(Win32PopupMenu.TrayCommandLogWindow);
            Ui.Pump(50);
            Assert.Same(log, form.LogWindow);
            Assert.True(log.Visible);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Exit_EndsARealMessageLoopWithinTwoSeconds_WithEveryChildWindowOpen(bool logWindowHidden)
    {
        using var env = new MainFormEnvironment();
        Ui.Run(() =>
        {
            using var form = env.CreateForm();
            form.Start();
            OpenAllWindows(form);
            if (logWindowHidden)
            {
                form.LogWindow!.Close(); // the user's close only hides it (review B1)
                Ui.Pump(50);
            }

            using var context = new ApplicationContext();
            form.ExitMessageLoop = context.ExitThread;
            var (elapsed, vetoed) = MessageLoop.RunUntilExit(
                () => form.HandleTrayCommandForTest(Win32PopupMenu.TrayCommandExit), context);

            Assert.False(vetoed, "Exit did not end the message loop (the watchdog had to).");
            Assert.True(elapsed < TimeSpan.FromSeconds(2), $"Exit took {elapsed.TotalMilliseconds:0} ms.");
            Assert.True(form.IsShutDown);
            Assert.False(form.TrayIcon.Visible);
            Assert.Null(form.SettingsWindow);
            Assert.Null(form.LogWindow);
            Assert.Null(form.AboutWindow);
            Assert.Null(form.SecurityWindow);
            Assert.Contains(env.Host.Log.Snapshot(), static line => line.EndsWith("Exiting.", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ApplicationExit_WithTheLogWindowOpened_EndsTheLoop()
    {
        using var env = new MainFormEnvironment();
        Ui.Run(() =>
        {
            using var form = env.CreateForm();
            form.HandleTrayCommandForTest(Win32PopupMenu.TrayCommandLogWindow);
            form.LogWindow!.Close();
            Ui.Pump(50);

            var (elapsed, vetoed) = MessageLoop.RunUntilExit(Application.Exit);
            Assert.False(vetoed, "Application.Exit() was vetoed (review B1).");
            Assert.True(elapsed < TimeSpan.FromSeconds(2), $"Exit took {elapsed.TotalMilliseconds:0} ms.");
        });
    }

    [Fact]
    public void RestartAsAdministrator_AsksFirst_AndDoesNothingWhenDeclined()
    {
        using var env = new MainFormEnvironment();
        Ui.Run(() =>
        {
            using var form = env.CreateForm();
            form.HandleTrayCommandForTest(Win32PopupMenu.TrayCommandElevate);

            Assert.Single(env.Confirmations);
            var expected = SecurityPosture.DescribeElevation(env.Host.Settings.Current, env.Host.GetSecurityContext().Install, everyStart: false);
            Assert.Equal(expected.Select(static i => i.Id), env.Confirmations[0].Select(static i => i.Id));
            Assert.False(form.IsShutDown);
            Assert.Contains(env.Host.Log.Snapshot(), static line => line.Contains("Restart as administrator cancelled.", StringComparison.Ordinal));
            Assert.DoesNotContain(env.Host.Log.Snapshot(), static line => line.Contains("Restarting", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ThemeChangeMessages_Repaint_WithTheNewColours()
    {
        using var env = new MainFormEnvironment();
        using var themeScope = ThemeScope.Use(Theme.Light);
        Ui.Run(() =>
        {
            using var form = env.CreateForm();
            form.Flyout.Open();
            Ui.Pump(400);
            Assert.Equal(ThemeHelper.Colors.GetFlyoutBackgroundColor(), form.Flyout.BackColor);

            using (ThemeScope.Use(Theme.Dark))
            {
                foreach (var message in new[] { WmSettingChange, WmThemeChanged, WmDwmColorizationColorChanged })
                {
                    SendMessage(form.Handle, message, IntPtr.Zero, IntPtr.Zero);
                    Ui.Pump(50);
                }

                Assert.Equal(ThemeHelper.Colors.GetFlyoutBackgroundColor(), form.Flyout.BackColor);
                Assert.False(ThemeHelper.IsLightTheme);
                form.Flyout.Refresh();
            }

            SendMessage(form.Handle, WmThemeChanged, IntPtr.Zero, IntPtr.Zero);
            Ui.Pump(50);
            Assert.Equal(ThemeHelper.Colors.GetFlyoutBackgroundColor(), form.Flyout.BackColor);
            form.Flyout.HideNow();
        });
    }

    [Fact]
    public void Dispose_StopsEverything()
    {
        using var env = new MainFormEnvironment();
        Ui.Run(() =>
        {
            var form = env.CreateForm();
            form.Start();
            OpenAllWindows(form);
            form.Flyout.Open();
            Ui.Pump(100);
            form.Dispose();
            Ui.Pump(50);
            Assert.False(form.TrayIcon.Visible);
            Assert.True(form.Flyout.IsDisposed);
            Assert.False(form.Flyout.IsAnimating);
        });
    }

    private static void OpenAllWindows(MainForm form)
    {
        form.HandleTrayCommandForTest(Win32PopupMenu.TrayCommandAdvanced);
        form.HandleTrayCommandForTest(Win32PopupMenu.TrayCommandLogWindow);
        form.HandleTrayCommandForTest(Win32PopupMenu.TrayCommandAbout);
        form.HandleTrayCommandForTest(Win32PopupMenu.TrayCommandSecurity);
        Ui.Pump(100);
        Assert.NotNull(form.SettingsWindow);
        Assert.NotNull(form.LogWindow);
        Assert.NotNull(form.AboutWindow);
        Assert.NotNull(form.SecurityWindow);
    }

    private static Point ExpectedAnchor(Size flyoutSize)
    {
        var screen = Screen.PrimaryScreen!;
        var taskbar = TaskbarInfo.TryGetPrimaryTaskbar() ?? new TaskbarInfo(screen.WorkingArea, TaskbarEdge.Bottom);
        return FlyoutLayout.GetAnchor(screen.WorkingArea, flyoutSize, taskbar.Edge);
    }

    private const int WmSettingChange = 0x001A;
    private const int WmThemeChanged = 0x031A;
    private const int WmDwmColorizationColorChanged = 0x0320;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}

/// <summary>A real AppHost over a temp folder, with every corner set to "none".</summary>
internal sealed class MainFormEnvironment : IDisposable
{
    private readonly string _directory;
    private readonly SettingsPaths _paths;

    internal MainFormEnvironment()
    {
        _directory = Path.Combine(Path.GetTempPath(), "wxc-ui-main-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _paths = new SettingsPaths(Path.Combine(_directory, SettingsPaths.FileName), null);

        // Written before the host loads, so no corner can ever run a real action during a test.
        var initial = new ApplicationSettings
        {
            TopLeftActionId = ActionIds.None,
            TopRightActionId = ActionIds.None,
            BottomLeftActionId = ActionIds.None,
            BottomRightActionId = ActionIds.None
        };
        Assert.True(new SettingsStore(_paths).Save(initial).Succeeded);

        Host = AppHost.Create(CommandLineOptions.Parse([]), _paths, new MemoryApprovals(), isElevated: false,
            startupValueName: "WinXCornersPlus.UiTests." + Guid.NewGuid().ToString("N"));
    }

    internal AppHost Host { get; }

    internal List<IReadOnlyList<RiskItem>> Confirmations { get; } = [];

    internal List<(string Title, string Text)> Balloons { get; } = [];

    internal MainForm CreateForm()
    {
        var form = new MainForm(Host);
        form.ConfirmElevation = items =>
        {
            Confirmations.Add(items);
            return false;
        };
        form.ShowBalloon = (title, text, _) => Balloons.Add((title, text));
        form.ShowTrayMenu = static _ => 0;
        form.ExitMessageLoop = static () => { };
        return form;
    }

    /// <summary>The settings as saved on disk, or null when the file cannot be read yet.</summary>
    internal ApplicationSettings? LoadFromDisk()
    {
        var result = new SettingsStore(_paths).Load();
        return result.Status == SettingsLoadStatus.Loaded ? result.Settings : null;
    }

    public void Dispose()
    {
        Host.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Cleaned up with the runner's temp folder.
        }
    }
}
