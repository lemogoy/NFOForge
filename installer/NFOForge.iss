
#define MyAppName "NFOForge"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Guidessence"
#define MyAppExeName "NFOForge.App.exe"

[Setup]
AppId=NFOForge.Guidessence
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\NFOForge
DefaultGroupName=NFOForge
OutputDir=..\dist\installer
OutputBaseFilename=NFOForge-Setup-1.0.0-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"; Flags: unchecked

[Files]
Source: "..\dist\NFOForge-1.0.0-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NFOForge"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\NFOForge"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer NFOForge"; Flags: nowait postinstall skipifsilent
