using WinXCorners.Core.Actions;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Tests.Actions;

public class ActionCatalogTests
{
    private static readonly string[] BuiltInIds =
    [
        ActionIds.FileExplorer, ActionIds.Settings, ActionIds.TaskManager, ActionIds.AllWindows,
        ActionIds.Desktop, ActionIds.ScreenSaver, ActionIds.MonitorsOff, ActionIds.ActionCenter,
        ActionIds.NotificationCenter, ActionIds.LockScreen, ActionIds.StartMenu, ActionIds.HideOtherWindows
    ];

    private static ApplicationSettings WithNames(params string?[] names)
    {
        var settings = new ApplicationSettings();
        for (var i = 0; i < names.Length; i++)
        {
            settings.CustomCommands[i].Name = names[i]!;
        }

        return settings;
    }

    [Fact]
    public void EveryBuiltInIdIsKnownAndBuiltIn()
    {
        Assert.Equal(BuiltInIds.Order(StringComparer.Ordinal), ActionCatalog.BuiltIn.Select(static d => d.Id).Order(StringComparer.Ordinal));
        foreach (var id in BuiltInIds)
        {
            Assert.True(ActionCatalog.IsKnown(id), id);
            Assert.True(ActionCatalog.TryGet(id, out var definition));
            Assert.Equal((id, ActionKind.BuiltIn, -1), (definition.Id, definition.Kind, definition.CustomIndex));
            Assert.False(ActionCatalog.IsNoOp(id));
            Assert.False(ActionCatalog.TryGetCustomIndex(id, out var index));
            Assert.Equal(-1, index);
            Assert.False(string.IsNullOrWhiteSpace(definition.MenuText));
            Assert.False(string.IsNullOrWhiteSpace(definition.ButtonText));
        }
    }

    [Fact]
    public void DefaultCornerActionsAreKnownBuiltIns()
    {
        foreach (var id in new[]
                 {
                     ApplicationSettings.DefaultTopLeftActionId, ApplicationSettings.DefaultTopRightActionId,
                     ApplicationSettings.DefaultBottomLeftActionId, ApplicationSettings.DefaultBottomRightActionId
                 })
        {
            Assert.True(ActionCatalog.TryGet(id, out var definition));
            Assert.Equal(ActionKind.BuiltIn, definition.Kind);
        }
    }

    [Fact]
    public void IdsAndLabelsAreUnique()
    {
        var ids = new[] { ActionCatalog.None.Id }
            .Concat(ActionCatalog.BuiltIn.Select(static d => d.Id))
            .Concat(Enumerable.Range(0, ApplicationSettings.CustomCommandCount).Select(ActionCatalog.CustomId))
            .ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(ActionCatalog.BuiltIn.Count, ActionCatalog.BuiltIn.Select(static d => d.MenuText).Distinct().Count());
        Assert.All(ActionCatalog.BuiltIn, static d => Assert.False(d.Id.StartsWith(ActionIds.CustomPrefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void CustomId_RoundTripsWithTryGetCustomIndex()
    {
        for (var i = 0; i < ApplicationSettings.CustomCommandCount; i++)
        {
            var id = ActionCatalog.CustomId(i);
            Assert.Equal($"custom-{i + 1}", id);
            Assert.True(ActionCatalog.TryGetCustomIndex(id, out var index));
            Assert.Equal(i, index);
            Assert.True(ActionCatalog.TryGet(id, out var definition));
            Assert.Equal(ActionKind.Custom, definition.Kind);
            Assert.False(ActionCatalog.IsNoOp(id));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void CustomId_RejectsOutOfRangeSlots(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ActionCatalog.CustomId(index));
    }

    [Theory]
    [InlineData("custom-0")]
    [InlineData("custom-5")]
    [InlineData("custom-01")]
    [InlineData("custom-")]
    [InlineData("custom-1 ")]
    [InlineData("Custom-1")]
    [InlineData(null)]
    public void TryGetCustomIndex_RejectsAnythingElse(string? id)
    {
        Assert.False(ActionCatalog.TryGetCustomIndex(id, out var index));
        Assert.Equal(-1, index);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("none")]
    [InlineData("rm -rf /")]
    [InlineData("custom-9")]
    [InlineData("NONE")]
    public void IsNoOp_ForMissingBlankUnknownAndNone(string? id)
    {
        Assert.True(ActionCatalog.IsNoOp(id));
    }

    [Theory]
    [InlineData("File-Explorer")]
    [InlineData("FILE-EXPLORER")]
    [InlineData("Lock-Screen")]
    [InlineData("None")]
    [InlineData("CUSTOM-1")]
    [InlineData(" file-explorer")]
    public void Lookups_AreCaseAndWhitespaceSensitive(string id)
    {
        Assert.False(ActionCatalog.IsKnown(id));
        Assert.False(ActionCatalog.TryGet(id, out var definition));
        Assert.Same(ActionCatalog.None, definition);
        Assert.True(ActionCatalog.IsNoOp(id));
    }

    [Fact]
    public void None_IsKnownButANoOp()
    {
        Assert.True(ActionCatalog.IsKnown(ActionIds.None));
        Assert.True(ActionCatalog.TryGet(ActionIds.None, out var definition));
        Assert.Equal(ActionKind.None, definition.Kind);
        Assert.True(ActionCatalog.IsNoOp(ActionIds.None));
        Assert.False(ActionCatalog.IsKnown(null));
    }

    [Fact]
    public void BuildCornerMenu_WithoutCustomCommands()
    {
        var menu = ActionCatalog.BuildCornerMenu(new ApplicationSettings(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(1 + ActionCatalog.BuiltIn.Count, menu.Count);
        Assert.Equal(new CornerMenuItem("None", ActionIds.None), menu[0]);
        Assert.DoesNotContain(menu, static item => item.IsSeparator);
        Assert.DoesNotContain(menu, static item => item.ActionId.StartsWith(ActionIds.CustomPrefix, StringComparison.Ordinal));

        var texts = menu.Skip(1).Select(static item => item.Text).ToList();
        Assert.Equal(texts.Order(StringComparer.OrdinalIgnoreCase), texts);
        Assert.Equal("Action Center", texts[0]);
        Assert.Equal("Turn Display Off", texts[^1]);
        Assert.All(menu, static item => Assert.True(ActionCatalog.IsKnown(item.ActionId)));
    }

    [Fact]
    public void BuildCornerMenu_WithCustomCommandsAddsSeparatorAndSortedSlots()
    {
        var settings = WithNames("zebra", "", "Apple", "Command 4");
        settings.EnableCustomCommands = true;

        var menu = ActionCatalog.BuildCornerMenu(settings, StringComparer.OrdinalIgnoreCase);

        var separatorIndex = 1 + ActionCatalog.BuiltIn.Count;
        Assert.Equal(separatorIndex + 1 + ApplicationSettings.CustomCommandCount, menu.Count);
        Assert.Equal(CornerMenuItem.Separator, menu[separatorIndex]);
        Assert.Equal(1, menu.Count(static item => item.IsSeparator));
        Assert.Equal(
            [
                new CornerMenuItem("Apple", "custom-3"),
                new CornerMenuItem("Command 2", "custom-2"),
                new CornerMenuItem("Command 4", "custom-4"),
                new CornerMenuItem("zebra", "custom-1")
            ],
            menu.Skip(separatorIndex + 1));
    }

    [Fact]
    public void BuildCornerMenu_DefaultComparerAndNullSettings()
    {
        Assert.Equal(1 + ActionCatalog.BuiltIn.Count, ActionCatalog.BuildCornerMenu(new ApplicationSettings()).Count);
        Assert.Throws<ArgumentNullException>(() => ActionCatalog.BuildCornerMenu(null!));
    }

    [Fact]
    public void CornerMenuItemSeparator_HasNoAction()
    {
        Assert.True(CornerMenuItem.Separator.IsSeparator);
        Assert.Equal(string.Empty, CornerMenuItem.Separator.ActionId);
        Assert.True(ActionCatalog.IsNoOp(CornerMenuItem.Separator.ActionId));
    }

    [Theory]
    [InlineData(null, "Command 1")]
    [InlineData("", "Command 1")]
    [InlineData("   ", "Command 1")]
    [InlineData("Command 1", "Command 1")]
    [InlineData("command 1", "Command 1")]
    [InlineData("Custom Command 1", "Command 1")]
    [InlineData("  CUSTOM COMMAND 1  ", "Command 1")]
    [InlineData("Command 2", "Command 2")]           // another slot's placeholder is a real name here
    [InlineData("  Open Notepad ", "Open Notepad")]
    public void GetCustomCommandDisplayName_ForSlotOne(string? name, string expected)
    {
        Assert.Equal(expected, ActionCatalog.GetCustomCommandDisplayName(WithNames(name), 0));
    }

    [Fact]
    public void GetCustomCommandDisplayName_ToleratesUnsanitizedSettings()
    {
        var nullArray = new ApplicationSettings { CustomCommands = null! };
        Assert.Equal("Command 2", ActionCatalog.GetCustomCommandDisplayName(nullArray, 1));

        var nullElement = new ApplicationSettings { CustomCommands = [null!, null!] };
        Assert.Equal("Command 1", ActionCatalog.GetCustomCommandDisplayName(nullElement, 0));

        var shortArray = new ApplicationSettings { CustomCommands = [new CustomCommandSettings { Name = "x" }] };
        Assert.Equal("Command 4", ActionCatalog.GetCustomCommandDisplayName(shortArray, 3));
        Assert.Equal("Command 0", ActionCatalog.GetCustomCommandDisplayName(shortArray, -1));

        Assert.Throws<ArgumentNullException>(() => ActionCatalog.GetCustomCommandDisplayName(null!, 0));
    }

    [Fact]
    public void GetButtonText_BuiltInsCustomsNoneAndUnknown()
    {
        var settings = WithNames("Terminal");
        Assert.Equal("Hide Windows", ActionCatalog.GetButtonText(settings, ActionIds.HideOtherWindows));
        Assert.Equal("All Windows", ActionCatalog.GetButtonText(settings, ActionIds.AllWindows));
        Assert.Equal("Terminal", ActionCatalog.GetButtonText(settings, "custom-1"));
        Assert.Equal("Command 2", ActionCatalog.GetButtonText(settings, "custom-2"));
        Assert.Equal(string.Empty, ActionCatalog.GetButtonText(settings, ActionIds.None));

        // The original echoed unknown ids onto the button; now they render empty.
        Assert.Equal(string.Empty, ActionCatalog.GetButtonText(settings, "<script>"));
        Assert.Equal(string.Empty, ActionCatalog.GetButtonText(settings, null));
        Assert.Equal(string.Empty, ActionCatalog.GetButtonText(settings, "File-Explorer"));
    }

    [Fact]
    public void GetDescriptiveName_IsNeverEmpty()
    {
        var settings = WithNames("Terminal");
        Assert.Equal("Show All Windows", ActionCatalog.GetDescriptiveName(settings, ActionIds.AllWindows));
        Assert.Equal("Terminal", ActionCatalog.GetDescriptiveName(settings, "custom-1"));
        Assert.Equal("None", ActionCatalog.GetDescriptiveName(settings, ActionIds.None));
        Assert.Equal("None", ActionCatalog.GetDescriptiveName(settings, "unknown"));
        Assert.Equal("None", ActionCatalog.GetDescriptiveName(settings, null));
    }

    [Fact]
    public void ButtonTextMatchesTheOriginalLabels()
    {
        // GetActionDisplayText in the original Form1, for every built-in id.
        var expected = new Dictionary<string, string>
        {
            [ActionIds.FileExplorer] = "File Explorer",
            [ActionIds.Settings] = "System Settings",
            [ActionIds.TaskManager] = "Task Manager",
            [ActionIds.ScreenSaver] = "Screen Saver",
            [ActionIds.HideOtherWindows] = "Hide Windows",
            [ActionIds.AllWindows] = "All Windows",
            [ActionIds.Desktop] = "Show Desktop",
            [ActionIds.MonitorsOff] = "Display Off",
            [ActionIds.ActionCenter] = "Action Center",
            [ActionIds.NotificationCenter] = "Notification Center",
            [ActionIds.LockScreen] = "Lock Screen",
            [ActionIds.StartMenu] = "Start Menu"
        };

        var settings = new ApplicationSettings();
        foreach (var (id, text) in expected)
        {
            Assert.Equal(text, ActionCatalog.GetButtonText(settings, id));
        }
    }
}
