; Inno Setup script for Daisy's Server. Built by build.ps1, which passes AppVersion and PublishDir.
; The in-app updater downloads this installer from GitHub Releases (rosscarlson/DaisysServer) and runs it with /SILENT.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

#define AppName "Daisy's Server"
#define AppShortName "DaisysServer"
#define AppExe "DaisysServer.exe"
#define RepoUrl "https://github.com/rosscarlson/DaisysServer"

[Setup]
; Never change the AppId: upgrades and the updater rely on it.
AppId={{48F1E574-1C87-4FCC-A041-4AACFEAC298A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=rosscarlson
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Daisys Server
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
OutputBaseFilename={#AppShortName}-Setup-{#AppVersion}
SetupIconFile=..\src\DaisysServer\Assets\DaisysServer.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; the server runs in the tray: closed for the upgrade, started again after
CloseApplications=force
CloseApplicationsFilter=*.exe
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; "Start when I sign in" is set by the server (Settings); removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppShortName}"; Flags: uninsdeletevalue

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Windows Firewall: clients may connect to the server (on whatever port it's set to); made again on each install
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#AppName}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#AppName}"" dir=in action=allow program=""{app}\{#AppExe}"" enable=yes profile=any"; Flags: runhidden waituntilterminated; StatusMsg: "Letting clients through Windows Firewall..."
; Interactive install: the Launch checkbox on the last page.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Silent install (an update): started again as the signed-in user, not elevated.
Filename: "{app}\{#AppExe}"; Flags: nowait skipifnotsilent runasoriginaluser

[UninstallRun]
; ask a running copy to close cleanly (its users' data saved), then make sure it's gone
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitApp"
Filename: "{sys}\timeout.exe"; Parameters: "/t 2 /nobreak"; Flags: runhidden; RunOnceId: "WaitApp"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#AppName}"""; Flags: runhidden waituntilterminated; RunOnceId: "Firewall"
