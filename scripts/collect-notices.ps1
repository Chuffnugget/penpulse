#requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('win-x64','win-arm64')][string]$RuntimeIdentifier)
. "$PSScriptRoot\common.ps1"
if (!$RuntimeIdentifier) { $RuntimeIdentifier = Get-HostRuntimeIdentifier }
$architecture = $RuntimeIdentifier.Substring(4)
[xml]$project = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'PenPulse.csproj') -Raw
$framework = [string]$project.Project.PropertyGroup.TargetFramework
$deps = Get-Content -LiteralPath (Join-Path $script:RepoRoot "bin\Release\$framework\$RuntimeIdentifier\PenPulse.deps.json") -Raw | ConvertFrom-Json
$assets = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'obj\project.assets.json') -Raw | ConvertFrom-Json
$output = Get-PublishDirectory $RuntimeIdentifier
$licenseDir = Join-Path $output 'licenses'
Remove-ProjectDirectory $licenseDir
New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
$components = @()
$files = @()
foreach ($library in $deps.libraries.PSObject.Properties) {
    if ($library.Name -eq ('PenPulse/'+(Get-ProjectVersion))) { continue }
    if ($library.Name -notmatch '^runtimepack\.(.+)/([^/]+)$') { throw "Unreviewed bundled dependency: $($library.Name)" }
    $id = $Matches[1]; $version = $Matches[2]
    $package = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder ($id.ToLowerInvariant()+'\'+$version)
        if (Test-Path -LiteralPath $candidate -PathType Container) { $package = $candidate; break }
    }
    if (!$package) { throw "Missing restored package: $id $version" }
    $sources = @()
    switch ($id) {
        "Microsoft.NETCore.App.Runtime.$RuntimeIdentifier" {
            $sources = @((Join-Path $package 'LICENSE.TXT'), (Join-Path $package 'THIRD-PARTY-NOTICES.TXT'))
        }
        "Microsoft.WindowsDesktop.App.Runtime.$RuntimeIdentifier" {
            $sources = @((Join-Path $package 'LICENSE'))
            $sources += @(Get-ChildItem -LiteralPath $package -File -Filter '*NOTICE*' | Select-Object -ExpandProperty FullName)
        }
        'Microsoft.Windows.SDK.NET.Ref' {
            if ($version -ne '10.0.22621.57') { throw 'Review the Windows SDK licence and C#/WinRT version before changing this package.' }
            $winrt = Join-Path $package 'lib\net8.0\WinRT.Runtime.dll'
            $winrtVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($winrt).ProductVersion
            if ($winrtVersion -ne '2.2.0.48161+8649ee3eeb2445ca2a36d80d878ef60b96a6c65d') { throw "Review the bundled C#/WinRT licence: $winrtVersion" }
            $sources = @((Join-Path $script:RepoRoot 'third-party\WINDOWS-SDK-LICENSE.rtf'), (Join-Path $script:RepoRoot 'third-party\CSWINRT-LICENSE.txt'))
        }
        default { throw "Unreviewed runtime pack: $id $version" }
    }
    $components += [ordered]@{ id=$id; version=$version }
    $destination = Join-Path $licenseDir "$id-$version"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($source in $sources) {
        Copy-Item -LiteralPath $source -Destination $destination -Force
        $relative = "licenses/$id-$version/"+[IO.Path]::GetFileName($source)
        $files += [ordered]@{ path=$relative; sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}
$manifest = [ordered]@{ applicationVersion=(Get-ProjectVersion); runtimeIdentifier=$RuntimeIdentifier; components=$components; files=$files }
$json = $manifest | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText((Join-Path $output 'THIRD-PARTY-NOTICES.json'), $json+[Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
Write-Host "Collected licence notices for $($components.Count) bundled runtime packs."
