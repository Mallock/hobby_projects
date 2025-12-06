[Setup]
AppId={{A1B2C3D4-E5F6-47A8-9B0C-1234567890AB}
AppName=MinimalBrowser
AppVersion=1.0.0
AppPublisher=Mika Leiman
AppPublisherURL=https://github.com/Mallock/hobby_projects
AppSupportURL=https://github.com/Mallock/hobby_projects/issues
AppUpdatesURL=https://github.com/Mallock/hobby_projects/releases
DefaultDirName={userappdata}\MinimalBrowser
DefaultGroupName=MinimalBrowser
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=MinimalBrowserSetup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\chrome_canary_browser_logo_icon_153008.ico
SetupIconFile=.\MinimalBrowser\chrome_canary_browser_logo_icon_153008.ico
VersionInfoVersion=1.0.0.0
VersionInfoCompany=YourName
VersionInfoDescription=MinimalBrowser - A minimal .NET browser

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: ".\MinimalBrowser\bin\Release\net10.0-windows7.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: ".\MinimalBrowser\chrome_canary_browser_logo_icon_153008.ico"; DestDir: "{app}"

[Icons]
Name: "{userdesktop}\MinimalBrowser"; Filename: "{app}\MinimalBrowser.exe"; WorkingDir: "{app}"; IconFilename: "{app}\chrome_canary_browser_logo_icon_153008.ico"
Name: "{userappdata}\Microsoft\Windows\Start Menu\Programs\MinimalBrowser.lnk"; Filename: "{app}\MinimalBrowser.exe"; WorkingDir: "{app}"; IconFilename: "{app}\chrome_canary_browser_logo_icon_153008.ico"

[Run]
Filename: "{app}\MinimalBrowser.exe"; Description: "Launch MinimalBrowser"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\MinimalBrowser"; Flags: uninsdeletekeyifempty

[Code]
// Optionally check for .NET 10 runtime and show a warning if not found
function IsDotNet10Installed(): Boolean;
var
  key: string;
begin
  // This is a placeholder. .NET 10 detection may require a custom check or external tool.
  // You may want to update this for the actual .NET 10 registry key when available.
  key := 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full';
  Result := RegValueExists(HKLM, key, 'Release');
end;

procedure InitializeWizard;
begin
  if not IsDotNet10Installed then
    MsgBox('Warning: .NET 10 runtime was not detected. The application may not run unless it is installed.', mbInformation, MB_OK);
end;