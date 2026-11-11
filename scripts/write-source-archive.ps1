#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)
. "$PSScriptRoot\common.ps1"
$changes = & git -C $script:RepoRoot status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit tracked changes before creating a release source archive.' }
$directory = Split-Path -Parent ([IO.Path]::GetFullPath($Destination))
New-Item -ItemType Directory -Path $directory -Force | Out-Null
Invoke-Tool git @('-C',$script:RepoRoot,'archive','--format=zip','--prefix=PenPulse/',('-o='+$Destination),'HEAD')
Write-Host "Source archive: $Destination"
