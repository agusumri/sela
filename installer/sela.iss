; Sela installer (Inno Setup 6). Per-user install, no admin rights needed.
; Build: ISCC.exe /DAppVersion=1.0.0 installer\sela.iss
; Expects the published single-file exe at dist\publish\Sela.exe (override with /DPublishDir=<dir>).
; Wizard art comes from installer\art (regenerate with tools\make-installer-art.ps1).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "Sela"
#define AppExe "Sela.exe"
#define AppPublisher "Agus Umriadi"
#define AppUrl "https://github.com/agusumri/sela"
#define Root ".."
#ifndef PublishDir
  #define PublishDir Root + "\dist\publish"
#endif

; Indonesian.isl is an unofficial translation and is not bundled with every Inno 6 install.
#if FileExists(AddBackslash(CompilerPath) + "Languages\Indonesian.isl")
  #define HaveIndonesian
#endif

[Setup]
; Fixed AppId: keeps upgrades and uninstall entries tied to the same product. Never change it.
AppId={{5E1A7C3B-9D2F-4B8E-A6C1-3F7D0B2E9A54}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile={#Root}\LICENSE
SetupIconFile={#Root}\src\Sela\Assets\Sela.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#Root}\dist
OutputBaseFilename=Sela-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
; Forced dark: matches the true-black brand and the dark wizard art in every Windows theme.
WizardStyle=modern dark
; Art is pre-rendered at each DPI's exact image-area size (Inno 6.6+ defaults), so Setup picks a 1:1 match.
WizardImageFile=art\wizard-202x386.png,art\wizard-269x515.png,art\wizard-336x643.png,art\wizard-403x772.png,art\wizard-430x824.png,art\wizard-498x953.png,art\wizard-534x1022.png
WizardSmallImageFile=art\small-58.png,art\small-77.png,art\small-97.png,art\small-116.png,art\small-124.png,art\small-143.png,art\small-159.png
WizardImageStretch=no
WizardImageBackColor=#0E1116
; Inno hides the Welcome page by default; show it so the branded panel greets the user.
DisableWelcomePage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Close a running Sela on upgrade/uninstall (mutex name from App.xaml.cs).
AppMutex=Local\Sela.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
#ifdef HaveIndonesian
Name: "id"; MessagesFile: "compiler:Languages\Indonesian.isl"
#endif
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
en.AutostartTask=Start Sela with Windows
en.LaunchApp=Run Sela
#ifdef HaveIndonesian
id.AutostartTask=Mulai bersama Windows
id.LaunchApp=Jalankan Sela
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartTask}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value name the app uses (Services/Autostart.cs), so the in-app toggle and installer agree.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Sela"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

; User data in %LOCALAPPDATA%\Sela is intentionally left in place.

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

// The app can set the Run value itself (Settings toggle). Remove it on uninstall,
// but only when it points at this install, so a portable copy's autostart survives.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, RunKey, 'Sela', Value) then
      if Pos(Lowercase(ExpandConstant('{app}\{#AppExe}')), Lowercase(Value)) > 0 then
        RegDeleteValue(HKCU, RunKey, 'Sela');
end;
