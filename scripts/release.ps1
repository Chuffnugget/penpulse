#requires -Version 5.1
[CmdletBinding()]
param([string]$DotNetPath, [string]$InnoSetupPath, [switch]$SkipInstallerTests, [ValidateSet('win-x64','win-arm64')][string]$RuntimeIdentifier)
. "$PSScriptRoot\common.ps1"
if (!$RuntimeIdentifier) { $RuntimeIdentifier = Get-HostRuntimeIdentifier }
$architecture = $RuntimeIdentifier.Substring(4)
& "$PSScriptRoot\build-installer.ps1" -RuntimeIdentifier $RuntimeIdentifier -DotNetPath $DotNetPath -InnoSetupPath $InnoSetupPath
if (!$SkipInstallerTests) { & "$PSScriptRoot\test-installer.ps1" -RuntimeIdentifier $RuntimeIdentifier -InnoSetupPath $InnoSetupPath }
$version = Get-ProjectVersion
$release = Get-ReleaseDirectory
$zip = Join-Path $release "PenPulse-$version-Windows-$architecture.zip"
# Explicit allowlist: never package local settings, diagnostics, or compiler caches.
$files = @('PenPulse.exe','README.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md','THIRD-PARTY-NOTICES.json','licenses','docs') | ForEach-Object { Join-Path (Get-PublishDirectory $RuntimeIdentifier) $_ }
Compress-Archive -LiteralPath $files -DestinationPath $zip -Force
& "$PSScriptRoot\test-package.ps1" -RuntimeIdentifier $RuntimeIdentifier -ZipPath $zip
$installer = Join-Path $release "PenPulse-$version-Setup-$architecture.exe"
$checksums = foreach ($file in @($zip,$installer)) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($file)
}
[IO.File]::WriteAllLines((Join-Path $release "SHA256SUMS-$architecture.txt"), $checksums, (New-Object Text.UTF8Encoding($false)))
Write-Host "Release files: $release"
