#requires -Version 5.1
[CmdletBinding()]
param()
. "$PSScriptRoot\common.ps1"
& "$PSScriptRoot\test-package.ps1"
$fixture = Join-Path $script:RepoRoot ('.tools\package-tests\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$passed = 0
function Expect-Rejected([string]$Description, [string]$Zip) {
    $rejected = $false
    try { & "$PSScriptRoot\test-package.ps1" -PackageDirectory $fixture -ZipPath $Zip }
    catch { $rejected = $true }
    if (!$rejected) { throw "Invalid package was accepted: $Description" }
    $script:passed++
    Write-Host "PASS rejects $Description"
}
try {
    foreach ($name in @('PenPulse.exe','README.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md','THIRD-PARTY-NOTICES.json','licenses','docs')) {
        Copy-Item -LiteralPath (Join-Path (Get-PublishDirectory) $name) -Destination $fixture -Recurse
    }
    & "$PSScriptRoot\test-package.ps1" -PackageDirectory $fixture
    $manifestPath = Join-Path $fixture 'THIRD-PARTY-NOTICES.json'
    $original = [IO.File]::ReadAllText($manifestPath)
    $manifest = $original | ConvertFrom-Json
    $notice = Join-Path $fixture $manifest.files[0].path
    $bytes = [IO.File]::ReadAllBytes($notice)
    Remove-Item -LiteralPath $notice
    Expect-Rejected 'a missing dependency licence'
    [IO.File]::WriteAllText($notice,'damaged notice')
    Expect-Rejected 'a changed dependency licence'
    [IO.File]::WriteAllBytes($notice,$bytes)
    $manifest.components[0].version = '0.0.0'
    [IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 6))
    Expect-Rejected 'a stale runtime version'
    $manifest = $original | ConvertFrom-Json
    $manifest.files[0].path = '../outside.txt'
    [IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 6))
    Expect-Rejected 'a notice path outside the package'
    [IO.File]::WriteAllText($manifestPath,$original,(New-Object Text.UTF8Encoding($false)))
    $licence = Join-Path $fixture 'LICENSE'
    Remove-Item -LiteralPath $licence
    Expect-Rejected 'a missing project licence'
    Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'LICENSE') -Destination $fixture
    $zip = Join-Path $fixture 'invalid.zip'
    Compress-Archive -LiteralPath (Join-Path $fixture 'PenPulse.exe') -DestinationPath $zip
    Expect-Rejected 'a ZIP missing its licence files' $zip
    Write-Host "$passed packaging failure checks passed."
}
finally { Remove-ProjectDirectory $fixture }
