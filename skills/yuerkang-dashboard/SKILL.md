---
name: yuerkang-dashboard
description: 帆软BI数据大屏（DashboardKiosk）项目的 AI 接手技能。当用户提到"帆软BI大屏"、"DashboardKiosk"、"数据大屏"、"大屏程序"并要求修改/排查/打包该项目时使用。自动克隆仓库、理解架构、按硬性约束修改并交付。
---

# 帆软BI数据大屏 —— AI 接手技能

> 本技能让任何 AI 助手在 5 分钟内接管该项目：拉代码 → 读文档 → 按约束改 → 交付。

## 第一步：获取代码

```bash
git clone https://github.com/RunWang925/gyyearconsports-dashboard.git
cd gyyearconsports-dashboard
```

仓库为 Private，推送需要 RunWang925 账号的 GitHub 凭据。

## 第二步：必读文档（按顺序）

| 顺序 | 文件 | 作用 |
|---|---|---|
| 1 | `AI_PROJECT_CONTEXT.md` | **最重要**：架构、文件地图、三条核心链路、配置 schema、血泪坑 |
| 2 | `开发维护手册.md` | 逐文件讲解、编译/发布/打包流程、踩坑实录 |
| 3 | `README.md` | 项目概览、技术栈、快速开始 |
| 4 | `dist/使用说明.txt` | 面向最终用户的功能与 FAQ |

**读完 AI_PROJECT_CONTEXT.md 再动手，不要凭经验猜测本项目约定。**

## 第三步：理解项目是什么

- **Windows 大屏壳程序**：C# 12 / .NET 8 WinForms + WebView2（net8.0-windows），全屏展示帆软 BI（FineReport）的"驾驶舱大屏"和"天气大屏"，替代浏览器 F11。
- 单项目约 16 个 .cs 文件，无前端、无数据库；发布为自包含单文件 exe，Inno Setup 7 打安装包。
- 当前版本 v2.2.1，安装包在 `dist/意尔康大屏程序安装包-<版本>.exe`（如 意尔康大屏程序安装包-2.2.1.exe）。

## 硬性约束（违反任何一条都可能造成客户现场事故）

1. **高 DPI**（客户 4K @300%）：
   - 每个窗体构造**末尾必须调 `Dpi.ScaleForm(this)`**；自绘尺寸用 `Dpi.S()/SF()`。
   - **禁止 `AutoScaleMode.Dpi`**（实测不可靠）。
   - **TextBox 宽度不被 ScaleChildren 缩放**：含 TextBox 的界面用 OnResize+Relayout 模式（见 LoginForm.FieldRow）。
   - **禁止二次缩放**：自行排版子控件的容器必须实现 `IDpiSelfLayout`，内部用比例法换算。
   - 布局是否正确**必须截图验证**，日志数字不可信（打印时机早于二次缩放）。
2. **全屏切换**：禁止用 `FormBorderStyle` 切全屏（会 RecreateHandle → WebView2 永久黑屏）。只能用 `SetWindowLong + SetWindowPos + SWP_FRAMECHANGED`（见 MainForm）。
3. **WebView2 相关**：WebView2 吞按键 → 全局钩子（GlobalHooks）；清 Cookie 后必须 `await` 再 Navigate；登录失败红字残留需延时复核页面是否真的跳离登录页。
4. **版本号三处同步**：csproj `<Version>`、setup.iss `AppVersion`、dist/使用说明.txt 标题。
5. **exe 图标**：csproj `<ApplicationIcon>` 默认嵌 `icon_cockpit.ico`，勿删。
6. **UI 全中文、全自绘控件**；特殊 Unicode 字符（如 ✕）可能缺字形，图标一律画线实现。

## 标准修改交付流程

```bash
# 1. 编译验证（0 错误才继续）
dotnet build
# 2. 发布单文件（仅版本升级/交付时）
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
# 产物：bin/Release/net8.0-windows/win-x64/publish/DashboardKiosk.exe（单文件即可运行）
# 3. 安装包（仅客户要新安装版时）：Inno Setup 7 编译 setup.iss（C:\Program Files\Inno Setup 7\ISCC.exe）
# 4. 提交并推送
git add -A && git commit -m "..." && git push origin main
# 5. 同步更新 AI_PROJECT_CONTEXT.md / 开发维护手册.md 受影响章节
```

## 排障

- 第一步永远是看日志：`%LOCALAPPDATA%\YuErKangDashboard\logs\` 当天文件尾部。
- 运行配置在 `%LOCALAPPDATA%\YuErKangDashboard\config.json`；删 `WebView2\` 目录 = 强制重新登录。
- UI 截图调试工具：`devtools/grab_window.py 窗口标题关键字 输出.png`（PrintWindow，不受遮挡影响）。
- 测试环境变量 `YERKANG_DPI=2/3` 可强制模拟缩放（测完必须回退）。

## 已知限制

- **exe 未签名，客户机必须先关闭 Windows 11「智能应用控制」程序才能运行**（Windows 安全中心 → 应用和浏览器控制 → 智能应用控制 → 关闭；关闭后不可再开启）。给客户部署时必须提前告知此操作，详见 `dist/使用说明.txt` FAQ。
- 首次切换公网代理需重新登录（帆软 Cookie 按域名隔离，预期行为）。
- 账号权限：个别帆软账号可能只有部分大屏的查看权限（登录后停在窗口模式是权限问题，不是程序 bug）。
