#requires -Version 5.1
[CmdletBinding()]
param([string]$DotNetPath, [ValidateSet('win-x64','win-arm64')][string]$RuntimeIdentifier)
. "$PSScriptRoot\common.ps1"
if (!$RuntimeIdentifier) { $RuntimeIdentifier = Get-HostRuntimeIdentifier }
$architecture = $RuntimeIdentifier.Substring(4)
$dotnet = Resolve-DotNet $DotNetPath
$output = Get-PublishDirectory $RuntimeIdentifier
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $script:RepoRoot
try {
    Invoke-Tool $dotnet @('publish','PenPulse.csproj','-c','Release','-r',$RuntimeIdentifier,'--self-contained','true',
        '-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true','-p:DebugType=None','-o',$output,'--nologo')
    foreach ($name in @('README.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $script:RepoRoot $name) -Destination $output -Force }
    Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'docs') -Destination $output -Recurse -Force
    & "$PSScriptRoot\collect-notices.ps1" -RuntimeIdentifier $RuntimeIdentifier
    Assert-PublishedVersion $RuntimeIdentifier
    & "$PSScriptRoot\test-package.ps1" -RuntimeIdentifier $RuntimeIdentifier
    Write-Host "Published: $output"
}
finally { Pop-Location }
