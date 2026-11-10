#requires -Version 5.1
[CmdletBinding()]
param([string]$DotNetPath, [string]$InnoSetupPath)
. "$PSScriptRoot\common.ps1"
& "$PSScriptRoot\build-installer.ps1" -DotNetPath $DotNetPath -InnoSetupPath $InnoSetupPath
& "$PSScriptRoot\test-installer.ps1" -InnoSetupPath $InnoSetupPath
$version = Get-ProjectVersion
$release = Get-ReleaseDirectory
$zip = Join-Path $release "PenPulse-$version-Windows-x64.zip"
# Explicit allowlist: never package local settings, diagnostics, or compiler caches.
$files = @('PenPulse.exe','README.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md','THIRD-PARTY-NOTICES.json','licenses','docs') | ForEach-Object { Join-Path (Get-PublishDirectory) $_ }
Compress-Archive -LiteralPath $files -DestinationPath $zip -Force
& "$PSScriptRoot\test-package.ps1" -ZipPath $zip
$installer = Join-Path $release "PenPulse-$version-Setup-x64.exe"
$checksums = foreach ($file in @($zip,$installer)) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($file)
}
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'), $checksums, (New-Object Text.UTF8Encoding($false)))
Write-Host "Release files: $release"
