; LostAndDivine — установщик (Inno Setup).
; Компилируется: iscc.exe installer.iss
; Либо: .\build-installer.ps1  (сам найдёт ISCC или подскажет скачать).
;
; Установка идёт в %LOCALAPPDATA%\Programs\LostAndDivine (пользовательская папка,
; без UAC), чтобы лаунчер мог сам себя обновлять без прав администратора.

#define MyAppName "LostAndDivine"
#define MyAppVersion "0.1.0"
#define MyPublisher "LostAndDivine"

[Setup]
AppId={{8E2C9A1B-3D4E-4F5A-9B6C-7D8E9F0A1B2C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=dist
OutputBaseFilename={#MyAppName}-Setup
Compression=lzma2
SolidCompression=yes
; Обновляемая игра не должна требовать прав админа:
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
DisableProgramGroupPage=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "LostAndDivine.Server\client_build\install_source\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\LostAndDivine.Launcher.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\LostAndDivine.Launcher.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на Рабочем столе"; GroupDescription: "Дополнительно:"

[Run]
Filename: "{app}\LostAndDivine.Launcher.exe"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent
