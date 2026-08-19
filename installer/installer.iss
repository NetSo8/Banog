; Banog — Inno Setup script.
;
; Local:    run ISCC directly, or `publish.cmd` then ISCC.
; CI:       the release workflow passes /DAppVersion, /DMySourceDir and /DMyOutputDir.

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef MySourceDir
  #define MySourceDir "..\publish\app"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "..\artifacts"
#endif

#define MyAppName "Banog"
#define MyAppPublisher "Banog"
#define MyAppExeName "Banog.exe"

[Setup]
AppId={{8808B0C7-801C-46B8-A929-23F86AC8898D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#MyOutputDir}
OutputBaseFilename=Banog-setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#MySourceDir}\Banog.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\*.dll";  DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
; Startup with Windows: the value is created at install (so uninsdeletevalue can remove
; it), and the app rewrites it on every launch to keep the path aligned if it is moved.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Banog"; ValueData: """{app}\{#MyAppExeName}"" --background"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
