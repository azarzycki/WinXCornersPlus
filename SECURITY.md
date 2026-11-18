# Security policy

## Supported versions

Security fixes go into the latest release only. Please update before reporting.

| Version | Supported |
|---|---|
| 1.5.x (latest) | Yes |
| 1.4.0 and earlier | No. They have known problems, including a way for any program running as you to gain administrator rights through elevated mode, addressed in 1.5.0 (what remains is listed in [docs/SECURITY-MODEL.md](docs/SECURITY-MODEL.md)). |

## Reporting a vulnerability

Please do **not** open a public issue, discussion or pull request for a security problem.

Report it privately through GitHub: open the repository's
[Security tab](https://github.com/Asteski/WinXCornersPlus/security) and choose **Report a vulnerability**
(direct link: <https://github.com/Asteski/WinXCornersPlus/security/advisories/new>). Only you and the
repository's maintainers can see the report.

Please include:

- the WinXCornersPlus version (About window) and how it was installed (installer or portable zip, and the folder);
- the Windows version and architecture (x64 or ARM64), and whether the app ran as administrator;
- the steps to reproduce, and what an attacker gains (for example: a program running without administrator
  rights gets them);
- the relevant `settings.json` content, the text of the Log window, and `crash.log` if there is one. Remove
  anything private first.

## What to expect

This is a small project maintained in spare time, so there is no fixed response time. You will get a reply in the
private advisory, where the problem and the fix can be discussed. Once a fixed release is out, the advisory can be
published, crediting you if you wish. Please give us a reasonable time to release a fix
before you disclose the problem publicly.

## Scope

[docs/SECURITY-MODEL.md](docs/SECURITY-MODEL.md) explains what the app defends and what it does not. In short,
the main boundary is between programs running with your normal rights and administrator rights. Reports that
cross it are the most valuable, for example:

- anything WinXCornersPlus starts with administrator rights on an account with UAC on (on an account where
  everything runs as administrator, UAC off or the built-in Administrator, this is expected and the app says so);
- an administrator session that restarts itself, or a replaced program file, without a UAC prompt;
- an administrator session that creates, moves or overwrites a file outside its settings file, or follows a link
  planted in the settings folder;
- an elevated session acting on settings you did not approve;
- a way to make the app load code from an unexpected place.

Something a program running as you could do anyway, without WinXCornersPlus, is usually not a vulnerability in
the app; if you are unsure, report it privately anyway.
