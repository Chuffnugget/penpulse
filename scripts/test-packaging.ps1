#requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('win-x64','win-arm64')][string]$RuntimeIdentifier)
. "$PSScriptRoot\common.ps1"
if (!$RuntimeIdentifier) { $RuntimeIdentifier = Get-HostRuntimeIdentifier }
$architecture = $RuntimeIdentifier.Substring(4)
& "$PSScriptRoot\test-package.ps1" -RuntimeIdentifier $RuntimeIdentifier
$fixture = Join-Path $script:RepoRoot ('.tools\package-tests\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$passed = 0
function Expect-Rejected([string]$Description, [string]$Zip) {
    $rejected = $false
    try { & "$PSScriptRoot\test-package.ps1" -RuntimeIdentifier $RuntimeIdentifier -PackageDirectory $fixture -ZipPath $Zip }
    catch { $rejected = $true }
    if (!$rejected) { throw "Invalid package was accepted: $Description" }
    $script:passed++
    Write-Host "PASS rejects $Description"
}
try {
    foreach ($name in @('PenPulse.exe','README.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md','THIRD-PARTY-NOTICES.json','licenses','docs')) {
        Copy-Item -LiteralPath (Join-Path (Get-PublishDirectory $RuntimeIdentifier) $name) -Destination $fixture -Recurse
    }
    & "$PSScriptRoot\test-package.ps1" -RuntimeIdentifier $RuntimeIdentifier -PackageDirectory $fixture
    $manifestPath = Join-Path $fixture 'THIRD-PARTY-NOTICES.json'
    $original = [IO.File]::ReadAllText($manifestPath)
    $manifest = $original | ConvertFrom-Json
    $manifest.runtimeIdentifier = if ($RuntimeIdentifier -eq 'win-x64') { 'win-arm64' } else { 'win-x64' }
    [IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 6))
    Expect-Rejected 'a manifest for the wrong architecture'
    [IO.File]::WriteAllText($manifestPath,$original,(New-Object Text.UTF8Encoding($false)))
    $executable = Join-Path $fixture 'PenPulse.exe'
    $stream = [IO.File]::Open($executable,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        $stream.Position = $offset + 4
        $wrongMachine = if ($RuntimeIdentifier -eq 'win-x64') { [byte[]](0x64,0xaa) } else { [byte[]](0x64,0x86) }
        $stream.Write($wrongMachine,0,2)
    } finally { $reader.Dispose(); $stream.Dispose() }
    Expect-Rejected 'an executable for the wrong architecture'
    Copy-Item -LiteralPath (Join-Path (Get-PublishDirectory $RuntimeIdentifier) 'PenPulse.exe') -Destination $executable -Force
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
