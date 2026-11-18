# Security model

This page describes what WinXCornersPlus does on your PC, which attacks it is designed to stop, and which it
cannot stop. It describes version 1.5.0. The code that implements each point is named so it can be checked.

## What the app does on your machine

While it runs, WinXCornersPlus:

- **Reads the pointer position.** It registers for raw mouse input (no data from the packets is used, they only
  wake it up) and also checks every 50 ms. It reads the cursor position, whether the left, middle or right
  button is held, and, if you chose a modifier key for the corners, whether that key is held. It installs no
  keyboard or mouse hook and never records keystrokes. It asks Windows to tell it when the session is locked,
  unlocked, disconnected or reconnected, and checks that the desktop receiving input is its own. Hot corners pause
  while the workstation is locked, while another desktop has the input (for example the secure desktop of a UAC
  prompt), while the session is disconnected, and whenever the pointer position cannot be read. Afterwards no corner
  fires until the pointer has been seen outside every corner.
- **Looks at windows.** To skip full-screen apps it reads the foreground window's class name, size, style and
  owning process. For `#[...]` and `@[...]` custom commands it reads the foreground window's class and title, or
  asks Windows whether a window with a given class and title exists. Nothing of this is stored, except that the
  Log window (memory only) names the action that ran or was skipped.
- **Sends keystrokes** with `SendInput` for built-in actions (for example Win+Tab) and for your hotkey commands.
  Before a sequence it reads which Shift, Ctrl, Alt and Windows keys you are holding, so it can release them.
- **Starts programs, documents and links**: File Explorer, Task Manager, System Settings, your custom commands,
  and web pages from the About and Settings windows (only `https://` addresses).
- **Asks Windows** to start the screen saver, turn the display off, open Start, or lock the workstation.
- **Writes files** under your user profile: `%APPDATA%\WinXCornersPlus\settings.json` and
  `%LOCALAPPDATA%\WinXCornersPlus\crash.log`. With normal rights it also renames a damaged settings file to
  `settings.corrupt-<date>-<time>.json`. The 1.4.0 settings file beside the program is read for migration and never
  written. An elevated session writes only `settings.json`, in place, and only after checking that it is not
  redirected (see below).
- **Writes the registry**: one value `WinXCornersPlus` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`,
  only when you turn on *Run at startup*; and, only when it runs as administrator, your approval under
  `HKLM\SOFTWARE\WinXCornersPlus\ElevatedApprovals` (one value per Windows user, named by the user's SID). It
  reads the Windows theme settings.
- **Makes no network connections.** There is no update check, telemetry or crash upload. The *Check for
  updates* link opens the Releases page in your browser.

## The trust boundary that matters

Every program you run without "Run as administrator" runs **as you**. Such a program can already do anything
you can do without administrator rights: read and change your files (including `settings.json`), change your
part of the registry (including the autostart entry), send keystrokes to your normal windows, start programs,
and end or block your other programs, WinXCornersPlus included.

WinXCornersPlus does not try to defend your normal session against such a program, because it has nothing to
give that the program does not already have. The boundary it defends is the one between **programs running as
you** and **administrator rights**: a program running as you must not be able to use WinXCornersPlus to get
administrator rights.

In normal mode (the default) the app has no administrator rights, so there is nothing to escalate to. The rest
of this page is about elevated mode.

## Elevated mode

### What it is for

Windows does not let a normal program send input to a window that runs as administrator (User Interface
Privilege Isolation). Hotkeys sent by a normal-rights WinXCornersPlus into, say, an administrator console or
Task Manager are silently dropped. Running WinXCornersPlus as administrator lets its corner actions and hotkey
commands reach those windows. That is the only reason to elevate.

You get there by *Restart as administrator…* in the tray menu (one session) or *Always run as administrator*
in Settings (every start). Both show the risks first. An elevated session says so in its tray tooltip, its tray
menu (*Running as administrator*), the About window and the Security page.

### What protects it

**1. What it launches is started without administrator rights, where Windows has such rights to give.** This
covers custom commands, the built-in Explorer / Task Manager / System Settings actions, *Open settings folder*, the
About and update links, and the restart without administrator rights described below. At start the app reads its
own token and classifies the session into one of three situations
(`Core/Security/ElevationMode.cs`, `Core/Security/LaunchPlanner.cs`, `App/Platform/ProcessLauncher.cs`,
`App/Platform/DeElevatedLauncher.cs`):

| Situation | How programs are started | Rights they get |
|---|---|---|
| Normal session (the default) | Directly, with `ShellExecute` | Your normal rights, the same as the app |
| Administrator session on an account with UAC on (Windows keeps a normal-rights token for your sign-in) | The app asks the desktop shell (Explorer) to start them. Before every launch it checks the token of the Explorer process that will do it. If Explorer cannot be reached, has no desktop registered, runs as administrator itself, or its rights cannot be read, the launch fails, is logged, and **nothing is started**. There is no fallback that starts the program with the app's own token. | Explorer's rights, which are your normal rights |
| Administrator session on an account where everything already runs as administrator (UAC turned off, or the built-in Administrator account) | Directly, because there is no lower level to drop to | **Administrator rights.** The approval dialog, the Security page and the Log window say so plainly. |

A program that needs administrator rights shows its own UAC prompt. Some Windows tools, Task Manager for example,
raise themselves to administrator for members of the Administrators group without a prompt; that is Windows
behaviour, not the app's.

**Not yet verified on real Windows:** that a program started from an administrator session on an account with UAC
on really runs *without* administrator rights. The automated tests check the decision rules in Core, and on
Windows they check that the launch fails and starts nothing when Explorer itself is elevated. They cannot check the
positive case, because GitHub's hosted Windows runners sign in as the built-in Administrator, where everything is
elevated (see [Not verified](#not-verified-on-every-pc)). It is the first item of the
[manual test plan](MANUAL-TEST-PLAN.md).

**2. You approve what an administrator session will run.** `settings.json` is in your profile, so any program
running as you can change it. An elevated session therefore keeps the security-relevant part of the settings
you last approved in `HKLM\SOFTWARE\WinXCornersPlus\ElevatedApprovals`, which programs without administrator
rights can read but not change. At every elevated start, before the tray icon, raw input or any action starts,
it compares:

- **No approval yet** (first elevated run): the dialog *Approve what the administrator session will run* lists
  every corner's action, whether custom commands are on, every configured command (program and parameters shown
  in full, each value in its own “quotes”, never shortened), the modifier key, whether the tray icon is hidden and
  whether *Always run as administrator* is on.
- **Something changed**, or the stored approval cannot be read: the dialog *Settings changed since you last
  approved them* shows each difference and then everything that will run.
- **Unchanged**: it continues without asking.

If the elevated session could not use the settings file (damaged, or redirected, see point 7), it runs on default
settings, and those defaults are what the dialog shows and what you approve.

*Approve and continue* stores the new approval. *Don't run as administrator* (or Esc, Enter, or closing the
dialog) restarts the app with normal rights, through Explorer as in point 1, and does not elevate on that start. On
an account where everything runs as administrator there are no normal rights to restart with, so the app just
exits.

The approval covers: the four corner actions, *Enable Custom Commands*, each slot's name, command, parameters and
*Hide the program's window*, the modifier key, *Always hide tray icon* and *Always run as administrator*. It does
not cover delays, the countdown, the full-screen option, the animation direction, *Run at startup*, or whether hot
corners are enabled: none of these changes what the elevated session can do.

Changes you make in the elevated session's own Settings window or popup are approved automatically. Programs
without administrator rights cannot send input to those windows (UIPI), so such a change can only come from you.
(`Core/Security/SettingsSeal.cs`, `App/Platform/ApprovedSettingsStore.cs`, `App/Startup.cs`, `App/UI/ElevatedApprovalDialog.cs`.)

**3. The "Always run as administrator" switch cannot raise a UAC prompt on its own.** The switch lives in
`settings.json`. The app goes straight to the UAC prompt only when the recorded approval matches the current
settings exactly, which means you approved an administrator session with this very switch on and nothing has
changed since. In every other case (no approval, or the settings differ) it first explains what running as
administrator means (*Settings ask WinXCornersPlus to run as administrator*) and shows the UAC prompt only if you
click *Continue to the Windows prompt*. Declining the UAC prompt continues with normal rights. An approval in an
older format, which did not include the switch, counts as unreadable and is asked for again.
(`Core/Security/AutoElevatePolicy.cs`, `Core/Security/SettingsSeal.cs`.)

*Restart as administrator…* in the tray menu always shows *Before WinXCornersPlus runs as administrator* first, and
starts the UAC prompt only after *Continue to the Windows prompt*.

**4. No ambient search paths.** Built-in actions start `explorer.exe` and `taskmgr.exe` by their full path in the
Windows folder, and System Settings by the `ms-settings:` address. The process's current folder is set to
System32 before settings are loaded, so a program or DLL planted in the folder the app was started from is not
found through the current-folder search. Native libraries that the app imports are loaded from System32 only.
Restarts use the full path of the program. The autostart entry is written in quotes, so a path with spaces cannot
be cut short to a planted program; an existing unquoted entry with spaces is reported at start as needing to be
rewritten. (`App/Platform/AppHost.cs`, `App/AssemblyInfo.cs`, `Core/Platform/StartupCommandLine.cs`.)

**5. One instance, and it cannot hide.** Only one copy runs per Windows session. Starting the app again, with or
without administrator rights, asks the running copy to open its Settings window and show its tray icon, then
exits without loading settings or elevating. A hidden tray icon therefore never leaves you without a way back. The
second start lets only processes with the app's own name in your session come to the front, not every program.
The single-instance objects grant your account only the rights to wait for them and signal them, so a program
running as you cannot change their permissions to lock the app out. It can still create an object of the same name
first; then the running copy cannot be reached, and the second start says *WinXCornersPlus appears to be running
but did not respond* instead of exiting silently. (`App/Platform/SingleInstance.cs`, `App/Startup.cs`.)

**6. It tells you where it runs from.** The app checks the permissions of its own folder, of
`WinXCornersPlus.exe` and its two DLLs, and of every folder above it (whether someone could delete or rename the
folder below). If programs without administrator rights could change any of them, or the folder is on a network
share, or the permissions cannot be read, it warns you before you elevate and while you run elevated (see the
tables below). (`Core/Security/InstallLocationAssessor.cs`, `App/Platform/InstallLocationProbe.cs`.)

In an elevated session, the tray menu's *Restart* starts the program file directly only from a folder that is
confirmed admin-only. From any other folder it asks Windows to elevate the program again (the `runas` request that
normally shows the UAC prompt), so that a replaced file is not silently given administrator rights.
(`Core/Security/RestartPolicy.cs`.) **Not verified:** whether Windows actually shows a UAC prompt when that request
comes from a process that is already elevated. If it does not, this restart gives a replaced file administrator
rights without asking. Item 4.12 of the [manual test plan](MANUAL-TEST-PLAN.md) checks it; until then, do not run
an elevated session from a changeable folder.

**7. An elevated session handles your settings folder carefully.** `%APPDATA%\WinXCornersPlus` belongs to you, so
a program running as you controls what is in it, and could replace the folder or the file with a link to somewhere
else. An elevated session therefore:

- turns on Windows' Redirection Guard first thing at start (before settings are loaded), so the process does not
  follow junctions created by non-administrators. Windows builds older than Windows 10 21H2 / Server 2022 do not
  have it; the app then logs that and relies on the checks below
  (`App/Platform/ProcessMitigations.cs`);
- opens `%APPDATA%`, the settings folder and `settings.json` one handle at a time, never following a link; none of
  them may be a junction or symbolic link, the file must have a single hard link, and each handle must resolve to
  the expected path (`App/Platform/VerifiedSettingsFile.cs`);
- writes `settings.json` in place through that checked handle, instead of writing a temporary file and renaming it;
- never moves, backs up or migrates settings files (`Core/Settings/SettingsStorePolicy.cs`);
- if the settings file is damaged, or the folder or file is redirected, leaves it untouched, runs on default
  settings, does not save, and says that the file must be fixed or deleted from a normal-rights session;
- writes no `crash.log`; unexpected errors go to the in-memory Log window only.

An elevated session writes no file before the approval dialog has been answered. Migration from the
1.4.0 location happens only in a normal-rights session. This keeps an administrator process from being tricked
into creating, moving or overwriting files somewhere else.

## What is NOT protected, and why

**Program files in a folder you can write to.** An elevated session runs whatever code is in its folder. If that
folder is writable by programs running as you (a portable copy in Downloads or on the desktop), such a program
can replace `WinXCornersPlus.exe` or one of its DLLs. The next time you approve the UAC prompt for
WinXCornersPlus, that program runs as administrator. The settings approval cannot help here, because the code
that checks it is the code that was replaced. The app warns about this but cannot prevent it. Install with the
installer, which puts the program under Program Files, before you use elevated mode. A network folder is always
treated as changeable, whatever its local permissions say.

**Keystrokes that you configured, sent to elevated windows.** Elevated hotkeys go to whatever window is in front
when the corner fires, including administrator consoles. That is the purpose of elevated mode and also its risk:
an approved sequence that types into the wrong elevated window does so with full effect. Conditions such as
`#[ConsoleWindowClass]` only narrow this down: any program can create a window with a given class name and title,
so a condition decides which of your approved branches runs, not which program receives the keys.

**A program running as you, in a normal session.** It can change any setting, including custom commands; in
normal mode those run with your rights, as that program could already do. It can end WinXCornersPlus or stop it
from starting (for example by creating its single-instance object first; you then see the "did not respond"
message), change or remove the autostart entry, or put another program in its place. If it changes *Always run as
administrator* or anything else the approval covers, the next start explains before any UAC prompt, and an elevated
session asks you to approve again.

**An account where everything runs as administrator.** With UAC off, or on the built-in Administrator account,
every program you run already has administrator rights, so there is no boundary for the app to defend: what it
launches gets administrator rights, and so does any program that edits its settings. The app says so; it does not
try to prevent it.

**Unsigned binaries.** The builds are not code-signed yet. The UAC prompt shows "Unknown publisher" for
WinXCornersPlus and for any other unsigned program that calls itself WinXCornersPlus, so the prompt cannot tell you
which one you are approving. Approve a UAC prompt for WinXCornersPlus only when you just started it or just signed
in. Each release publishes SHA-256 checksums and a build provenance attestation you can check before installing
(see [RELEASING.md](RELEASING.md) and [INSTALLING.md](INSTALLING.md)); the plan for signing is in
[CODE-SIGNING-POLICY.md](CODE-SIGNING-POLICY.md).

**Administrators.** Anyone or anything with administrator rights can change the approval in HKLM, the program
files and everything else. They are outside this model.

### Not verified on every PC

What the automated tests prove, and what they do not:

- **Launches from an administrator session: only the refusal half is tested.** GitHub's hosted Windows runners
  (Windows Server 2022 and 2025 x64, Windows 11 ARM64) sign in as the built-in Administrator. UAC is on there, but
  the account has no normal-rights token, so Explorer itself runs elevated and the app is always in the "everything
  already runs as administrator" situation. On those runners the tests check that a launch through Explorer is
  refused and starts nothing when Explorer is elevated (or, on the ARM64 runner, has no desktop registered), and
  that the app then launches directly and says so. The positive case, **a program started from an administrator
  session on an account with UAC on does not get administrator rights**, has never been observed on real Windows.
  The Core decision rules for it (which situation, which launch method, which restart method) are unit-tested. It
  must be checked by hand before a release: item 0.1 of [MANUAL-TEST-PLAN.md](MANUAL-TEST-PLAN.md).
- **The elevated *Restart* from a changeable folder** (point 6 above): whether Windows shows a UAC prompt is not
  verified.
- **ARM64 hardware**, **real multi-monitor and mixed-DPI behaviour**, and **the lock screen with the pointer parked
  in the top-left corner** have not been verified on real hardware. The settings-folder checks of point 7 are
  tested on Windows with junctions, symbolic links and hard links created by the tests, but not with Redirection
  Guard against a junction planted by a separate normal-rights program.
- Starting programs without administrator rights depends on Explorer being the running desktop shell. With a
  replacement shell, or none, every launch from an elevated session on a UAC account fails (closed, not elevated).

All of these are in [MANUAL-TEST-PLAN.md](MANUAL-TEST-PLAN.md).

## Warnings the app shows, and when

The text of every warning is generated in one place, `Core/Security/SecurityPosture.cs`, from the current
settings and facts, and is covered by unit tests.

### When you turn something on

| You turn on | Dialog | Items shown |
|---|---|---|
| *Always run as administrator* (Settings, Advanced tab) | *Before WinXCornersPlus runs as administrator* | Hotkeys can control elevated windows; Programs it opens do not get admin rights; Setting changes need your approval; **Program files can be replaced** (only if the program folder is not admin-only); A UAC prompt at every sign-in |
| *Restart as administrator…* (tray menu) | *Before WinXCornersPlus runs as administrator*, with *Continue to the Windows prompt* | The same, without "A UAC prompt at every sign-in" |
| *Enable Custom Commands* | *Before you turn on custom commands* | Custom commands are on: how many are set up; a command runs as soon as the pointer touches its corner; any program running as you can change what a command runs |
| *Always hide tray icon* | *Before you hide the tray icon* | Tray icon is hidden: start the app again to get Settings back; stronger wording when *Run at startup* is also on |
| *Hide the program's window* (a command slot) | *Before this command runs without a window* | Command runs with no visible window: end it in Task Manager |

*Cancel* (the default button) turns the switch back off. Loading the Settings window never prompts.

In an administrator session on an account where everything already runs as administrator (UAC off, or the
built-in Administrator account), the item about programs it opens is replaced by the warning **Programs it opens get
administrator rights**. On such an account the app never runs with normal rights, so the dialogs shown before
elevating, which a normal-rights session shows, always use the "do not get admin rights" wording.

### At start

| Situation | What you see |
|---|---|
| Normal start, *Always run as administrator* on, and no recorded approval that matches the current settings exactly | *Settings ask WinXCornersPlus to run as administrator* with the elevation items above; *Continue to the Windows prompt* or *Cancel* |
| Elevated start, first time | *Approve what the administrator session will run*: what will run, the elevation items, and why you are asked |
| Elevated start, settings changed or approval unreadable | *Settings changed since you last approved them*: what changed, what will run, the elevation items |
| Approval could not be stored | A message that you will be asked again next time; the session continues |
| Second start, running copy does not answer | *WinXCornersPlus appears to be running but did not respond*, with what to do |
| Settings file damaged, unreadable, or (elevated) redirected | A tray notification and a Log entry: defaults are in use, and whether the file was kept aside or left untouched |
| Settings migrated from 1.4.0 | A tray notification with the new path |
| *Run at startup* starts another copy, or its entry is not in quotes | A tray notification: turn *Run at startup* off and on again |

Tray notifications are not shown while the tray icon is hidden; the Log window always has them.

### On the Security & privacy page

The page (tray menu, *Security & privacy…*) lists, most serious first, every item that is true right now:

| Item | Level | Shown when |
|---|---|---|
| Administrator rights from a changeable folder | Warning | Running elevated and the program folder is not confirmed admin-only |
| Programs it opens get administrator rights | Warning | Running elevated on an account where everything runs as administrator |
| Running as administrator | Caution | Running elevated |
| Always runs as administrator | Warning if the folder is not admin-only, otherwise Caution | The switch is on |
| Custom commands are on | Caution | The switch is on |
| Command runs with no visible window | Caution | Custom commands are on and a valid program command has *Hide the program's window* on |
| Tray icon is hidden | Caution with *Run at startup*, otherwise Info | *Always hide tray icon* is on |
| Starts when you sign in | Info | *Run at startup* is on |

Below the list it states facts: what the app watches, that it makes no network connections, where the settings
file is, whether it has an autostart entry, whether it is elevated and, if so, whether programs it opens get
administrator rights, and that the build is not code-signed.
