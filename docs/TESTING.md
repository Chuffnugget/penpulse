# Testing and known limits

## Automated checks

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

The console runner exits with a nonzero code on failure; it is not a `dotnet test`
project. The script also runs the Windows control suite in invisible test forms
without registering pen listeners or changing real settings. Regression tests
cover gestures, motion expiry, saved settings, native structure sizes
and Bluetooth shortcuts. Scheduler tests use the real `Haptics.cs` with fake
Windows adapters and a controlled clock. They cannot confirm physical vibration.

Release creation also checks dependency versions and licence hashes, rejects
damaged package fixtures, verifies ZIP contents, and tests the installer with a
separate identity. Installer tests preserve the real app and settings. Failed
runs leave logs under `.tools/installer-tests`.

## Manual release check

1. Tap, hold, right-click and draw on the connection pad with a connected pen.
2. Minimise to the tray and repeat in another app. Check that lifting the pen
   stops movement feedback.
3. Try eraser contact and movement, then top-button click, double-click and hold
   away from the display. Windows' usual shortcuts should still work.
4. Check strength, pulse spacing, pause/resume and an excluded app.
5. Use title-bar minimise and X. The process should keep running, and the tray
   icon should reopen it. Exit PenPulse should stop it.
6. Enable Start with Windows, sign out and back in, and check hidden startup.
   Restore the user's preferred startup setting afterward.

7. Save and reload a profile, export/import it, and assign it to an app. Switch
   apps while a pattern plays; a different profile must cancel old contact
   feedback. A top-button sequence should retain its original timing when its
   Windows shortcut opens another app.
8. Try the configurable pause shortcut and a timed tray pause. Check that a
   conflicting shortcut is reported and does not disable tray controls.
9. Run calibration, cancel once, then apply. Check that Cancel leaves defaults
   unchanged and saved profiles change only when explicitly replaced.
10. Try each texture the pen advertises, then test pressure response. Lift the
    pen, pause, exclude an app and disconnect during playback. Confirm stopping
    physically; a successful API return alone is insufficient.
11. Sleep/wake and lock/unlock the PC. Check retries and the blue-pad fallback.

Earlier builds passed physical tests for background tap/hold/right-click,
drawing/dragging, eraser contact/movement and top-button click/double-click on a
Surface Pro 9 with Slim Pen 2. At 140 ms, double-click pulses felt like one pulse;
350 ms gave two distinct pulses. Those observations do not validate a later build.

Profile and scheduling tests cover malformed imports, snapshot isolation, app
matching, backups, timed pauses, reconnect backoff, pressure ranges, bounded
texture refresh and fallback. Windows control tests cover profile saving/app
assignment, reload, calibration drafts, supported texture choices and scrolling.

## Limits

CI tests both architectures on native GitHub Windows runners. Package checks
verify the executable's PE architecture and reject mismatched runtime manifests.
Release guard checks reject invalid/mismatched tags, missing architecture assets
and altered checksums without contacting GitHub. No CI runner has a physical pen.

- This .NET 10 release needs a fresh physical pen check. Screen double-tap and
  top-button hold have not been physically confirmed.
- Background feedback depends on a newer Windows haptics API. The older
  controller-only fallback did not produce background vibration on the tested PC.
- Other pens, multiple displays, elevated apps and exclusive-input apps have not
  been comprehensively tested. Only one pen/digitizer is selected at a time.
- The Slim Pen workaround accepts a release-only shortcut only after a recent
  Windows-key event from that pen's own keyboard collection. Other pens may need
  different handling.
- Drawing defaults to finite Click waveforms; experimental textures use bounded
  duration requests. Feel, smoothness and stop timing need
  broader testing. A successful API call does not prove a pen vibrated.
- Releases are unsigned. No HID fuzzing, penetration test or long-duration soak
  has been performed.
