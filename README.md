# PenPulse

Configurable pen vibration across Windows apps.

PenPulse adds feedback for taps, holds, right-clicks, drawing, dragging, erasing
and the pen's top button. It runs in the system tray while you use other apps.

**Experimental.** Earlier builds were tested with a Surface Slim Pen 2 on a
Surface Pro 9. Version 0.9.1 needs a fresh physical pen check; other combinations
of pens, PCs and drivers remain unverified.

## Install

Use `PenPulse-0.9.1-Setup-x64.exe`, or extract the portable ZIP into a writable
folder and open `PenPulse.exe`. Windows 11 and a compatible haptic pen are
required. The .NET runtime is included. The installer is currently unsigned.

Setup installs for your Windows account without administrator privileges.
Exit PenPulse before upgrading. Reinstalling and uninstalling preserve your
settings; startup stays off unless you enable it.

## Connect your pen

1. Turn on Bluetooth and wake your paired pen.
2. Open PenPulse and touch the blue pad with the writing tip.
3. Once connected, choose **Minimise to tray** and try the pen in another app.

PenPulse retries automatically after sleep or connection loss, with faster retries
when pen input arrives. Touch the pad again if Windows still needs a direct pen
contact. You can turn automatic retry off in Tools.

## Startup and the system tray

- **Start with Windows** is in the General tab and the tray menu. It starts
  PenPulse hidden when you sign in.
- **Minimise to tray**, the title-bar minimise button and **X** hide the window
  while feedback keeps running.
- Click the tray icon to reopen the controls. Windows may put the icon in its
  hidden-icons menu. Launching PenPulse again also opens the existing copy.
- Choose **Exit PenPulse** to stop feedback and close the app completely.

## Adjust the feedback

- **Drawing & dragging:** strength, pulse speed, movement sensitivity and stop
  delay. Turn off **Only vibrate while the pen is moving** to include stationary
  contact. Lifting the pen stops movement feedback.
- **Taps, holds & clicks:** separate patterns for each contact and top-button
  action. Change strength, pulse count and spacing; use **Try this effect**
  to preview it with a connected pen.
- **General:** overall strength, presets, gesture timing, excluded apps and startup.
  Exclusions take process names separated by semicolons, such as `OneNote;Photoshop`.

Changes save automatically. Overall, drawing and action strengths default to 100%.
Overall strength scales each action's strength. Saved custom values are preserved
when upgrading. Presets preserve your custom click patterns.
Top-button double-click defaults to two pulses 350 ms apart, which felt distinct
on the tested pen. Saved settings keep their existing values.

## Profiles, shortcuts and calibration

- **Profiles:** save your current default settings as a named profile, or import
  and export profiles as JSON. Use **Use as default** to load one into the controls;
  **Replace saved** updates the selected snapshot after you edit the defaults.
- **App profiles:** assign an executable name such as `OneNote.exe` to a saved
  profile. It takes effect when that app is in front. Unassigned apps use your
  defaults, and global exclusions always apply.
- **Quick pause:** choose a timed pause in the tray menu, or enable a configurable
  pause/resume keyboard shortcut in Tools. Shortcut conflicts are reported there.
- **Calibration:** Tools walks you through strength, double-click spacing and
  supported drawing textures. Preview changes before applying them to your defaults.
- **Troubleshooting:** Tools explains the current connection, input, pause,
  exclusion and capability state. You can copy a report; nothing is uploaded.

Drawing settings also offer experimental **Ink, Pencil, Brush, Marker and Eraser**
textures and optional **pressure-responsive strength**. Textures use short,
bounded playback requests and fall back to clicks when unavailable or rejected.
Pressure affects drawing only; missing pressure or intensity support uses normal
strength. Existing settings keep pulse mode and pressure feedback off.

## Compatibility

A pen needs a physical haptic motor and a PC/driver combination that exposes
Windows pen haptics. Bluetooth alone does not establish compatibility.

| Pen | PenPulse testing |
| --- | --- |
| Surface Slim Pen 2 / Surface Slim Pen (2nd Edition) | Tested on Surface Pro 9 with Windows build 26300; earlier builds only. |
| ASUS Pen 3.0 SA205H | Haptic-capable; not tested with PenPulse. |
| MSI Pen 2 | Haptic-capable; not tested with PenPulse. |

See Microsoft's [supported haptic devices](https://learn.microsoft.com/en-us/windows/apps/develop/input/haptics#supported-devices)
and [Surface pen compatibility](https://support.microsoft.com/en-us/surface/accessories/pen/surface-pen-compatibility-and-features).
The top-button release workaround currently recognises the Slim Pen 2 specifically.

PenPulse runs in your signed-in desktop session, excluding the lock screen and
secure UAC desktop. The default drawing mode uses repeated clicks; PenPulse cannot
tell whether an app is drawing, selecting text or dragging an object. See
[testing and known limits](docs/TESTING.md).

## Run from source

Prepare the local tools as described in [DEVELOPING.md](docs/DEVELOPING.md), then run:

```powershell
.\run.cmd
```

The launcher builds the current version if needed. Use `-Rebuild` after source edits.
It allows PowerShell scripts for that launch only, without changing your saved policy.
Build, test, installer and release commands are in the development guide.

If you prefer `task run`, install [Task](https://taskfile.dev/docs/installation)
once with `winget install --id Task.Task --exact --source winget --scope user`,
then restart your terminal. The included `Taskfile.yml` provides `task run`,
`task build`, `task test`, `task build-installer` and `task release`.
Use `task --list` for every command. `run.cmd` works without Task.

## Local data

PenPulse makes no network requests. Settings and connection status are stored in
`data` beside the executable (`settings.json`, `profiles.json` and `desktop.json`).
It does not record handwriting, typed text or screen contents. To move preferences
and profiles between copies, exit PenPulse and copy those three JSON files, when
present, into the new copy's `data` folder. The Windows startup registration belongs
to each executable path; enable it again in the new copy if you move the app.

## Licence

PenPulse's original source is [MIT-licensed](LICENSE), copyright 2026 PenPulse
contributors. Bundled dependencies keep their own terms; see
[third-party notices](THIRD-PARTY-NOTICES.md).
