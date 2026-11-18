namespace WinXCorners.Core.Security;

public enum AutoElevateAction
{
    /// <summary>Carry on with the rights the process already has.</summary>
    DoNotElevate,

    /// <summary>Restart elevated straight away (Windows still shows its UAC prompt).</summary>
    Elevate,

    /// <summary>Explain the risks and ask before triggering the UAC prompt.</summary>
    AskBeforeElevating
}

/// <summary>
/// Decides what a normal-rights start does when settings say "Always run as administrator".
/// </summary>
/// <remarks>
/// That flag lives in settings.json, which any program running as the user can edit. If such a program switched it
/// on, the user's first sign of trouble would be a UAC prompt carrying this app's familiar name. So the flag alone is
/// never enough to raise that prompt without an explanation first: it is honoured straight away only when the
/// approval stored where normal-rights programs cannot write (the settings seal) matches the current settings
/// exactly. The seal covers the flag itself (format version 3), so <see cref="SealStatus.Unchanged"/> means the user
/// approved an administrator session with "Always run as administrator" on and nothing security-relevant has changed
/// since. Any other status (never approved, changed, unreadable, or an approval of an older format) asks first
/// (review V2 D3: "some approval exists" used to be enough).
/// </remarks>
public static class AutoElevatePolicy
{
    /// <param name="alwaysRunAsAdministrator">The setting from settings.json (untrusted).</param>
    /// <param name="isElevated">Whether this process already has administrator rights.</param>
    /// <param name="noElevateRequested">True when started with <c>--no-elevate</c>.</param>
    /// <param name="approval">
    /// The result of <see cref="SettingsSeal.Check"/> for the current settings against the stored approval.
    /// </param>
    public static AutoElevateAction Decide(
        bool alwaysRunAsAdministrator,
        bool isElevated,
        bool noElevateRequested,
        SealStatus approval)
    {
        if (isElevated || noElevateRequested || !alwaysRunAsAdministrator)
        {
            return AutoElevateAction.DoNotElevate;
        }

        return approval == SealStatus.Unchanged ? AutoElevateAction.Elevate : AutoElevateAction.AskBeforeElevating;
    }
}
