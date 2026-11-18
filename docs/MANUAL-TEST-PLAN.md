# Manual test plan

Run this on a real Windows 11 PC before a release. It covers what the automated tests cannot prove: how the
corners feel with a real mouse and real monitors, games, the UAC prompt, the real desktop shell, upgrades, and
ARM64 hardware. Each item has steps and the expected result. Note the build, the PC and anything that differs.

## Before you start

- Use the release candidate's installer and portable zip from the release workflow's artifacts.
- Keep the Log window open (tray menu, *Log window*); most results can be confirmed there.
- **How to see whether a process runs as administrator:** open Task Manager, *Details* tab, right-click a
  column header, *Select columns*, tick **Elevated**. The column shows *Yes* or *No* for each process.
- To start clean, exit the app and delete `%APPDATA%\WinXCornersPlus` and, from an administrator prompt,
  `reg delete HKLM\SOFTWARE\WinXCornersPlus /f`.

Record for the run: Windows version and build (`winver`), x64 or ARM64, monitor layout and scale of each monitor,
app version (About window), installer or portable.

## 0. Run first: what CI cannot prove

GitHub's hosted Windows runners sign in as the built-in Administrator, so every automated test runs where
everything is already elevated. The most important security promise, that an administrator session starts
programs **without** administrator rights, has therefore never been observed on real Windows. Do not release
without 0.1.

| # | Steps | Expected |
|---|---|---|
| 0.1 | Use an ordinary Windows 11 account that is a member of Administrators (**not** the built-in Administrator account), with UAC on at its default level. Install the release candidate with the MSI, so the app is under `C:\Program Files\WinXCornersPlus`. In Settings set *Command 1* to `notepad.exe`, turn on *Enable Custom Commands* and assign *Command 1* to a corner. Tray menu, *Restart as administrator…*, *Continue to the Windows prompt*, *Yes*, then *Approve and continue*. Fire the *Command 1* corner. Then open About and click a link (or *Check for updates* in Settings). Open Task Manager, *Details* tab, right-click a column header, *Select columns*, tick **Elevated**. | `WinXCornersPlus.exe`: Elevated **Yes**. `notepad.exe` and the browser process started by the link: Elevated **No**. The Log window says "without administrator rights" for each launch. If a launch fails instead, nothing must have started; note the Log text. Any **Yes** for a launched program is a security bug: stop the release. |
| 0.2 | Optional, if you have one: repeat 0.1 from a standard (non-administrator) account, entering an administrator's password at the UAC prompt. | Note the result: the elevated session runs as the other account (its own settings and approval), and launched programs should run as the standard user with Elevated **No**. |

Also high priority, because no automated test covers them on real hardware:

- ARM64 hardware: [section 8](#8-arm64-hardware).
- Two monitors with different scaling: [section 2](#2-two-monitors-and-mixed-scaling).
- Lock screen with the pointer parked in the top-left corner: item 1.10.
- A junction planted in the settings folder before an elevated start: item 5.11.
- Wording on an account where UAC is off (or the built-in Administrator): item 4.13.
- Elevated *Restart* from a copy in Downloads: item 4.12.

## 1. Hot corners on one monitor

| # | Steps | Expected |
|---|---|---|
| 1.1 | Default settings. Move the pointer into each corner in turn. | Each corner runs its action once, with no delay. Nothing fires again while the pointer stays in the corner. |
| 1.2 | After 1.1, move the pointer a little way out (less than about 30 pixels) and back into the same corner. | It does not fire again. Moving clearly out and back in fires it again. |
| 1.3 | Settings, *Delays* tab: enable the global delay at 1.00 and tick *Show Countdown*. Rest in a corner; then leave a corner before the countdown ends. | A countdown appears in that corner and the action runs when it ends. Leaving early cancels it and hides the countdown. |
| 1.4 | Untick the global delay; give one corner its own delay (0.25) and leave the others without. | Only that corner waits; the others fire at once. |
| 1.5 | Set *Hot Corners modifier key* to Ctrl. Enter a corner without Ctrl, then press Ctrl while still in it. Try Shift and Alt the same way. | Nothing fires without the key. Pressing it in the corner starts the action (after the delay, if any). Left and right Ctrl both work. |
| 1.6 | Drag a window title bar or select text with the left button into a corner. Repeat with the right and middle buttons. | No corner fires while a button is held. |
| 1.7 | Park the pointer in a corner. Start the app (or change any setting). | Nothing fires until the pointer leaves the corner and comes back. |
| 1.8 | Tray menu, *Disable hot corners*. Visit the corners. Exit and start the app. | Nothing fires; the tray icon shows the disabled state. The state is kept after the restart. |
| 1.9 | Open the popup (left-click the tray icon) and move into a corner while it or a corner menu is open. | Nothing fires while the popup or its menu is open. |
| 1.10 | Set the top-left corner to a visible action such as *File Explorer*. Rest the pointer in the top-left corner and press Win+L. Wait on the lock screen, then sign back in without moving the mouse. Also trigger a UAC prompt (for example run `regedit`) and move into a corner while the prompt is shown. | Nothing runs while locked or while the UAC prompt is shown (in particular, no File Explorer window appears after unlocking). The Log window says "Hot corners paused" and later "Hot corners resumed". After unlocking, no corner fires until the pointer has been outside every corner and comes back. |
| 1.11 | Connect to the PC by Remote Desktop, then disconnect without signing out. Reconnect. | Nothing runs while disconnected; after reconnecting corners work once the pointer has left any corner. |
| 1.12 | Try each built-in action once. | Each does what its name says. Note any that do nothing or something else on this Windows build. |

## 2. Two monitors and mixed scaling

Use two monitors side by side, one at 100% and one at 150% or more (Settings > System > Display > Scale).

| # | Steps | Expected |
|---|---|---|
| 2.1 | Visit the four outer corners of each monitor. | Each fires the action configured for that corner. |
| 2.2 | Visit the corners where the monitors meet (top and bottom of the shared edge). | Note what happens. Where the pointer can rest, the corner fires; where it passes straight onto the other monitor, it may not. No corner fires twice or fires the wrong action. |
| 2.3 | Set a delay with *Show Countdown* and rest in a corner of each monitor. | The countdown appears in the corner being used, at a readable size on both monitors. |
| 2.4 | Move the taskbar's monitor (or make the scaled monitor the main display). Open the popup, the Settings, About, Log and Security windows on each monitor; drag Settings from one monitor to the other and back, several times. | The popup opens next to the tray. Text is sharp and not cut off on either monitor, before and after dragging. Settings keeps the same size relative to its text after each move (watch for it growing or shrinking each time: it is scaled on `WM_DPICHANGED` and also uses WinForms' DPI scaling, which the single-monitor CI runners cannot check). |
| 2.5 | Change the scale or the resolution of a monitor while the app runs. Then visit the corners. | Corners still work on the new layout. |

## 3. Full-screen applications

| # | Steps | Expected |
|---|---|---|
| 3.1 | *Do nothing on Full Screen* on (default). Play a video full-screen in a browser (F11 or the player's full-screen button) and visit a corner. | Nothing fires. The Log window says once that it was not run because a full-screen window is in front. No countdown is shown. |
| 3.2 | Same with a game in exclusive full-screen and in borderless full-screen mode. | Nothing fires; the game does not lose focus or stutter when the pointer reaches a corner. |
| 3.3 | Maximize a normal window (title bar visible) and visit a corner. | The corner fires: a maximized window is not full screen. |
| 3.4 | Turn *Do nothing on Full Screen* off and repeat 3.1. | The corner fires over the full-screen video. |
| 3.5 | With the desktop in front (click the wallpaper) and with Task View open, visit a corner. | Corners fire; the desktop and shell views are not treated as full screen. |

## 4. Elevated mode: install location, warnings, approval, de-elevated launches

Prepare two copies: **A** installed with the MSI (Program Files) and **B** from the portable zip, unzipped into
`Downloads`. Run only one at a time. Set custom command 1 to `cmd.exe`, enable custom commands and assign
*Command 1* to a corner.

| # | Steps | Expected |
|---|---|---|
| 4.1 | Start **B**. Tray menu, *Restart as administrator…*. | A dialog *Before WinXCornersPlus runs as administrator* lists the risks, including **Program files can be replaced**. *Cancel* (default) does nothing. |
| 4.2 | Repeat 4.1 and click *Continue to the Windows prompt*. | The UAC prompt says "Unknown publisher". After *Yes*, the approval dialog *Approve what the administrator session will run* lists each corner, custom commands on, command 1 as `cmd.exe` in quotes. |
| 4.3 | Approve. Open *Security & privacy…*. | The page lists **Administrator rights from a changeable folder** (warning) and *Running as administrator*. The tray tooltip says "(Administrator)". |
| 4.4 | Fire the corner with *Command 1*. Click *Check for updates* in Settings and a link in About. Check Task Manager's **Elevated** column. | `WinXCornersPlus.exe`: *Yes*. `cmd.exe` and the browser processes started now: **No**. The Log window says "without administrator rights" for each launch. |
| 4.5 | Set command 2 to `!a+b+c` and assign it to a corner. With the app elevated, open *Terminal (Admin)*, click into it and fire that corner. Repeat with the app running with normal rights. | Elevated: `abc` is typed into the administrator terminal. Normal rights: nothing is typed (Windows blocks it). |
| 4.6 | Exit. Start **A**, *Restart as administrator…*. | The dialog does **not** contain *Program files can be replaced*. After approval, the Security page has no "changeable folder" warning. 4.4 gives the same results. |
| 4.7 | In **A**, Settings, Advanced tab: tick *Always run as administrator*. | The dialog lists the risks including *A UAC prompt at every sign-in*. *Cancel* unticks the box again. Tick again and confirm. |
| 4.8 | Turn on *Run at startup*, sign out and in. | A UAC prompt appears for WinXCornersPlus at sign-in. Approving starts it elevated with no approval dialog (settings unchanged). Declining leaves it running with normal rights. |
| 4.9 | Run `WinXCornersPlus.exe --no-elevate` after exiting. | It starts with normal rights, no UAC prompt. |
| 4.10 | With the approval dialog open (4.2), press Esc; repeat with the window's close button. | Each means "Don't run as administrator": the elevated copy exits and a normal-rights copy starts (Elevated = No). |
| 4.11 | Task Manager as a built-in corner action in an elevated session. | Note the result. Task Manager may raise itself to administrator for members of Administrators; that is Windows behaviour. |
| 4.12 | Copy **B** (Downloads), elevated and approved, *Always run as administrator* off: open the tray menu and click *Restart without administrator rights*. Then start **B** elevated again, turn on *Always run as administrator*, and click the same item. Then copy **A**, elevated and approved: tray menu, *Restart*. | **B**: the item reads *Restart without administrator rights*. The first time, the new copy runs with normal rights (Elevated = No) and no UAC prompt appears. The second time, a UAC prompt for WinXCornersPlus appears before the new copy runs elevated; declining leaves it running with normal rights. **A**: the item reads *Restart*, and it restarts elevated without a UAC prompt (the Log says `StartDirectly`). If copy **B** ever starts elevated without a UAC prompt, it is a security bug: stop the release. |
| 4.13 | On a test PC or VM, sign in as the built-in Administrator account, and separately on an administrator account with UAC turned off. Start copy **A** (it runs elevated at once). Read the approval dialog, then the Security page and its facts, then fire *Command 1*. Then start again and click *Don't run as administrator*. | The approval dialog and the Security page show the warning **Programs it opens get administrator rights**, and the facts say programs it opens get administrator rights too. The Log says the launch is "with administrator rights: this account has no other kind". `cmd.exe` shows Elevated = Yes. *Don't run as administrator* exits the app (the Log says it cannot be started without administrator rights on this account); it does not restart. |

## 5. Tampering with settings between elevated runs

Use copy **A** with an approval already recorded (4.6).

| # | Steps | Expected |
|---|---|---|
| 5.1 | Exit the app. With normal rights, edit `%APPDATA%\WinXCornersPlus\settings.json`: change command 1 to `powershell.exe` and its parameters to `-NoExit`. Start the app elevated. | Before the tray icon appears: *Settings changed since you last approved them*, with a *What changed* section naming command 1's old and new program and parameters, then *What will run*. |
| 5.2 | Click *Don't run as administrator*. | The app restarts with normal rights (Elevated = No) and does not ask for UAC. |
| 5.3 | Repeat 5.1 and approve. Restart elevated again. | No dialog the second time. |
| 5.4 | Exit. Change only a delay and the animation direction in the file. Start elevated. | No dialog: these are not security-relevant. |
| 5.5 | Exit. Set `"AlwaysHideTrayIcon": true` in the file. Start elevated. | The dialog lists the tray icon change. |
| 5.6 | Exit. Put a right-to-left override or a newline (`‮`, `\n`) inside command 1's text in the file. Start elevated. | The characters are removed when the file is read (the Log window lists the correction); the dialog shows the cleaned value. |
| 5.7 | As administrator, open `regedit` at `HKLM\SOFTWARE\WinXCornersPlus\ElevatedApprovals`, change your value's text slightly. Start elevated. | The dialog asks again: it says the stored approval could not be read (or, if the edited text is still a valid approval, lists what differs). It never continues without asking. |
| 5.8 | Elevated session running: change command 1 in its own Settings window and click OK. Exit, start elevated. | No dialog: changes made in the elevated session's windows are approved automatically. |
| 5.9 | Exit. Write invalid JSON into `settings.json`. Start the app with normal rights. | Defaults are used; a balloon says the file was kept as `settings.corrupt-<date>-<time>.json`, and that file exists. |
| 5.10 | Exit. Write invalid JSON into `settings.json` again. Start the app elevated. Change a setting, exit. | The file is neither moved nor overwritten (same bytes and time stamp afterwards); no `settings.corrupt-*` file appears; a notification and the Log say defaults are in use, changes are not saved, and the file must be fixed or deleted after starting without administrator rights. No `crash.log` is written by the elevated session. |
| 5.11 | Exit. With normal rights, rename `%APPDATA%\WinXCornersPlus` and create a junction in its place pointing to another folder that contains a `settings.json` (`mklink /J "%APPDATA%\WinXCornersPlus" C:\SomeFolder`). Start the app elevated, change a setting, exit. Repeat with `settings.json` replaced by a symbolic link (`mklink`, from an administrator prompt or with Developer Mode on) and by a hard link (`mklink /H`). | Before the approval dialog nothing is created, changed or moved in the target folder. The notification says the settings folder or file points somewhere else and was not read or changed; defaults are in use and changes are not saved. The Log says Redirection Guard is on (or not available on this build) and why the location was refused. Afterwards the target files are unchanged. Remove the links afterwards. |
| 5.12 | Leave a 1.4.0 `settings.json` beside copy **B**'s exe, delete `%APPDATA%\WinXCornersPlus`, and start **B** elevated first. Then start it with normal rights. | Elevated: no migration, nothing copied. Normal rights: the file is migrated. |

## 6. Second launch and the hidden tray icon

| # | Steps | Expected |
|---|---|---|
| 6.1 | Settings: tick *Always hide tray icon* (confirm the dialog). | The tray icon disappears. |
| 6.2 | Start WinXCornersPlus again from the Start menu or Explorer. | The running copy opens its Settings window and its tray icon reappears. No second `WinXCornersPlus.exe` stays in Task Manager. No UAC prompt or other dialog. |
| 6.3 | Repeat 6.2 while the running copy is elevated and the second start is normal rights. | Same result; no error. |
| 6.4 | With *Always hide tray icon* off, choose *Hide tray icon* in the tray menu. Start the app a second time, then choose *Exit* in the tray menu and start it again. | The menu command hides the icon for this session only. The second start brings it back; after exit and start it is visible as usual. |
| 6.5 | Open the Log window, close it, then choose *Exit* in the tray menu. | The process ends within a few seconds (it used to keep running invisibly). |
| 6.6 | Exit the app. In a normal PowerShell window run `$m = New-Object System.Threading.Mutex($true, 'Local\WinXCornersPlus.SingleInstance')` and leave the window open. Start the app. Then close the PowerShell window and start the app again. | First start: a message *WinXCornersPlus appears to be running but did not respond*, and no copy keeps running. After the PowerShell window is closed, the app starts normally. |

## 7. Upgrade from 1.4.0

| # | Steps | Expected |
|---|---|---|
| 7.1 | Unzip 1.4.0 into a folder, run it, set non-default actions, a delay, a custom command, and *Run at startup*. Exit. Copy the 1.5.0 portable files over the 1.4.0 files in the same folder. Start it. | A balloon says settings were moved to `%APPDATA%\WinXCornersPlus\settings.json`. All values are the same as in 1.4.0. The old `settings.json` beside the exe is unchanged. |
| 7.2 | In 7.1, look at the custom command. | If it used a mouse button name or had a typo, Settings shows the error on OK and the corner does nothing (Log window explains). |
| 7.3 | Install 1.5.0 with the MSI instead, with the 1.4.0 copy still in its own folder and still registered to run at startup. Start 1.5.0. | Settings are **not** found (the old file is beside the old exe, not the new one); defaults are used. A balloon says *Run at startup* starts another copy. Following [UPGRADING.md](UPGRADING.md) (copy the file, turn *Run at startup* off and on) gives the old settings and a correct autostart entry. |
| 7.4 | 1.4.0 had *Always run as administrator* on. Start 1.5.0 normally. | The explanation dialog appears before any UAC prompt (no approval on record yet). |
| 7.5 | Uninstall 1.5.0 (MSI). | What is removed and what is kept matches [INSTALLING.md](INSTALLING.md). No `WinXCornersPlus.exe` is left running. |

## 8. ARM64 hardware

No CI runner could check that launches from an elevated session really start without administrator rights (see
section 0); on the ARM64 runner Explorer also had no desktop view registered. On a real ARM64 PC (for example a
Snapdragon laptop) with the ARM64 installer, on an ordinary administrator account with UAC on:

| # | Steps | Expected |
|---|---|---|
| 8.1 | Check Task Manager's *Architecture* column (Details tab) for `WinXCornersPlus.exe`. | ARM64, not emulated x64. |
| 8.2 | Run item 0.1, and section 4 items 4.2 to 4.4 and 4.10. | Same results as on x64; in particular launched programs show Elevated = **No**. If a launch fails instead, the Log window shows the reason and nothing is started elevated. |
| 8.3 | Run section 1 and section 6 quickly. | Same as on x64. |

## 9. Theme switching while running

| # | Steps | Expected |
|---|---|---|
| 9.1 | Open the popup, Settings, About, Log and Security windows. Switch Windows between light and dark mode (Settings > Personalization > Colors). | The tray icon switches to the matching variant. All open windows repaint in the new theme, including title bars where Windows supports it. Text stays readable. |
| 9.2 | Change the accent colour. Show the countdown. | The countdown stays readable. |
| 9.3 | Switch to a high-contrast theme and back. | No crash; windows stay usable. |

## 10. Sign-out and shutdown with windows open

| # | Steps | Expected |
|---|---|---|
| 10.1 | Open Settings, About, Log and Security. Change a corner action in the popup. Sign out right away. | Sign-out is not blocked; WinXCornersPlus is not listed as preventing it. After signing in, the changed action is still set. |
| 10.2 | Same, with Shut down and with Restart. | Same. |
| 10.3 | Elevated session, approval dialog open (do not answer), sign out. | Sign-out completes; nothing ran elevated. |
| 10.4 | After 10.1, check `%LOCALAPPDATA%\WinXCornersPlus\crash.log`. | No new entries. |

## After the run

File an issue for each unexpected result, with the item number, the Log window text and `crash.log`. Security
problems go through [SECURITY.md](../SECURITY.md), not public issues.
