# AI 项目交接文档（AI_PROJECT_CONTEXT.md）

> **代码仓库**：https://github.com/RunWang925/gyyearconsports-dashboard.git （Private，main 分支）
> 拉取：`git clone https://github.com/RunWang925/gyyearconsports-dashboard.git`
> **AI 快速接手**：仓库内 `skills/yuerkang-dashboard/SKILL.md` 是打包好的接手技能（含仓库地址、必读文档、硬性约束、交付流程），发给 AI 助手即可自动接管。

> **给接手的 AI 的说明**：把本文件连同项目源码（或本文件单独）发给任意 AI 助手，它即可立刻理解本项目全貌并直接修改代码，无需重新摸索。本文档由开发过程 AI 于 2026-11-25 编写，随代码同步更新。

---

## 1. 一句话概括

**Windows 大屏壳程序**：C#/.NET 8 WinForms + WebView2，全屏展示公司内网帆软 BI（帆软 FineReport 决策平台，内网部署，真实地址不入库）的"驾驶舱大屏"和"天气大屏"两块数据大屏，替代浏览器 F11 方案。内置帆软风格登录窗、会话保持、开机自启、看门狗、公网代理地址切换。

- 项目名：`DashboardKiosk`（程序显示名定义在 Config.cs `Title`；文档统一称"帆软BI数据大屏"，版本 2.2.0）
- 账号：帆软决策平台测试账号（凭据不入库，见本地测试记录）

## 2. 技术栈与硬性约束

| 项 | 值 |
|---|---|
| 语言/框架 | C# 12，.NET 8，`net8.0-windows`，WinForms（无 WPF、无前端框架） |
| 网页内核 | NuGet `Microsoft.Web.WebView2` 1.0.2903.40（系统 Edge 运行时） |
| 发布方式 | 自包含单文件 exe：`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true` |
| 安装包 | Inno Setup 7（`C:\Program Files\Inno Setup 7\ISCC.exe`），脚本 `setup.iss`，安装到 `{autopf}\YuErKangDashboard`；源文件取 `dist\单程序测试\`，输出 `installer\意尔康大屏程序安装包.exe` |
| exe 图标 | csproj `<ApplicationIcon>` 默认指向 `icon_cockpit.ico`（勿删兜底行）；未嵌图标会导致快捷方式/任务栏空白 |
| UI 语言 | 全中文，全部自绘控件（无图片资源，图标 .ico 仅用于窗体） |

**代码风格约定**：中文注释（/// summary 规范）、单文件单职责类、配置用带注释的 JSON（ConfigUpdater 做增量字段更新，保留注释）。

**高 DPI 约束（客户环境 4K 3840×2160 @300% 缩放，改 UI 必读）**：
- 所有窗体布局常量按 96 DPI（100% 缩放）像素设计；字号是"点"单位系统会自动放大，控件坐标不会——不处理就会"字大框小全部重叠"。
- 每个窗体构造函数**末尾必须调 `Dpi.ScaleForm(this)`**（`Dpi.cs`，按 `DeviceDpi/96` 等比放大全部子控件）；自绘图标/线宽/间距用 `Dpi.S()` / `Dpi.SF()` 换算；**字体不要二次缩放**。
- ⚠️ 禁止用 `AutoScaleMode.Dpi`（实测只放大窗体、控件不跟随，客户现场已验证不可靠）。
- ⚠️ **TextBox 宽度不会被 ScaleChildren 缩放**（AutoSize=true 只挪位置）：含 TextBox 的界面必须用 OnResize+Relayout 模式按容器宽度实时重算输入框宽度（见 LoginForm.FieldRow / SettingsDialog.OnResize）。构造函数中先设 Size 再建子控件时，重排方法必须判空（OnResize 会提前触发）。
- ⚠️ **禁止二次缩放**：容器若自己排子控件，必须实现 `IDpiSelfLayout`（ScaleForm 只放大容器自身、再回调 `OnDpiScale()`），且内部换算用 **比例法**（当前宽/设计宽）而不是 `DeviceDpi`——否则子控件会被乘两次倍率（表现为控件右移、溢出、盖住下划线、清空按钮消失）。
- 主窗口非全屏尺寸**不写死**：`MainForm.ComputeWindowedBounds()` 按当前屏幕可用区域 80% 居中实时计算（启动、亮出主窗、退出全屏三处都用它）。
- 验证手段：`[DPI] 窗体名 factor=x client=WxH` 日志只能看窗体尺寸，**子控件布局必须截图看画面**（日志在二次缩放发生前打印，可能"看起来正常"却实际错位）；临时把 `Dpi.Factor` 硬编码返回 2f/3f 可在普通屏模拟缩放（测完务必回退）。

## 3. 文件地图（改动先看这张表）

| 文件 | 行数 | 职责 | 关键点 |
|---|---|---|---|
| `Program.cs` | 55 | 入口 | `--watch` 看门狗模式、`--uninstall` 卸载、单实例互斥 `Local\DashboardKioskMutex`、`AppPaths.Ensure` |
| `MainForm.cs` | ~1100 | **核心** | 全屏切换、登录流程编排、设置保存、Cookie/会话管理、全屏保活 Timer、过渡遮罩、诊断能力 |
| `LoginForm.cs` | ~800 | 登录窗 | 帆软浅色风格自绘：左侧蓝渐变栏(logo+模式)、右侧下划线输入行、记住密码勾选、齿轮"设置"按钮在登录按钮正下方居中 |
| `SettingsDialog.cs` | ~520 | 设置弹窗 | 模式卡片(选中蓝描边+对勾)、驾驶舱/天气地址、**代理地址 + 滑动开关 `ToggleSwitch`（默认关）**、开机自启、保留登录、**启动时自动登录（v2.2.0）**、删账号记录；`ConfigUpdater` 也在此文件（支持 string/bool 字段增量写回） |
| `AboutDialog.cs` | ~300 | 关于弹窗 | 内含 `HelpForm`（分节帮助，RichTextBox 浅色） |
| `Config.cs` | 125 | 配置 | `AppConfig`：mode/cockpitUrl/weatherUrl/**proxyUrl + proxyEnabled**/useDirectEntry/...；**`Url` 属性：`ProxyEnabled` 且 proxyUrl 非空才返回 proxyUrl**（否则走当前模式内网地址） |
| `SettingsStore.cs` | 72 | settings.json + 开机自启注册表（HKCU Run）；`KioskSettings`：AutoStart / KeepLogin / **AutoLogin（v2.2.0，QQ 式自动登录）** |
| `CredentialStore.cs` | 66 | 记住账号 | credentials.json，Base64 混淆（非加密，防直接可读）；TryGet/Save/Clear |
| `Dpi.cs` | 85 | 高 DPI 缩放 | `ScaleForm`/`S`/`SF`/`Factor` + **`IDpiSelfLayout`**（自行排版子控件的容器实现它，缩放时只放大容器自身、回调 `OnDpiScale()` 重排，避免二次缩放）；每个窗体构造末尾必须调用；环境变量 `YERKANG_DPI=3` 可强制模拟缩放（测试） |
| `Scripts.cs` | 350 | 注入 JS | 悬浮控制条（顶部悬停3秒弹出：退出全屏/刷新/退出登录/退出程序）、登录页检测、诊断脚本 |
| `GlobalHooks.cs` | 99 | 全局钩子 | Esc/F11/双击切全屏——**WebView2 吞按键，普通 KeyPreview 收不到，必须全局钩子** |
| `KioskButton.cs` | — | 自绘按钮 | 浅色：Normal #F0F4FA/文字 #465469/描边 #D4DDEA，accent 蓝 #2BA3E8；圆角四角用 `BackColor` 填充（父容器背景要一致） |
| `Logger.cs` | 27 | 日志 | 写 `AppPaths.DataDir\logs\yyyy-MM-dd.log` |
| `Watchdog.cs` | 93 | 看门狗 | 隐藏子进程 `--watch --parent=<pid>`，命名事件通知正常退出 |
| `Uninstaller.cs` | 44 | 卸载 | 安装版转调 Inno 卸载器，保留 config.json |
| `setup.iss` | — | 安装包 | AppVersion 需与 csproj `<Version>` 同步改 |

## 4. 运行期数据布局（排障入口）

```
%LOCALAPPDATA%\YuErKangDashboard\
├── config.json          运行配置（模式/地址/代理——用户改设置写这里）
├── config.default.json  默认模板
├── settings.json        开机自启/保留登录等开关
├── credentials.json     记住的账号（Base64）
├── logs\yyyy-MM-dd.log  日志
└── WebView2\            浏览器用户数据（登录会话 Cookie 在这里；删掉=强制重新登录）
```

## 5. 核心行为流程（必须理解的三条链路）

### 5.1 启动
`Main` → 初始化数据目录/日志 → 单实例互斥 → `Watchdog.Spawn()`（看门狗子进程）→ `new MainForm(cfg)`。
主窗体**屏外停放启动**：`Bounds=(-20000,-20000)`、不进任务栏——WebView2 必须在已创建的窗口里初始化，但直接显示会闪一下大屏框，屏外停放两头兼顾。有保存的登录会话 → 自动登录 → `RevealMainWindow()`（拉回正常位置）→ `EnterFullscreen()`；无会话 → 弹登录窗（主窗保持屏外）。

### 5.2 登录成功
登录窗点击登录 → `HandleLoginAsync(user, pass, remember)` → WebView2 后台导航帆软登录页/注入脚本登录 → 成功后**同帧**：关登录窗 + `RevealMainWindow()` + `EnterFullscreen()`（顺序不能乱，避免白屏/黑帧）。

**自动登录（v2.2.0）**：`settings.json` 的 `AutoLogin=true` 且有记住账号时，`ShowLoginWindow` 在**冷启动**（`_loginSucceededOnce=false`）会自动调 `HandleLoginAsync(记住的账号, remember:true)`；失败停在登录窗显示原因（人工可介入）。运行中弹出的登录窗（会话过期/手动退出账号）**不**自动登录——否则"退出账号"就失去意义。`_loginSucceededOnce` 在登录成功时置位。

### 5.3 退出登录（用户语义，注意区分！）
- **退出登录**（控制条按钮）：清登录会话 Cookie（`DeleteSessionCookies` + `ClearBrowsingDataAsync(Cookies)`），回登录窗；**保留**记住的账号密码（供回填）。主窗先 `Hide()`，登录窗显示后再做尺寸切换。
- **删除账号记录**（设置弹窗按钮）：`CredentialStore.Clear()`，连记住的凭证一起删，下次登录窗空白。

## 6. 全屏切换的机械细节（改前必读）

**禁止**用 `FormBorderStyle` 切换全屏——WinForms 会 RecreateHandle，WebView2 原生子窗口失联 → **永久黑屏卡死**（已踩坑验证）。
现行方案：`SetWindowLong(Handle, GWL_STYLE, ...)` 去掉/恢复标题栏 + `SetWindowPos` + `SWP_FRAMECHANGED`，句柄不变。切换瞬间用 `_overlay`（纯色 Panel）遮 600ms 兜住缩放黑帧；进全屏隐藏光标（动鼠标恢复），`TopMost=true` 保活 Timer 防遮挡。

其他已踩坑：
- 主窗 `Hide()` 后重新登录必须真正 `Show()` 回来，否则窗口隐藏期间被保活逻辑强行弹出 = 黑屏卡死（`RevealMainWindow` 已修，勿删其中逻辑）。
- 主窗完全不显示（`SetVisibleCore(false)`）会让 `OnLoad` 不触发、WebView2 不初始化 → 只能屏外停放。
- U+2715 (✕) 等字符在 Microsoft YaHei UI 缺字形渲染为空白 → 图标一律自绘（画线/画齿轮），不要用特殊 Unicode 字符。
- KioskButton 圆角四角填 `Parent.BackColor`，父容器背景必须与按钮所在处一致（曾因按钮在白卡片上露出灰角被用户指出）。

## 7. 配置 schema（config.json）

```jsonc
{
  "mode": "cockpit",            // cockpit=驾驶舱 | weather=天气
  // 注意：cockpitUrl/weatherUrl 真实地址不入库，由部署方按实际帆软环境填写
  "cockpitUrl": "http://<帆软服务器>:8080/webroot/decision#?activeTab=<驾驶舱tabId>",
  "weatherUrl": "http://<帆软服务器>:8080/webroot/decision#?activeTab=<天气tabId>",
  "proxyUrl": "",               // 留空=直连；填公网反向代理地址后全程序走代理（v2.0 新增）
  "useDirectEntry": true,
  "topHoverSeconds": 3,         // 顶部悬停弹出控制条的秒数
  "mouseIdleHideSeconds": 8,
  "retrySeconds": 10,           // 断网自动重试
  "cssHideSelectors": [],
  "probe": false, "screenshotPath": null,
  "disableGpu": false,          // 花屏/闪烁时改 true
  "extraArgs": null, "debugLogin": null
}
```

`AppConfig.Url` 的代理逻辑：`proxyUrl` 非空 → `Url` 返回 `proxyUrl`，程序内所有直连地址推导（如登录页 `/webroot/decision/login`）都基于 `Url`，自动跟随。已知约束：代理域名 ≠ 内网域名 → 帆软 Cookie 按域名隔离，**首次走代理要重登一次（预期行为）**；代理服务需整站映射 `/webroot/`。**代理尚未实测**（用户还没搭代理服务器），此功能属预埋。

## 8. 当前状态与待办（截至 2026-11-25）

**已完成并经用户验证：**
- 登录→全屏→退出全屏→退出登录全链路无黑屏（根因已修：RecreateHandle 黑屏 + 重新登录后主窗未 Show）
- 帆软浅色风格登录窗、浅色设置/关于弹窗、齿轮"设置"按钮（登录按钮正下方居中）
- 退出登录/删账号语义、模式即时刷新、公网代理开关（默认关，实测开关切换正常）
- **自动登录（v2.2.0，实测通过）**：退出账号后第二天重启，无需人工点登录，冷启动自动登录直达全屏（约 7 秒）；登录失败回落登录窗

**待办：**
1. 公网代理功能跨公网环境实测
2. 如需新安装包：先 publish 再 Inno Setup 编译 setup.iss（AppVersion 已同步 2.2.0；dist 里的 2.1.2 安装包是上一版）
3. **部署限制（长期有效）**：exe 无代码签名，Win11「智能应用控制」会硬拦截（无"仍要运行"）→ 客户机需关闭该功能，详见使用说明 FAQ；自用可不签名

## 9. 修改后如何交付（标准流程）
```bat
:: 1. 编译验证（0 错误）
dotnet build
:: 2. 发布单文件
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
::: 3. 产物 bin\Release\net8.0-windows\win-x64\publish\DashboardKiosk.exe
::    exe 单独即可运行（AppConfig 已内置两个大屏地址默认值）；config.default.json 仅在需覆盖默认值时放 exe 旁
:: 4. 安装版：Inno Setup 编译 setup.iss（记得同步改 AppVersion）
:: 5. 更新本文件（AI_PROJECT_CONTEXT.md）和 开发维护手册.md 中受影响的章节
```

排障第一步永远是：`%LOCALAPPDATA%\YuErKangDashboard\logs\` 当天日志尾部。
