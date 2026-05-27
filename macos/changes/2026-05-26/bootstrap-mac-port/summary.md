# Bootstrap macOS Port Workspace

## 变更概述

创建爱弥斯桌宠 macOS 版本试验工作区，用于评估和推进从 Windows WPF 应用到 macOS 应用的迁移。

## 关键结论

当前 Windows 版基于 WPF、Windows Forms 和 Windows 屏幕/任务栏 API，不能直接编译为 macOS 应用。推荐路线是使用 Avalonia UI + .NET 重建跨平台桌面壳，同时抽取并复用现有 Core 业务逻辑、角色素材、AI 对话、设置、长期记忆和小屋行事历。

## 新增内容

- `PROJECT.md`：macOS 试验子项目入口。
- `rules/porting-plan.md`：迁移阶段、复用边界和发布策略。
- `rules/feature-parity.md`：Windows/macOS 功能对齐清单。
- `docs/migration-map.md`：当前文件到 macOS 架构的映射。
- `src/README.md`：源码目录说明。
- `.gitignore`：忽略 macOS/NET 试验工程的 `bin/`、`obj/` 和本地 IDE 文件。

## 追加试验

已创建最小 .NET 解决方案：

- `src/DockPet.Core/DockPet.Core.csproj`
- `src/DockPet.Aemeath.Mac/DockPet.Aemeath.Mac.csproj`
- `src/DockPet.Aemeath.Mac.sln`

当前原型先验证跨平台 Core 拆分，不包含 Avalonia UI。原因是本机未安装 Avalonia 模板，也没有 Avalonia NuGet 缓存；安装新模板属于依赖变更，需要用户明确确认。

## Avalonia UI 原型

已安装 Avalonia 官方模板 `Avalonia.Templates 12.0.3`，并新增：

- `src/DockPet.Aemeath.Avalonia/DockPet.Aemeath.Avalonia.csproj`
- `src/DockPet.Aemeath.Avalonia/MainWindow.axaml`
- `src/DockPet.Aemeath.Avalonia/Assets/Images/aemeath-stand.png`

当前 UI 原型具备：

- 透明无边框置顶窗口。
- 爱弥斯展示图。
- 默认 API 引导气泡。
- 左键拖拽窗口。
- 右键菜单入口：聊天、小屋、设置 API、重启、退出。

已 cross-publish 一个 macOS Apple Silicon 试验产物：

- `output/publish/osx-arm64/`
- `output/app/DockPetWin-Aemeath.app/`
- `output/app-clean/DockPetWin-Aemeath.app/`
- `output/DockPetWin-Aemeath-macOS-arm64-prototype.zip`
- `output/release/DockPetWin-Aemeath-osx-arm64-prototype.zip`
- `output/release/DockPetWin-Aemeath-osx-x64-prototype.zip`

新增 `scripts/package-macos.ps1`，用于重复生成 arm64/x64 两套 macOS 试验包。
