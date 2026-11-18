## What and why

<!-- What does this change, and why? Link the issue it fixes, if any. -->

## How it was tested

<!-- Which test suites you ran, on which Windows version and architecture, and what you checked by hand. -->

## Checklist

- [ ] The build has no warnings, and the Core, Windows platform and UI tests pass (or skip only for documented reasons).
- [ ] New or changed behaviour has tests; a bug fix has a test that fails without it.
- [ ] Decisions are in `WinXCorners.Core`, Windows side effects in `WinXCorners.App` (see CONTRIBUTING.md).
- [ ] No source guard test was weakened; if a rule was changed, the reason is explained above.
- [ ] If a risky behaviour changed (elevation, launching, hidden windows, tray icon, autostart, the settings approval), the risk text in `SecurityPosture`, its tests and `docs/SECURITY-MODEL.md` are updated.
- [ ] If launching or elevation changed: the platform tests were run from an administrator prompt on an account with UAC on (CI cannot check that launched programs are not elevated), or manual test 0.1 was done.
- [ ] No settings property or action id was renamed or reused.
- [ ] README, docs and CHANGELOG are updated where users will notice the change.

<!-- Security problems must be reported privately (SECURITY.md), not in a pull request. -->
