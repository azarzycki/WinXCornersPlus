# Changelog

All notable changes to WinXCornersPlus are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Version numbers follow
[Semantic Versioning](https://semver.org/) and are set by the release workflow (see
[docs/RELEASING.md](docs/RELEASING.md)).

## [1.5.0] - Unreleased

Upgrading from 1.4.0? Read [docs/UPGRADING.md](docs/UPGRADING.md) first.

### Security

- Programs, documents, folders and links opened while the app runs as administrator now start without administrator rights; if that is not possible, nothing is started.
- Before each such launch the app checks that the Windows desktop (Explorer) it starts programs from is not itself running as administrator, and starts nothing if it is or if this cannot be checked.
- On an account where everything already runs as administrator (UAC off, or the built-in Administrator account), the risk text now says that programs the app starts will have administrator rights.
- An administrator session now shows exactly what it will run and asks for your approval the first time and whenever the relevant settings have changed, because any program running as you can edit `settings.json`.
- The approval is stored under `HKEY_LOCAL_MACHINE`, where programs without administrator rights cannot change it.
- *Always run as administrator* in `settings.json` no longer raises a UAC prompt without explanation unless you approved exactly the current settings, including that switch, before.
- The approval covers *Always run as administrator* and *Always hide tray icon* as well as the corner actions and commands.
- The app warns you, before elevating and while elevated, when its folder, its program files or any folder above it can be changed by programs running as you.
- In an administrator session started from such a folder, *Restart* starts the app again without administrator rights, and the tray menu item says so.
- If *Always run as administrator* is on, that new copy asks for administrator rights, and Windows shows its prompt.
- If the app cannot be started without administrator rights, *Restart* starts nothing and tells you why.
- An administrator session no longer moves, backs up or migrates settings files, and writes no crash log.
- An administrator session refuses to read or write its settings through a junction, symbolic link or hard link, and turns on Windows' Redirection Guard.
- An administrator session leaves a damaged or redirected settings file untouched, runs on default settings and does not save over it.
- An administrator session writes no file before you have answered its approval dialog.
- File Explorer and Task Manager are started from their full paths in the Windows folder instead of by name.
- The app's current folder is now System32, so Windows does not search the folder the app was started from for programs or DLLs.
- Windows libraries the app imports are loaded from System32 only.
- The autostart entry is always written in quotes.
- The About and update links open only `https://` addresses and also start without administrator rights.
- Starting the app a second time now opens the running copy's Settings window and shows its tray icon, so a hidden app can always be found.
- If the running copy does not respond to a second start, a message says so.
- Undocumented Windows dark-mode functions are only called on Windows builds that have them.
- Invisible control and text-direction characters are removed from command names, commands and parameters when settings are read.
- Turning on *Always run as administrator*, *Enable Custom Commands*, *Always hide tray icon* or *Hide the program's window* now first explains the risk.

### Fixed

- *Exit* now works after the Log window has been opened; before, the app kept running invisibly.
- The app no longer crashes at startup when its folder is not writable.
- A damaged or hand-edited `settings.json` no longer crashes the app or is silently replaced with defaults; bad values are corrected and logged, and a file that is not valid settings is kept as `settings.corrupt-<date>-<time>.json` (with normal rights).
- Settings are saved atomically with normal rights, so a crash or power cut cannot leave a half-written file.
- *Restart* (formerly *Reload*) saves your settings before restarting.
- Enabling or disabling hot corners from the tray menu is now saved.
- Clicking OK in Settings no longer undoes corner changes made in the popup while Settings was open.
- Modifier keys no longer stay stuck after a hotkey sequence.
- Arrow keys, Home, End, Page Up/Down, Insert, Delete, right Ctrl/Alt and similar keys are now sent as the real keys (they could arrive as number pad keys before).
- A typo in a hotkey no longer sends the first half of the sequence.
- A conditional command whose condition cannot be read no longer runs its "otherwise" branch.
- `?(...)` inside a window title is no longer mistaken for the "otherwise" branch.
- `@[,title]` (title without class) now finds the window.
- Slow actions no longer run on the input path, where Windows could drop the app's mouse handling.
- Restarting, or restarting as administrator, no longer races the old copy for the single-instance check.
- Starting the app normally while an administrator copy runs no longer fails.
- A corner without a delay now fires on the first move into it.
- A corner assigned to a disabled, empty or invalid custom command no longer shows a countdown.
- The popup animation no longer keeps the CPU busy.
- An `&` in a custom command's name is now shown in the popup instead of disappearing.
- The countdown text is black or white, whichever contrasts more with your accent colour.
- Buttons and drop-down arrows in Settings follow dark mode, and the default button is outlined.
- Links in the About window are readable in dark mode, and its text is no longer cut off.
- Settings no longer opens with the focus on *Check for updates*, so pressing Enter no longer opens the browser.
- Several controls now have names that screen readers can announce.
- At sign-out, shutdown, or when the installer needs to replace the program, the app saves its settings and exits on its own.
- The Log window no longer grows without limit.

### Changed

- Settings are now stored in `%APPDATA%\WinXCornersPlus\settings.json`; an existing 1.4.0 file next to the program is copied there on the first normal-rights start.
- The autostart entry is no longer rewritten at every start; a notification tells you when it starts a different copy or is not in quotes.
- Custom programs start in your user profile folder, like the Windows *Run* dialog.
- Hotkey commands are checked in full; Settings shows the error and does not apply invalid commands.
- Hot corners fire only when the pointer moves into a corner, not when it is already there at start, after a settings change or after unlocking.
- Hot corners pause while the PC is locked, while a UAC prompt is shown and while a remote session is disconnected, and fire again only after the pointer has left the corners.
- Modifier keys you hold are released before a hotkey sequence and are not pressed again afterwards.
- Tray menu entries were renamed: *Restart as administrator…*, *Restart*, *Hide tray icon*, *Settings…*.

### Added

- A *Security & privacy…* page (tray menu) that lists what needs your attention and what the app does.
- A *Hide the program's window* switch for each custom command (before, it could only be set in the file).
- Key name aliases such as `ctrl`, `esc`, `enter`, `del`, `pgup`, `pgdn`, `capslock` and `backspace`.
- `--no-elevate` and `--show-settings` command-line switches.
- A crash log at `%LOCALAPPDATA%\WinXCornersPlus\crash.log` (not written while running as administrator); an error in a window is reported once and the app keeps running.
- Installers (MSI) and portable zips for x64 and ARM64, see [docs/INSTALLING.md](docs/INSTALLING.md).
- Automated tests on Windows x64, Windows ARM64 and Linux, run for every pull request.

### Removed

- Mouse button names (`lbutton`, `rbutton`, `mbutton`, `xbutton1`, `xbutton2`) in hotkeys; they never sent anything.
- The low-level mouse hook; raw input and a timer detect the corners.
- The "Beta build" label in the Settings window.

## [1.4.0]

Last release before this changelog was started. See the
[releases page](https://github.com/Asteski/WinXCornersPlus/releases).
