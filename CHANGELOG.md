# Changes

## 0.9.1 - 10 November 2026

- Default overall, drawing and action strengths to 100%.
- Keep saved custom strengths, pulse timing and alternating patterns intact.

## 0.9.0 - 10 November 2026

- Add named profiles with import/export and automatic per-app selection.
- Add reconnect backoff, quick retries on fresh pen input, and sleep/lock recovery.
- Add configurable pause shortcuts and timed tray pauses.
- Add a three-step calibration flow with previews and explicit Apply/Cancel.
- Add supported drawing textures with bounded playback and pulse fallback.
- Add optional pressure-responsive drawing with adjustable sensitivity and minimum strength.
- Add a troubleshooting page and copyable local report.
- Preserve existing feedback settings and keep experimental modes opt-in.
- Add regression and isolated Windows control tests for the new features.

## 0.8.2 - 10 November 2026

- Minimise to the tray from the window button, title-bar minimise or X; keep Exit PenPulse as the explicit way to stop.
- Make Start with Windows available in General and the tray menu, with hidden startup at sign-in.
- Reopen controls with a single click on the tray icon.
- Add run.cmd to launch without changing PowerShell's saved execution policy.
- Shorten the README and move build and testing details into dedicated guides.
- Format C# consistently and name timing thresholds and Windows haptics interface slots.
- Publish into versioned folders so a new release can build while an older copy runs.

## 0.8.1 - 10 November 2026

- License PenPulse's original source under MIT; retain separate Microsoft and third-party terms.
- Update the bundled .NET and Windows Forms runtimes to security servicing release 10.0.15 and the tool bootstrap to SDK 10.0.404.
- Include runtime licence files, Windows SDK terms, C#/WinRT licence and a hashed component manifest in the installer and portable ZIP.
- Reject missing or changed notices, unexpected bundled dependencies and mismatched package versions during release creation.
- Add a run script, package verification and packaging failure tests; extend installer checks to cover licences.
- Keep pen behaviour unchanged from 0.8.0. Fresh physical verification is still needed.

## 0.8.0 — 7 November 2026

- Add PowerShell build, test, publish, installer, release, tool setup and clean scripts.
- Add a per-user Inno Setup installer with shortcuts and an uninstaller that preserves personal settings.
- Treat repeated screen taps as separate double-tap pairs.
- Preserve pulse spacing after a delayed timer or device call.
- Expire stale raw contact before recognizing a hold; verify stationary mouse-fallback contact against the current button state.
- Cancel queued and drawing feedback when an excluded app becomes active.
- Default top-button double-click spacing to the physically verified 350 ms, while preserving saved preferences.
- Move the application and tests to .NET 10; publish with runtime 10.0.14.
- Expand automated coverage from 57 to 80 checks, including the actual haptics scheduler with simulated Windows adapters.

Physical pen feedback after these changes still requires hands-on verification.
