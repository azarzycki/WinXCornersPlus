# WinXCornersPlus

WinXCornersPlus is a lightweight utility for Windows 10 and Windows 11 that enhances your desktop experience by allowing you to assign custom actions triggered when you hover your mouse cursor over the corners of your screen. Whether you're a power user, developer, or just someone who appreciates efficiency, WinXCornersPlus provides a seamless way to streamline your workflow.

- [Key Features](#features)
- [Installing](#install)
- [How To Use](#howto)
- [Custom Commands and Hotkeys](#howtohotkey)
- [Settings File](#settings)
- [Security and Privacy](#security)
- [Notes](#notes)
- [Building and Testing from Source](#building)
- [Credits](#credits)

## <a name="features"></a> Key Features <a href="#winxcornersplus"></a>

1. **Hot Corner Actions**: Choose from a variety of predefined actions for each corner:
    - **Action Center**: Open the Action Center.
    - **File Explorer**: Open File Explorer window.
    - **Hide Other Windows**: Minimize all windows and keep the currently focused window in view.
    - **Lock Screen**: Lock your screen.
    - **Notification Center**: Open the Notification Center.
    - **System Settings**: Open System Settings.
    - **Show All Windows**: Activate Windows Task View to manage your open applications.
    - **Show Desktop**: Minimize all windows to show the desktop.
    - **Start Menu**: Open the Start Menu.
    - **Start Screen Saver**: Trigger your screen saver for privacy or energy-saving purposes.
    - **Task Manager**: Open Task Manager.
    - **Turn Display Off**: Turn off your display when not in use.
    - **Custom Actions**: Open other programs, documents or web pages with optional command line parameters, or send custom hotkeys (sequences of key presses, holds and releases), optionally depending on which window is open.

2. **Customization**: Tailor WinXCornersPlus to your preferences:
    - Assign specific actions to individual screen corners.
    - Set an activation delay (0.25 to 1.5 seconds) for all corners or for each corner separately. Without a delay, a corner fires as soon as the pointer reaches it.
    - Enable or disable application startup with Windows.
    - Set flyout animation direction.
    - Hide the tray icon persistently or run the application with elevated privileges.
    - Suppress actions while in full-screen mode.
    - Trigger hot corners only while holding one of the modifier keys - control, shift or alt.

3. **System Tray Integration**: WinXCornersPlus runs discreetly in the system tray, ensuring it doesn't clutter your desktop or Taskbar.

4. **Unobtrusive**: Its usage won't interfere with your common tasks, unless you decide to do so.
    - It won't trigger actions while dragging content with your mouse (left, middle or right button held).
    - It won't trigger while using a **Full Screen application**, like games or media, for instance.
    - It won't trigger a corner the pointer is already resting in when the app starts, settings change or you unlock the PC: move out of the corner and back in.
    - It pauses while the PC is locked, while a UAC prompt is shown, and while a remote session is disconnected.
    - You can disable it temporarily from the tray icon menu (*Disable hot corners*).
    - You can hide it from the taskbar tray while keeping it running in the background.

5. **Visible Countdown Counter**: Helps you, visually, to know if a corner action is about to be triggered (shown when a delay is set).

## <a name="install"></a> Installing <a href="#winxcornersplus"></a>

Download WinXCornersPlus from the [Releases page](https://github.com/Asteski/WinXCornersPlus/releases). Builds are available for x64 and ARM64 PCs:

- **Installer (recommended)**: an MSI that installs the app for all users of the PC, in a folder that only administrators can change. This matters if you ever run WinXCornersPlus as administrator (see [Security and Privacy](#security)).
- **Portable zip**: unzip anywhere and run `WinXCornersPlus.exe`. Fine for normal use; do not use a portable copy in a folder you can write to for elevated mode.

WinXCornersPlus needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) for your PC's architecture.

The builds are not code-signed yet, so Windows SmartScreen and the UAC prompt show "Unknown publisher". Step-by-step instructions are in [docs/INSTALLING.md](docs/INSTALLING.md).

Upgrading from 1.4.0 or earlier? Read [docs/UPGRADING.md](docs/UPGRADING.md): your settings move to a new place and a few behaviours changed. All changes are listed in [CHANGELOG.md](CHANGELOG.md).

## <a name="howto"></a> How To Use  <a href="#WinXCornersPlus"></a>
1. Launch WinXCornersPlus popup window by left-clicking the system tray icon.
2. Click a corner in the popup window and choose its action.
3. Hover your mouse cursor over a corner to trigger the assigned action. Every monitor's corners work: the corners of the monitor the pointer is on.
4. Right click the WinXCornersPlus tray icon and select *Settings…* to configure more advanced settings (delays, startup, modifier key, custom commands).
5. Right click the tray icon and select *Security & privacy…* to see what the app is doing on your PC and anything that needs your attention.
6. *Log window* in the same menu shows what the app did and why (for example, which action ran, or why a corner did not fire).

If you hid the tray icon, start WinXCornersPlus again (for example from the Start menu): the running copy opens its Settings window and shows its tray icon again until it exits. A second copy never keeps running. If the running copy does not answer, you see *WinXCornersPlus appears to be running but did not respond*; end `WinXCornersPlus.exe` in Task Manager (or sign out and in) and start it again.

## <a name="howtohotkey"></a> Custom Commands and Hotkeys  <a href="#WinXCornersPlus"></a>

Turn on *Enable Custom Commands* at the bottom of the Settings window. Each of the four slots (*Command 1* to *Command 4*) has a name, a command, optional parameters, and a *Hide the program's window* switch. Once custom commands are on, the slots appear at the end of each corner's menu in the popup window.

A command is one of three things:

| Command starts with | What it does | Example |
|---|---|---|
| anything else | Opens a program, document, folder or web address. *Parameters* are passed to the program. | `notepad.exe` |
| `!` | Sends a sequence of hotkeys. | `!_win+tab` |
| `#` or `@` | Sends hotkeys depending on which windows are open. | `#[Notepad,readme]:(escape)?(_win+d)` |

Programs start in your user profile folder, as they do from the Windows *Run* dialog. When WinXCornersPlus runs as administrator, they still start **without** administrator rights (except on an account where everything runs as administrator, see [Security and Privacy](#security)); a program that needs them shows its own UAC prompt.

When you click *OK* or *Apply*, Settings checks every command in full. If one is invalid, nothing is applied: a message says what is wrong and where (key number and column) and the command is selected for you to fix. A command that is invalid in the settings file does nothing when its corner is reached, and the reason is written to the Log window; no part of it is sent.

### Hotkey sequences

The hotkeys will be as follows:
`_control` or `control` or `control_` where `_` means hold or release (prefixed, appended) and without it, a full key press. Keys are separated by `+`. This will be useful if you have a sequence of hotkeys to do, like `!_control+k+control_+_control+_b` for VSCode for instance, that will do a `ctrl+k` then `ctrl+b` to toggle the sidebar.

- `_key` holds a key down, `key_` releases it, `key` presses and releases it. A token cannot be both (`_key_` is an error).
- Any key the sequence holds and does not release is released at the end, so a sequence cannot leave a key stuck down.
- Modifier keys you are physically holding (for example the modifier that arms the corner) are released before the sequence runs, so they do not combine with it. They are not pressed again afterwards.
- Key names ignore case. Letters `a`-`z` and digits `0`-`9` are single characters. Other names include `control` (`ctrl`), `shift`, `alt` (`menu`), `win` (`lwin`, `windows`), `rwin`, `lcontrol`/`rcontrol` (`lctrl`/`rctrl`), `lshift`/`rshift`, `lmenu`/`rmenu` (`lalt`/`ralt`), `escape` (`esc`), `return` (`enter`), `tab`, `space`, `back` (`backspace`), `delete` (`del`), `insert` (`ins`), `home`, `end`, `prior` (`pgup`, `pageup`), `next` (`pgdn`, `pagedown`), `left`, `up`, `right`, `down`, `f1`-`f24`, `numpad0`-`numpad9`, `capital` (`capslock`), `snapshot` (`printscreen`), `apps`, `volume_mute`, `volume_down`, `volume_up`, `media_play_pause`, `media_next_track`, `media_prev_track`, and the other Windows virtual-key names (`oem_plus`, `browser_back`, ...).
- Mouse buttons (`lbutton`, `rbutton`, `mbutton`, `xbutton1`, `xbutton2`) are not accepted: hotkeys can only send keyboard keys.
- A sequence has at most 64 keys.

### Conditional hotkeys

There is more, it will check for windows on foreground/currently focused, or globally, whether by only its classname or with titlebar text too. The conditional pseudo script will be as follows:
```
! = follows sequence of hotkeys as mentioned above
# = follows [classname,title] where title is optional, to match with the current focused window
@ = follows [classname,title] where title is optional, to match with any opened window
```

#### Rule:

`#[classname,title]:(sequence of hotkeys)?(optional sequence of hotkeys in case condition is not met)`

- The hotkeys inside the parentheses are written without `!`, and cannot contain parentheses.
- A condition can name a class, a title, or both: `#[class]`, `#[class,title]`, `#[,title]`. The title runs to the closing `]` and may contain commas.
- `#` matches when the focused window's class is the given one (ignoring case) and its title **contains** the given text (ignoring case).
- `@` matches when any top-level window has exactly that class and exactly that title (Windows' `FindWindow`).
- Several conditions in a row (`#[a]#[b]:(...)`) mean "any of them".
- Without the `?(...)` part, nothing happens when no condition matches. Nothing may follow the last `)`.

For instance the following will check if current window is VSCode's (VSCode, like Chrome and other Chromium-based apps, uses the window class `Chrome_WidgetWin_1`) and will invoke `ctrl+k` `ctrl+b` sequence of hotkeys, other wise if not on VSCode, just invoke the Start Menu.

`#[Chrome_WidgetWin_1]:(_control+k+control_+_control+_b)?(win)`

E.g. `#[conditional match]:(hotkey if match)?(hotkey if not)`

Another example for Windows 10:

This will check if Alt+Tab's window is visible, if so, it will hide it, otherwise it will invoke it, as a faster alternative to Task View.
`#[MultitaskingViewFrame]:(escape)?(_control+_alt+tab)`

## <a name="settings"></a> Settings File <a href="#winxcornersplus"></a>

Settings are stored per user in `%APPDATA%\WinXCornersPlus\settings.json`. *Security & privacy…* shows the exact path and has an *Open settings folder* button.

- Version 1.4.0 and earlier kept `settings.json` next to `WinXCornersPlus.exe`. On first start with normal rights, if there is no file in the new place, 1.5.0 copies that old file over and tells you. The old file is left as it was. Details in [docs/UPGRADING.md](docs/UPGRADING.md).
- Values that make no sense (an unknown action, an out-of-range delay, invisible control characters in a command) are corrected when the file is read, and each correction is written to the Log window.
- A file that is not valid settings is renamed to `settings.corrupt-<date>-<time>.json` and defaults are used; it is never silently overwritten. A file that cannot be read at all is left alone and nothing is saved over it.
- Saving writes a temporary file and then replaces `settings.json`, so a crash or power cut leaves the old or the new file, not a broken one. (Running as administrator, the app writes the file in place instead; see below.)
- Exit WinXCornersPlus before editing `settings.json` by hand: the running app does not reload the file and saves its own settings over it on the next change.
- Unexpected errors are appended to `%LOCALAPPDATA%\WinXCornersPlus\crash.log` (not while running as administrator). An error in a window is reported once in a notification and the app keeps running.
- While running as administrator, the app does not move, back up or migrate settings files and will not read or write them through a junction, symbolic link or hard link. If the settings file is damaged or redirected, it uses default settings and saves nothing; fix or delete the file while the app runs with normal rights.

## <a name="security"></a> Security and Privacy <a href="#winxcornersplus"></a>

What WinXCornersPlus watches and does:

- It watches only the pointer position to detect corners, and whether a mouse button or the chosen modifier key is held. It never records keystrokes and installs no keyboard or mouse hooks.
- To skip full-screen apps and to evaluate `#`/`@` conditions, it reads the class name, title and size of the foreground window or looks up windows by class and title. It does not store them.
- It makes no network connections. The *Check for updates* link only opens the Releases page in your browser.
- It writes its settings file, a crash log, and (only while *Run at startup* is on) one autostart entry under your user account (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
- When it runs as administrator, it also keeps a record of the settings you approved under `HKEY_LOCAL_MACHINE\SOFTWARE\WinXCornersPlus`.

**Elevated mode** (*Restart as administrator…* in the tray menu, or *Always run as administrator* in Settings) exists for one reason: Windows does not let a normal program send keys to a window that runs as administrator. Running WinXCornersPlus as administrator lets its corner actions and hotkeys reach those windows. It does not give anything else administrator rights:

- Programs, documents and links it opens are started by the Windows desktop (Explorer) without administrator rights. The app first checks that Explorer itself is not running as administrator; if it cannot confirm that, nothing is started. (Exception: on an account where everything already runs as administrator, because UAC is off or it is the built-in Administrator account, there is no lower level, so programs it opens **do** get administrator rights, and the app says so.) This has been tested automatically only for the refusal case; see [docs/SECURITY-MODEL.md](docs/SECURITY-MODEL.md#not-verified-on-every-pc).
- Before an administrator session uses its settings for the first time, or after they have changed, it shows you what will run and asks you to approve. Any program running under your account can edit `settings.json`; it cannot forge your approval. The approval also covers *Always hide tray icon* and *Always run as administrator*; a program that switches the latter on cannot make the next start raise a UAC prompt without an explanation first.
- If its own files are in a folder that programs running under your account can change (for example a portable copy in Downloads), it warns you: a program could replace them, and approving the UAC prompt would then give that program administrator rights. Use the installer if you want elevated mode.
- While running as administrator it turns on Windows' Redirection Guard, writes no crash log, and handles the settings folder as described in [Settings File](#settings).

When you turn on a risky option (run as administrator, custom commands, hidden command windows, hidden tray icon) the app explains the risk before it takes effect, and *Security & privacy…* lists everything that currently needs your attention.

The full threat model, including what is **not** protected, is in [docs/SECURITY-MODEL.md](docs/SECURITY-MODEL.md). To report a vulnerability, see [SECURITY.md](SECURITY.md); please do not open a public issue for it.

## <a name="notes"></a> Notes <a href="#winxcornersplus"></a>

- Hot corners work on every monitor, but multi-monitor setups (especially with different display scaling per monitor) have had less testing than a single monitor. Please report problems.
- Triggering hotkeys over software that runs with elevated privileges won't work unless WinXCornersPlus runs as administrator too, due to the nature of separate privileges. Use *Restart as administrator…* in the tray context menu for one session, or *Always run as administrator* in the Advanced tab of Settings to do it at every start. Read [Security and Privacy](#security) first, and install the app with the installer before you turn this on.
- To start once without administrator rights while *Always run as administrator* is on, run `WinXCornersPlus.exe --no-elevate`. `--show-settings` opens the Settings window right after start.
- If you encounter other unknown issues, please fill a bug report at the [GitHub issues page](https://github.com/Asteski/WinXCornersPlus/issues). The text of the Log window and `crash.log` help a lot.

## <a name="building"></a> Building and Testing from Source <a href="#winxcornersplus"></a>

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The app builds on Windows; the core library and its tests also build and run on Linux and macOS.

```
git clone https://github.com/Asteski/WinXCornersPlus.git
cd WinXCornersPlus
dotnet build src/WinXCorners.App/WinXCorners.App.csproj -c Release
dotnet test tests/WinXCorners.Core.Tests/WinXCorners.Core.Tests.csproj
```

The program is then in `src/WinXCorners.App/bin/Release/net10.0-windows/WinXCornersPlus.exe`. The Windows platform tests and the UI tests, and how the code is organised, are described in [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Releases are built by GitHub Actions, see [docs/RELEASING.md](docs/RELEASING.md).

Contributions are welcome: please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

## <a name="credits"></a> Credits

Original author and developer:

* Victor Alberto Gil [@vhanla](https://apps.codigobit.info/) (Author, Developer)
