# Notice provenance

These are unmodified upstream licence files. They cover runtime files from
Microsoft.Windows.SDK.NET.Ref 10.0.22621.57, whose NuGet package points to the
Windows SDK licence but does not include a local copy.

- `WINDOWS-SDK-LICENSE.rtf`: https://download.microsoft.com/download/0/F/F/0FF2B061-47DD-4F55-89B6-FD1D8C44F14D/sdk_license.rtf
  (the package's https://aka.ms/WinSDKLicenseURL redirect), retrieved 2026-11-10.
  SHA-256: `dd07eb178e00c6bba4148457fc00ff77cd4887eb521d504186fe59c9ec8bbe62`.
- `CSWINRT-LICENSE.txt`: https://raw.githubusercontent.com/microsoft/CsWinRT/8649ee3eeb2445ca2a36d80d878ef60b96a6c65d/LICENSE,
  retrieved 2026-11-10. This commit matches the bundled WinRT.Runtime.dll product
  version `2.2.0.48161+8649ee3eeb2445ca2a36d80d878ef60b96a6c65d`.
  SHA-256: `9906940f61b1f0b533fa7d99baf55178b2808fbe113ea51dfbfad8572ccd5f2b`.

The publish script copies .NET and Windows Desktop runtime licence notices from
their restored package versions instead of maintaining stale duplicates here.
Build tools are downloaded locally and are not included in PenPulse releases.
