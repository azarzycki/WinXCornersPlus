using System.Reflection;
using WinXCorners.Core.Model;
using WinXCorners.Core.Settings;
using WinXCorners.Core.Tests.Model;

namespace WinXCorners.Core.Tests.Settings;

public class SettingsMergeTests
{
    private static readonly string[] DialogOwned =
    [
        nameof(ApplicationSettings.GlobalDelayEnabled), nameof(ApplicationSettings.GlobalDelayIndex),
        nameof(ApplicationSettings.TopLeftDelayEnabled), nameof(ApplicationSettings.TopLeftDelayIndex),
        nameof(ApplicationSettings.TopRightDelayEnabled), nameof(ApplicationSettings.TopRightDelayIndex),
        nameof(ApplicationSettings.BottomLeftDelayEnabled), nameof(ApplicationSettings.BottomLeftDelayIndex),
        nameof(ApplicationSettings.BottomRightDelayEnabled), nameof(ApplicationSettings.BottomRightDelayIndex),
        nameof(ApplicationSettings.ShowCountdown), nameof(ApplicationSettings.StartWithWindows),
        nameof(ApplicationSettings.IgnoreFullScreen), nameof(ApplicationSettings.AlwaysRunAsAdministrator),
        nameof(ApplicationSettings.AlwaysHideTrayIcon), nameof(ApplicationSettings.FlyoutAnimationDirection),
        nameof(ApplicationSettings.HotCornerModifierKey), nameof(ApplicationSettings.EnableCustomCommands),
        nameof(ApplicationSettings.CustomCommands)
    ];

    // Edited from the tray menu / flyout while the dialog may be open (review B6).
    private static readonly string[] NotDialogOwned =
    [
        nameof(ApplicationSettings.HotCornersEnabled),
        nameof(ApplicationSettings.TopLeftActionId), nameof(ApplicationSettings.TopRightActionId),
        nameof(ApplicationSettings.BottomLeftActionId), nameof(ApplicationSettings.BottomRightActionId)
    ];

    private static IEnumerable<PropertyInfo> Properties =>
        typeof(ApplicationSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public void EveryPropertyIsClassifiedExactlyOnce()
    {
        var all = Properties.Select(property => property.Name).ToHashSet();
        var unclassified = all.Except(DialogOwned).Except(NotDialogOwned).ToArray();

        Assert.True(unclassified.Length == 0,
            $"Decide whether the Settings dialog owns {string.Join(", ", unclassified)} and update SettingsMerge and this test.");
        Assert.Empty(DialogOwned.Intersect(NotDialogOwned));
        Assert.Empty(DialogOwned.Concat(NotDialogOwned).Except(all));
    }

    [Fact]
    public void DialogOwnedProperties_AreCopied()
    {
        var live = ApplicationSettings.CreateDefault();
        var edited = ApplicationSettingsTests.CreateNonDefault();

        SettingsMerge.ApplyDialogEdits(live, edited);

        foreach (var property in Properties.Where(property => DialogOwned.Contains(property.Name) && property.Name != nameof(ApplicationSettings.CustomCommands)))
        {
            Assert.True(Equals(property.GetValue(edited), property.GetValue(live)), property.Name);
        }

        Assert.Equal(edited.CustomCommands.Select(command => command.Name), live.CustomCommands.Select(command => command.Name));
        Assert.Equal(edited.CustomCommands.Select(command => command.LaunchHidden), live.CustomCommands.Select(command => command.LaunchHidden));
    }

    [Fact]
    public void ToggleAndCornerActionsChangedWhileTheDialogWasOpen_Survive()
    {
        var live = ApplicationSettings.CreateDefault();
        var dialogCopy = live.Clone();
        // While the dialog is open the user disables hot corners and picks a new corner action from the flyout.
        live.HotCornersEnabled = false;
        live.TopLeftActionId = "lock-screen";
        dialogCopy.ShowCountdown = true;

        SettingsMerge.ApplyDialogEdits(live, dialogCopy);

        Assert.False(live.HotCornersEnabled);
        Assert.Equal("lock-screen", live.TopLeftActionId);
        Assert.True(live.ShowCountdown);
    }

    [Fact]
    public void NotDialogOwnedProperties_AreNeverCopied()
    {
        var live = ApplicationSettings.CreateDefault();
        var before = live.Clone();

        SettingsMerge.ApplyDialogEdits(live, ApplicationSettingsTests.CreateNonDefault());

        foreach (var property in Properties.Where(property => NotDialogOwned.Contains(property.Name)))
        {
            Assert.True(Equals(property.GetValue(before), property.GetValue(live)), property.Name);
        }
    }

    [Fact]
    public void CustomCommands_AreDeepCopied()
    {
        var live = ApplicationSettings.CreateDefault();
        var edited = ApplicationSettingsTests.CreateNonDefault();

        SettingsMerge.ApplyDialogEdits(live, edited);
        edited.CustomCommands[0].Command = "changed later";

        Assert.NotSame(edited.CustomCommands, live.CustomCommands);
        Assert.Equal("cmd0", live.CustomCommands[0].Command);
    }

    [Fact]
    public void NullCommandsInTheEditedCopy_DoNotLeaveNullsInLiveSettings()
    {
        var live = ApplicationSettings.CreateDefault();
        var edited = new ApplicationSettings { CustomCommands = [null!, null!, null!, null!] };

        SettingsMerge.ApplyDialogEdits(live, edited);

        Assert.All(live.CustomCommands, command => Assert.NotNull(command));
    }

    [Fact]
    public void ArgumentsAreRequired()
    {
        Assert.Throws<ArgumentNullException>(() => SettingsMerge.ApplyDialogEdits(null!, new ApplicationSettings()));
        Assert.Throws<ArgumentNullException>(() => SettingsMerge.ApplyDialogEdits(new ApplicationSettings(), null!));
    }
}
