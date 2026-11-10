#requires -Version 5.1
[CmdletBinding()]
param([string]$PackageDirectory, [string]$ZipPath)
. "$PSScriptRoot\common.ps1"
if (!$PackageDirectory) { $PackageDirectory = Get-PublishDirectory }
$root = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\')+'\'
$required = @('PenPulse.exe','README.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md','THIRD-PARTY-NOTICES.json','docs/DEVELOPING.md','docs/TESTING.md')
foreach ($name in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)) { throw "Package is missing $name" }
}
$actual = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $root 'PenPulse.exe')).ProductVersion.Split('+')[0]
if ($actual -ne (Get-ProjectVersion)) { throw 'Packaged executable has the wrong version.' }
foreach ($name in @('README.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md','docs/DEVELOPING.md','docs/TESTING.md')) {
    if ((Get-FileHash -LiteralPath (Join-Path $root $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $script:RepoRoot $name)).Hash) { throw "Stale packaged $name" }
}
$manifest = Get-Content -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.json') -Raw | ConvertFrom-Json
if ($manifest.applicationVersion -ne (Get-ProjectVersion)) { throw 'Stale notices manifest.' }
[xml]$project = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'PenPulse.csproj') -Raw
$references = @($project.Project.ItemGroup.FrameworkReference | Where-Object { $_ })
$expected = @{
    'Microsoft.NETCore.App.Runtime.win-x64' = [string]($references | Where-Object Update -eq 'Microsoft.NETCore.App').RuntimeFrameworkVersion
    'Microsoft.WindowsDesktop.App.Runtime.win-x64' = [string]($references | Where-Object Update -eq 'Microsoft.WindowsDesktop.App.WindowsForms').RuntimeFrameworkVersion
    'Microsoft.Windows.SDK.NET.Ref' = '10.0.22621.57'
}
if (@($manifest.components).Count -ne $expected.Count) { throw 'Unexpected component inventory.' }
$seen = @{}
foreach ($component in $manifest.components) {
    if (!$expected.ContainsKey($component.id) -or $expected[$component.id] -ne $component.version -or $seen.ContainsKey($component.id)) { throw "Unexpected component: $($component.id) $($component.version)" }
    $seen[$component.id] = $true
}
$expectedNotices = @(
    "licenses/Microsoft.NETCore.App.Runtime.win-x64-$($expected['Microsoft.NETCore.App.Runtime.win-x64'])/LICENSE.TXT",
    "licenses/Microsoft.NETCore.App.Runtime.win-x64-$($expected['Microsoft.NETCore.App.Runtime.win-x64'])/THIRD-PARTY-NOTICES.TXT",
    "licenses/Microsoft.WindowsDesktop.App.Runtime.win-x64-$($expected['Microsoft.WindowsDesktop.App.Runtime.win-x64'])/LICENSE",
    'licenses/Microsoft.Windows.SDK.NET.Ref-10.0.22621.57/WINDOWS-SDK-LICENSE.rtf',
    'licenses/Microsoft.Windows.SDK.NET.Ref-10.0.22621.57/CSWINRT-LICENSE.txt'
)
$seenPaths = @{}
foreach ($file in $manifest.files) {
    if ($file.path -notmatch '^licenses/[A-Za-z0-9.-]+/[A-Za-z0-9.-]+$' -or $seenPaths.ContainsKey($file.path)) { throw 'Invalid or duplicate notice path.' }
    $seenPaths[$file.path] = $true
    $path = Join-Path $root $file.path
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Missing or changed notice: $($file.path)" }
}
foreach ($notice in $expectedNotices) { if (!$seenPaths.ContainsKey($notice)) { throw "Required notice absent from manifest: $notice" } }
$licenseFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'licenses') -File -Recurse)
if ($licenseFiles.Count -ne $seenPaths.Count) { throw 'Unexpected files in the licences directory.' }
if ($ZipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ZipPath).Path)
    try {
        $expectedPaths = @($required)+@($manifest.files.path)
        $entries = @($archive.Entries | Where-Object { $_.Name })
        if ($entries.Count -ne $expectedPaths.Count) { throw 'Unexpected ZIP contents.' }
        $zipSeen = @{}
        foreach ($entry in $entries) {
            $relative = $entry.FullName.Replace('\','/')
            if ($relative -cnotin $expectedPaths -or $zipSeen.ContainsKey($relative)) { throw "Unexpected ZIP entry: $relative" }
            $zipSeen[$relative] = $true
            $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
            finally { $sha.Dispose(); $stream.Dispose() }
            if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $root $relative) -Algorithm SHA256).Hash) { throw "ZIP entry differs from published file: $relative" }
        }
    }
    finally { $archive.Dispose() }
}
Write-Host 'Package checks passed: application version, runtime versions, licences, notice hashes, and requested ZIP contents.'
