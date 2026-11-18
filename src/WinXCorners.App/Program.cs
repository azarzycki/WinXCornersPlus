using WinXCorners.Core.Platform;

namespace WinXCorners.App;

/// <summary>The entry point: runs the startup steps in order, then the tray app's message loop.</summary>
internal static class Program
{
    /// <summary>
    /// Startup sequence. Each step is security-relevant; the decisions themselves live in <see cref="Startup"/>
    /// (and below it in Core) so they are tested without starting processes.
    /// </summary>
    [STAThread]
    internal static int Main(string[] args)
    {
        // 1. Arguments, then WinForms defaults. DPI awareness is NOT set here: the manifest
        //    (WinXCorners.App.manifest, <dpiAware>true/pm</dpiAware>) declares per-monitor awareness and always
        //    wins over any SetHighDpiMode call, so the call the original made was dead and misleading (review B8).
        //    (ApplicationConfiguration.Initialize also passes the project's default mode to SetHighDpiMode; the
        //    manifest wins over that too.)
        var options = CommandLineOptions.Parse(args);

        // 0. Elevated only (review V2 D1): Redirection Guard before anything touches the file system, so junctions that
        //    programs without administrator rights planted in the user's profile are not followed by this process.
        var isElevated = ProcessElevation.IsElevated;
        var mitigationNote = isElevated ? ProcessMitigations.EnableRedirectionGuard() : null;

        ApplicationConfiguration.Initialize();

        // 2. Global exception handling (review M3), before any window exists. A UI-thread exception is logged and
        //    reported once; the tray app carries on. An elevated process writes no crash.log (review V2 D1): it would
        //    live under %LOCALAPPDATA%, where programs without administrator rights could point it at any file, and an
        //    admin-only location would mean creating and securing a folder elevated. The in-app log has the summary.
        var crashes = new CrashReporter(isElevated ? null : CrashReporter.DefaultCrashLogPath());
        crashes.Install();

        // 3. Single instance (review S4/B7). Done before loading settings or elevating, so a second launch only
        //    asks the running copy to show Settings - it never raises a UAC prompt or a dialog of its own (only a
        //    plain message when the running copy does not answer, review V2 D6).
        var outcome = Startup.AcquireSingleInstance(
            options.RestartedFromProcessId,
            (processId, timeout) => SingleInstance.TryAcquire(processId, timeout),
            SignalRunningInstance,
            text => MessageBox.Show(text, Startup.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning),
            out var instance);
        if (outcome != StartupOutcome.Continue || instance is null)
        {
            return 0;
        }

        AppHost? host = null;
        MainForm? form = null;
        try
        {
            // 4. Composition root: loads settings (sanitized, migrated, never discarded) and the platform services.
            host = AppHost.Create(options);
            crashes.AttachLog(host.Log);
            if (mitigationNote is not null)
            {
                host.Log.Add(mitigationNote);
            }

            if (crashes.CrashLogPath is null)
            {
                host.Log.Add("Running as administrator: unexpected errors are kept in this log only, not written to crash.log.");
            }

            // 5. "Always run as administrator" (review S1). The only place a start raises a UAC prompt. A declined
            //    prompt continues with normal rights. The new elevated copy waits for this one to exit before it
            //    takes the single-instance mutex.
            if (Startup.AutoElevate(host, Startup.DefaultPrompts.ConfirmAutoElevation) != StartupOutcome.Continue)
            {
                return 0;
            }

            // 6. Elevated gate (brief design point 2): approve the settings this administrator session will use.
            //    Nothing - tray icon, raw input, actions - starts before it has passed.
            if (Startup.PassElevatedGate(host, Startup.DefaultPrompts, instance.Release) != StartupOutcome.Continue)
            {
                return 0;
            }

            // 7. The tray app.
            form = new MainForm(host);
            RunTrayApp(form, instance, crashes, options.ShowSettings);
            return 0;
        }
        finally
        {
            // Form, then host (stops the action worker), then the single instance, so a replacement process
            // can only take over once this one has stopped acting.
            form?.Dispose();
            host?.Dispose();
            instance.Dispose();
            crashes.Uninstall();
        }
    }

    /// <summary>
    /// Runs the message loop with an ApplicationContext and no main window (the flyout is shown from the tray icon).
    /// MainForm ends the loop through ExitMessageLoop on its single shutdown path.
    /// </summary>
    private static void RunTrayApp(MainForm form, SingleInstance instance, CrashReporter crashes, bool showSettings)
    {
        using var context = new ApplicationContext();
        form.ExitMessageLoop = context.ExitThread;
        crashes.AttachNotifier(form.ShowErrorNotification);
        instance.ActivationRequested += form.OnActivationRequested;
        try
        {
            form.Start();
            if (showSettings)
            {
                form.ShowSettingsWindow();
            }

            Application.Run(context);
        }
        finally
        {
            instance.ActivationRequested -= form.OnActivationRequested;
            crashes.AttachNotifier(null);
        }
    }

    /// <summary>Asks the running copy to open its Settings window, and lets it come to the front.</summary>
    private static bool SignalRunningInstance()
    {
        // This process was started by the user, so it may hand its right to take the foreground to the running copy;
        // without that, Windows would only flash the Settings window's taskbar button. The right goes only to
        // processes of this executable's name in this session (review V2: ASFW_ANY handed it to every process).
        foreach (var processId in SingleInstance.FindRunningCopies())
        {
            _ = NativeMethods.AllowSetForegroundWindow(processId);
        }

        return SingleInstance.SignalExistingInstance();
    }
}
