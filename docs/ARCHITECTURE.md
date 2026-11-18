# Architecture

This page is for whoever maintains WinXCornersPlus. It explains how the code is split, how the main paths work
(a hot corner firing, settings, startup), where the tests are and what each suite proves, and how to add a
corner action. The security reasoning behind several of these choices is in
[SECURITY-MODEL.md](SECURITY-MODEL.md).

## Two projects, one rule

| Project | Target | What it contains |
|---|---|---|
| `src/WinXCorners.Core` (assembly `WinXCornersPlus.Core`) | `net10.0` | Every decision: what a corner does, when it fires, how a hotkey is parsed and sent, how a launch is planned, how settings are read, repaired and saved, what the security warnings say. |
| `src/WinXCorners.App` (assembly `WinXCornersPlus`) | `net10.0-windows`, WinForms | The Windows side effects: windows and dialogs, tray icon, raw input, `SendInput`, starting processes, the registry, the single-instance mutex. |

**The rule: decisions live in Core, Windows side effects live in the app.** The app gathers facts from Windows,
asks Core what to do, and does it. If you find yourself writing an `if` with real logic in the app, it probably
belongs in Core.

Why: Core has no WinForms, no P/Invoke, no registry and no package references, so it builds and is tested on any
OS, quickly, with no real clock (time is passed in as a number or a `TimeProvider`). That is where the
security-relevant rules are, so they are the best-tested part of the code. A source guard test fails the build
if Core starts using a Windows API (see [Tests](#tests)).

Shared settings: `Directory.Build.props` turns on nullable reference types and treats warnings as errors for
every project.

## Map

### Core (`src/WinXCorners.Core`)

| Folder | Main types | Purpose |
|---|---|---|
| `Model/` | `ApplicationSettings`, `CustomCommandSettings`, `HotCornerArea` | The settings object. Property names are the `settings.json` format and must not be renamed. |
| `Actions/` | `ActionCatalog`, `ActionIds`, `ActionPlanner`, `ActionPlan` | The list of corner actions (ids, labels, menu) and what each one means as a plan: send keys, launch, system command, lock. |
| `HotCorners/` | `HotCornerEngine`, `HotCornerGeometry`, `HotCornerRules`, `FullScreenHeuristics`, `SessionActivity` | The state machine that turns pointer samples into "count down" / "fire" / "blocked by full screen"; corner geometry; delays; the full-screen test; whether the session is locked or disconnected. |
| `Input/` | `CustomCommandAnalyzer`, `HotkeySequence`, `ConditionalHotkeyExpression`, `WindowCondition`, `KeyEventPlanner`, `VirtualKeys` | Parsing and validating custom commands, and turning a hotkey sequence into the exact key events to inject. |
| `Security/` | `ElevationMode`, `LaunchPlanner`, `SystemTargets`, `SettingsSeal`, `ElevatedStartPolicy`, `AutoElevatePolicy`, `RestartPolicy`, `InstallLocationAssessor`, `SecurityPosture`, `DisplayText` | The three elevation situations; how a launch is made (file, arguments, working folder, and whether it is same-rights, de-elevated or, on an all-administrator account, elevated); the settings approval for elevated sessions; when to elevate; how a restart is done; whether the program folder is safe; every warning text; making untrusted text safe to display. |
| `Settings/` | `SettingsStore`, `SettingsStorePolicy`, `SettingsSession`, `SettingsSanitizer`, `SettingsSerializer`, `SettingsPaths`, `SettingsMerge` | Reading, migrating, repairing and writing `settings.json`; what a normal-rights and an elevated store may do on disk (`IVerifiedSettingsFile` is the elevated store's file access); the live settings while the app runs. |
| `Platform/` | `CommandLineOptions`, `StartupCommandLine`, `SystemDirectories`, `ThemeCapabilities`, `WindowsPath`, `SessionEndPolicy` | Command-line switches, the autostart command line and whether an existing Run entry is stale, Windows path logic done with strings (so it is testable on Linux), which undocumented theme APIs a Windows build supports, what to do at sign-out or when an installer closes the app. |
| `Layout/` | `FlyoutLayout`, `FlyoutAnimation`, `PopupPlacementCalculator`, `DpiScale`, `TaskbarEdge`, `ColorContrast` | Where the popup goes and how it animates; black or white countdown text by contrast ratio. |
| `Diagnostics/` | `BoundedLog`, `LogText` | The in-memory log behind the Log window (newest 500 entries, thread-safe). |

### App (`src/WinXCorners.App`)

| Path | Main types | Purpose |
|---|---|---|
| `Program.cs`, `Startup.cs` | `Program`, `Startup`, `StartupOutcome` | The startup sequence (below). Each step is a method in `Startup` that takes its collaborators as parameters, so it is tested with fakes. |
| `CrashReporter.cs` | `CrashReporter` | Global exception handling; `crash.log`. |
| `IAppHost.cs`, `Platform/AppHost.cs` | `IAppHost`, `AppHost` | The composition root: builds the log, the settings session and every platform service once. Windows and dialogs talk to `IAppHost` only. |
| `Platform/` | `ActionExecutor`, `KeySender`, `ProcessLauncher`, `DeElevatedLauncher`, `ProcessElevation`, `ProcessMitigations`, `VerifiedSettingsFile`, `ApprovedSettingsStore`, `StartupRegistration`, `SingleInstance`, `InstallLocationProbe`, `InputDesktop`, `ForegroundWindowInspector`, `WindowQuery`, `ThemeHelper`, `Win32PopupMenu`, `Win32SystemActions`, `NativeMethods`, `Seams.cs` | One class per Windows capability. `Seams.cs` holds the small interfaces (`IKeySender`, `IProcessLauncher`, `ISystemActions`) that tests replace with fakes. All `[DllImport]`s are in `NativeMethods`. |
| `UI/` | `MainForm`, `FlyoutForm`, `HotCornerMonitor`, `CountdownOverlay`, `AdvancedForm` (Settings), `AboutForm`, `LogWindowForm`, `SecurityInfoForm`, `RiskDialog` (+ `ConsentForm`), `ElevatedApprovalDialog`, `StartupNotices` | `MainForm` is a hidden window that is never shown: it owns the tray icon, raw input, the hot-corner timer, saving, the child windows and shutdown. `FlyoutForm` is the popup with the four corner buttons. |

## How a hot corner fires

1. **Wake-up.** `MainForm` registers for raw mouse input (`RIDEV_INPUTSINK`, so it gets `WM_INPUT` while in the
   background) and runs a 50 ms timer. Both just call `HotCornerMonitor.Evaluate()`; the raw packet is not read.
   There is no low-level mouse hook.
2. **Facts.** `HotCornerMonitor` samples, through delegates: the cursor position (`InputDesktop.TryGetCursorPosition`,
   which reports a failed `GetCursorPos` instead of returning 0,0; `Cursor.Position` is banned here by a source
   guard), the bounds of the monitor that contains it (cached, refreshed on `WM_DISPLAYCHANGE`), whether a mouse
   button is held, whether the configured modifier is down (`GetAsyncKeyState`), `Environment.TickCount64`, and
   whether the popup or its menu is open. Whether the pointer is *available* combines three facts: the position
   could be read, Core's `SessionActivity` says the session is neither locked nor disconnected (fed by
   `WM_WTSSESSION_CHANGE`, registered with `WTSRegisterSessionNotification`), and the input desktop is the app's
   own (`InputDesktop.IsInputDesktopOurs`, false on the secure desktop of a UAC prompt or the lock screen).
3. **Decision.** Core's `HotCornerEngine.Evaluate(settings, input)` returns `Idle`, `Counting` (with time left and
   whether to show the countdown), `Triggered` (once per visit) or `BlockedByFullScreen`. It only asks the
   full-screen predicate (`ForegroundWindowInspector` facts judged by `FullScreenHeuristics`) when a dwell starts
   and just before firing. While the pointer is not available the engine resets and nothing fires. It arms on entry
   only: after start-up or `Reset()` (settings changed, display changed, lock/unlock, disconnect/reconnect, pointer
   unavailable) the pointer must be seen outside every corner first. After firing, the pointer must leave a
   32-pixel square before that corner can fire again.
4. **Countdown.** `Counting` with `ShowCountdown` moves `CountdownOverlay`.
5. **Queue.** `Triggered` logs the action and calls `ActionExecutor.Execute(actionId, settings.Snapshot())`, which
   only queues it. The executor has one STA background thread and a queue of at most 8; extra requests are
   dropped and logged. Nothing slow ever runs on the UI thread.
6. **Plan.** On the action thread, Core's `ActionPlanner.Plan(...)` maps the id to an `ActionPlan`: `SendKeys`,
   `ConditionalKeys`, `Launch`, `SystemCommand`, `LockWorkstation` or `Nothing` (with a reason). Custom commands go
   through `CustomCommandAnalyzer` first; an invalid one becomes `Nothing`.
7. **Perform.**
   - Keys: `KeyEventPlanner.Plan` produces the events (releasing the user's held modifiers first, releasing
     everything the sequence held at the end, extended-key flags); `KeySender` sends them with `SendInput`.
     A conditional command first evaluates its conditions with `WindowQuery`.
   - Launch: `ProcessLauncher` asks `LaunchPlanner.TryPlan` for file, arguments, working folder and integrity,
     which follows the session's `ElevationMode` (read once at start by `ProcessElevation.CurrentMode` from the
     app's own token). `NotElevated`: `Process.Start` with `UseShellExecute`. `ElevatedWithStandardShell` (UAC on):
     `DeElevatedLauncher` finds Explorer's desktop view through `IShellWindows`, checks that the Explorer process
     owning it is not elevated (`CheckShellToken`), and asks it to do the `ShellExecute`; if any step fails,
     nothing starts. `ElevatedWithoutStandardRights` (UAC off or the built-in Administrator; token type
     `Default`): `Process.Start`, and the log says the program gets administrator rights.
   - System commands are posted as `WM_SYSCOMMAND` to `MainForm`'s window; lock calls `LockWorkStation`.
   Every outcome is logged.

## Settings: load, change, save

- **Where.** `SettingsPaths.ForApp` gives `%APPDATA%\WinXCornersPlus\settings.json` (primary) and
  `<program folder>\settings.json` (legacy, 1.4.0 and earlier; read for migration only, never written).
- **Load** (`SettingsStore.Load`, called once by `AppHost.Create`). What the store may do on disk is decided once
  by `SettingsStorePolicy.For(isElevated)`. The cases below are for a normal-rights session:
  - primary file parsed: sanitized, corrections returned for the log;
  - primary missing: the legacy file, if any, is read, sanitized and copied to the primary location; otherwise
    defaults (nothing written);
  - primary present but not valid settings (bad JSON, not UTF-8, over 1 MiB): moved to
    `settings.corrupt-yyyyMMdd-HHmmss.json`, defaults used; if the move fails, saving is blocked;
  - primary present but unreadable (locked, no access): `AppHost` retries three times, 150 ms apart; then defaults
    are used and saving is blocked for the session, so the file is not overwritten.

  An elevated session uses `SettingsStorePolicy.Elevated`: every read and write goes through
  `App/Platform/VerifiedSettingsFile.cs`, which opens `%APPDATA%`, then the settings folder relative to that
  handle, then `settings.json` relative to the folder handle, never following a reparse point, and refuses if any
  level is a junction or symbolic link, the file has more than one hard link, or a handle's final path is not the
  expected one. Nothing is moved, backed up or migrated: a missing file gives defaults, an invalid file gives
  `KeptInvalidFile`, a redirected location `RefusedRedirectedLocation`; in both of those cases defaults are used,
  the file is left untouched and saving is blocked. Redirection Guard is turned on before any of this (startup
  step 0).

  `StartupNotices` turns migration, recovery, an untouched invalid or redirected file, and a stale or unquoted
  autostart entry into one tray balloon (and the log).
- **Sanitize** (`SettingsSanitizer`): unknown action ids become `none`, empty ones the default, out-of-range
  numbers and enums are reset, the command array is made exactly four entries, and command text loses control,
  bidirectional-control and unpaired-surrogate characters and is length-limited. It runs on load and after every
  change.
- **Change.** `SettingsSession` owns the live object; every change goes through `SettingsSession.Update(...)`,
  which sanitizes, marks it dirty and raises `Changed`. The Settings window edits a copy and applies it once on
  OK/Apply through `SettingsMerge.ApplyDialogEdits` (which leaves the corner actions and the enabled switch
  alone, because those are changed from the popup and tray while the window is open).
- **Save.** `MainForm` saves 180 ms after the last change, on exit and restart, and on `WM_QUERYENDSESSION`; a
  confirmed `WM_ENDSESSION` (sign-out, shutdown, or an installer closing the app) runs the normal shutdown, which
  saves (`SessionEndPolicy`). With normal rights `SettingsStore.Save` writes a temp file in the same folder,
  flushes it, and renames it over `settings.json`; an elevated store writes in place through the verified handle
  (no extra names in a folder other programs control, at the cost of a torn file if power fails mid-write, which
  the next normal-rights start moves aside). Either way it skips the write when the file already holds the same
  bytes. A failed save keeps the session dirty, so the next save retries.
- **Format.** `SettingsSerializer` writes the 1.4.0 format unchanged (same names, enums as numbers, indented),
  so a file can be copied back to 1.4.0.
- **Elevated sessions** also compare the settings with the approval in HKLM (`SettingsSeal`,
  `ApprovedSettingsStore`). After the gate has passed, `AppHost` re-approves on every `Changed`.

## Startup sequence

`Program.Main`, in order. Steps 3 to 6 are security-relevant and are tested one by one, with a fake host, in
`tests/WinXCorners.App.Tests/StartupSequence/StartupTests.cs`.

0. **Redirection Guard** (elevated only, `ProcessMitigations.EnableRedirectionGuard`), right after the command
   line is parsed and before anything touches the file system; a source guard test checks that it comes before
   `AppHost.Create`. Builds without the policy log that and rely on the verified file access.
1. Parse the command line (`--no-elevate`, `--show-settings`, `--restarted-from <pid>`), then
   `ApplicationConfiguration.Initialize()`. DPI awareness comes from the manifest (per-monitor).
2. Install `CrashReporter`: an exception in a UI event handler is logged, appended to
   `%LOCALAPPDATA%\WinXCornersPlus\crash.log` (not in an elevated session, which writes no file and keeps the error in the Log window) and shown once
   in a balloon, and the app keeps running. An exception on another thread is logged and the runtime ends the
   process.
3. **Single instance** (`Startup.AcquireSingleInstance`). Take the per-session mutex; if another copy holds it,
   signal it to open Settings (and show its tray icon) and exit. This runs before settings are loaded or anything
   elevates, so a second launch never shows a UAC prompt or the approval dialog. If the running copy cannot be
   signalled, the second launch shows *WinXCornersPlus appears to be running but did not respond* and exits. Before
   signalling it lets processes with this executable's name in the session take the foreground
   (`AllowSetForegroundWindow` per process id, never for every process). The mutex and event are opened first
   with only wait and signal rights, and created granting the user only those rights. A restarted copy
   (`--restarted-from`) first waits up to 5 s for its predecessor to exit.
4. **Composition** (`AppHost.Create`). Set the current folder to System32, read the elevation mode, load settings
   with the matching store policy, build the services. An elevated session writes nothing here.
5. **Auto-elevate** (`Startup.AutoElevate`, `AutoElevatePolicy`). If *Always run as administrator* is on and the
   process is not elevated and `--no-elevate` was not given: restart through the UAC prompt, after an
   explanation unless the recorded approval matches the current settings exactly. A declined prompt continues
   with normal rights.
6. **Elevated gate** (`Startup.PassElevatedGate`, `ElevatedStartPolicy`). If elevated: compare with the approval;
   ask when it is the first run, changed or unreadable. Declining releases the mutex, restarts with normal rights
   through Explorer (`RestartPolicy`: not possible on an all-administrator account, where it only exits) and
   exits.
7. **Tray app.** Create `MainForm`, `Start()` (tray icon, timer, startup notices), open Settings if
   `--show-settings`, run the message loop. On exit: dispose the form, then the host (stops the action worker),
   then release the single instance.

Shutdown has one path, `MainForm.Shutdown` (on *Exit*, a successful restart, or a confirmed `WM_ENDSESSION`): stop
the timers, save, remove the tray icon, dispose every window, end the message loop. No window can veto it.

Restarts (`AppHost.TryRestart`) go through Core's `RestartPolicy`, and `RestartStarter` performs them. From a normal
session, *Restart* starts the executable directly and *Restart as administrator…* uses the `runas` verb
(`ProcessElevation`). Only a process that is not elevated uses `runas`, because Windows shows no UAC prompt to an
elevated caller; `ProcessElevation` refuses the request in an elevated process. From an elevated session, *Restart*
starts the executable directly only when the install folder is assessed `Protected`, or on an account where
everything runs as administrator. Otherwise it starts the app without administrator rights through Explorer, without
`--no-elevate`, and the tray item reads *Restart without administrator rights*. That copy follows the normal start
rules, so *Always run as administrator* raises a real UAC prompt. If Explorer cannot start it, nothing is started.

## Tests

| Suite | Runs on | What it proves |
|---|---|---|
| `tests/WinXCorners.Core.Tests` | Any OS (CI: Windows x64, Windows ARM64, Ubuntu) | Every Core rule: parsing, planning, the engine (including pausing and re-arming when the pointer is unavailable), settings load/repair/save under both store policies (with a fake verified file for the elevated one), the seal, the three elevation situations and the launch and restart decisions for each, the install-location assessment, the warning texts. `Adversarial/` holds hostile-input tests (crafted settings, look-alike characters, path tricks, forged approvals). CI requires 95% line and 90% branch coverage. |
| `tests/WinXCorners.Core.Tests/Architecture` | Any OS, part of the Core suite | **Source guard tests** that read the app's source and the workflows and enforce the security invariants (see below). |
| `tests/WinXCorners.App.Tests` | Windows only (CI: Server 2022, Server 2025, Windows 11 ARM64) | The platform layer against real Windows: struct sizes, `SendInput` flags, the single-instance mutex and signal (including the narrow permissions and a name squatted with a hostile DACL), the HKLM approval store and the Run value (with throw-away names), the folder and file permission probe, the elevated settings file access against junctions, symbolic links and hard links created by the test, launching (an elevated launch never falls back to a normal `Process.Start`; the de-elevated launch fails and starts nothing when Explorer is elevated or unreachable), the action executor with fakes, the startup steps with a fake host, the crash reporter, and a smoke test that starts the built exe (elevated on CI: it must stop at the approval dialog with no main window, and turn on Redirection Guard). |
| `tests/WinXCorners.App.UiTests` | Windows desktop session only (CI: same three images) | Every window with real message loops: layout without overlaps, light and dark theme, the About and Settings windows also at simulated 125/150/200% scale, default buttons and keyboard behaviour of the risk and approval dialogs, risky switches reverting on Cancel, invalid commands blocking OK, Exit still working after the Log window was opened, the tray toggle being saved, a second-launch request showing Settings and a hidden tray icon, the hot-corner monitor with a pointer that cannot be read, another desktop having the input, and lock/unlock with the pointer parked in a corner. Screenshots of each window are uploaded as a workflow artifact. |

**What CI does not prove.** GitHub's hosted Windows runners run the tests as the built-in Administrator: UAC is on,
but there is no normal-rights token and Explorer itself is elevated. Every Windows test that uses the real token
or the real Explorer therefore sees the "everything already runs as administrator" situation
(`ElevationMode.ElevatedWithoutStandardRights`). The test that
would show a program started from an administrator session getting a non-elevated token
(`ProcessLauncherTests.De_elevated_launch_gives_the_child_a_non_elevated_token_or_fails_closed`) can only verify its
fail-closed half there and reports itself as skipped with that reason. So the positive case has never been observed
on real Windows; only the refusal half and the Core decision tables are tested. It runs fully only from an
elevated prompt on an account with UAC on, and it is item 0.1 of [MANUAL-TEST-PLAN.md](MANUAL-TEST-PLAN.md). The
same applies to single-instance behaviour across integrity levels (normal-rights second start, elevated running
copy), real multi-monitor DPI changes, and ARM64 hardware.

Workflows: `.github/workflows/ci.yml` (Core and app tests) and `.github/workflows/ui-tests.yml` (UI tests) run
on pushes to `master`, on pull requests and on demand; both can be called by the release workflow as a gate.

### Source guard tests

`SourceInvariantTests` (in the Core suite, so they run everywhere) fail when the app's source:

- calls `Process.Start` outside `ProcessLauncher.cs`, `ProcessElevation.cs` and `AppHost.cs`;
- uses the `runas` verb outside `ProcessElevation.cs`;
- uses `SetWindowsHookEx`, `keybd_event` or `Environment.Exit` anywhere;
- has a bare `catch`, an empty `catch`, or a catch-all with no statements (Core included);
- touches the registry outside `StartupRegistration.cs`, `ApprovedSettingsStore.cs` and `ThemeHelper.cs`;
- declares `[DllImport]` / `[LibraryImport]` outside `NativeMethods.cs`, or drops
  `[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` from `AssemblyInfo.cs`;
- lets Core use WinForms, `Microsoft.Win32` or P/Invoke, or gives Core a package reference;
- tracks build output or binaries in git;
- moves, replaces or copies files (`File.Move`, `File.Replace`, `File.Copy`, `Directory.Move`) anywhere but
  `SettingsStore.cs` (Core included);
- loads settings (`AppHost.Create`) before `Program.cs` turns on Redirection Guard;
- reads the hot-corner pointer from `Cursor.Position` instead of `InputDesktop.TryGetCursorPosition`;
- calls `AllowSetForegroundWindow` for every process (`ASFW_ANY`).

They also fail when a workflow's `uses:` line is not pinned to a full commit SHA with a version comment (this
applies to `release.yml` too), or when `.github/dependabot.yml` stops covering GitHub Actions and NuGet.

The rules ignore comments and string contents (except where the rule is about a string). What to do when one
fails is in [CONTRIBUTING.md](../CONTRIBUTING.md).

## How to add a corner action

1. **Id and label.** Add a constant to `ActionIds` and an `ActionDefinition` to `ActionCatalog.BuiltIn`
   (`src/WinXCorners.Core/Actions/ActionCatalog.cs`). Ids are written to `settings.json`: never rename or reuse
   one. The menu, the button labels, the sanitizer (which turns unknown ids into `none`) and the settings
   approval all read this table, so they pick up the new id without changes.
2. **Meaning.** Add a `case` to `ActionPlanner.Plan` that returns an existing plan:
   - keys: `new ActionPlan.SendKeys(ParseBuiltIn("_win+x"))` (parsed once into a static field, as the others are);
   - a program: add a factory to `SystemTargets` that returns a `LaunchTargetKind.BuiltIn` request with an
     **absolute** path inside the Windows folder (or an `ms-settings:` address); `LaunchPlanner` refuses anything
     else for built-ins;
   - a `WM_SYSCOMMAND`: add a `SystemCommandKind`.
3. **A new kind of side effect** (only if none of the above fits): add a record to `ActionPlan`, handle it in
   `ActionExecutor`'s switch, and reach Windows through a method on a seam interface in `Platform/Seams.cs` (with
   its P/Invoke in `NativeMethods`), so the executor tests can use a fake.
4. **Tests.** `ActionCatalogTests` (menu order and labels), `ActionPlannerTests` (the plan), and for a new side
   effect `ActionExecutorTests`.
5. **Docs.** Add the action to the feature list in `README.md` and to `CHANGELOG.md`.

Version 1.5.0 and later read an id they do not know as `none` (the corner does nothing) rather than failing.
