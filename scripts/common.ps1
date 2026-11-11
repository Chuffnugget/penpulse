#requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RepoRoot = Split-Path -Parent $PSScriptRoot

function Get-ProjectVersion {
    [xml]$project = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'PenPulse.csproj') -Raw
    return [string]$project.Project.PropertyGroup.Version
}

function Invoke-Tool {
    param([string]$FilePath, [string[]]$Arguments)
    & $FilePath @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$FilePath failed with exit code $LASTEXITCODE." }
}

function Resolve-DotNet {
    param([string]$Path)
    if (!$Path) { $Path = $env:PENPULSE_DOTNET }
    if (!$Path) {
        $local = Join-Path $script:RepoRoot '.tools\dotnet\dotnet.exe'
        if (Test-Path -LiteralPath $local) { $Path = $local }
        else { $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue; if ($command) { $Path = $command.Source } }
    }
    if (!$Path -or !(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'Install .NET 10 with scripts\setup-tools.ps1, or pass -DotNetPath.'
    }
    $sdks = & $Path --list-sdks
    if ($LASTEXITCODE -ne 0 -or !($sdks -match '^10\.')) {
        throw 'A .NET 10 SDK is required. Run scripts\setup-tools.ps1 or pass -DotNetPath.'
    }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_NOLOGO = '1'
    if (!$env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $script:RepoRoot '.tools\dotnet-home' }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Resolve-InnoSetup {
    param([string]$Path)
    if (!$Path) { $Path = $env:PENPULSE_ISCC }
    if (!$Path) {
        $candidates = @((Join-Path $script:RepoRoot '.tools\inno\ISCC.exe'))
        if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe' }
        if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe' }
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $candidates += $command.Source }
        $Path = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
    if (!$Path -or !(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'Install Inno Setup 7 with scripts\setup-tools.ps1 -IncludeInstaller, or pass -InnoSetupPath.'
    }
    $compilerVersion = (& $Path --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $compilerVersion -notmatch '^\d+\.\d+\.\d+$' -or ([version]$compilerVersion).Major -lt 7) { throw 'Inno Setup 7 or newer is required.' }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-HostRuntimeIdentifier {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    if ($architecture -eq 'Arm64') { return 'win-arm64' }
    if ($architecture -eq 'X64') { return 'win-x64' }
    throw "Unsupported host architecture: $architecture"
}

function Get-PublishDirectory {
    param([ValidateSet('win-x64','win-arm64')][string]$RuntimeIdentifier = (Get-HostRuntimeIdentifier))
    return Join-Path $script:RepoRoot ('artifacts\publish\'+(Get-ProjectVersion)+'\'+$RuntimeIdentifier)
}
function Get-ReleaseDirectory { return Join-Path $script:RepoRoot 'artifacts\release' }

function Get-ExecutableRuntimeIdentifier {
    param([string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Not a Windows executable.' }
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt $stream.Length - 6) { throw 'Invalid executable header.' }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550) { throw 'Invalid PE signature.' }
        switch ($reader.ReadUInt16()) {
            0x8664 { return 'win-x64' }
            0xaa64 { return 'win-arm64' }
            default { throw 'Unsupported executable architecture.' }
        }
    } finally { $reader.Dispose(); $stream.Dispose() }
}

function Assert-ReleaseTag {
    param([string]$Tag)
    if ($Tag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:alpha|beta|rc)\.[1-9][0-9]*)?$') {
        throw 'Use a version tag such as v0.0.1 or v0.0.2-beta.1.'
    }
    if ($Tag.Substring(1) -cne (Get-ProjectVersion)) { throw "Tag $Tag does not match PenPulse.csproj version $(Get-ProjectVersion)." }
}

function Assert-PublishedVersion {
    param([ValidateSet('win-x64','win-arm64')][string]$RuntimeIdentifier = (Get-HostRuntimeIdentifier))
    $exe = Join-Path (Get-PublishDirectory $RuntimeIdentifier) 'PenPulse.exe'
    if (!(Test-Path -LiteralPath $exe)) { throw 'No published executable. Run scripts\publish.ps1 first.' }
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion.Split('+')[0]
    if ($actual -ne (Get-ProjectVersion)) { throw "Published version $actual does not match the project. Publish again." }
    if ((Get-ExecutableRuntimeIdentifier $exe) -ne $RuntimeIdentifier) { throw 'Published executable has the wrong architecture.' }
}

function Remove-ProjectDirectory {
    param([string]$Path)
    $root = [IO.Path]::GetFullPath($script:RepoRoot).TrimEnd('\') + '\'
    $target = [IO.Path]::GetFullPath($Path)
    if (!$target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to remove outside project: $target" }
    if (!(Test-Path -LiteralPath $target)) { return }
    $item = Get-Item -LiteralPath $target -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing to remove a linked directory: $target" }
    $linked = Get-ChildItem -LiteralPath $target -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1
    if ($linked) { throw "Refusing to remove a directory containing links: $target" }
    Remove-Item -LiteralPath $target -Force -Recurse
}
