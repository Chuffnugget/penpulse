#requires -Version 5.1
[CmdletBinding()]
param([string]$Tag, [ValidateSet('win-x64','win-arm64')][string]$RuntimeIdentifier)
. "$PSScriptRoot\common.ps1"
if ($Tag) { Assert-ReleaseTag $Tag }
if ($RuntimeIdentifier -and $RuntimeIdentifier -ne (Get-HostRuntimeIdentifier)) {
    throw 'CI must use a native runner for each target architecture.'
}
Write-Host "Release preflight passed: $(Get-ProjectVersion) $(Get-HostRuntimeIdentifier)"
