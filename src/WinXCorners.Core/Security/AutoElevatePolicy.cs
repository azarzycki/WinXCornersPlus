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
/// That flag lives in settings.json, which any program running as the user can edit. If such a
/// program switched it on, the user's first sign of trouble would be a UAC prompt carrying this
/// app's familiar name. So the flag alone is never enough to raise that prompt silently: it is
/// honoured without a question only when an administrator session has been approved on this
/// machine before, which is recorded where normal-rights programs cannot write.
/// </remarks>
public static class AutoElevatePolicy
{
    /// <param name="alwaysRunAsAdministrator">The setting from settings.json (untrusted).</param>
    /// <param name="isElevated">Whether this process already has administrator rights.</param>
    /// <param name="noElevateRequested">True when started with <c>--no-elevate</c>.</param>
    /// <param name="approvalOnRecord">Whether the admin-only store holds an approval for this user.</param>
    public static AutoElevateAction Decide(
        bool alwaysRunAsAdministrator,
        bool isElevated,
        bool noElevateRequested,
        bool approvalOnRecord)
    {
        if (isElevated || noElevateRequested || !alwaysRunAsAdministrator)
        {
            return AutoElevateAction.DoNotElevate;
        }

        return approvalOnRecord ? AutoElevateAction.Elevate : AutoElevateAction.AskBeforeElevating;
    }
}
