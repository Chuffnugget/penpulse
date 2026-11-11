#requires -Version 5.1
[CmdletBinding()]
param([string]$InnoSetupPath, [ValidateSet('win-x64','win-arm64')][string]$RuntimeIdentifier)
. "$PSScriptRoot\common.ps1"
if (!$RuntimeIdentifier) { $RuntimeIdentifier = Get-HostRuntimeIdentifier }
$architecture = $RuntimeIdentifier.Substring(4)
if ($RuntimeIdentifier -ne (Get-HostRuntimeIdentifier)) { throw 'Installer execution tests require a native runner for the target architecture.' }
$compiler = Resolve-InnoSetup $InnoSetupPath
Assert-PublishedVersion $RuntimeIdentifier
& "$PSScriptRoot\test-package.ps1" -RuntimeIdentifier $RuntimeIdentifier
# Compile the same recipe with an isolated identity. Never start the app or
# touch the real installation, its mutex, startup entry, or personal settings.
$id = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $script:RepoRoot ('.tools\installer-tests\'+$id)
$installDir = Join-Path $testRoot 'installed'
$startupName = 'PenPulse-Test-'+$id
$runKey = 'Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\PenPulse.Test.'+$id+'_is1'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$passed = 0
function Assert-Installer([bool]$Condition,[string]$Message) {
    if (!$Condition) { throw $Message }
    $script:passed++
    Write-Host "PASS $Message"
}
function Run-Setup([string]$Executable,[string[]]$Arguments) {
    $p = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "Installer command failed ($($p.ExitCode)). Logs: $testRoot" }
}
function Read-Startup {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKey)
    try { if ($key) { return $key.GetValue($startupName) }; return $null }
    finally { if ($key) { $key.Dispose() } }
}
function Write-Startup([string]$Value) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runKey)
    try { $key.SetValue($startupName,$Value) } finally { $key.Dispose() }
}
$success = $false
try {
    Invoke-Tool $compiler @('--quiet',("--define=AppVersion="+(Get-ProjectVersion)),("--define=AppArchitecture="+$architecture),
        ("--define=PublishDir="+(Get-PublishDirectory $RuntimeIdentifier)),("--define=ReleaseDir="+$testRoot),
        ("--define=SmokeTestId="+$id),(Join-Path $script:RepoRoot 'installer\PenPulse.iss'))
    $setup = Join-Path $testRoot ('PenPulse-'+(Get-ProjectVersion)+'-Setup-'+$architecture+'.exe')
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$installDir+'"'),('/LOG="'+(Join-Path $testRoot 'install.log')+'"'))
    $mutex = New-Object Threading.Mutex($true,('Local\PenPulse-Test-'+$id+'-'+[Environment]::UserName))
    try {
        $blocked = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
        Assert-Installer ($blocked.ExitCode -ne 0 -and !(Test-Path -LiteralPath (Join-Path $installDir 'PenPulse.exe'))) 'Setup refuses to modify a running application'
    }
    finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
    Run-Setup $setup $arguments
    $installedExe = Join-Path $installDir 'PenPulse.exe'
    $uninstaller = Join-Path $installDir 'unins000.exe'
    Assert-Installer (Test-Path -LiteralPath $installedExe) 'Fresh install creates the executable'
    Assert-Installer ((Get-FileHash -LiteralPath $installedExe).Hash -eq (Get-FileHash -LiteralPath (Join-Path (Get-PublishDirectory $RuntimeIdentifier) 'PenPulse.exe')).Hash) 'Installed executable matches the published build'
    & "$PSScriptRoot\test-package.ps1" -RuntimeIdentifier $RuntimeIdentifier -PackageDirectory $installDir
    Assert-Installer $true 'Installed package contains the MIT licence and complete verified third-party notices'
    $registration = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallKey)
    Assert-Installer ($null -ne $registration) 'Uninstaller is registered for the current user'
    if ($registration) { $registration.Dispose() }
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) ('PenPulse Installer Test '+$id+'\PenPulse.lnk')
    Assert-Installer (Test-Path -LiteralPath $shortcut) 'Start menu shortcut is created'
    Assert-Installer ($null -eq (Read-Startup)) 'Installation does not enable startup automatically'
    $data = Join-Path $installDir 'data'
    New-Item -ItemType Directory -Path $data | Out-Null
    $settings = Join-Path $data 'settings.json'
    [IO.File]::WriteAllText($settings,'{"audit-test":"preserve-me"}')
    $settingsHash = (Get-FileHash -LiteralPath $settings).Hash
    $ownedStartup = '"'+$installedExe+'" --silent'
    Write-Startup $ownedStartup
    Run-Setup $setup $arguments
    Assert-Installer ((Get-FileHash -LiteralPath $settings).Hash -eq $settingsHash) 'Reinstall preserves personal settings'
    Assert-Installer ((Read-Startup) -eq $ownedStartup) 'Reinstall preserves the optional startup setting'
    Run-Setup $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
    Assert-Installer (!(Test-Path -LiteralPath $installedExe)) 'Uninstall removes installed application files'
    Assert-Installer (!(Test-Path -LiteralPath (Join-Path $installDir 'licenses')) -and !(Test-Path -LiteralPath (Join-Path $installDir 'LICENSE'))) 'Uninstall removes installed licence files'
    Assert-Installer ((Get-FileHash -LiteralPath $settings).Hash -eq $settingsHash) 'Uninstall preserves personal settings'
    Assert-Installer ($null -eq (Read-Startup)) 'Uninstall removes its own startup entry'
    Assert-Installer (!(Test-Path -LiteralPath $shortcut)) 'Uninstall removes its Start menu shortcut'
    Run-Setup $setup $arguments
    $unrelated = '"C:\Unrelated portable copy\PenPulse.exe" --silent'
    Write-Startup $unrelated
    Run-Setup $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
    Assert-Installer ((Read-Startup) -eq $unrelated) 'Uninstall preserves a startup entry owned by another copy'
    $registration = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallKey)
    Assert-Installer ($null -eq $registration) 'Uninstall removes its registration'
    if ($registration) { $registration.Dispose() }
    $success = $true
    Write-Host "$passed installer checks passed. The running PenPulse app was not touched."
}
finally {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKey,$true)
    try { if ($key) { $key.DeleteValue($startupName,$false) } } finally { if ($key) { $key.Dispose() } }
    if ($success) { Remove-ProjectDirectory $testRoot }
    else { Write-Warning "Installer test artifacts retained for diagnosis: $testRoot" }
}
