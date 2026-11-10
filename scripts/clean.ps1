#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess=$true)]
param()
. "$PSScriptRoot\common.ps1"
foreach ($relative in @('bin','obj','Tests\bin','Tests\obj','artifacts')) {
    $target = Join-Path $script:RepoRoot $relative
    if (Test-Path -LiteralPath $target) {
        $personalData = Get-ChildItem -LiteralPath $target -Directory -Filter data -Recurse -Force | Select-Object -First 1
        if ($personalData) { Write-Warning "Keeping $target because it contains an app data folder. Move that folder somewhere safe before cleaning."; continue }
    }
    if ((Test-Path -LiteralPath $target) -and $PSCmdlet.ShouldProcess($target,'Remove generated build output')) {
        Remove-ProjectDirectory $target
    }
}
