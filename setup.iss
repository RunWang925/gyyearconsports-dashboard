; 贵渝意尔康体育数据大屏 安装包脚本
; 编译：ISCC.exe setup.iss
; 特性：正规安装向导 / 安装到 Program Files / 写注册表（应用和功能可见、可卸载）
;       运行数据（日志/登录会话/config.json）存放在 %LOCALAPPDATA%\YuErKangDashboard，
;       卸载清缓存但保留配置；开机自启注册表值在卸载时一并清理

[Setup]
AppName=贵渝意尔康体育数据大屏
AppVersion=2.2.1
AppVerName=贵渝意尔康体育数据大屏 2.2.1
VersionInfoVersion=2.2.1
AppPublisher=Run
; 64 位安装模式：{autopf} 解析为 C:\Program Files
ArchitecturesInstallIn64BitMode=x64compatible
DefaultDirName={autopf}\YuErKangDashboard
PrivilegesRequired=admin
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=意尔康大屏程序安装包-2.2.1
SetupIconFile=icon_cockpit.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=贵渝意尔康体育数据大屏
UninstallDisplayIcon={app}\DashboardKiosk.exe

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加任务："; Flags: checkedonce

[Files]
Source: "dist\单程序测试\DashboardKiosk.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\单程序测试\config.default.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\单程序测试\使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\贵渝意尔康体育数据大屏"; Filename: "{app}\DashboardKiosk.exe"
Name: "{autodesktop}\贵渝意尔康体育数据大屏"; Filename: "{app}\DashboardKiosk.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DashboardKiosk.exe"; Description: "立即运行贵渝意尔康体育数据大屏"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载清理安装目录里的默认配置模板；运行数据在 %LOCALAPPDATA% 单独清理
Type: files; Name: "{app}\config.default.json"

[Code]
// 卸载收尾：清掉开机自启注册表值（若用户在设置里开启过）；
// 清理 %LOCALAPPDATA% 下的日志/登录会话/设置，但保留 config.json（大屏地址/类型，重装自动复用）
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'DashboardKiosk');
    DelTree(ExpandConstant('{localappdata}\YuErKangDashboard\UserData'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\YuErKangDashboard\logs'), True, True, True);
    DeleteFile(ExpandConstant('{localappdata}\YuErKangDashboard\settings.json'));
  end;
end;
