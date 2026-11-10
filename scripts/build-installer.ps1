#requires -Version 5.1
[CmdletBinding()]
param([string]$DotNetPath, [string]$InnoSetupPath, [switch]$SkipTests)
. "$PSScriptRoot\common.ps1"
$compiler = Resolve-InnoSetup $InnoSetupPath
if (!$SkipTests) { & "$PSScriptRoot\test.ps1" -DotNetPath $DotNetPath }
& "$PSScriptRoot\publish.ps1" -DotNetPath $DotNetPath
if (!$SkipTests) { & "$PSScriptRoot\test-packaging.ps1" }
$release = Get-ReleaseDirectory
New-Item -ItemType Directory -Path $release -Force | Out-Null
Invoke-Tool $compiler @('--quiet',("--define=AppVersion="+(Get-ProjectVersion)),
    ("--define=PublishDir="+(Get-PublishDirectory)),("--define=ReleaseDir="+$release),
    (Join-Path $script:RepoRoot 'installer\PenPulse.iss'))
Write-Host "Installer: $(Join-Path $release ('PenPulse-'+(Get-ProjectVersion)+'-Setup-x64.exe'))"
