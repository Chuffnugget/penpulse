#requires -Version 5.1
[CmdletBinding()]
param([string]$DotNetPath, [switch]$Rebuild, [switch]$Background)
. "$PSScriptRoot\common.ps1"
$exe = Join-Path (Get-PublishDirectory) 'PenPulse.exe'
$publish = $Rebuild -or !(Test-Path -LiteralPath $exe)
if (!$publish) {
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion.Split('+')[0]
    $publish = $version -ne (Get-ProjectVersion)
}
if ($publish) { & "$PSScriptRoot\publish.ps1" -DotNetPath $DotNetPath }
& "$PSScriptRoot\test-package.ps1"
if ($Background) { Start-Process -FilePath $exe -ArgumentList '--silent' -WindowStyle Hidden }
else { Start-Process -FilePath $exe -WindowStyle Normal }
Write-Host 'Opened PenPulse. If another copy is already running, Windows opens that copy instead.'
