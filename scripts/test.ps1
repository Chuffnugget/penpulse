#requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release', [string]$DotNetPath)
. "$PSScriptRoot\common.ps1"
& "$PSScriptRoot\test-release-guards.ps1"
$dotnet = Resolve-DotNet $DotNetPath
Push-Location $script:RepoRoot
try { Invoke-Tool $dotnet @('run','--project','Tests\PenPulse.Tests.csproj','-c',$Configuration) }
finally { Pop-Location }
& "$PSScriptRoot\test-windows.ps1" -DotNetPath $dotnet
