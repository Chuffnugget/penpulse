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

function Get-PublishDirectory { return Join-Path $script:RepoRoot ('artifacts\publish\'+(Get-ProjectVersion)+'\win-x64') }
function Get-ReleaseDirectory { return Join-Path $script:RepoRoot 'artifacts\release' }

function Assert-PublishedVersion {
    $exe = Join-Path (Get-PublishDirectory) 'PenPulse.exe'
    if (!(Test-Path -LiteralPath $exe)) { throw 'No published executable. Run scripts\publish.ps1 first.' }
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion.Split('+')[0]
    if ($actual -ne (Get-ProjectVersion)) { throw "Published version $actual does not match the project. Publish again." }
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
