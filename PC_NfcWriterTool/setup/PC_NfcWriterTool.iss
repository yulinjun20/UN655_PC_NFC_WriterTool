; UN655 NFC 发卡工具 — Inno Setup 6
; Build: run setup\build_setup.ps1 (fills staging\, then ISCC this file)

#define MyAppName "UN655 NFC 发卡工具"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "UN655"
#define MyAppExeName "PC_NfcWriterTool.exe"
#define MyAppId "{{8B7C2A11-4E6F-4D3A-9C1B-6F0E2A9D4B71}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\UN655\NFC发卡工具
DefaultGroupName=UN655
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=UN655_NFC_发卡工具_Setup_{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
SetupLogging=yes
; User SQLite lives under %LocalAppData%\UN655\PC_NfcWriterTool\ — not removed on uninstall.

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"

[Files]
Source: "staging\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function DotNet472Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
  begin
    { 461808 = .NET Framework 4.7.2 }
    Result := Release >= 461808;
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrCode: Integer;
begin
  Result := True;
  if not DotNet472Installed then
  begin
    if MsgBox(
      '未检测到 .NET Framework 4.7.2 或更高版本。' + #13#10 +
      'Win7 / 部分工控机需要先安装运行库，否则程序无法启动。' + #13#10 + #13#10 +
      '是否打开微软下载页面？' + #13#10 +
      '（仍可继续安装本软件）',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open',
        'https://dotnet.microsoft.com/download/dotnet-framework/net472',
        '', '', SW_SHOWNORMAL, ewNoWait, ErrCode);
    end;
  end;
end;

