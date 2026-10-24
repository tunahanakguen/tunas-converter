; Inno-Setup-Skript für TUNAS CONVERTER.
; Wird von tools\build-installer.ps1 aufgerufen; AppVersion und SourceDir kommen von dort.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\app"
#endif

[Setup]
AppId={{6B0E2A51-7C4D-4E1B-9C8A-3F2D7E5A1B90}
AppName=TUNAS CONVERTER
AppVersion={#AppVersion}
AppVerName=TUNAS CONVERTER {#AppVersion}
AppPublisher=Tunahan A.
AppCopyright=Copyright (C) 2026 Tunahan A.
DefaultDirName={autopf}\TUNAS Converter
DefaultGroupName=TUNAS CONVERTER
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\TuNAS.Converter\Resources\TunasConverter.ico
UninstallDisplayIcon={app}\TuNASConverter.exe
UninstallDisplayName=TUNAS CONVERTER
OutputDir=..\publish
OutputBaseFilename=TUNAS-Converter-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Immer für alle Benutzer nach C:\Program Files (nie ins Benutzerprofil)
PrivilegesRequired=admin
WizardStyle=modern
MinVersion=10.0

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\TUNAS CONVERTER"; Filename: "{app}\TuNASConverter.exe"
Name: "{group}\{cm:UninstallProgram,TUNAS CONVERTER}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TUNAS CONVERTER"; Filename: "{app}\TuNASConverter.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TuNASConverter.exe"; Description: "{cm:LaunchProgram,TUNAS CONVERTER}"; Flags: nowait postinstall skipifsilent
