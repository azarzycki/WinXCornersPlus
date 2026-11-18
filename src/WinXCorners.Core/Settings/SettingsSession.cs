using WinXCorners.Core.Model;

namespace WinXCorners.Core.Settings;

/// <summary>
/// The single owner of the live settings while the app runs. All changes go through <see cref="Update"/>,
/// which keeps the live object sanitized, tracks whether it needs saving and tells listeners.
/// </summary>
/// <remarks>
/// Not thread-safe by design: create it, read it and update it on the UI thread only. Work running on
/// other threads should take a <see cref="Snapshot"/> on the UI thread and use that.
/// </remarks>
public sealed class SettingsSession
{
    private readonly SettingsStore _store;

    /// <param name="store">Where <see cref="Flush"/> saves to.</param>
    /// <param name="initial">The loaded settings. The session takes ownership of this instance and sanitizes it.</param>
    public SettingsSession(SettingsStore store, ApplicationSettings initial)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(initial);
        _store = store;
        SettingsSanitizer.Sanitize(initial);
        Current = initial;
    }

    /// <summary>Raised after every <see cref="Update"/> with the live instance.</summary>
    public event Action<ApplicationSettings>? Changed;

    /// <summary>The live instance. Treat as read-only: mutate only through <see cref="Update"/>.</summary>
    public ApplicationSettings Current { get; }

    /// <summary>True when there are changes that have not been saved successfully.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Result of the most recent save attempt made by <see cref="Flush"/>; null until one is made.</summary>
    public SettingsSaveResult? LastSaveResult { get; private set; }

    /// <summary>A deep copy that is safe to edit (e.g. by the Settings dialog) or hand to another thread.</summary>
    public ApplicationSettings Snapshot() => Current.Clone();

    /// <summary>
    /// Applies <paramref name="mutate"/> to the live settings, sanitizes the result, marks the session dirty
    /// and raises <see cref="Changed"/>. If <paramref name="mutate"/> throws, whatever it changed is still
    /// sanitized and marked dirty, the exception propagates and <see cref="Changed"/> is not raised.
    /// </summary>
    public void Update(Action<ApplicationSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        try
        {
            mutate(Current);
        }
        finally
        {
            SettingsSanitizer.Sanitize(Current);
            IsDirty = true;
        }

        Changed?.Invoke(Current);
    }

    /// <summary>
    /// Saves when there are unsaved changes; otherwise returns a successful result without touching disk.
    /// The dirty flag is cleared only when the save succeeds, so a failed save is retried on the next flush.
    /// </summary>
    public SettingsSaveResult Flush()
    {
        if (!IsDirty)
        {
            return new SettingsSaveResult(true, false, null);
        }

        var result = _store.Save(Current);
        LastSaveResult = result;
        if (result.Succeeded)
        {
            IsDirty = false;
        }

        return result;
    }
}
