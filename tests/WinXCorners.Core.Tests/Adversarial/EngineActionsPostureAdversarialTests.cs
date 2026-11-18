using System.Drawing;
using WinXCorners.Core.Actions;
using WinXCorners.Core.HotCorners;
using WinXCorners.Core.Input;
using WinXCorners.Core.Model;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Adversarial;

public class HotCornerEngineAdversarialTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);

    private static HotCornerDecision At(HotCornerEngine engine, ApplicationSettings s, long t, int x, int y, Rectangle? bounds = null) =>
        engine.Evaluate(s, new HotCornerInput(new Point(x, y), bounds ?? Screen, t, false, true, false));

    // Contract: Triggered is "reported exactly once per corner visit". Reset() (documented for "after settings change or
    // the session resumes") re-arms a corner the pointer is still parked in. With Lock Screen on a corner, unlocking
    // with the pointer still there re-locks immediately.
    [Fact]
    public void ResetWhileParkedInCorner_NeverTriggersAgainWithoutLeaving()
    {
        var s = new ApplicationSettings { TopLeftActionId = ActionIds.LockScreen };
        var engine = new HotCornerEngine();
        // Corners fire on entry only, so the pointer has to come from somewhere outside the corner first.
        Assert.Equal(HotCornerOutcome.Idle, At(engine, s, -10, 960, 540).Outcome);
        Assert.Equal(HotCornerOutcome.Triggered, At(engine, s, 0, 0, 0).Outcome);
        Assert.Equal(HotCornerOutcome.Idle, At(engine, s, 10, 0, 0).Outcome);
        engine.Reset();
        Assert.NotEqual(HotCornerOutcome.Triggered, At(engine, s, 20, 0, 0).Outcome);
    }

    // An enabled custom slot with no command counts down (countdown overlay shown) and "fires" a no-op.
    [Fact]
    public void EmptyCustomCommandIsIdle()
    {
        var s = new ApplicationSettings
        {
            TopLeftActionId = ActionCatalog.CustomId(0),
            EnableCustomCommands = true,
            GlobalDelayEnabled = true,
            GlobalDelayIndex = 0,
            ShowCountdown = true
        };
        var engine = new HotCornerEngine();
        Assert.Equal(HotCornerOutcome.Idle, At(engine, s, 0, 0, 0).Outcome);
    }

    [Theory]
    [InlineData(-1920, -1080, 1920, 1080)]
    [InlineData(-2560, 200, 2560, 1440)]
    [InlineData(1920, -300, 1080, 1920)]
    public void Geometry_ExactCornerPixelsOnNegativeOriginMonitors(int left, int top, int width, int height)
    {
        var b = new Rectangle(left, top, width, height);
        (int X, int Y, HotCornerArea Area)[] corners =
        [
            (b.Left, b.Top, HotCornerArea.TopLeft),
            (b.Right - 1, b.Top, HotCornerArea.TopRight),
            (b.Left, b.Bottom - 1, HotCornerArea.BottomLeft),
            (b.Right - 1, b.Bottom - 1, HotCornerArea.BottomRight)
        ];
        foreach (var (x, y, area) in corners)
        {
            Assert.Equal(area, HotCornerGeometry.GetArea(new Point(x, y), b));
            var dx = x == b.Left ? 1 : -1;
            var dy = y == b.Top ? 1 : -1;
            Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(x + dx, y), b));
            Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(x, y + dy), b));
            Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(x - dx, y), b));
            Assert.True(HotCornerGeometry.IsWithinReleaseZone(new Point(x + dx * 31, y + dy * 31), area, b));
            Assert.False(HotCornerGeometry.IsWithinReleaseZone(new Point(x + dx * 32, y), area, b));
            Assert.False(HotCornerGeometry.IsWithinReleaseZone(new Point(x, y + dy * 32), area, b));
        }
    }

    // Randomised: no two Triggered decisions without the pointer leaving the release zone in between (no Reset).
    [Fact]
    public void Engine_RandomWalkNeverTriggersTwiceInOneVisit()
    {
        var random = new Random(7);
        var s = new ApplicationSettings { GlobalDelayEnabled = true, GlobalDelayIndex = 0, IgnoreFullScreen = true };
        var blocked = false;
        var engine = new HotCornerEngine(_ => blocked);
        HotCornerArea? latched = null;
        Rectangle latchedBounds = default;
        long t = 0;
        for (var i = 0; i < 200_000; i++)
        {
            t += random.Next(0, 120);
            blocked = random.Next(50) == 0;
            var bounds = random.Next(500) == 0 ? new Rectangle(-1920, 0, 1920, 1080) : Screen;
            var x = random.Next(4) == 0 ? random.Next(-40, 40) + (random.Next(2) == 0 ? bounds.Left : bounds.Right - 1) : random.Next(bounds.Left, bounds.Right);
            var y = random.Next(4) == 0 ? random.Next(-40, 40) + (random.Next(2) == 0 ? bounds.Top : bounds.Bottom - 1) : random.Next(bounds.Top, bounds.Bottom);
            var input = new HotCornerInput(new Point(x, y), bounds, t, random.Next(20) == 0, random.Next(20) != 0, random.Next(30) == 0);
            if (latched is { } a && !HotCornerGeometry.IsWithinReleaseZone(input.Cursor, a, latchedBounds))
            {
                latched = null;
            }

            var d = engine.Evaluate(s, input);
            if (d.Outcome == HotCornerOutcome.Triggered)
            {
                Assert.Null(latched);
                Assert.False(input.AnyMouseButtonDown || input.Suppressed || !input.ModifierSatisfied);
                latched = d.Area;
                latchedBounds = bounds;
            }
        }
    }
}

public class ActionPlannerAdversarialTests
{
    private static readonly SystemDirectories Dirs = new(@"C:\Windows", @"C:\Windows\System32", @"C:\Users\alex", []);

    [Fact]
    public void EveryCatalogIdMapsToTheDocumentedPlan()
    {
        var s = ApplicationSettings.CreateDefault();
        var expected = new Dictionary<string, string>
        {
            [ActionIds.FileExplorer] = @"Launch C:\Windows\explorer.exe",
            [ActionIds.Settings] = "Launch ms-settings:",
            [ActionIds.TaskManager] = @"Launch C:\Windows\System32\taskmgr.exe",
            [ActionIds.AllWindows] = "Keys 5B+,09+,09-,5B-",
            [ActionIds.Desktop] = "Keys 5B+,44+,44-,5B-",
            [ActionIds.ActionCenter] = "Keys 5B+,41+,41-,5B-",
            [ActionIds.NotificationCenter] = "Keys 5B+,4E+,4E-,5B-",
            [ActionIds.HideOtherWindows] = "Keys 5B+,24+,24-,5B-",
            [ActionIds.ScreenSaver] = "Sys StartScreenSaver",
            [ActionIds.MonitorsOff] = "Sys TurnDisplayOff",
            [ActionIds.StartMenu] = "Sys OpenStartMenu",
            [ActionIds.LockScreen] = "Lock",
            [ActionIds.None] = "Nothing",
        };
        foreach (var definition in ActionCatalog.BuiltIn.Append(ActionCatalog.None))
        {
            var plan = ActionPlanner.Plan(definition.Id, s, Dirs, taskViewIsForeground: false);
            var text = plan switch
            {
                ActionPlan.Launch l => "Launch " + l.Request.Target,
                ActionPlan.SendKeys k => "Keys " + string.Join(",", KeyEventPlanner.Plan(k.Sequence, []).Select(e => e.VirtualKey.ToString("X2") + (e.KeyUp ? "-" : "+"))),
                ActionPlan.SystemCommand c => "Sys " + c.Kind,
                ActionPlan.LockWorkstation => "Lock",
                ActionPlan.Nothing => "Nothing",
                _ => "?"
            };
            Assert.Equal(expected[definition.Id], text);
            if (plan is ActionPlan.Launch launch)
            {
                Assert.Equal(LaunchTargetKind.BuiltIn, launch.Request.Kind);
                Assert.True(LaunchPlanner.TryPlan(launch.Request, true, Dirs, out var lp, out _));
                Assert.Equal(LaunchIntegrity.DeElevated, lp.Integrity);
            }
        }

        var escape = (ActionPlan.SendKeys)ActionPlanner.Plan(ActionIds.AllWindows, s, Dirs, taskViewIsForeground: true);
        Assert.Equal([0x1B, 0x1B], KeyEventPlanner.Plan(escape.Sequence, []).Select(e => (int)e.VirtualKey));
    }

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData("!win+r")]
    [InlineData("#[a]:(b)")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    public void CustomCommandsDisabled_NeverDoesAnything(string command)
    {
        for (var slot = 0; slot < ApplicationSettings.CustomCommandCount; slot++)
        {
            var s = ApplicationSettings.CreateDefault();
            s.CustomCommands[slot].Command = command;
            Assert.IsType<ActionPlan.Nothing>(ActionPlanner.Plan(ActionCatalog.CustomId(slot), s, Dirs, false));
        }

        var extra = ApplicationSettings.CreateDefault();
        extra.EnableCustomCommands = true;
        extra.CustomCommands = [.. extra.CustomCommands, new CustomCommandSettings { Command = command }];
        Assert.IsType<ActionPlan.Nothing>(ActionPlanner.Plan("custom-5", extra, Dirs, false));
        Assert.IsType<ActionPlan.Nothing>(ActionPlanner.Plan("Custom-1", extra, Dirs, false));
    }

    // Mutation gap: Kind=CustomCommand for a custom target that happens to sit under C:\Windows. If it were BuiltIn
    // the launch would get System32 as its working directory and drop the hidden flag silently.
    [Fact]
    public void CustomLaunchUnderWindowsStaysACustomCommand()
    {
        var s = ApplicationSettings.CreateDefault();
        s.EnableCustomCommands = true;
        s.CustomCommands[2] = new CustomCommandSettings { Command = @"  C:\Windows\System32\cmd.exe ", Parameters = "/c x", LaunchHidden = true };
        var launch = Assert.IsType<ActionPlan.Launch>(ActionPlanner.Plan(ActionCatalog.CustomId(2), s, Dirs, false));
        Assert.Equal(new LaunchRequest(@"C:\Windows\System32\cmd.exe", "/c x", true, LaunchTargetKind.CustomCommand), launch.Request);
        Assert.True(LaunchPlanner.TryPlan(launch.Request, true, Dirs, out var plan, out _));
        Assert.Equal(@"C:\Users\alex", plan.WorkingDirectory);
        Assert.True(plan.Hidden);
    }
}

public class SecurityPostureAdversarialTests
{
    private static readonly InstallLocationAssessment Protected = new(InstallLocationKind.Protected, @"C:\Program Files\X", "p");

    // With "Always run as administrator" on (and an approval on record), a normal start re-elevates (AutoElevatePolicy
    // -> Elevate). The advice "exit the app and start it normally" therefore does not stop elevation.
    [Fact]
    public void RunningElevatedAdviceSaysToTurnOffAlwaysRunAsAdministratorFirst()
    {
        var s = ApplicationSettings.CreateDefault();
        s.AlwaysRunAsAdministrator = true;
        Assert.Equal(AutoElevateAction.Elevate, AutoElevatePolicy.Decide(true, false, false, true));
        var items = SecurityPosture.Evaluate(s, new SecurityContext(true, Protected, false, "x"));
        var running = items.Single(i => i.Id == SecurityPosture.Ids.RunningElevated);
        Assert.True(!running.Detail.Contains("start it normally", StringComparison.Ordinal) ||
                    running.Detail.Contains("Always run as administrator", StringComparison.OrdinalIgnoreCase), running.Detail);
    }

    // A hotkey command opens no window, so "starts with no window ... end it in Task Manager" is false for it.
    [Fact]
    public void HiddenWindowRiskIsNotReportedForHotkeyCommands()
    {
        var s = ApplicationSettings.CreateDefault();
        s.EnableCustomCommands = true;
        s.CustomCommands[0] = new CustomCommandSettings { Command = "!win+d", LaunchHidden = true };
        var items = SecurityPosture.Evaluate(s, new SecurityContext(false, Protected, false, "x"));
        Assert.DoesNotContain(items, i => i.Id == SecurityPosture.Ids.CommandLaunchesHidden);
    }

    [Fact]
    public void EveryCombinationStaysWithinLimitsAndFlagsElevatedWritableInstall()
    {
        var installs = new[] { Protected, new InstallLocationAssessment(InstallLocationKind.UserWritable, "d", "w"), new InstallLocationAssessment(InstallLocationKind.Unknown, "", "u") };
        for (var bits = 0; bits < 64; bits++)
        {
            foreach (var install in installs)
            {
                var s = ApplicationSettings.CreateDefault();
                s.AlwaysRunAsAdministrator = (bits & 1) != 0;
                s.EnableCustomCommands = (bits & 2) != 0;
                s.AlwaysHideTrayIcon = (bits & 4) != 0;
                s.CustomCommands[1] = new CustomCommandSettings { Command = "x.exe", LaunchHidden = (bits & 8) != 0 };
                var context = new SecurityContext((bits & 16) != 0, install, (bits & 32) != 0, @"C:\Users\a\settings.json");
                var items = SecurityPosture.Evaluate(s, context);
                Assert.All(items, i => Assert.InRange(i.Title.Length, 1, SecurityPosture.MaxTitleLength));
                Assert.All(items, i => Assert.InRange(i.Detail.Length, 1, SecurityPosture.MaxDetailLength));
                Assert.Equal(items.OrderByDescending(i => i.Severity).Select(i => i.Id), items.Select(i => i.Id));
                var elevatedWritable = context.IsElevated && install.Kind != InstallLocationKind.Protected;
                Assert.Equal(elevatedWritable, items.Any(i => i.Id == SecurityPosture.Ids.ElevatedFromUserWritableFolder && i.Severity == RiskSeverity.Warning));
                if (s.AlwaysRunAsAdministrator && install.Kind != InstallLocationKind.Protected)
                {
                    Assert.Contains(items, i => i.Id == SecurityPosture.Ids.AlwaysRunAsAdministrator && i.Severity == RiskSeverity.Warning);
                }
            }
        }
    }
}
