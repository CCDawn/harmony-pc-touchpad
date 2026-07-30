#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #error SourceDir must point to the published Windows application.
#endif
#ifndef OutputDir
  #error OutputDir must point to the installer output directory.
#endif
#ifndef SetupIconFile
  #error SetupIconFile must point to the generated application icon.
#endif

[Setup]
AppId={{A8B10FD7-0B34-4D65-8E5F-B12BF42D790C}
AppName=Harmony PC Touchpad
AppVersion={#AppVersion}
AppPublisher=CCDawn
AppPublisherURL=https://github.com/CCDawn/harmony-pc-touchpad
AppSupportURL=https://github.com/CCDawn/harmony-pc-touchpad/issues
DefaultDirName={localappdata}\Programs\Harmony PC Touchpad
DefaultGroupName=Harmony PC Touchpad
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=HarmonyPcTouchpad-Setup-{#AppVersion}
SetupIconFile={#SetupIconFile}
UninstallDisplayIcon={app}\HarmonyPcTouchpad.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\CCDawn.HarmonyPcTouchpad.Agent.Mutex
UsedUserAreasWarning=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: checkedonce
Name: "autostart"; Description: "Start automatically when I sign in"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Harmony PC Touchpad"; Filename: "{app}\HarmonyPcTouchpad.exe"; Parameters: "--show-pairing"; WorkingDir: "{app}"
Name: "{autodesktop}\Harmony PC Touchpad"; Filename: "{app}\HarmonyPcTouchpad.exe"; Parameters: "--show-pairing"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\Harmony PC Touchpad"; Filename: "{app}\HarmonyPcTouchpad.exe"; WorkingDir: "{app}"; Tasks: autostart

[Run]
Filename: "{app}\HarmonyPcTouchpad.exe"; Parameters: "--show-pairing"; Description: "Launch Harmony PC Touchpad"; Flags: nowait postinstall skipifsilent
