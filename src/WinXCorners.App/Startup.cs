using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;
using WinXCorners.Core.Settings;

namespace WinXCorners.App;

/// <summary>What <see cref="Program.Main"/> does after a startup step.</summary>
internal enum StartupOutcome
{
    /// <summary>Go on to the next step.</summary>
    Continue,

    /// <summary>An elevated copy was started (the user accepted the UAC prompt); this process exits.</summary>
    ExitRestartedElevated,

    /// <summary>Another copy is running and was asked to open its Settings window; this process exits.</summary>
    ExitAnotherInstance,

    /// <summary>The user did not approve the elevated session; a normal-rights copy was started if possible.</summary>
    ExitDeclinedElevated
}

/// <summary>
/// What the startup steps need from the composition root. <see cref="AppHost"/> implements it; tests use a fake
/// so the sequence can be checked without starting processes, prompting UAC or writing HKLM.
/// </summary>
internal interface IStartupHost
{
    BoundedLog Log { get; }

    SettingsSession Settings { get; }

    bool IsElevated { get; }

    SecurityContext GetSecurityContext();

    AutoElevateAction DecideAutoElevate();

    (ElevatedStartAction Action, SealCheck Seal) EvaluateElevatedStart();

    bool ApproveCurrentSettings();

    bool TryRestart(RestartMode mode, out string? error);
}

/// <summary>
/// The modal questions startup may ask. <see cref="Startup.DefaultPrompts"/> shows the real dialogs; tests pass
/// delegates that answer at once, so no test ever blocks on a modal window.
/// </summary>
/// <param name="ConfirmAutoElevation">Shown before a settings-requested UAC prompt; true = continue to it.</param>
/// <param name="ApproveElevatedSettings">The elevated session's settings approval; true = approved.</param>
/// <param name="ShowError">A plain message the user should see (title is the app name).</param>
internal sealed record StartupPrompts(
    Func<IReadOnlyList<RiskItem>, bool> ConfirmAutoElevation,
    Func<ApplicationSettings, SealCheck, IReadOnlyList<RiskItem>, bool> ApproveElevatedSettings,
    Action<string> ShowError);

/// <summary>
/// The security-relevant startup decisions, one method per step, each returning a <see cref="StartupOutcome"/>.
/// <see cref="Program.Main"/> runs them in order; every collaborator is a parameter so each step is testable.
/// </summary>
internal static class Startup
{
    internal const string Title = "WinXCornersPlus";
    internal const string AutoElevateHeading = "Settings ask WinXCornersPlus to run as administrator";
    internal const string ContinueToWindowsPromptText = "Continue to the Windows prompt";

    /// <summary>How long a restarted copy waits for its predecessor to exit and release the single-instance mutex.</summary>
    internal static readonly TimeSpan RestartHandOverTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The real dialogs, all without an owner (no window exists yet), centred on the screen.</summary>
    internal static StartupPrompts DefaultPrompts { get; } = new(
        items => RiskDialog.Confirm(null, Title, AutoElevateHeading, items, ContinueToWindowsPromptText),
        (settings, seal, risks) => ElevatedApprovalDialog.Ask(null, settings, seal, risks),
        text => MessageBox.Show(text, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning));

    /// <summary>
    /// Single instance (review S4/B7). When another copy holds the mutex, it is signalled to open its Settings
    /// window - so a hidden tray icon never locks the user out - and this process exits.
    /// </summary>
    /// <param name="restartedFromProcessId">From <c>--restarted-from</c>: wait for that process to exit first.</param>
    /// <param name="tryAcquire">(pid to wait for, timeout) -> the instance, or null when another copy runs.</param>
    /// <param name="signalExisting">Asks the running copy to show Settings; false when it could not be reached.</param>
    internal static StartupOutcome AcquireSingleInstance(
        int? restartedFromProcessId,
        Func<int?, TimeSpan, SingleInstance?> tryAcquire,
        Func<bool> signalExisting,
        out SingleInstance? instance)
    {
        ArgumentNullException.ThrowIfNull(tryAcquire);
        ArgumentNullException.ThrowIfNull(signalExisting);

        instance = tryAcquire(restartedFromProcessId, RestartHandOverTimeout);
        if (instance is not null)
        {
            return StartupOutcome.Continue;
        }

        // Nothing to log to yet (the settings are not loaded in a second copy on purpose); the running copy
        // logs that it was activated.
        _ = signalExisting();
        return StartupOutcome.ExitAnotherInstance;
    }

    /// <summary>
    /// "Always run as administrator" on a normal-rights start (Core's <see cref="AutoElevatePolicy"/>). A UAC prompt is
    /// raised only here and only for Elevate, or for AskBeforeElevating after the user confirmed the risks. A
    /// declined or failed prompt continues with normal rights.
    /// </summary>
    internal static StartupOutcome AutoElevate(IStartupHost host, Func<IReadOnlyList<RiskItem>, bool> confirmAutoElevation)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(confirmAutoElevation);

        switch (host.DecideAutoElevate())
        {
            case AutoElevateAction.Elevate:
                return RestartElevated(host);

            case AutoElevateAction.AskBeforeElevating:
                // The flag lives in a user-writable file and no administrator session was ever approved here, so
                // the user sees what it means before Windows shows a familiar-looking prompt.
                var risks = SecurityPosture.DescribeElevation(host.Settings.Current, host.GetSecurityContext().Install, everyStart: true);
                if (confirmAutoElevation(risks))
                {
                    return RestartElevated(host);
                }

                host.Log.Add("Not restarting as administrator: you chose to continue with normal rights.");
                return StartupOutcome.Continue;

            default:
                return StartupOutcome.Continue;
        }
    }

    /// <summary>
    /// The settings seal for an elevated start (brief design point 2). Must pass before the tray icon, raw input
    /// or any action starts. Declining releases the single instance, restarts without administrator rights
    /// (when possible) and exits.
    /// </summary>
    /// <param name="releaseSingleInstance">Lets the normal-rights replacement take the mutex at once.</param>
    internal static StartupOutcome PassElevatedGate(IStartupHost host, StartupPrompts prompts, Action releaseSingleInstance)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(releaseSingleInstance);

        if (!host.IsElevated)
        {
            return StartupOutcome.Continue;
        }

        var (action, seal) = host.EvaluateElevatedStart();
        switch (action)
        {
            case ElevatedStartAction.NotElevated:
                return StartupOutcome.Continue;

            case ElevatedStartAction.Proceed:
                // Unchanged since the last approval. Recording it again turns on automatic re-approval, so edits
                // made in this elevated session (by the user, in our own windows) do not prompt next time.
                host.ApproveCurrentSettings();
                return StartupOutcome.Continue;
        }

        // AskFirstTimeConsent, AskToApproveChanges, or anything unexpected: ask (fail closed).
        var settings = host.Settings.Current;
        var risks = SecurityPosture.DescribeElevation(settings, host.GetSecurityContext().Install, settings.AlwaysRunAsAdministrator);
        if (prompts.ApproveElevatedSettings(settings, seal, risks))
        {
            if (!host.ApproveCurrentSettings())
            {
                host.Log.Add("The approval could not be recorded; you will be asked again at the next administrator start.");
                prompts.ShowError(
                    "Your approval could not be recorded, so you will be asked again the next time WinXCornersPlus " +
                    "runs as administrator. It continues to run now.");
            }

            return StartupOutcome.Continue;
        }

        host.Log.Add("The administrator session was not approved; restarting with normal rights.");
        releaseSingleInstance();
        if (!host.TryRestart(RestartMode.WithoutAdministrator, out var error))
        {
            host.Log.Add($"Could not restart with normal rights ({error}); exiting.");
        }

        return StartupOutcome.ExitDeclinedElevated;
    }

    private static StartupOutcome RestartElevated(IStartupHost host)
    {
        if (host.TryRestart(RestartMode.AsAdministrator, out _))
        {
            return StartupOutcome.ExitRestartedElevated;
        }

        // TryRestart logged the reason (typically the UAC prompt was declined).
        host.Log.Add("Continuing with normal rights.");
        return StartupOutcome.Continue;
    }
}
