# Installing WinXCornersPlus

Every release on the [Releases page](https://github.com/Asteski/WinXCornersPlus/releases) has four files:

| File | For |
|---|---|
| `WinXCornersPlus-<version>-x64.msi` | **Recommended.** Installer for most PCs (Intel or AMD, 64-bit). |
| `WinXCornersPlus-<version>-arm64.msi` | Installer for Windows on Arm PCs (for example Snapdragon). |
| `WinXCornersPlus-<version>-win-x64.zip` | Portable copy: extract anywhere and run `WinXCornersPlus.exe`. |
| `WinXCornersPlus-<version>-win-arm64.zip` | Portable copy for Windows on Arm. |

*Settings > System > About > System type* tells you which you have ("x64-based processor" or
"ARM-based processor"). The Arm64 files run natively on Arm; the x64 files also run there, emulated.
There is **no 32-bit (x86) build**: Windows 11 is 64-bit only and 32-bit Windows 10 is rare.

## Requirements

Windows 10 or 11 and the **.NET 10 Desktop Runtime** for the same architecture
(download "**.NET Desktop Runtime 10**" for Windows x64 or Arm64 from
<https://dotnet.microsoft.com/download/dotnet/10.0>). The installer checks for it before it changes
anything and, if it is missing, stops with a message saying exactly that. It never downloads or bundles
the runtime. The portable app shows Windows' own "download .NET" prompt instead.

## Installer or portable zip?

**Use the installer if you will ever run WinXCornersPlus as administrator** (the "Run as administrator"
option, needed for hot corners to work over elevated windows).

Why: when you approve the administrator prompt, Windows runs whatever files are in the program's folder.
A portable copy lives in a folder that any program running as you can change (Downloads, Desktop, a USB
stick), so malware with ordinary rights could quietly replace a DLL there and then simply wait for you to
approve the prompt. The installer puts the program in `C:\Program Files\WinXCornersPlus`, which only
administrators can change, which closes that gap. The app checks its own folder and warns on its
Security page when it is not protected this way.

The portable zip is fine if you never use the administrator option.

## What the installer does

* Installs for all users into `C:\Program Files\WinXCornersPlus` (always that folder: it cannot be
  redirected, so it can never end up somewhere writable).
* Adds a **WinXCornersPlus** shortcut to the Start menu and an entry under *Settings > Apps > Installed
  apps* (Add/Remove Programs) with the version, the publisher (Asteski) and a link to the project.
* Does **not** start the app (an installer runs as administrator, and so would anything it started), add it
  to startup, set it to run as administrator, touch `PATH`, or install services or scheduled tasks. Turn on
  *Start with Windows* in the app if you want it.

Silent install, for administrators and scripts:

```
msiexec /i WinXCornersPlus-<version>-x64.msi /qn /l*v install.log
```

## Upgrading

Run the newer installer. It replaces the installed version in place (one entry in Installed apps). If
WinXCornersPlus is running, the installer asks it to close first, the same way Windows does at sign-out
(the app saves its settings and removes its tray icon); only if it has not closed after 10 seconds is it
ended. No reboot is needed. The installer does **not** start the app again afterwards: start it from the
Start menu (or it starts at your next sign-in if *Start with Windows* is on).

Installing an **older** version over a newer one is refused with a message; uninstall the newer one first
if you really want to go back.

## Uninstalling

*Settings > Apps > Installed apps > WinXCornersPlus > Uninstall* (or `msiexec /x <the .msi> /qn`).
A running copy is closed first, as above. The uninstaller removes:

* the program folder and the Start-menu shortcut;
* `HKEY_LOCAL_MACHINE\SOFTWARE\WinXCornersPlus` (the approvals the app keeps for administrator sessions);
* the *Start with Windows* entry of the user who uninstalls (value `WinXCornersPlus` under
  `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`). Other accounts that turned it on keep
  a stale entry, which Windows simply skips.

It **keeps your settings** in `%APPDATA%\WinXCornersPlus\settings.json`
(`C:\Users\<you>\AppData\Roaming\WinXCornersPlus\`), so a reinstall picks them up again. Delete that folder
yourself if you want them gone. An upgrade keeps everything, including the administrator-session approvals.

## Moving from the portable zip to the installer

Quit the portable copy (tray icon > Exit), install the MSI, and start WinXCornersPlus from the Start menu.
Your settings are already in `%APPDATA%\WinXCornersPlus` and are used as they are. If *Start with Windows*
was on, turn it off and on again in the installed app so the entry points to the new location (the app
shows a notice while it points elsewhere). Then delete the portable folder.

## "Windows protected your PC" / "Unknown publisher"

The files are currently **not code-signed**, so Windows SmartScreen may show *Windows protected your PC*
(choose *More info* > *Run anyway*) and the installer's administrator prompt shows *Unknown publisher*.
Before you do that, verify the download:

1. **Checksum.** The release notes and the attached `SHA256SUMS.txt` list the SHA-256 of every file.
   In PowerShell, in your downloads folder:

   ```powershell
   (Get-FileHash .\WinXCornersPlus-<version>-x64.msi -Algorithm SHA256).Hash.ToLower()
   ```

   The output must equal the published value.

2. **Build provenance.** Each file carries a signed attestation that it was built by this repository's
   release workflow from a specific commit. With the [GitHub CLI](https://cli.github.com/):

   ```
   gh attestation verify WinXCornersPlus-<version>-x64.msi --repo Asteski/WinXCornersPlus
   ```

If a release is code-signed in the future, its notes will say so and name the publisher; see
[CODE-SIGNING-POLICY.md](CODE-SIGNING-POLICY.md).
