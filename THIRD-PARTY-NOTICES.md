# Third-party software

The MIT licence in `LICENSE` applies to PenPulse's original source code. It does
not replace the terms for Microsoft or other third-party components.

The self-contained Windows release includes:

| Component | Terms | Included notice |
| --- | --- | --- |
| .NET runtime | MIT, plus notices for incorporated third-party code | `licenses/Microsoft.NETCore.App.Runtime.win-<architecture>-<version>/` |
| Windows Desktop runtime (Windows Forms) | MIT and any accompanying notices | `licenses/Microsoft.WindowsDesktop.App.Runtime.win-<architecture>-<version>/` |
| Windows SDK .NET projection | Microsoft Windows SDK licence | `licenses/Microsoft.Windows.SDK.NET.Ref-10.0.22621.57/WINDOWS-SDK-LICENSE.rtf` |
| C#/WinRT runtime, included with the SDK projection | MIT | `licenses/Microsoft.Windows.SDK.NET.Ref-10.0.22621.57/CSWINRT-LICENSE.txt` |

`THIRD-PARTY-NOTICES.json` in each published release records the actual runtime
architecture (`x64` or `arm64`),
pack versions and SHA-256 hashes of their included licence files. The build
copies the runtime notices from the exact restored packages. The source folder
`third-party` records the provenance of the separately supplied SDK notices.

Use and redistribution of Microsoft's Windows SDK components are subject to
the included Microsoft terms. Review and agree to those terms before using the
portable executable. Distributors must retain all applicable notices and terms.
PenPulse is an independent project and is not endorsed by Microsoft.
