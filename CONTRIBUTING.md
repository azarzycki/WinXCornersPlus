# Contributing to WinXCornersPlus

Thank you for helping. Bug reports and feature ideas go to the
[issue tracker](https://github.com/Asteski/WinXCornersPlus/issues). **Security problems do not**: report them
privately as described in [SECURITY.md](SECURITY.md).

Before changing code, read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (how the code is organised) and, for
anything near elevation, launching, settings or the registry, [docs/SECURITY-MODEL.md](docs/SECURITY-MODEL.md).

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The app is Windows-only; the core
library builds anywhere.

```
dotnet build src/WinXCorners.App/WinXCorners.App.csproj -c Release
```

The program is in `src/WinXCorners.App/bin/Release/net10.0-windows/WinXCornersPlus.exe`. Warnings are errors in
every project (`Directory.Build.props`), so a build with a warning fails.

## Run the three test suites

| Suite | Where it runs | Command |
|---|---|---|
| Core unit tests (includes the source guard tests) | Windows, Linux or macOS | `dotnet test tests/WinXCorners.Core.Tests/WinXCorners.Core.Tests.csproj` |
| Windows platform tests | Windows | `dotnet test tests/WinXCorners.App.Tests/WinXCorners.App.Tests.csproj -c Release` |
| UI tests with screenshots | Windows, signed-in desktop session | `dotnet test tests/WinXCorners.App.UiTests/WinXCorners.App.UiTests.csproj -c Release` |

Things to know when running them on your own PC:

- **Platform tests.** Tests that write the approval under `HKLM` use a throw-away key and run only from an
  elevated prompt; otherwise they are skipped. Autostart tests use a throw-away value name under your `HKCU` Run
  key and remove it. One smoke test starts the built `WinXCornersPlus.exe` with your real settings; it is skipped
  if WinXCornersPlus is already running, so exit your own copy first if you want it to run.
- **De-elevated launching.** GitHub's Windows runners run as the built-in Administrator, where everything is
  elevated, so on CI the test that a program started from an administrator session is *not* elevated
  (`De_elevated_launch_gives_the_child_a_non_elevated_token_or_fails_closed`) only checks the refusal half and is
  reported as skipped. It checks the real thing only when you run the platform tests from an administrator prompt
  on an ordinary administrator account with UAC on. If you touch launching or elevation, run it that way and say
  so in the pull request; otherwise this is covered only by item 0.1 of
  [docs/MANUAL-TEST-PLAN.md](docs/MANUAL-TEST-PLAN.md).
- **UI tests** open real windows and switch your Windows app theme between light and dark while they run,
  restoring it afterwards. Do not use the PC during the run. Screenshots go to
  `tests/WinXCorners.App.UiTests/bin/Release/net10.0-windows/TestResults/screenshots`, or to the folder in the
  `UI_SCREENSHOT_DIR` environment variable. Some tests skip themselves when the screen is too small for a
  simulated scale.
- **Coverage.** CI requires 95% line and 90% branch coverage of Core. To check locally:
  ```
  dotnet test tests/WinXCorners.Core.Tests/WinXCorners.Core.Tests.csproj --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory TestResults
  pwsh ./build/Check-Coverage.ps1 -ResultsDirectory TestResults -MinimumLine 95 -MinimumBranch 90
  ```

On GitHub, `.github/workflows/ci.yml` runs the Core tests on Windows x64, Windows ARM64 and Ubuntu and the
platform tests on Windows Server 2022, Server 2025 and Windows 11 ARM64; `.github/workflows/ui-tests.yml` runs the
UI tests on the same Windows images and uploads the screenshots as an artifact. Check them on your pull request,
and look at the screenshots if you changed a window.

## The Core / App rule

**Decisions go in `src/WinXCorners.Core`; Windows side effects go in `src/WinXCorners.App`.**

- Core may not use WinForms, `Microsoft.Win32`, P/Invoke or NuGet packages. It must build and its tests must pass
  on Linux. Windows paths are handled as strings (`Core/Platform/WindowsPath.cs`), not with `System.IO.Path`.
- The app gathers facts from Windows, asks Core, and performs the result. If you write an `if` with real logic
  in the app, move it to Core and test it there.
- Every Core change comes with unit tests for its behaviour: boundaries, malformed and hostile input,
  round-trips. Tests use no real clock (pass a `TimeProvider` or timestamps) and no registry, and file-system
  tests use their own temporary folder.
- Text the user sees about risks comes from `Core/Security/SecurityPosture.cs`, so it is tested and stays true.
  If you change a risky behaviour (elevation, launching, hidden windows, the tray icon, autostart, what the
  settings approval covers), update that text, its tests, and [docs/SECURITY-MODEL.md](docs/SECURITY-MODEL.md).
- Settings property names and action ids are written to `settings.json`: never rename or reuse one.

## Source guard tests

`tests/WinXCorners.Core.Tests/Architecture/SourceInvariantTests.cs` reads the app's source and fails when a
change breaks one of the security rules: `Process.Start` or the `runas` verb outside the launch and elevation
classes; low-level hooks, `keybd_event` or `Environment.Exit` anywhere; empty or bare `catch` blocks; registry
access outside the three registry classes; native imports outside `NativeMethods.cs` or without the System32-only
search path; Windows APIs or packages in Core; build output committed to git; file moves outside the settings
store; Redirection Guard turned on too late; `Cursor.Position` for hot corners; `AllowSetForegroundWindow` for every
process; workflow actions not pinned to a commit SHA. The full list is in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#source-guard-tests).

When one fails, the message names the file, the line and the rule. Then:

1. **Route the code through the place that is allowed to do it.** Start programs through `IProcessLauncher` /
   `ProcessLauncher` (which plans with Core's `LaunchPlanner` and never launches elevated), put the import in
   `NativeMethods.cs`, catch the specific exception type and handle it, use the settings session instead of
   exiting the process.
2. **Do not weaken the rule** to make the test pass, and do not hide the code from it (for example by building
   names from strings).
3. If you believe the rule itself is wrong for a real need, change the rule in `SourceInvariantTests.cs` in the
   same pull request and explain in the description why it is safe. Expect that part to be reviewed closely.

## Commits and pull requests

- One topic per pull request. Large changes are easier to review as a short series.
- The build must have no warnings and all three suites must pass (or skip only for the documented reasons).
- Add or update tests for what you change. Bugs get a test that fails without the fix.
- Update `README.md`, the docs and `CHANGELOG.md` (under an *Unreleased* heading) when users will notice the change.
- Commit messages: a short summary line in the imperative ("Fix stuck Ctrl after a hotkey"), then a blank line
  and the why, if it is not obvious. The release workflow reads commit subjects to pick the next version number:
  `feat:` gives a minor version, `!` before the colon or a `BREAKING CHANGE:` line a major one, anything else a
  patch. See [docs/RELEASING.md](docs/RELEASING.md).
- Do not commit build output, `settings.json` files, or anything with personal data.
- In workflows, pin every action to a full commit SHA with its version in a trailing comment
  (`uses: actions/checkout@<sha> # v7.0.1`); a source guard test checks it, and Dependabot
  (`.github/dependabot.yml`) opens weekly pull requests to update actions and NuGet packages.

## Releases and version numbers

Version numbers come from git tags and are computed by the release workflow; nobody edits a version to release.
Merging a pull request does not publish a release by itself: the maintainer runs the release workflow, or turns
on automatic releases with a repository variable. The details, including how to force a minor or major version
and how to do a dry run, are in [docs/RELEASING.md](docs/RELEASING.md). Installers and code signing are described
in [docs/INSTALLING.md](docs/INSTALLING.md) and [docs/CODE-SIGNING-POLICY.md](docs/CODE-SIGNING-POLICY.md).
