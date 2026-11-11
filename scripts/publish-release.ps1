#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Repository,
    [string]$ArtifactDirectory,
    [switch]$CheckOnly,
    [switch]$ValidateOnly
)
. "$PSScriptRoot\common.ps1"
Assert-ReleaseTag $Tag
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid repository name.' }
function Invoke-Gh([string[]]$Arguments) {
    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub operation failed: $($Arguments[0])." }
    return $output
}
function Get-ReleaseTarget {
    $rows = Invoke-Gh @('api',"repos/$Repository/releases?per_page=100",'--paginate','--jq','.[] | {tagName: .tag_name, isDraft: .draft} | @json')
    $existing = @($rows | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object tagName -CEQ $Tag)
    if ($existing.Count -gt 1) { throw "More than one release uses $Tag." }
    if ($existing.Count -and !$existing[0].isDraft) { throw "Release $Tag is already published. Use a new version and tag; published assets are never replaced." }
    return $existing
}
if ($CheckOnly) { Get-ReleaseTarget | Out-Null; Write-Host "Release destination available: $Repository $Tag"; return }
if (!$ArtifactDirectory) { throw 'Specify the release artifact directory.' }
$ArtifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$version = $Tag.Substring(1)
$files = @()
foreach ($architecture in @('x64','arm64')) {
    $expected = @("PenPulse-$version-Setup-$architecture.exe","PenPulse-$version-Windows-$architecture.zip")
    $checksumPath = Join-Path $ArtifactDirectory "SHA256SUMS-$architecture.txt"
    $lines = @(Get-Content -LiteralPath $checksumPath | Where-Object { $_.Trim() })
    if ($lines.Count -ne 2) { throw "Expected two checksums for $architecture." }
    $seen = @{}
    foreach ($line in $lines) {
        if ($line -cnotmatch '^([0-9a-f]{64})  ([A-Za-z0-9.-]+)$') { throw 'Invalid checksum record.' }
        $hash = $Matches[1]; $name = $Matches[2]
        if ($name -cnotin $expected -or $seen.ContainsKey($name)) { throw "Unexpected checksum file: $name" }
        $seen[$name] = $true
        $file = Get-Item -LiteralPath (Join-Path $ArtifactDirectory $name)
        if ($file.Length -eq 0 -or (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant() -cne $hash) { throw "Release checksum mismatch: $name" }
        $files += $file
    }
}
$source = Get-Item -LiteralPath (Join-Path $ArtifactDirectory "PenPulse-$version-Source.zip")
if ($source.Length -eq 0) { throw 'Source archive is empty.' }
$files += $source
if ($ValidateOnly) { Write-Host 'Both architecture packages and source archive are present; checksums match.'; return }
$existing = @(Get-ReleaseTarget)
# Stable installer names make releases/latest/download links work across versions.
if ($Tag -notmatch '-') {
    $aliases = Join-Path $ArtifactDirectory 'latest-downloads'
    New-Item -ItemType Directory -Path $aliases -Force | Out-Null
    foreach ($architecture in @('x64','arm64')) {
        $alias = Join-Path $aliases "PenPulse-Windows-$architecture-Setup.exe"
        Copy-Item -LiteralPath (Join-Path $ArtifactDirectory "PenPulse-$version-Setup-$architecture.exe") -Destination $alias -Force
        $files += Get-Item -LiteralPath $alias
    }
}
$checksums = @($files | Sort-Object Name | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant(),$_.Name })
$checksumFile = Join-Path $ArtifactDirectory 'SHA256SUMS.txt'
[IO.File]::WriteAllLines($checksumFile,$checksums,(New-Object Text.UTF8Encoding($false)))
$files += Get-Item -LiteralPath $checksumFile
if (!$existing.Count) {
    Invoke-Gh @('release','create',$Tag,'--repo',$Repository,'--verify-tag','--draft','--title',"PenPulse $version",'--notes',
        'Choose x64 for Intel/AMD Windows PCs or ARM64 for Windows on Arm. Setup.exe installs the app; the Windows ZIP is portable. The .NET runtime and dependency notices are included. Builds are unsigned. Physical pen feedback depends on compatible hardware and drivers.','--generate-notes') | Out-Host
}
Invoke-Gh (@('release','upload',$Tag,'--repo',$Repository,'--clobber') + @($files.FullName)) | Out-Host
$release = (Invoke-Gh @('release','view',$Tag,'--repo',$Repository,'--json','databaseId,isDraft,tagName')) | ConvertFrom-Json
if (!$release.isDraft -or $release.tagName -cne $Tag) { throw 'Release changed during upload; refusing publication.' }
$assets = @(Invoke-Gh @('api',"repos/$Repository/releases/$($release.databaseId)/assets?per_page=100",'--paginate','--jq','.[] | @json') | ForEach-Object { $_ | ConvertFrom-Json })
if ($assets.Count -ne $files.Count) { throw 'Draft contains missing or unexpected assets.' }
foreach ($file in $files) {
    $asset = @($assets | Where-Object name -CEQ $file.Name)
    $digest = 'sha256:' + (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    if ($asset.Count -ne 1 -or $asset[0].state -ne 'uploaded' -or $asset[0].size -ne $file.Length -or $asset[0].digest -cne $digest) { throw "Uploaded asset verification failed: $($file.Name)" }
}
$flags = if ($Tag -match '-') { @('--prerelease','--latest=false') } else { @('--prerelease=false','--latest') }
Invoke-Gh (@('release','edit',$Tag,'--repo',$Repository,'--draft=false') + $flags) | Out-Host
