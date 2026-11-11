#requires -Version 5.1
[CmdletBinding()]
param([string]$DotNetPath, [string]$InnoSetupPath, [switch]$SkipTests, [ValidateSet('win-x64','win-arm64')][string]$RuntimeIdentifier)
. "$PSScriptRoot\common.ps1"
if (!$RuntimeIdentifier) { $RuntimeIdentifier = Get-HostRuntimeIdentifier }
$architecture = $RuntimeIdentifier.Substring(4)
$compiler = Resolve-InnoSetup $InnoSetupPath
if (!$SkipTests) { & "$PSScriptRoot\test.ps1" -DotNetPath $DotNetPath }
& "$PSScriptRoot\publish.ps1" -RuntimeIdentifier $RuntimeIdentifier -DotNetPath $DotNetPath
if (!$SkipTests) { & "$PSScriptRoot\test-packaging.ps1" -RuntimeIdentifier $RuntimeIdentifier }
$release = Get-ReleaseDirectory
New-Item -ItemType Directory -Path $release -Force | Out-Null
Invoke-Tool $compiler @('--quiet',("--define=AppVersion="+(Get-ProjectVersion)),("--define=AppArchitecture="+$architecture),
    ("--define=PublishDir="+(Get-PublishDirectory $RuntimeIdentifier)),("--define=ReleaseDir="+$release),
    (Join-Path $script:RepoRoot 'installer\PenPulse.iss'))
Write-Host "Installer: $(Join-Path $release ('PenPulse-'+(Get-ProjectVersion)+'-Setup-'+$architecture+'.exe'))"
