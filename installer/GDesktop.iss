#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\installer-app"
#endif

[Setup]
AppId={{73A54459-B32F-4E35-9377-021144E6B489}
AppName=GDesktop
AppVersion={#AppVersion}
AppPublisher=Eduardo Ellery
AppPublisherURL=https://github.com/eduardoellery/GDesktop-Public
DefaultDirName={localappdata}\Programs\GDesktop
DefaultGroupName=GDesktop
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\installer
OutputBaseFilename=GDesktop-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\GDesktop\Assets\GDesktop.ico
UninstallDisplayIcon={app}\GDesktop.ico
CloseApplications=yes
RestartApplications=no
InfoBeforeFile=LEIA-ANTES.txt
SetupLogging=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos adicionais:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.db,*.db-*,*.pfx,*.key"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\GDesktop\GDesktop"; Filename: "{app}\GDesktop.exe"; WorkingDir: "{app}"; IconFilename: "{app}\GDesktop.ico"
Name: "{userprograms}\GDesktop\Desinstalar GDesktop"; Filename: "{uninstallexe}"
Name: "{userdesktop}\GDesktop"; Filename: "{app}\GDesktop.exe"; WorkingDir: "{app}"; IconFilename: "{app}\GDesktop.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\GDesktop.exe"; Description: "Abrir GDesktop"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

; User data at %LOCALAPPDATA%\GDesktop is deliberately outside {app}.
; Uninstall removes installed files and shortcuts, preserving the personal index.
