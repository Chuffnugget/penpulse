#requires -Version 5.1
[CmdletBinding()]
param()
. "$PSScriptRoot\common.ps1"
$version = Get-ProjectVersion
$fixture = Join-Path $script:RepoRoot ('.tools\release-tests\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$passed = 0
function Reject([string]$Name, [scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw "Accepted invalid release input: $Name" }
    $script:passed++
    Write-Host "PASS rejects $Name"
}
try {
    Assert-ReleaseTag "v$version"
    foreach ($tag in @($version,'v999.0.0','v00.0.1','v0.0.1/other','v0.0.1;echo','v0.0.1-beta.0')) {
        Reject "tag $tag" { Assert-ReleaseTag $tag }
    }
    foreach ($architecture in @('x64','arm64')) {
        $lines = foreach ($name in @("PenPulse-$version-Setup-$architecture.exe","PenPulse-$version-Windows-$architecture.zip")) {
            $file = Join-Path $fixture $name
            [IO.File]::WriteAllText($file,"Fixture for $name")
            '{0}  {1}' -f (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant(),$name
        }
        [IO.File]::WriteAllLines((Join-Path $fixture "SHA256SUMS-$architecture.txt"),$lines)
    }
    [IO.File]::WriteAllText((Join-Path $fixture "PenPulse-$version-Source.zip"),'Source fixture')
    & "$PSScriptRoot\publish-release.ps1" -ValidateOnly -Tag "v$version" -Repository 'test/fixture' -ArtifactDirectory $fixture
    $passed++
    $arm = Join-Path $fixture "PenPulse-$version-Setup-arm64.exe"
    [IO.File]::AppendAllText($arm,'tampered')
    Reject 'changed ARM64 asset' { & "$PSScriptRoot\publish-release.ps1" -ValidateOnly -Tag "v$version" -Repository 'test/fixture' -ArtifactDirectory $fixture }
    Remove-Item -LiteralPath $arm
    Reject 'missing ARM64 asset' { & "$PSScriptRoot\publish-release.ps1" -ValidateOnly -Tag "v$version" -Repository 'test/fixture' -ArtifactDirectory $fixture }
    Write-Host "$passed release guard checks passed. No GitHub operations were performed."
} finally { Remove-ProjectDirectory $fixture }
