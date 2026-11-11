#ifndef AppVersion
  #error AppVersion must be supplied by scripts/build-installer.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by scripts/build-installer.ps1
#endif
#ifndef ReleaseDir
  #error ReleaseDir must be supplied by scripts/build-installer.ps1
#endif
#ifndef AppArchitecture
  #error AppArchitecture must be x64 or arm64
#endif
#if AppArchitecture == "arm64"
  #define AllowedArchitecture "arm64"
#elif AppArchitecture == "x64"
  #define AllowedArchitecture "x64compatible and not arm64"
#else
  #error Unsupported AppArchitecture
#endif
#ifdef SmokeTestId
  #define ProductName "PenPulse Installer Test " + SmokeTestId
  #define ProductId "PenPulse.Test." + SmokeTestId
  #define StartupName "PenPulse-Test-" + SmokeTestId
  #define MutexName "Local\PenPulse-Test-" + SmokeTestId + "-{username}"
#else
  #define ProductName "PenPulse"
  #define ProductId "PenPulse.6b76f877-bd2f-44a5-9c14-1ad286d0f82a"
  #define StartupName "PenPulse"
  #define MutexName "Local\PenPulse-{username}"
#endif

[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#AppVersion}
AppVerName={#ProductName} {#AppVersion}
AppCopyright=Copyright (c) 2026 PenPulse contributors
LicenseFile={#PublishDir}\licenses\Microsoft.Windows.SDK.NET.Ref-10.0.22621.57\WINDOWS-SDK-LICENSE.rtf
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
PrivilegesRequired=lowest
ArchitecturesAllowed={#AllowedArchitecture}
ArchitecturesInstallIn64BitMode={#AllowedArchitecture}
MinVersion=10.0.22000
AppMutex={#MutexName}
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir={#ReleaseDir}
OutputBaseFilename=PenPulse-{#AppVersion}-Setup-{#AppArchitecture}
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\PenPulse.exe
UninstallDisplayName={#ProductName}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\PenPulse.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\THIRD-PARTY-NOTICES.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs

[Messages]
LicenseLabel=PenPulse source code is MIT-licensed. Its bundled Windows SDK components have separate Microsoft terms shown below.

[Icons]
Name: "{autoprograms}\{#ProductName}\PenPulse"; Filename: "{app}\PenPulse.exe"
Name: "{autoprograms}\{#ProductName}\Uninstall"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\PenPulse.exe"; Tasks: desktopicon

[Run]
#ifndef SmokeTestId
Filename: "{app}\PenPulse.exe"; Description: "Open PenPulse"; Flags: nowait postinstall skipifsilent
#endif

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Remove only this installation's optional startup entry. Another portable
    // copy may own the same value. Personal data is deliberately not deleted.
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      '{#StartupName}', StartupCommand) then
      if CompareText(StartupCommand, '"' + ExpandConstant('{app}\PenPulse.exe') + '" --silent') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#StartupName}');
  end;
end;
