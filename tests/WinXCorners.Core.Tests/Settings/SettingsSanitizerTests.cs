using WinXCorners.Core.Actions;
using WinXCorners.Core.Model;
using WinXCorners.Core.Settings;
using WinXCorners.Core.Tests.Model;

namespace WinXCorners.Core.Tests.Settings;

public class SettingsSanitizerTests
{
    [Fact]
    public void CleanDefaults_ProduceNoCorrections()
    {
        Assert.Empty(SettingsSanitizer.Sanitize(ApplicationSettings.CreateDefault()));
    }

    [Fact]
    public void CleanNonDefaultSettings_AreLeftUnchanged()
    {
        var settings = ApplicationSettingsTests.CreateNonDefault();
        var before = settings.Clone();

        Assert.Empty(SettingsSanitizer.Sanitize(settings));
        SettingsAssert.Equivalent(before, settings);
    }

    [Fact]
    public void NullOrBlankActionIds_FallBackToEachCornersDefault()
    {
        var settings = new ApplicationSettings
        {
            TopLeftActionId = null!,
            TopRightActionId = "",
            BottomLeftActionId = "   ",
            BottomRightActionId = "\t"
        };

        var corrections = SettingsSanitizer.Sanitize(settings);

        Assert.Equal(4, corrections.Count);
        Assert.Equal(ApplicationSettings.DefaultTopLeftActionId, settings.TopLeftActionId);
        Assert.Equal(ApplicationSettings.DefaultTopRightActionId, settings.TopRightActionId);
        Assert.Equal(ApplicationSettings.DefaultBottomLeftActionId, settings.BottomLeftActionId);
        Assert.Equal(ApplicationSettings.DefaultBottomRightActionId, settings.BottomRightActionId);
    }

    [Theory]
    [InlineData("format-c")]
    [InlineData("File-Explorer")] // ids are case-sensitive
    [InlineData(" file-explorer")]
    [InlineData("custom-0")]
    [InlineData("custom-5")]
    public void UnknownActionIds_BecomeNoneRatherThanAGuess(string id)
    {
        var settings = new ApplicationSettings { TopLeftActionId = id, TopRightActionId = id, BottomLeftActionId = id, BottomRightActionId = id };

        var corrections = SettingsSanitizer.Sanitize(settings);

        Assert.Equal(4, corrections.Count);
        Assert.All(new[] { settings.TopLeftActionId, settings.TopRightActionId, settings.BottomLeftActionId, settings.BottomRightActionId },
            value => Assert.Equal(ActionIds.None, value));
    }

    [Fact]
    public void KnownCustomIds_AreKeptEvenWhenCustomCommandsAreDisabled()
    {
        var settings = new ApplicationSettings { TopLeftActionId = "custom-4", EnableCustomCommands = false };

        Assert.Empty(SettingsSanitizer.Sanitize(settings));
        Assert.Equal("custom-4", settings.TopLeftActionId);
    }

    [Fact]
    public void CorrectionForHostileActionId_IsShortAndFreeOfControlCharacters()
    {
        var settings = new ApplicationSettings { TopLeftActionId = "evil\r\nINJECTED " + new string('x', 5000) };

        var correction = Assert.Single(SettingsSanitizer.Sanitize(settings));

        Assert.True(correction.Length < 200, correction);
        Assert.DoesNotContain('\n', correction);
        Assert.DoesNotContain('\r', correction);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(ApplicationSettings.DelayOptionCount)]
    [InlineData(99)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void OutOfRangeDelayIndexes_ResetToDefault(int value)
    {
        var settings = new ApplicationSettings
        {
            GlobalDelayIndex = value,
            TopLeftDelayIndex = value,
            TopRightDelayIndex = value,
            BottomLeftDelayIndex = value,
            BottomRightDelayIndex = value
        };

        Assert.Equal(5, SettingsSanitizer.Sanitize(settings).Count);
        Assert.All(
            new[] { settings.GlobalDelayIndex, settings.TopLeftDelayIndex, settings.TopRightDelayIndex, settings.BottomLeftDelayIndex, settings.BottomRightDelayIndex },
            index => Assert.Equal(ApplicationSettings.DefaultDelayIndex, index));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ApplicationSettings.DelayOptionCount - 1)]
    public void BoundaryDelayIndexes_AreKept(int value)
    {
        var settings = new ApplicationSettings { GlobalDelayIndex = value, BottomRightDelayIndex = value };

        Assert.Empty(SettingsSanitizer.Sanitize(settings));
        Assert.Equal(value, settings.GlobalDelayIndex);
        Assert.Equal(value, settings.BottomRightDelayIndex);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(42)]
    [InlineData(-1)]
    public void UndefinedAnimationDirection_ResetsToBottom(int value)
    {
        var settings = new ApplicationSettings { FlyoutAnimationDirection = (FlyoutAnimationDirection)value };

        Assert.Single(SettingsSanitizer.Sanitize(settings));
        Assert.Equal(FlyoutAnimationDirection.Bottom, settings.FlyoutAnimationDirection);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(-3)]
    public void UndefinedModifierKey_ResetsToNone(int value)
    {
        var settings = new ApplicationSettings { HotCornerModifierKey = (HotCornerModifierKey)value };

        Assert.Single(SettingsSanitizer.Sanitize(settings));
        Assert.Equal(HotCornerModifierKey.None, settings.HotCornerModifierKey);
    }

    [Fact]
    public void NullCommandArray_IsReplacedWithDefaults()
    {
        var settings = new ApplicationSettings { CustomCommands = null! };

        Assert.Single(SettingsSanitizer.Sanitize(settings));
        Assert.Equal(ApplicationSettings.CustomCommandCount, settings.CustomCommands.Length);
        Assert.All(settings.CustomCommands, command => Assert.NotNull(command));
    }

    [Fact]
    public void NullCommandElements_BecomeEmptyCommands()
    {
        var settings = new ApplicationSettings { CustomCommands = [null!, null!, null!, null!] };

        Assert.Equal(4, SettingsSanitizer.Sanitize(settings).Count);
        Assert.All(settings.CustomCommands, command => Assert.Equal(string.Empty, command.Command));
    }

    [Fact]
    public void TooFewCommands_ArePaddedKeepingTheExistingOnes()
    {
        var first = new CustomCommandSettings { Name = "a", Command = "notepad.exe" };
        var settings = new ApplicationSettings { CustomCommands = [first, new CustomCommandSettings { Name = "b" }] };

        SettingsSanitizer.Sanitize(settings);

        Assert.Equal(ApplicationSettings.CustomCommandCount, settings.CustomCommands.Length);
        Assert.Same(first, settings.CustomCommands[0]);
        Assert.Equal("b", settings.CustomCommands[1].Name);
        Assert.Equal(string.Empty, settings.CustomCommands[3].Name);
    }

    [Fact]
    public void TooManyCommands_KeepOnlyTheFirstOnes()
    {
        var settings = new ApplicationSettings
        {
            CustomCommands = Enumerable.Range(0, 9).Select(index => new CustomCommandSettings { Name = $"n{index}" }).ToArray()
        };

        Assert.Single(SettingsSanitizer.Sanitize(settings));
        Assert.Equal(["n0", "n1", "n2", "n3"], settings.CustomCommands.Select(command => command.Name));
    }

    [Fact]
    public void EmptyCommandArray_IsPadded()
    {
        var settings = new ApplicationSettings { CustomCommands = [] };

        SettingsSanitizer.Sanitize(settings);

        Assert.Equal(ApplicationSettings.CustomCommandCount, settings.CustomCommands.Length);
    }

    [Fact]
    public void NullStrings_BecomeEmpty()
    {
        var command = new CustomCommandSettings { Name = null!, Command = null!, Parameters = null! };
        var settings = new ApplicationSettings { CustomCommands = [command, new(), new(), new()] };

        Assert.Equal(3, SettingsSanitizer.Sanitize(settings).Count);
        Assert.Equal(string.Empty, command.Name);
        Assert.Equal(string.Empty, command.Command);
        Assert.Equal(string.Empty, command.Parameters);
    }

    [Fact]
    public void Name_IsTrimmedAndStrippedOfControlCharacters()
    {
        var command = new CustomCommandSettings { Name = "  My\tTool\u0000\u007F\u0085  " };
        var settings = new ApplicationSettings { CustomCommands = [command, new(), new(), new()] };

        SettingsSanitizer.Sanitize(settings);

        Assert.Equal("MyTool", command.Name);
    }

    [Fact]
    public void Name_IsCappedAtMaxLengthWithoutTrailingSpace()
    {
        // 31 letters then a space then more: the cut lands right after the space, which must not survive.
        var command = new CustomCommandSettings { Name = new string('a', SettingsSanitizer.MaxNameLength - 1) + " tail" };
        var settings = new ApplicationSettings { CustomCommands = [command, new(), new(), new()] };

        SettingsSanitizer.Sanitize(settings);

        Assert.Equal(new string('a', SettingsSanitizer.MaxNameLength - 1), command.Name);
        Assert.Empty(SettingsSanitizer.Sanitize(settings));
    }

    [Fact]
    public void Name_AtExactlyMaxLength_IsKept()
    {
        var name = new string('n', SettingsSanitizer.MaxNameLength);
        var command = new CustomCommandSettings { Name = name };
        var settings = new ApplicationSettings { CustomCommands = [command, new(), new(), new()] };

        Assert.Empty(SettingsSanitizer.Sanitize(settings));
        Assert.Equal(name, command.Name);
    }

    [Fact]
    public void Truncation_DoesNotSplitASurrogatePair()
    {
        // The emoji occupies positions 31 and 32; cutting at 32 would leave a lone high surrogate.
        var command = new CustomCommandSettings { Name = new string('a', SettingsSanitizer.MaxNameLength - 1) + "\U0001F600xyz" };
        var settings = new ApplicationSettings { CustomCommands = [command, new(), new(), new()] };

        SettingsSanitizer.Sanitize(settings);

        Assert.Equal(new string('a', SettingsSanitizer.MaxNameLength - 1), command.Name);
    }

    [Fact]
    public void CommandAndParameters_LoseControlCharactersButKeepSpaces()
    {
        var command = new CustomCommandSettings { Command = " C:\\Tools\\x.exe\r\n", Parameters = "--a \"b c\"\u001B[0m\u0000" };
        var settings = new ApplicationSettings { CustomCommands = [command, new(), new(), new()] };

        Assert.Equal(2, SettingsSanitizer.Sanitize(settings).Count);
        Assert.Equal(" C:\\Tools\\x.exe", command.Command);
        Assert.Equal("--a \"b c\"[0m", command.Parameters);
    }

    [Fact]
    public void CommandAndParameters_AreCappedAtTheirLimits()
    {
        var command = new CustomCommandSettings
        {
            Command = new string('c', SettingsSanitizer.MaxCommandLength + 1),
            Parameters = new string('p', SettingsSanitizer.MaxParametersLength + 100)
        };
        var settings = new ApplicationSettings { CustomCommands = [command, new(), new(), new()] };

        Assert.Equal(2, SettingsSanitizer.Sanitize(settings).Count);
        Assert.Equal(SettingsSanitizer.MaxCommandLength, command.Command.Length);
        Assert.Equal(SettingsSanitizer.MaxParametersLength, command.Parameters.Length);
    }

    [Fact]
    public void LimitsHaveTheSpecifiedValues()
    {
        Assert.Equal(32, SettingsSanitizer.MaxNameLength);
        Assert.Equal(2048, SettingsSanitizer.MaxCommandLength);
        Assert.Equal(4096, SettingsSanitizer.MaxParametersLength);
    }

    [Fact]
    public void Sanitize_IsIdempotentOnThoroughlyBrokenInput()
    {
        var settings = new ApplicationSettings
        {
            TopLeftActionId = null!,
            TopRightActionId = "bogus",
            GlobalDelayIndex = 99,
            BottomLeftDelayIndex = -5,
            FlyoutAnimationDirection = (FlyoutAnimationDirection)42,
            HotCornerModifierKey = (HotCornerModifierKey)7,
            CustomCommands =
            [
                null!,
                new CustomCommandSettings { Name = " \u0001" + new string('x', 100) + " ", Command = null!, Parameters = "\n" },
                new CustomCommandSettings { Command = new string('c', 5000), Parameters = new string('p', 9000) },
                null!, new(), new()
            ]
        };

        Assert.NotEmpty(SettingsSanitizer.Sanitize(settings));
        var once = settings.Clone();

        Assert.Empty(SettingsSanitizer.Sanitize(settings));
        SettingsAssert.Equivalent(once, settings);
    }

    [Fact]
    public void Sanitize_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => SettingsSanitizer.Sanitize(null!));
    }
}
