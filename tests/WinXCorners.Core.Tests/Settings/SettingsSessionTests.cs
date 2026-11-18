using WinXCorners.Core.Actions;
using WinXCorners.Core.Model;
using WinXCorners.Core.Settings;

namespace WinXCorners.Core.Tests.Settings;

public sealed class SettingsSessionTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private SettingsStore CreateStore(string? primary = null) =>
        new(new SettingsPaths(primary ?? _directory.Combine("app", "settings.json"), null));

    [Fact]
    public void Constructor_SanitizesAndOwnsTheInitialInstance()
    {
        var initial = new ApplicationSettings { GlobalDelayIndex = 99 };

        var session = new SettingsSession(CreateStore(), initial);

        Assert.Same(initial, session.Current);
        Assert.Equal(ApplicationSettings.DefaultDelayIndex, session.Current.GlobalDelayIndex);
        Assert.False(session.IsDirty);
        Assert.Null(session.LastSaveResult);
    }

    [Fact]
    public void Snapshot_IsADeepCopy()
    {
        var session = new SettingsSession(CreateStore(), ApplicationSettings.CreateDefault());

        var snapshot = session.Snapshot();
        snapshot.CustomCommands[0].Name = "x";
        snapshot.ShowCountdown = true;

        Assert.NotSame(session.Current, snapshot);
        Assert.Equal(string.Empty, session.Current.CustomCommands[0].Name);
        Assert.False(session.Current.ShowCountdown);
    }

    [Fact]
    public void Update_AppliesSanitizesMarksDirtyAndRaisesChanged()
    {
        var session = new SettingsSession(CreateStore(), ApplicationSettings.CreateDefault());
        var raised = new List<ApplicationSettings>();
        session.Changed += raised.Add;

        session.Update(settings =>
        {
            settings.HotCornersEnabled = false;
            settings.TopLeftActionId = "not-an-action";
        });

        Assert.False(session.Current.HotCornersEnabled);
        Assert.Equal(ActionIds.None, session.Current.TopLeftActionId);
        Assert.True(session.IsDirty);
        Assert.Same(session.Current, Assert.Single(raised));
    }

    [Fact]
    public void Update_WhenMutationThrows_StillSanitizesAndMarksDirtyButDoesNotRaise()
    {
        var session = new SettingsSession(CreateStore(), ApplicationSettings.CreateDefault());
        var raised = 0;
        session.Changed += _ => raised++;

        Assert.Throws<InvalidOperationException>(() => session.Update(settings =>
        {
            settings.CustomCommands = null!;
            throw new InvalidOperationException("boom");
        }));

        Assert.NotNull(session.Current.CustomCommands);
        Assert.True(session.IsDirty);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Flush_WhenClean_DoesNotTouchDisk()
    {
        var primary = _directory.Combine("app", "settings.json");
        var session = new SettingsSession(CreateStore(primary), ApplicationSettings.CreateDefault());

        var result = session.Flush();

        Assert.True(result.Succeeded);
        Assert.False(result.Written);
        Assert.False(File.Exists(primary));
        Assert.Null(session.LastSaveResult);
    }

    [Fact]
    public void Flush_WhenDirty_SavesAndClearsDirty()
    {
        var primary = _directory.Combine("app", "settings.json");
        var store = CreateStore(primary);
        var session = new SettingsSession(store, ApplicationSettings.CreateDefault());
        session.Update(settings => settings.ShowCountdown = true);

        var result = session.Flush();

        Assert.True(result.Succeeded);
        Assert.True(result.Written);
        Assert.False(session.IsDirty);
        Assert.Same(result, session.LastSaveResult);
        Assert.True(store.Load().Settings.ShowCountdown);
    }

    [Fact]
    public void Flush_WhenSaveFails_StaysDirtyAndReportsTheError()
    {
        var blocker = _directory.Combine("blocker");
        File.WriteAllText(blocker, "a file where a folder should be");
        var session = new SettingsSession(CreateStore(Path.Combine(blocker, "settings.json")), ApplicationSettings.CreateDefault());
        session.Update(settings => settings.ShowCountdown = true);

        var result = session.Flush();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.True(session.IsDirty);
        Assert.Same(result, session.LastSaveResult);
    }

    [Fact]
    public void ArgumentsAreRequired()
    {
        Assert.Throws<ArgumentNullException>(() => new SettingsSession(null!, new ApplicationSettings()));
        Assert.Throws<ArgumentNullException>(() => new SettingsSession(CreateStore(), null!));
        var session = new SettingsSession(CreateStore(), new ApplicationSettings());
        Assert.Throws<ArgumentNullException>(() => session.Update(null!));
    }
}
