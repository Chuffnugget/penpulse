#requires -Version 5.1
[CmdletBinding()]
param([switch]$IncludeInstaller)
. "$PSScriptRoot\common.ps1"
if ($env:OS -ne 'Windows_NT' -or ![Environment]::Is64BitOperatingSystem) { throw 'These build tools require 64-bit Windows.' }
$manifest = Get-Content -LiteralPath "$PSScriptRoot\tools.json" -Raw | ConvertFrom-Json
$sdk = if ((Get-HostRuntimeIdentifier) -eq 'win-arm64') { $manifest.dotnetArm64 } else { $manifest.dotnet }
$toolRoot = Join-Path $script:RepoRoot '.tools'
$downloads = Join-Path $toolRoot 'downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
function Get-VerifiedDownload($entry, [string]$algorithm, [string]$expected) {
    $archive = Join-Path $downloads $entry.file
    if (!(Test-Path -LiteralPath $archive)) {
        Write-Host "Downloading $($entry.file)..."
        Invoke-WebRequest -Uri $entry.url -OutFile ($archive+'.partial') -UseBasicParsing
        if ((Get-FileHash -LiteralPath ($archive+'.partial') -Algorithm $algorithm).Hash -ne $expected) { throw 'Downloaded tool checksum does not match tools.json.' }
        Move-Item -LiteralPath ($archive+'.partial') -Destination $archive
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm $algorithm).Hash -ne $expected) { throw "Cached tool checksum mismatch: $archive" }
    return $archive
}
$dotnet = Join-Path $toolRoot 'dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet) -or (Get-ExecutableRuntimeIdentifier $dotnet) -ne (Get-HostRuntimeIdentifier) -or ((& $dotnet --version) -ne $sdk.version)) {
    $archive = Get-VerifiedDownload $sdk 'SHA512' $sdk.sha512
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $toolRoot 'dotnet') -Force
}
if ((& $dotnet --version) -ne $sdk.version) { throw 'The local .NET SDK was not installed correctly.' }
if ((Get-ExecutableRuntimeIdentifier $dotnet) -ne (Get-HostRuntimeIdentifier)) { throw 'The SDK must match the native host architecture.' }
Write-Host "Ready: .NET SDK $($sdk.version)"
if ($IncludeInstaller) {
    $compiler = Join-Path $toolRoot 'inno\ISCC.exe'
    if (!(Test-Path -LiteralPath $compiler) -or ((& $compiler --version) -ne $manifest.inno.version)) {
        $archive = Get-VerifiedDownload $manifest.inno 'SHA256' $manifest.inno.sha256
        $signature = Get-AuthenticodeSignature -LiteralPath $archive
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Pyrsys B\.V\.') { throw 'The Inno Setup publisher signature could not be verified.' }
        $destination = Join-Path $toolRoot 'inno'
        $process = Start-Process -FilePath $archive -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS','/TASKS=',('/DIR="'+$destination+'"')) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)) { throw "Inno Setup installation failed ($($process.ExitCode))." }
    }
    $null = Resolve-InnoSetup $compiler
    Write-Host 'Ready: Inno Setup compiler'
}
Write-Host 'Tools are local to .tools; no system PATH changes are needed.'
