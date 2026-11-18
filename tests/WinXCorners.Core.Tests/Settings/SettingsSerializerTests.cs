using System.Text.Json;
using WinXCorners.Core.Model;
using WinXCorners.Core.Settings;
using WinXCorners.Core.Tests.Model;

namespace WinXCorners.Core.Tests.Settings;

public class SettingsSerializerTests
{
    /// <summary>The exact shape v1.4.0 wrote (System.Text.Json, WriteIndented, enums as numbers).</summary>
    internal const string V140Json = """
        {
          "HotCornersEnabled": true,
          "TopLeftActionId": "task-manager",
          "TopRightActionId": "hide-other-windows",
          "BottomLeftActionId": "custom-2",
          "BottomRightActionId": "lock-screen",
          "StartWithWindows": true,
          "IgnoreFullScreen": false,
          "AlwaysRunAsAdministrator": false,
          "AlwaysHideTrayIcon": false,
          "FlyoutAnimationDirection": 2,
          "GlobalDelayEnabled": true,
          "GlobalDelayIndex": 5,
          "TopLeftDelayEnabled": false,
          "TopLeftDelayIndex": 3,
          "TopRightDelayEnabled": true,
          "TopRightDelayIndex": 0,
          "BottomLeftDelayEnabled": false,
          "BottomLeftDelayIndex": 3,
          "BottomRightDelayEnabled": false,
          "BottomRightDelayIndex": 3,
          "ShowCountdown": true,
          "EnableCustomCommands": true,
          "CustomCommands": [
            {
              "Name": "Notes",
              "Command": "notepad.exe",
              "Parameters": "C:\\notes.txt",
              "LaunchHidden": false
            },
            {
              "Name": "Snip",
              "Command": "#+s",
              "Parameters": "",
              "LaunchHidden": true
            },
            {
              "Name": "",
              "Command": "",
              "Parameters": "",
              "LaunchHidden": false
            },
            {
              "Name": "",
              "Command": "",
              "Parameters": "",
              "LaunchHidden": false
            }
          ]
        }
        """;

    [Fact]
    public void DefaultSettings_RoundTripFieldByField()
    {
        var original = ApplicationSettings.CreateDefault();

        var restored = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(original));

        SettingsAssert.Equivalent(original, restored);
    }

    [Fact]
    public void NonDefaultSettings_RoundTripFieldByField()
    {
        var original = ApplicationSettingsTests.CreateNonDefault();

        var restored = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(original));

        SettingsAssert.Equivalent(original, restored);
    }

    [Fact]
    public void V140File_LoadsEveryValue()
    {
        var settings = SettingsSerializer.Deserialize(V140Json);

        Assert.Equal("task-manager", settings.TopLeftActionId);
        Assert.Equal("custom-2", settings.BottomLeftActionId);
        Assert.Equal("lock-screen", settings.BottomRightActionId);
        Assert.True(settings.StartWithWindows);
        Assert.False(settings.IgnoreFullScreen);
        Assert.Equal(FlyoutAnimationDirection.Left, settings.FlyoutAnimationDirection);
        Assert.True(settings.GlobalDelayEnabled);
        Assert.Equal(5, settings.GlobalDelayIndex);
        Assert.True(settings.TopRightDelayEnabled);
        Assert.Equal(0, settings.TopRightDelayIndex);
        Assert.True(settings.ShowCountdown);
        Assert.True(settings.EnableCustomCommands);
        Assert.Equal("Notes", settings.CustomCommands[0].Name);
        Assert.Equal("C:\\notes.txt", settings.CustomCommands[0].Parameters);
        Assert.Equal("#+s", settings.CustomCommands[1].Command);
        Assert.True(settings.CustomCommands[1].LaunchHidden);
        // Absent in older files: keeps its default.
        Assert.Equal(HotCornerModifierKey.None, settings.HotCornerModifierKey);
        Assert.Empty(SettingsSanitizer.Sanitize(settings));
    }

    [Fact]
    public void Output_UsesV140PropertyNamesInOrderWithNumericEnums()
    {
        var json = SettingsSerializer.Serialize(new ApplicationSettings
        {
            FlyoutAnimationDirection = FlyoutAnimationDirection.Right,
            HotCornerModifierKey = HotCornerModifierKey.Shift
        });

        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        using var v140 = JsonDocument.Parse(V140Json);
        var expected = v140.RootElement.EnumerateObject().Select(property => property.Name).Append("HotCornerModifierKey");

        Assert.Equal(expected, names);
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("FlyoutAnimationDirection").ValueKind);
        Assert.Equal(3, document.RootElement.GetProperty("FlyoutAnimationDirection").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("HotCornerModifierKey").GetInt32());
        Assert.Equal(
            ["Name", "Command", "Parameters", "LaunchHidden"],
            document.RootElement.GetProperty("CustomCommands")[0].EnumerateObject().Select(property => property.Name));
        Assert.Contains("\n  \"HotCornersEnabled\": true", json.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Output_IsReadableByAPlainV140StyleDeserializer()
    {
        var original = ApplicationSettingsTests.CreateNonDefault();

        // v1.4.0 used JsonSerializer with default options and no enum converter.
        var old = JsonSerializer.Deserialize<V140Settings>(SettingsSerializer.Serialize(original))!;

        Assert.Equal(original.TopLeftActionId, old.TopLeftActionId);
        Assert.Equal((int)original.FlyoutAnimationDirection, (int)old.FlyoutAnimationDirection);
        Assert.Equal((int)original.HotCornerModifierKey, (int)old.HotCornerModifierKey);
        Assert.Equal(original.CustomCommands[3].Parameters, old.CustomCommands[3].Parameters);
    }

    [Fact]
    public void Deserialize_ToleratesCommentsTrailingCommasUnknownPropertiesAndBom()
    {
        const string json = "\uFEFF{\n  // hand edited\n  \"GlobalDelayIndex\": 1, /* block */\n  \"SomethingFromTheFuture\": {\"x\": [1,2]},\n  \"ShowCountdown\": true,\n}";

        var settings = SettingsSerializer.Deserialize(json);

        Assert.Equal(1, settings.GlobalDelayIndex);
        Assert.True(settings.ShowCountdown);
    }

    [Theory]
    [InlineData("\"left\"", FlyoutAnimationDirection.Left)]
    [InlineData("\"TOP\"", FlyoutAnimationDirection.Top)]
    [InlineData("\" Right \"", FlyoutAnimationDirection.Right)]
    [InlineData("0", FlyoutAnimationDirection.Top)]
    public void Deserialize_AcceptsEnumNamesInAnyCaseOrNumbers(string value, FlyoutAnimationDirection expected)
    {
        var settings = SettingsSerializer.Deserialize($"{{\"FlyoutAnimationDirection\": {value}, \"HotCornerModifierKey\": \"ctrl\"}}");

        Assert.Equal(expected, settings.FlyoutAnimationDirection);
        Assert.Equal(HotCornerModifierKey.Ctrl, settings.HotCornerModifierKey);
    }

    [Fact]
    public void Deserialize_KeepsUndefinedNumericEnumsForTheSanitizer()
    {
        var settings = SettingsSerializer.Deserialize("{\"FlyoutAnimationDirection\": 42, \"HotCornerModifierKey\": 7}");

        Assert.Equal((FlyoutAnimationDirection)42, settings.FlyoutAnimationDirection);
        Assert.Equal((HotCornerModifierKey)7, settings.HotCornerModifierKey);
    }

    [Fact]
    public void Deserialize_DoesNotSanitize()
    {
        var settings = SettingsSerializer.Deserialize("{\"CustomCommands\": [null, {\"Name\": null}], \"TopLeftActionId\": null, \"GlobalDelayIndex\": 99}");

        Assert.Null(settings.CustomCommands[0]);
        Assert.Null(settings.CustomCommands[1].Name);
        Assert.Null(settings.TopLeftActionId);
        Assert.Equal(99, settings.GlobalDelayIndex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("{\"GlobalDelayIndex\": 3")]
    [InlineData("{\"GlobalDelayIndex\": \"three\"}")]
    [InlineData("{\"HotCornersEnabled\": \"yes\"}")]
    [InlineData("{\"HotCornersEnabled\": null}")]
    [InlineData("{\"CustomCommands\": 5}")]
    [InlineData("{\"CustomCommands\": {}}")]
    [InlineData("{\"TopLeftActionId\": 12}")]
    [InlineData("{\"FlyoutAnimationDirection\": \"Sideways\"}")]
    [InlineData("{\"FlyoutAnimationDirection\": \"2\"}")]
    [InlineData("{\"HotCornerModifierKey\": \"Ctrl, Shift\"}")]
    [InlineData("{\"HotCornerModifierKey\": true}")]
    [InlineData("{\"HotCornerModifierKey\": 1.5}")]
    [InlineData("{\"HotCornerModifierKey\": 99999999999}")]
    [InlineData("[]")]
    [InlineData("42")]
    public void Deserialize_ThrowsJsonExceptionForInvalidInput(string json)
    {
        Assert.ThrowsAny<JsonException>(() => SettingsSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_ErrorForLongBogusEnumNameIsBounded()
    {
        var exception = Assert.ThrowsAny<JsonException>(() =>
            SettingsSerializer.Deserialize($"{{\"HotCornerModifierKey\": \"{new string('z', 10_000)}\"}}"));

        Assert.True(exception.Message.Length < 500);
    }

    [Fact]
    public void ArgumentsAreRequired()
    {
        Assert.Throws<ArgumentNullException>(() => SettingsSerializer.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => SettingsSerializer.Deserialize(null!));
    }

    private enum V140Direction { Top, Bottom, Left, Right }

    private enum V140Modifier { None, Ctrl, Shift, Alt }

    private sealed class V140Settings
    {
        public string TopLeftActionId { get; set; } = "";
        public V140Direction FlyoutAnimationDirection { get; set; }
        public V140Modifier HotCornerModifierKey { get; set; }
        public V140Command[] CustomCommands { get; set; } = [];
    }

    private sealed class V140Command
    {
        public string Parameters { get; set; } = "";
    }
}
