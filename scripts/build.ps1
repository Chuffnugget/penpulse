#requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release', [string]$DotNetPath)
. "$PSScriptRoot\common.ps1"
$dotnet = Resolve-DotNet $DotNetPath
Push-Location $script:RepoRoot
try { Invoke-Tool $dotnet @('build','PenPulse.csproj','-c',$Configuration,'--nologo') }
finally { Pop-Location }
