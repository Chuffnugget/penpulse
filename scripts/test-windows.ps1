#requires -Version 5.1
[CmdletBinding()]
param([string]$DotNetPath)
. "$PSScriptRoot\common.ps1"
$dotnet = Resolve-DotNet $DotNetPath
Push-Location $script:RepoRoot
try { Invoke-Tool $dotnet @('run','--project','Tests\Windows\PenPulse.WindowsTests.csproj','-c','Release') }
finally { Pop-Location }
