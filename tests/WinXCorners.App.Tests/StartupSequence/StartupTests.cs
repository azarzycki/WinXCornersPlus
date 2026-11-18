using WinXCorners.App.Tests.Infrastructure;
using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;
using WinXCorners.Core.Settings;

namespace WinXCorners.App.Tests.StartupSequence;

/// <summary>Every branch of the Startup steps with a fake host and prompts that answer at once.</summary>
public sealed class StartupTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private sealed class FakeHost : IStartupHost
    {
        internal FakeHost(SettingsSession settings, bool isElevated)
        {
            Settings = settings;
            IsElevated = isElevated;
        }

        public BoundedLog Log { get; } = new();

        public SettingsSession Settings { get; }

        public bool IsElevated { get; }

        internal AutoElevateAction AutoElevate { get; set; } = AutoElevateAction.DoNotElevate;

        internal ElevatedStartAction StartAction { get; set; } = ElevatedStartAction.NotElevated;

        internal SealCheck Seal { get; set; } = new(SealStatus.NeverApproved, []);

        internal bool ApproveResult { get; set; } = true;

        internal bool RestartResult { get; set; } = true;

        internal List<string> Calls { get; } = [];

        public SecurityContext GetSecurityContext() => new(
            IsElevated,
            new InstallLocationAssessment(InstallLocationKind.Protected, @"C:\Program Files\WinXCornersPlus", "test"),
            false,
            @"C:\Users\test\settings.json");

        public AutoElevateAction DecideAutoElevate()
        {
            Calls.Add("decide");
            return AutoElevate;
        }

        public (ElevatedStartAction Action, SealCheck Seal) EvaluateElevatedStart()
        {
            Calls.Add("evaluate");
            return (StartAction, Seal);
        }

        public bool ApproveCurrentSettings()
        {
            Calls.Add("approve");
            return ApproveResult;
        }

        public bool TryRestart(RestartMode mode, out string? error)
        {
            Calls.Add("restart:" + mode);
            error = RestartResult ? null : "declined";
            return RestartResult;
        }
    }

    private FakeHost Host(bool isElevated = false, ApplicationSettings? settings = null)
    {
        var store = new SettingsStore(new SettingsPaths(_temp.File("settings.json"), null));
        return new FakeHost(new SettingsSession(store, settings ?? new ApplicationSettings()), isElevated);
    }

    private sealed class Prompts
    {
        internal bool ConfirmAnswer { get; set; }

        internal bool ApproveAnswer { get; set; }

        internal List<string> Asked { get; } = [];

        internal IReadOnlyList<RiskItem>? Risks { get; private set; }

        internal SealCheck? SealShown { get; private set; }

        internal StartupPrompts Build() => new(
            risks =>
            {
                Asked.Add("confirm");
                Risks = risks;
                return ConfirmAnswer;
            },
            (_, seal, risks) =>
            {
                Asked.Add("approve");
                SealShown = seal;
                Risks = risks;
                return ApproveAnswer;
            },
            text => Asked.Add("error:" + text));
    }

    // ---- single instance -------------------------------------------------------------------------

    [Fact]
    public void The_first_instance_continues_without_signalling()
    {
        var suffix = ".Test." + Guid.NewGuid().ToString("N");
        var signalled = false;
        int? waitedFor = null;
        TimeSpan timeout = default;

        var outcome = WinXCorners.App.Startup.AcquireSingleInstance(
            1234,
            (pid, t) =>
            {
                waitedFor = pid;
                timeout = t;
                return SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
            },
            () => signalled = true,
            _ => Assert.Fail("No error may be shown to the first instance."),
            out var instance);

        Assert.Equal(StartupOutcome.Continue, outcome);
        Assert.NotNull(instance);
        Assert.False(signalled);
        Assert.Equal(1234, waitedFor);
        Assert.Equal(WinXCorners.App.Startup.RestartHandOverTimeout, timeout);
        instance.Release();
    }

    [Fact]
    public void Another_instance_is_signalled_and_this_one_exits_quietly()
    {
        var signals = 0;
        var errors = new List<string>();

        var outcome = WinXCorners.App.Startup.AcquireSingleInstance(null, (_, _) => null, () =>
        {
            signals++;
            return true;
        }, errors.Add, out var instance);

        Assert.Equal(StartupOutcome.ExitAnotherInstance, outcome);
        Assert.Null(instance);
        Assert.Equal(1, signals);
        Assert.Empty(errors);
    }

    /// <summary>Review V2 D6: a running copy that cannot be signalled used to make the second launch exit silently.</summary>
    [Fact]
    public void A_running_copy_that_does_not_answer_is_reported_and_this_one_exits()
    {
        var errors = new List<string>();

        var outcome = WinXCorners.App.Startup.AcquireSingleInstance(null, (_, _) => null, () => false, errors.Add, out var instance);

        Assert.Equal(StartupOutcome.ExitAnotherInstance, outcome);
        Assert.Null(instance);
        var error = Assert.Single(errors);
        Assert.StartsWith("WinXCornersPlus appears to be running but did not respond.", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review V2 D6 end to end: another program holds the single-instance mutex (squatting the name) and no running
    /// copy listens on the activation event. The second launch must say so rather than vanish.
    /// </summary>
    [Fact]
    public void A_squatted_mutex_with_nobody_listening_is_reported()
    {
        var suffix = ".Test." + Guid.NewGuid().ToString("N");
        using var squatter = new Mutex(initiallyOwned: true, SingleInstance.DefaultMutexName + suffix, out var created);
        Assert.True(created);
        var errors = new List<string>();
        StartupOutcome outcome = default;

        var thread = new Thread(() => outcome = WinXCorners.App.Startup.AcquireSingleInstance(
            null,
            (pid, t) => SingleInstance.TryAcquire(pid, t, suffix),
            () => SingleInstance.SignalExistingInstance(suffix),
            errors.Add,
            out _));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        squatter.ReleaseMutex();

        Assert.Equal(StartupOutcome.ExitAnotherInstance, outcome);
        Assert.Equal([WinXCorners.App.Startup.NotRespondingText], errors);
    }

    [Fact]
    public void A_second_launch_signals_the_real_running_instance()
    {
        var suffix = ".Test." + Guid.NewGuid().ToString("N");
        using var running = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
        Assert.NotNull(running);
        using var activated = new ManualResetEventSlim();
        running.ActivationRequested += activated.Set;

        StartupOutcome outcome = default;
        var thread = new Thread(() => outcome = WinXCorners.App.Startup.AcquireSingleInstance(
            null,
            (pid, t) => SingleInstance.TryAcquire(pid, t, suffix),
            () => SingleInstance.SignalExistingInstance(suffix),
            _ => Assert.Fail("The running copy answered; no error may be shown."),
            out _));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));

        Assert.Equal(StartupOutcome.ExitAnotherInstance, outcome);
        Assert.True(activated.Wait(TimeSpan.FromSeconds(10)));
    }

    // ---- auto-elevation --------------------------------------------------------------------------

    [Fact]
    public void Do_not_elevate_continues_without_prompting_or_restarting()
    {
        var host = Host();
        var prompts = new Prompts();

        Assert.Equal(StartupOutcome.Continue, WinXCorners.App.Startup.AutoElevate(host, prompts.Build().ConfirmAutoElevation));

        Assert.Equal(["decide"], host.Calls);
        Assert.Empty(prompts.Asked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Elevate_restarts_as_administrator_without_asking(bool restartSucceeds)
    {
        var expected = restartSucceeds ? StartupOutcome.ExitRestartedElevated : StartupOutcome.Continue;
        var host = Host();
        host.AutoElevate = AutoElevateAction.Elevate;
        host.RestartResult = restartSucceeds;
        var prompts = new Prompts();

        Assert.Equal(expected, WinXCorners.App.Startup.AutoElevate(host, prompts.Build().ConfirmAutoElevation));

        Assert.Equal(["decide", "restart:AsAdministrator"], host.Calls);
        Assert.Empty(prompts.Asked);
        if (!restartSucceeds)
        {
            Assert.Contains(host.Log.Snapshot(), line => line.Contains("Continuing with normal rights", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Ask_before_elevating_shows_the_risks_and_restarts_only_after_confirmation()
    {
        var host = Host(settings: new ApplicationSettings { AlwaysRunAsAdministrator = true });
        host.AutoElevate = AutoElevateAction.AskBeforeElevating;
        var prompts = new Prompts { ConfirmAnswer = true };

        Assert.Equal(StartupOutcome.ExitRestartedElevated, WinXCorners.App.Startup.AutoElevate(host, prompts.Build().ConfirmAutoElevation));

        Assert.Equal(["confirm"], prompts.Asked);
        Assert.NotNull(prompts.Risks);
        Assert.NotEmpty(prompts.Risks);
        Assert.Contains("restart:AsAdministrator", host.Calls);
    }

    [Fact]
    public void Ask_before_elevating_declined_continues_with_normal_rights_and_no_UAC_prompt()
    {
        var host = Host(settings: new ApplicationSettings { AlwaysRunAsAdministrator = true });
        host.AutoElevate = AutoElevateAction.AskBeforeElevating;
        var prompts = new Prompts { ConfirmAnswer = false };

        Assert.Equal(StartupOutcome.Continue, WinXCorners.App.Startup.AutoElevate(host, prompts.Build().ConfirmAutoElevation));

        Assert.DoesNotContain(host.Calls, call => call.StartsWith("restart", StringComparison.Ordinal));
        Assert.Contains(host.Log.Snapshot(), line => line.Contains("continue with normal rights", StringComparison.Ordinal));
    }

    // ---- elevated gate ---------------------------------------------------------------------------

    [Fact]
    public void Not_elevated_the_gate_passes_without_evaluating()
    {
        var host = Host(isElevated: false);
        var prompts = new Prompts();
        var released = false;

        Assert.Equal(StartupOutcome.Continue, WinXCorners.App.Startup.PassElevatedGate(host, prompts.Build(), () => released = true));

        Assert.Empty(host.Calls);
        Assert.Empty(prompts.Asked);
        Assert.False(released);
    }

    [Fact]
    public void Unchanged_settings_proceed_without_asking_and_turn_on_re_approval()
    {
        var host = Host(isElevated: true);
        host.StartAction = ElevatedStartAction.Proceed;
        host.Seal = new SealCheck(SealStatus.Unchanged, []);
        var prompts = new Prompts();

        Assert.Equal(StartupOutcome.Continue, WinXCorners.App.Startup.PassElevatedGate(host, prompts.Build(), () => { }));

        Assert.Equal(["evaluate", "approve"], host.Calls);
        Assert.Empty(prompts.Asked);
    }

    [Theory]
    [InlineData(ElevatedStartAction.AskFirstTimeConsent, SealStatus.NeverApproved)]
    [InlineData(ElevatedStartAction.AskToApproveChanges, SealStatus.Changed)]
    [InlineData(ElevatedStartAction.AskToApproveChanges, SealStatus.Unreadable)]
    [InlineData(ElevatedStartAction.NotElevated + 99, SealStatus.Changed)]
    public void Approved_settings_are_recorded_and_the_session_continues(ElevatedStartAction action, SealStatus status)
    {
        var host = Host(isElevated: true);
        host.StartAction = action;
        host.Seal = new SealCheck(status, ["Custom command 1: “x.exe”"]);
        var prompts = new Prompts { ApproveAnswer = true };
        var released = false;

        Assert.Equal(StartupOutcome.Continue, WinXCorners.App.Startup.PassElevatedGate(host, prompts.Build(), () => released = true));

        Assert.Equal(["approve"], prompts.Asked);
        Assert.Same(host.Seal, prompts.SealShown);
        Assert.Equal(["evaluate", "approve"], host.Calls);
        Assert.False(released);
    }

    [Fact]
    public void An_approval_that_cannot_be_recorded_is_shown_and_the_session_continues()
    {
        var host = Host(isElevated: true);
        host.StartAction = ElevatedStartAction.AskFirstTimeConsent;
        host.ApproveResult = false;
        var prompts = new Prompts { ApproveAnswer = true };

        Assert.Equal(StartupOutcome.Continue, WinXCorners.App.Startup.PassElevatedGate(host, prompts.Build(), () => { }));

        Assert.Equal(2, prompts.Asked.Count);
        Assert.StartsWith("error:", prompts.Asked[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Declining_releases_the_instance_restarts_without_administrator_and_exits(bool restartSucceeds)
    {
        var host = Host(isElevated: true);
        host.StartAction = ElevatedStartAction.AskToApproveChanges;
        host.Seal = new SealCheck(SealStatus.Changed, ["changed"]);
        host.RestartResult = restartSucceeds;
        var prompts = new Prompts { ApproveAnswer = false };
        var order = new List<string>();

        var outcome = WinXCorners.App.Startup.PassElevatedGate(host, prompts.Build(), () => order.Add("release:" + host.Calls.Count));

        Assert.Equal(StartupOutcome.ExitDeclinedElevated, outcome);
        Assert.Equal(["evaluate", "restart:WithoutAdministrator"], host.Calls);
        Assert.Equal(["release:1"], order); // Released before the replacement starts.
        Assert.DoesNotContain("approve", host.Calls);
        if (!restartSucceeds)
        {
            Assert.Contains(host.Log.Snapshot(), line => line.Contains("exiting", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Null_collaborators_are_rejected()
    {
        var host = Host();
        var prompts = new Prompts().Build();
        Assert.Throws<ArgumentNullException>(() => WinXCorners.App.Startup.AutoElevate(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => WinXCorners.App.Startup.AutoElevate(host, null!));
        Assert.Throws<ArgumentNullException>(() => WinXCorners.App.Startup.PassElevatedGate(host, null!, () => { }));
        Assert.Throws<ArgumentNullException>(() => WinXCorners.App.Startup.PassElevatedGate(host, prompts, null!));
        Assert.Throws<ArgumentNullException>(() => WinXCorners.App.Startup.AcquireSingleInstance(null, null!, () => true, _ => { }, out _));
        Assert.Throws<ArgumentNullException>(() => WinXCorners.App.Startup.AcquireSingleInstance(null, (_, _) => null, null!, _ => { }, out _));
        Assert.Throws<ArgumentNullException>(() => WinXCorners.App.Startup.AcquireSingleInstance(null, (_, _) => null, () => true, null!, out _));
    }
}
