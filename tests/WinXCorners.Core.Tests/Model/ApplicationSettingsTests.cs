using System.Reflection;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Tests.Model;

public class ApplicationSettingsTests
{
    [Fact]
    public void Defaults_MatchTheDocumentedConstantsAndAreKnownActions()
    {
        var settings = ApplicationSettings.CreateDefault();

        Assert.True(settings.HotCornersEnabled);
        Assert.Equal(ActionIds.FileExplorer, settings.TopLeftActionId);
        Assert.Equal(ActionIds.HideOtherWindows, settings.TopRightActionId);
        Assert.Equal(ActionIds.AllWindows, settings.BottomLeftActionId);
        Assert.Equal(ActionIds.ActionCenter, settings.BottomRightActionId);
        Assert.All(
            new[] { settings.TopLeftActionId, settings.TopRightActionId, settings.BottomLeftActionId, settings.BottomRightActionId },
            id => Assert.True(ActionCatalog.IsKnown(id)));
        Assert.True(settings.IgnoreFullScreen);
        Assert.False(settings.StartWithWindows);
        Assert.False(settings.AlwaysRunAsAdministrator);
        Assert.False(settings.AlwaysHideTrayIcon);
        Assert.False(settings.EnableCustomCommands);
        Assert.False(settings.ShowCountdown);
        Assert.Equal(FlyoutAnimationDirection.Bottom, settings.FlyoutAnimationDirection);
        Assert.Equal(HotCornerModifierKey.None, settings.HotCornerModifierKey);
        Assert.All(
            new[] { settings.GlobalDelayIndex, settings.TopLeftDelayIndex, settings.TopRightDelayIndex, settings.BottomLeftDelayIndex, settings.BottomRightDelayIndex },
            index => Assert.Equal(ApplicationSettings.DefaultDelayIndex, index));
        Assert.InRange(ApplicationSettings.DefaultDelayIndex, 0, ApplicationSettings.DelayOptionCount - 1);
    }

    [Fact]
    public void CreateDefaultCommands_ReturnsDistinctEmptyCommandsEveryCall()
    {
        var first = ApplicationSettings.CreateDefaultCommands();
        var second = ApplicationSettings.CreateDefaultCommands();

        Assert.Equal(ApplicationSettings.CustomCommandCount, first.Length);
        Assert.Equal(first.Length, first.Distinct().Count());
        Assert.NotSame(first[0], second[0]);
        Assert.All(first, command =>
        {
            Assert.Equal(string.Empty, command.Name);
            Assert.Equal(string.Empty, command.Command);
            Assert.Equal(string.Empty, command.Parameters);
            Assert.False(command.LaunchHidden);
        });
    }

    [Fact]
    public void Clone_CopiesEveryPublicProperty()
    {
        var original = CreateNonDefault();

        var clone = original.Clone();

        foreach (var property in typeof(ApplicationSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == nameof(ApplicationSettings.CustomCommands))
            {
                continue;
            }

            Assert.Equal(property.GetValue(original), property.GetValue(clone));
            // Guard against a property being added to the type but forgotten in Clone: CreateNonDefault must
            // set every property to a non-default value for this test to mean anything.
            Assert.NotEqual(property.GetValue(new ApplicationSettings()), property.GetValue(clone));
        }
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var original = CreateNonDefault();

        var clone = original.Clone();
        clone.CustomCommands[0].Name = "changed";
        clone.CustomCommands[1] = new CustomCommandSettings { Name = "replaced" };

        Assert.NotSame(original.CustomCommands, clone.CustomCommands);
        Assert.Equal("name0", original.CustomCommands[0].Name);
        Assert.Equal("name1", original.CustomCommands[1].Name);
        Assert.Equal("cmd2", clone.CustomCommands[2].Command);
        Assert.Equal("args3", clone.CustomCommands[3].Parameters);
        Assert.True(clone.CustomCommands[3].LaunchHidden);
    }

    [Fact]
    public void Clone_ToleratesNullArray()
    {
        var original = new ApplicationSettings { CustomCommands = null! };

        var clone = original.Clone();

        Assert.NotNull(clone.CustomCommands);
        Assert.Empty(clone.CustomCommands);
    }

    [Fact]
    public void Clone_ReplacesNullElementsAndNullStrings()
    {
        var original = new ApplicationSettings
        {
            CustomCommands = [null!, new CustomCommandSettings { Name = null!, Command = null!, Parameters = null! }]
        };

        var clone = original.Clone();

        Assert.Equal(2, clone.CustomCommands.Length);
        Assert.NotNull(clone.CustomCommands[0]);
        Assert.Equal(string.Empty, clone.CustomCommands[1].Name);
        Assert.Equal(string.Empty, clone.CustomCommands[1].Command);
        Assert.Equal(string.Empty, clone.CustomCommands[1].Parameters);
    }

    [Fact]
    public void Clone_KeepsWrongLengthAndInvalidValuesForTheSanitizerToFix()
    {
        var original = new ApplicationSettings
        {
            CustomCommands = new CustomCommandSettings[9],
            GlobalDelayIndex = 99,
            HotCornerModifierKey = (HotCornerModifierKey)7,
            TopLeftActionId = null!
        };

        var clone = original.Clone();

        Assert.Equal(9, clone.CustomCommands.Length);
        Assert.Equal(99, clone.GlobalDelayIndex);
        Assert.Equal((HotCornerModifierKey)7, clone.HotCornerModifierKey);
        Assert.Null(clone.TopLeftActionId);
    }

    [Fact]
    public void CustomCommandClone_IsIndependentCopy()
    {
        var original = new CustomCommandSettings { Name = "n", Command = "c", Parameters = "p", LaunchHidden = true };

        var clone = original.Clone();
        clone.Name = "x";

        Assert.NotSame(original, clone);
        Assert.Equal("n", original.Name);
        Assert.Equal("c", clone.Command);
        Assert.Equal("p", clone.Parameters);
        Assert.True(clone.LaunchHidden);
    }

    internal static ApplicationSettings CreateNonDefault()
    {
        return new ApplicationSettings
        {
            HotCornersEnabled = false,
            TopLeftActionId = ActionIds.LockScreen,
            TopRightActionId = ActionIds.Desktop,
            BottomLeftActionId = "custom-1",
            BottomRightActionId = ActionIds.None,
            StartWithWindows = true,
            IgnoreFullScreen = false,
            AlwaysRunAsAdministrator = true,
            AlwaysHideTrayIcon = true,
            FlyoutAnimationDirection = FlyoutAnimationDirection.Left,
            GlobalDelayEnabled = true,
            GlobalDelayIndex = 0,
            TopLeftDelayEnabled = true,
            TopLeftDelayIndex = 1,
            TopRightDelayEnabled = true,
            TopRightDelayIndex = 2,
            BottomLeftDelayEnabled = true,
            BottomLeftDelayIndex = 4,
            BottomRightDelayEnabled = true,
            BottomRightDelayIndex = 5,
            ShowCountdown = true,
            EnableCustomCommands = true,
            HotCornerModifierKey = HotCornerModifierKey.Alt,
            CustomCommands = Enumerable.Range(0, ApplicationSettings.CustomCommandCount)
                .Select(index => new CustomCommandSettings
                {
                    Name = $"name{index}",
                    Command = $"cmd{index}",
                    Parameters = $"args{index}",
                    LaunchHidden = index % 2 == 1
                })
                .ToArray()
        };
    }
}
