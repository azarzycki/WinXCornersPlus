using WinXCorners.Core.Actions;
using WinXCorners.Core.Input;
using WinXCorners.Core.Model;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Actions;

public class ActionPlannerTests
{
    private const ushort VkTab = 0x09;
    private const ushort VkEscape = 0x1B;
    private const ushort VkHome = 0x24;
    private const ushort VkLWin = 0x5B;

    private static readonly SystemDirectories Dirs = new(
        @"C:\Windows",
        @"C:\Windows\System32",
        @"C:\Users\alex",
        [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows"]);

    public static TheoryData<string> AllCatalogIds()
    {
        var data = new TheoryData<string> { ActionCatalog.None.Id };
        foreach (var definition in ActionCatalog.BuiltIn)
        {
            data.Add(definition.Id);
        }

        for (var i = 0; i < ApplicationSettings.CustomCommandCount; i++)
        {
            data.Add(ActionCatalog.CustomId(i));
        }

        return data;
    }

    private static ApplicationSettings WithCustom(int index, string? command, string? parameters = "", bool hidden = false, bool enabled = true)
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.EnableCustomCommands = enabled;
        settings.CustomCommands[index] = new CustomCommandSettings
        {
            Name = "x",
            Command = command!,
            Parameters = parameters!,
            LaunchHidden = hidden
        };
        return settings;
    }

    private static ActionPlan Plan(string? id, ApplicationSettings? settings = null, bool taskView = false) =>
        ActionPlanner.Plan(id, settings ?? ApplicationSettings.CreateDefault(), Dirs, taskView);

    private static (ushort Key, KeyStepKind Kind)[] StepsOf(ActionPlan plan) =>
        Assert.IsType<ActionPlan.SendKeys>(plan).Sequence.Steps.Select(static s => (s.VirtualKey, s.Kind)).ToArray();

    [Theory]
    [MemberData(nameof(AllCatalogIds))]
    public void Plan_EveryCatalogIdProducesAPlan_WithAndWithoutCustomCommandsAndTaskView(string id)
    {
        foreach (var taskView in new[] { false, true })
        {
            foreach (var enabled in new[] { false, true })
            {
                var settings = ApplicationSettings.CreateDefault();
                settings.EnableCustomCommands = enabled;
                Assert.NotNull(ActionPlanner.Plan(id, settings, Dirs, taskView));
            }
        }
    }

    [Fact]
    public void Plan_EveryBuiltInIdDoesSomething()
    {
        foreach (var definition in ActionCatalog.BuiltIn)
        {
            Assert.IsNotType<ActionPlan.Nothing>(Plan(definition.Id));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("unknown-action")]
    [InlineData("FILE-EXPLORER")]
    [InlineData("custom-0")]
    [InlineData("custom-5")]
    public void Plan_NullUnknownOrNoneIdsDoNothing(string? id)
    {
        Assert.IsType<ActionPlan.Nothing>(Plan(id, WithCustom(0, "notepad.exe")));
    }

    [Fact]
    public void Plan_FileExplorerLaunchesAbsoluteExplorerAsBuiltIn()
    {
        var launch = Assert.IsType<ActionPlan.Launch>(Plan(ActionIds.FileExplorer));
        Assert.Equal(SystemTargets.FileExplorer(Dirs), launch.Request);
        Assert.Equal(@"C:\Windows\explorer.exe", launch.Request.Target);
        Assert.Equal(LaunchTargetKind.BuiltIn, launch.Request.Kind);
    }

    [Fact]
    public void Plan_TaskManagerLaunchesAbsoluteTaskmgr()
    {
        var launch = Assert.IsType<ActionPlan.Launch>(Plan(ActionIds.TaskManager));
        Assert.Equal(@"C:\Windows\System32\taskmgr.exe", launch.Request.Target);
        Assert.Equal(LaunchTargetKind.BuiltIn, launch.Request.Kind);
    }

    [Fact]
    public void Plan_SettingsOpensMsSettingsUri()
    {
        var launch = Assert.IsType<ActionPlan.Launch>(Plan(ActionIds.Settings));
        Assert.Equal(SystemTargets.SystemSettings(), launch.Request);
    }

    [Fact]
    public void Plan_BuiltInLaunchesAreAcceptedByTheLaunchPlanner()
    {
        foreach (var id in new[] { ActionIds.FileExplorer, ActionIds.TaskManager, ActionIds.Settings })
        {
            var launch = Assert.IsType<ActionPlan.Launch>(Plan(id));
            Assert.True(LaunchPlanner.TryPlan(launch.Request, appIsElevated: true, Dirs, out _, out var error), error);
        }
    }

    [Fact]
    public void Plan_AllWindowsTogglesTaskViewWithWinTab()
    {
        Assert.Equal([(VkLWin, KeyStepKind.Hold), (VkTab, KeyStepKind.Press)], StepsOf(Plan(ActionIds.AllWindows, taskView: false)));
    }

    [Fact]
    public void Plan_AllWindowsClosesTaskViewWithEscapeWhenAlreadyInFront()
    {
        Assert.Equal([(VkEscape, KeyStepKind.Press)], StepsOf(Plan(ActionIds.AllWindows, taskView: true)));
    }

    [Fact]
    public void Plan_TaskViewFlagOnlyAffectsAllWindows()
    {
        Assert.Equal(StepsOf(Plan(ActionIds.Desktop, taskView: false)), StepsOf(Plan(ActionIds.Desktop, taskView: true)));
    }

    [Theory]
    [InlineData(ActionIds.Desktop, (ushort)'D')]
    [InlineData(ActionIds.ActionCenter, (ushort)'A')]
    [InlineData(ActionIds.NotificationCenter, (ushort)'N')]
    [InlineData(ActionIds.HideOtherWindows, VkHome)]
    public void Plan_WinShortcutActionsHoldWinThenPressKey(string id, ushort key)
    {
        Assert.Equal([(VkLWin, KeyStepKind.Hold), (key, KeyStepKind.Press)], StepsOf(Plan(id)));
    }

    [Theory]
    [InlineData(ActionIds.ScreenSaver, SystemCommandKind.StartScreenSaver)]
    [InlineData(ActionIds.MonitorsOff, SystemCommandKind.TurnDisplayOff)]
    [InlineData(ActionIds.StartMenu, SystemCommandKind.OpenStartMenu)]
    public void Plan_SystemCommandActions(string id, SystemCommandKind kind)
    {
        Assert.Equal(new ActionPlan.SystemCommand(kind), Plan(id));
    }

    [Fact]
    public void Plan_LockScreenLocksTheWorkstation()
    {
        Assert.IsType<ActionPlan.LockWorkstation>(Plan(ActionIds.LockScreen));
    }

    [Fact]
    public void Plan_CustomCommandIsSkippedWhenCustomCommandsAreDisabled()
    {
        var nothing = Assert.IsType<ActionPlan.Nothing>(Plan("custom-1", WithCustom(0, "notepad.exe", enabled: false)));
        Assert.Contains("disabled", nothing.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Plan_EmptyCustomSlotDoesNothing(string? command)
    {
        var nothing = Assert.IsType<ActionPlan.Nothing>(Plan("custom-2", WithCustom(1, command)));
        Assert.Contains("empty", nothing.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("!control+nosuchkey")]
    [InlineData("#[Chrome_WidgetWin_1]")]
    [InlineData("notepad.exe\nevil.exe")]
    public void Plan_InvalidCustomCommandDoesNothingAndExplains(string command)
    {
        var nothing = Assert.IsType<ActionPlan.Nothing>(Plan("custom-3", WithCustom(2, command)));
        Assert.StartsWith("Custom command 3 is invalid: ", nothing.Reason, StringComparison.Ordinal);
        Assert.True(nothing.Reason.Length > "Custom command 3 is invalid: ".Length);
    }

    [Fact]
    public void Plan_CustomHotkeySendsTheParsedSequence()
    {
        Assert.Equal([(VkLWin, KeyStepKind.Hold), (VkTab, KeyStepKind.Press)], StepsOf(Plan("custom-4", WithCustom(3, "  !_win+tab  "))));
    }

    [Fact]
    public void Plan_CustomConditionalProducesConditionalKeys()
    {
        var plan = Assert.IsType<ActionPlan.ConditionalKeys>(Plan("custom-1", WithCustom(0, "#[Notepad]:(f5)?(f6)")));
        var condition = Assert.Single(plan.Expression.Conditions);
        Assert.Equal("Notepad", condition.ClassName);
        Assert.NotNull(plan.Expression.Otherwise);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_CustomLaunchCarriesTargetParametersAndHiddenFlag(bool hidden)
    {
        var launch = Assert.IsType<ActionPlan.Launch>(Plan("custom-2", WithCustom(1, "  notepad.exe  ", "notes.txt", hidden)));
        Assert.Equal(new LaunchRequest("notepad.exe", "notes.txt", hidden, LaunchTargetKind.CustomCommand), launch.Request);
    }

    [Fact]
    public void Plan_CustomLaunchWithNullParametersUsesEmptyArguments()
    {
        var launch = Assert.IsType<ActionPlan.Launch>(Plan("custom-1", WithCustom(0, "https://example.com", parameters: null)));
        Assert.Equal(string.Empty, launch.Request.Arguments);
    }

    [Fact]
    public void Plan_UsesTheSlotMatchingTheId()
    {
        var settings = WithCustom(2, "calc.exe");
        Assert.IsType<ActionPlan.Nothing>(Plan("custom-1", settings));
        Assert.Equal("calc.exe", Assert.IsType<ActionPlan.Launch>(Plan("custom-3", settings)).Request.Target);
    }

    [Fact]
    public void Plan_NullCommandArrayDoesNotThrow()
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.EnableCustomCommands = true;
        settings.CustomCommands = null!;
        Assert.IsType<ActionPlan.Nothing>(Plan("custom-1", settings));
    }

    [Fact]
    public void Plan_NullCommandElementDoesNotThrow()
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.EnableCustomCommands = true;
        settings.CustomCommands = [null!, null!, null!, null!];
        Assert.IsType<ActionPlan.Nothing>(Plan("custom-4", settings));
    }

    [Fact]
    public void Plan_ShortCommandArrayDoesNotThrow()
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.EnableCustomCommands = true;
        settings.CustomCommands = [new CustomCommandSettings { Command = "notepad.exe" }];
        Assert.IsType<ActionPlan.Launch>(Plan("custom-1", settings));
        Assert.IsType<ActionPlan.Nothing>(Plan("custom-4", settings));
    }

    [Fact]
    public void Plan_DoesNotModifyTheSettings()
    {
        var settings = WithCustom(0, "notepad.exe", null);
        Plan("custom-1", settings);
        Assert.Null(settings.CustomCommands[0].Parameters);
    }

    [Fact]
    public void Plan_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ActionPlanner.Plan("desktop", null!, Dirs, false));
        Assert.Throws<ArgumentNullException>(() => ActionPlanner.Plan("desktop", ApplicationSettings.CreateDefault(), null!, false));
    }

    [Fact]
    public void ParseBuiltIn_ThrowsForInvalidText()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ActionPlanner.ParseBuiltIn("_win+nosuchkey"));
        Assert.Contains("_win+nosuchkey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseBuiltIn_ParsesValidText()
    {
        Assert.Single(ActionPlanner.ParseBuiltIn("escape").Steps);
    }
}
