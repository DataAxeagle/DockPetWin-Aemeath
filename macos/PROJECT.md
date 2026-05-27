# DockPetWin Aemeath macOS Port

## 项目定位

这是 `桌面宠物` 子项目下的 macOS 版本试验工作区，目标是评估并逐步实现爱弥斯桌宠的 macOS 版本。

当前 Windows 本体基于 WPF、Windows Forms 托盘和 Windows 屏幕/任务栏 API，不能直接编译成 macOS 应用。macOS 版需要保留角色、素材、AI 对话、长期记忆、设置、行事历、小屋等功能体验，但桌面窗口、菜单栏、屏幕贴边、打包发布等平台层需要重做。

## 推荐路线

优先路线：`Avalonia UI + .NET`

选择原因：

- 继续使用 C# 和 .NET，便于迁移现有 Core 逻辑。
- 支持 macOS、Windows、Linux，后续有机会做真正跨平台版本。
- 能实现透明无边框窗口、置顶窗口、菜单栏图标、普通设置窗口和小屋窗口。
- 比 SwiftUI 重写成本低，比 Electron/Tauri 更容易复用现有 C# 代码。

不建议尝试把现有 WPF 项目原地改成 macOS 项目。WPF 的 `net8.0-windows`、`UseWPF`、`System.Windows.Forms.Screen`、NotifyIcon、任务栏几何计算都绑定 Windows。

## 目录地图

```text
DockPetWin-Aemeath-mac/
├── PROJECT.md
├── rules/
│   ├── porting-plan.md
│   └── feature-parity.md
├── docs/
│   └── migration-map.md
├── src/
│   └── README.md
├── changes/
│   └── 2026-05-26/bootstrap-mac-port/
├── output/
└── .gitignore
```

## 启动读取顺序

1. 上级 `my code/AGENTS.md`。
2. 上级 `桌面宠物/PROJECT.md`。
3. 本文件。
4. `rules/porting-plan.md`。
5. `rules/feature-parity.md`。
6. `docs/migration-map.md`。

## 当前状态

本目录目前是 macOS 迁移试验工程，已具备 Avalonia 桌宠壳原型和 macOS 测试包脚本。它还不是功能完整的正式 macOS 应用。下一步需要继续把 Windows 项目拆成：

- `DockPet.Core`：可跨平台复用的业务逻辑。
- `DockPet.Aemeath.Mac`：macOS/Avalonia 桌面壳。
- `DockPet.Aemeath.SharedAssets`：角色素材、内置 agent、说明文档和默认 UserData。

## 验证方式

当前阶段默认验证：

```powershell
dotnet build "D:\桌面\重要文件同步\my code\桌面宠物\mac-version\DockPetWin-Aemeath-mac\src\DockPet.Aemeath.Avalonia\DockPet.Aemeath.Avalonia.csproj"
.\scripts\package-macos.ps1
```

需要在真实 macOS 环境上验证透明窗口、菜单栏、拖拽、开机启动、权限提示和打包。

## 安全边界

- 不复制用户本地 API key、聊天记录、私人称呼和个人记忆。
- 不删除 Windows 主项目和已发布版本。
- 不安装全局依赖、不改系统配置，除非用户明确确认。
- macOS 发布包必须保留爱弥斯角色、默认素材、人设世界观、内置 agent 基础能力和新用户 API 引导。
