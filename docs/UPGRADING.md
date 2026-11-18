# Upgrading from 1.4.0

Version 1.5.0 keeps your settings and works the way you are used to, but several things changed, mostly to
close security holes and fix bugs. This page lists what you will notice. Everything that changed is in
[CHANGELOG.md](../CHANGELOG.md).

## Your settings move

1.4.0 kept `settings.json` next to `WinXCornersPlus.exe`. 1.5.0 keeps it per user in

```
%APPDATA%\WinXCornersPlus\settings.json
```

(type that path into File Explorer's address bar, or use *Open settings folder* on the *Security & privacy…*
page).

**Automatic migration.** When 1.5.0 starts with normal rights (not as administrator) and there is no file in
the new place yet, it looks for
`settings.json` **in the folder 1.5.0 itself runs from**. If it finds one, it copies it to the new place and
shows a notification. The old file is left untouched; once the new file exists, the old one is not read again.

- **Portable zip, unzipped over your 1.4.0 folder:** migration happens on its own.
- **Installer, or a portable copy in a different folder:** 1.5.0 cannot see your old file and starts with
  defaults. To keep your settings: exit WinXCornersPlus, create the folder `%APPDATA%\WinXCornersPlus` if it does
  not exist, copy your old `settings.json` into it (replacing the one there, if any), and start the app again.

The file format did not change, so you can copy the file back to a 1.4.0 folder if you ever go back. 1.4.0 only
reads the file next to its exe; changes made in 1.5.0 are not there unless you copy them.

Values that 1.4.0 accepted but that make no sense (for example an unknown action or an empty command list) are
corrected when 1.5.0 reads the file, and each correction is listed in the Log window. A file that cannot be read
as settings is kept as `settings.corrupt-<date>-<time>.json` instead of being overwritten.

## "Run at startup" is no longer rewritten at every start

1.4.0 rewrote its autostart entry every time it started. 1.5.0 changes the entry only when you change *Run at
startup* in Settings. If the entry starts a different copy of WinXCornersPlus (for example your old 1.4.0 folder
after you installed 1.5.0 with the installer), 1.5.0 tells you in a notification. To fix it, untick *Run at
startup*, click Apply, tick it again and click OK.

## Elevated mode asks for your approval

If you use *Always run as administrator* or *Restart as administrator…*:

- The first time 1.5.0 runs as administrator, it shows **what the administrator session will run** (every
  corner, every custom command with its parameters, the modifier key, whether the tray icon is hidden, whether it
  always runs as administrator) and asks
  you to approve. It asks again whenever these settings have changed since your last approval, for example
  because another program edited `settings.json`. Changes you make in the elevated session's own windows do not
  need a second approval.
- *Don't run as administrator* restarts the app with normal rights. (On an account where everything runs as
  administrator, it just exits: there are no normal rights to restart with.)
- If *Always run as administrator* is on, 1.5.0 explains the risks **before** the UAC prompt and lets you
  cancel, unless you already approved exactly the current settings in an administrator session. The first start
  after upgrading always explains.
- If you used 1.4.0 always elevated, start 1.5.0 once with normal rights after upgrading, so it can migrate your
  settings (an administrator session never migrates, moves or backs up settings files). If a settings file is
  damaged, or the settings folder is a link to somewhere else, an administrator session leaves it alone, runs with
  default settings and saves nothing; fix or delete it from a normal-rights session.
- If the program files are in a folder that programs running as you can change (for example a portable copy in
  Downloads), you are warned that elevated mode is not safe there. Use the installer.

Details: [SECURITY-MODEL.md](SECURITY-MODEL.md).

## Custom commands start without administrator rights

In 1.4.0, when the app ran as administrator, every program it opened ran as administrator too. In 1.5.0,
programs, documents, folders and links opened by an elevated session start **with normal rights**. A tool that
really needs administrator rights shows its own UAC prompt when it starts. (On an account where everything runs as
administrator anyway, because UAC is off or it is the built-in Administrator account, there are no lower rights to
start with, and the app tells you so.) Before each launch from an administrator session, the app checks that the
Windows desktop it starts programs from runs without administrator rights; if it cannot confirm that, the program
is not started and the Log window says why.

Hotkey commands are not affected: an elevated session still sends keys to elevated windows, which is what elevated
mode is for.

Custom programs now start in your user profile folder (like the Windows *Run* dialog). If a command relied on a
relative path from the program folder, use a full path.

## Stricter hotkey syntax

1.4.0 checked a hotkey one key at a time while sending it, so a typo in the middle sent the first half and
stopped. 1.5.0 checks the whole command first.

- In Settings, *OK* and *Apply* show what is wrong (key number and column) and do not apply until it is fixed.
- A broken command that is already in `settings.json` does nothing when its corner is reached; the reason is in
  the Log window.
- Mouse button names (`lbutton`, `rbutton`, `mbutton`, `xbutton1`, `xbutton2`) are no longer accepted. They never
  worked: keyboard input cannot click.
- Tokens such as `_ctrl_` (held and released at once), a lone `_`, a conditional command without any
  `#[...]`/`@[...]` condition, nested parentheses, or text after the last `)` are errors now.
- In conditional commands, `#[...]` matches when the focused window's **title contains** the text you give,
  ignoring case; `@[...]` needs the **exact** title of some open window. `@[,title]` (title only) now works.
- Without `?(...)`, nothing happens when no condition matches. In 1.4.0 a command whose condition could not be
  read could run its second branch anyway.
- Modifier keys you hold while a sequence runs are released first and **not pressed again** afterwards, so they no
  longer get stuck.

New key name aliases were added: `ctrl`, `esc`, `enter`, `del`, `ins`, `pgup`/`pageup`, `pgdn`/`pagedown`, `lwin`,
`windows`, `menu`, `lalt`, `ralt`, `lctrl`, `rctrl`, `capslock`, `printscreen`, `backspace`.

## Hot corners fire only when you move into them

If the pointer is already resting in a corner when the app starts, after you change a setting, after the
display layout changes, or after you unlock the PC, that corner does not fire. Hot corners also pause while the PC
is locked, while a UAC prompt is shown, and while a remote session is disconnected. Move out of the corner and
back in. Also, a corner without a delay now fires as soon as the pointer reaches it.

## Hidden tray icon: start the app again

If the tray icon is hidden (*Always hide tray icon*, or *Hide tray icon* in the tray menu), start WinXCornersPlus
again from the Start menu or Explorer. The running copy opens its Settings window and shows its tray icon until it
exits. If the running copy does not answer, the second start says *WinXCornersPlus appears to be running but did
not respond*. In 1.4.0 a second start did nothing, so a hidden app could only be stopped from Task Manager.

## Renamed and removed

- Tray menu: *Elevate* is now *Restart as administrator…*, *Reload* is now *Restart*, *Hide tray* is now *Hide tray
  icon*. New entry: *Security & privacy…*.
- Removed: mouse button names in hotkeys (see above), and the "Beta build" label in the Settings window.
- *Hide the program's window* is now a visible switch for each custom command. In 1.4.0 it could only be set in
  `settings.json`.

## Other things you may notice

- Turning on *Always run as administrator*, *Enable Custom Commands*, *Always hide tray icon* or *Hide the
  program's window* first shows what it means. *Cancel* turns the switch off again.
- *Exit* works after the Log window has been opened (in 1.4.0 the app stayed running invisibly).
- Unexpected errors are written to `%LOCALAPPDATA%\WinXCornersPlus\crash.log` (except in an administrator session,
  where they are only in the Log window).
- At sign-out, shutdown, or when the installer needs to replace the program, the app saves its settings and exits
  on its own.
- In an administrator session started from a folder that is not admin-only, the tray menu's *Restart* reads
  *Restart without administrator rights*. It starts the program again without administrator rights. If *Always run
  as administrator* is on, the new copy asks for them, and Windows shows its prompt.
- The program is not code-signed yet, so Windows shows "Unknown publisher". See [INSTALLING.md](INSTALLING.md).
