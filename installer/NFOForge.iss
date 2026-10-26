; ============================================================
; NFOForge - Installateur Windows
; Version : 1.0.2
; Editeur : Guidessence
; Framework : .NET 10 / WPF
; Architecture : Windows x64
; ============================================================

#define MyAppName "NFOForge"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "Guidessence"
#define MyAppExeName "NFOForge.App.exe"

[Setup]

; ============================================================
; IDENTITE DU LOGICIEL
; ============================================================

; Ne jamais modifier cet identifiant entre les versions.
AppId=NFOForge.Guidessence

AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

AppCopyright=Copyright (C) 2026 Guidessence
AppComments=Editeur professionnel de fiches NFO et BBCode

; ============================================================
; METADONNEES WINDOWS DE L'INSTALLATEUR
; ============================================================

VersionInfoVersion=1.0.2.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=NFOForge - Programme d'installation
VersionInfoProductName={#MyAppName}

; ============================================================
; IDENTITE GRAPHIQUE
; ============================================================

SetupIconFile=..\src\NFOForge.App\Resources\NFOForge.ico

WizardStyle=modern

UninstallDisplayIcon={app}\{#MyAppExeName}

; ============================================================
; INSTALLATION WINDOWS
; ============================================================

DefaultDirName={autopf}\NFOForge
DefaultGroupName=NFOForge

PrivilegesRequired=admin

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; ============================================================
; GESTION DES MISES A JOUR
; ============================================================

UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
UsePreviousLanguage=yes

; Demander la fermeture des applications utilisant
; les fichiers devant etre remplaces.

CloseApplications=yes
RestartApplications=no

; ============================================================
; GENERATION DU SETUP
; ============================================================

OutputDir=..\dist\installer

OutputBaseFilename=NFOForge-Setup-1.0.2-win-x64

Compression=lzma2
SolidCompression=yes

; ============================================================
; DESINSTALLATION
; ============================================================

Uninstallable=yes
CreateUninstallRegKey=yes

[Languages]

Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]

Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"; Flags: unchecked

[Files]
; Application NFOForge 1.0.2 et dependances Windows.
; Exclusion des symboles de debogage.

Source: "..\dist\NFOForge-1.0.2-win-x64\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]

Name: "{group}\NFOForge"; Filename: "{app}\{#MyAppExeName}"

Name: "{autodesktop}\NFOForge"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]

Filename: "{app}\{#MyAppExeName}"; Description: "Lancer NFOForge"; Flags: nowait postinstall skipifsilent