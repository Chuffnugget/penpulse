# Developing PenPulse

PenPulse uses C#/.NET 10 and Windows Forms. Build on Windows with SDK 10.0.404
or a compatible .NET 10 SDK. The self-contained release pins .NET and Windows Forms
to 10.0.15; review both pins when a new servicing release is available.

## Set up and run

From the repository folder:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-tools.ps1 -IncludeInstaller
.\run.cmd
```

Setup downloads pinned tools from Microsoft and the official Inno Setup release,
verifies checksums and the Inno installer signature, and keeps tools in `.tools`.
It leaves the system PATH alone. Installer builds require [Inno Setup 7](https://jrsoftware.org/isdl.php);
omit `-IncludeInstaller` for app-only builds. Restore needs NuGet access or a cache.

Scripts support Windows PowerShell 5.1 and PowerShell 7. If execution is blocked,
use the same `powershell.exe -NoProfile -ExecutionPolicy Bypass -File` prefix for
any script below. This affects only that process; organisation policies may still
take precedence. `run.cmd` supplies the prefix for you.

## Commands

The optional [Task runner](https://taskfile.dev/docs/installation) provides short
names for the scripts below. Install it once for your Windows account:

```powershell
winget install --id Task.Task --exact --source winget --scope user
```

Restart your terminal after installation, then run from the repository folder:

```powershell
task run
task build
task test
task build-installer
task --list
```

Task uses the same scripts and process-only execution-policy bypass as `run.cmd`.
Pass script options after `--`, for example `task run -- -Rebuild`,
`task build -- -Configuration Debug`, or `task clean -- -WhatIf`.
Installing Task makes `task` available on your user PATH; it is separate from the
project-local tools installed by `setup-tools.ps1`. If Task is unavailable, use
`run.cmd` or the PowerShell commands directly.

| Script | Purpose |
| --- | --- |
| `run.cmd` | Open the published version. Pass `-Rebuild` after edits, or `-Background` to start hidden. |
| `scripts/build.ps1` | Compile in Release; accepts `-Configuration Debug`. |
| `scripts/test.ps1` | Run regression and isolated Windows control checks. |
| `scripts/test-windows.ps1` | Test the actual configuration controls in isolated, invisible forms. |
| `scripts/publish.ps1` | Publish a self-contained x64 app, documentation and licence notices. |
| `scripts/build-installer.ps1` | Test, publish and compile the installer. `-SkipTests` skips regression and packaging failure tests. |
| `scripts/test-package.ps1` | Check versions and licence hashes. Add `-ZipPath` to check a portable ZIP. |
| `scripts/test-packaging.ps1` | Verify that damaged package copies are rejected. |
| `scripts/test-installer.ps1` | Test installation, reinstall and uninstall under an isolated identity. |
| `scripts/release.ps1` | Run the checks and create installer, portable ZIP and checksums. |
| `scripts/clean.ps1` | Remove generated output, preserving `.tools` and personal data. Supports `-WhatIf`. |

Use `-DotNetPath` / `-InnoSetupPath`, or `PENPULSE_DOTNET` / `PENPULSE_ISCC`, to
select existing tools. Pass these parameters only to scripts that use that tool.

Published files go to `artifacts/publish/<version>/win-x64`; release files go to
`artifacts/release`. Versioned folders let a new release build while an older copy
runs. Exit the older copy before using the new version, since only one instance
runs per user. Copy its settings to keep them. Rebuilding the same version still
requires exiting a copy running from that folder.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\release.ps1
```

Packaging uses an explicit file list. Personal data and tools are excluded. The
notice collector reads the published dependency inventory and copies licences
from those exact packages. An unrecognised dependency stops the release for review.

## Code layout

| File | Responsibility |
| --- | --- |
| `Program.cs` | Single-instance coordination and the message loop. |
| `MainWindow.cs` | Window/tray lifecycle, input listeners and configuration. |
| `RawPenInput.cs` | Pen HID reports and shortcut-key Raw Input. |
| `Gestures.cs`, `PenContactState.cs` | Contact actions, holds, double-taps and stale-contact recovery. |
| `StrokeMotion.cs`, `DrawingPulseClock.cs` | Movement detection and pulse rate limiting. |
| `PenShortcutRecognizer.cs` | Top-button chords and the Slim Pen release-only workaround. |
| `Haptics.cs`, `DesktopHaptics.cs` | Effect scheduling, cancellation and Windows haptics routing. |
| `Settings.cs`, `EffectSettings.cs`, `ConfigurationPanel.cs` | Feedback preferences and controls. |
| `ProfileLibrary.cs`, `LocalJson.cs` | Profile snapshots, app mappings, import/export and local persistence. |
| `RuntimeState.cs`, `DesktopOptions.cs`, `ShortcutRegistration.cs` | Pause/reconnect timing, pressure response and global shortcut registration. |
| `FeaturePanels.cs`, `CalibrationForm.cs`, `Diagnostics.cs` | Profile/tools pages, calibration and troubleshooting. |

Raw pen input takes priority over promoted mouse input. The fallback briefly
delays events to let matching HID reports arrive, then checks their age. Contact
and motion leases prevent lost release reports from leaving feedback active.
Comments explain these hardware workarounds; timing tolerances are application
choices, not Windows guarantees.

## Formatting

The repository's `.editorconfig` defines C# whitespace and line breaks:

```powershell
.\.tools\dotnet\dotnet.exe format whitespace PenPulse.csproj --no-restore
.\.tools\dotnet\dotnet.exe format whitespace Tests\PenPulse.Tests.csproj --no-restore
.\.tools\dotnet\dotnet.exe format whitespace Tests\Windows\PenPulse.WindowsTests.csproj --no-restore
```

Add `--verify-no-changes` to check without editing. Do not reformat vendor licences.

## Installation and process lifetime

Setup installs per-user under `%LOCALAPPDATA%\Programs\PenPulse`. It checks the
running app's mutex instead of closing it. Uninstall preserves `data` and removes
the startup entry only when it belongs to that installation.

`PenPulse.exe --silent` starts hidden; `--show` opens the existing instance;
`--quit` exits it. An ordinary second launch also opens the current instance.

## References

- [Windows pen haptics](https://learn.microsoft.com/en-us/windows/apps/develop/input/pen-haptics)
- [InputHapticsManager](https://learn.microsoft.com/en-us/uwp/api/windows.devices.haptics.inputhapticsmanager)
- [Windows pen states](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/windows-pen-states)
- [Bluetooth pen buttons](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/windows-pen-designs)
- [Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/using-raw-input)

Profiles contain feedback settings only. App exclusions stay in settings.json;
startup remains a Windows setting; shortcuts and reconnect options live in
desktop.json. Profiles use an explicit versioned envelope, bounded file sizes and
new IDs on import. Local profile/options saves keep the previous file as a backup.

Continuous textures are requested for 120 ms and refreshed at 80 ms while contact
and motion remain current. A lost request expires, and cancellation also sends a
stop. The existing Click pulse scheduler remains the fallback. The duration ABI
signature follows Microsoft's windows-rs IInputHapticsManager vtable (slot 11).
Pressure is decoded from Digitizers/Tip Pressure, not Generic Desktop/X, and is
normalised using each HID descriptor's logical range.
