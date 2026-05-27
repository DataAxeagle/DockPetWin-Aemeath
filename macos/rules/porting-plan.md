# macOS 迁移路线

## 结论

这个项目不能从 WPF 直接“一键转换”为 macOS 版，应该做跨平台重构。推荐先把可复用逻辑抽出，再用 Avalonia 做 macOS 壳。

## Phase 0：盘点与边界

目标：弄清楚哪些代码可复用，哪些代码必须重写。

可复用优先级高：

- `Core/Agents`：AI 对话、工具调用、聊天设置、消息结构。
- `Core/Settings`：应用设置结构和存储逻辑。
- `Core/Assets`：素材包 manifest、加载规则。
- `Core/HomeLife`：小屋行事历、状态持久化。
- `Core/Reminder`：提醒模型和调度逻辑，但 UI 提醒入口要重接。
- 角色素材、内置 agent、默认 UserData 模板、README/manual。

需要重写或抽象：

- `MainWindow.xaml` / `MainWindow.xaml.cs`：WPF 桌宠窗口。
- `HomeWindow.xaml` / `HomeWindow.xaml.cs`：WPF 小屋窗口。
- `SettingsWindow.xaml` / `AgentChatWindow.xaml`：WPF 设置和聊天窗口。
- `UI/Tray/TrayIconController.cs`：Windows 托盘。
- `Platform/TaskbarGeometry.cs`：Windows 任务栏和屏幕工作区算法。
- `Launcher`：Windows exe 启动器。

## Phase 1：拆 Core

建议新增跨平台 class library：

```text
DockPet.Core/
├── Agents/
├── Assets/
├── HomeLife/
├── Reminder/
├── Settings/
└── Shared/
```

要求：

- Core 不引用 WPF、Windows Forms、`System.Windows`。
- Core 只保留纯模型、文件读写、网络请求、规划逻辑。
- 平台能力通过接口注入，例如通知、窗口置顶、屏幕信息、菜单栏。

## Phase 2：macOS/Avalonia 壳

建议新增：

```text
DockPet.Aemeath.Mac/
├── App.axaml
├── Views/
├── ViewModels/
├── Platform/Mac/
└── Resources/
```

关键能力：

- 透明无边框桌宠窗口。
- 始终置顶或近似置顶。
- 可拖拽移动。
- 菜单栏图标和右键菜单。
- 聊天、设置、小屋窗口。
- UserData 初始化和迁移。
- API 未配置时的爱弥斯角色内提示。

## Phase 3：功能对齐

优先级：

1. 启动后显示爱弥斯，而不是默认宠物。
2. 菜单栏里能打开聊天、设置、小屋、重启、退出。
3. API 设置页能填写 DeepSeek 和 Tavily。
4. 聊天能使用内置人设、长期记忆和动态称呼。
5. 小屋能展示家具、人物动作、行事历。
6. 提醒气泡和计时行为与 Windows 版一致。
7. 打包成 `.app` 或 `.dmg`，用户解压/拖入 Applications 后可用。

## Phase 4：发布

需要在 macOS 真机上验证：

- Intel Mac 和 Apple Silicon Mac 至少各覆盖一种，或先声明仅验证 Apple Silicon。
- 首次启动 Gatekeeper 提示。
- 文件读写路径是否落在应用目录外的用户数据目录。
- 透明窗口、菜单栏图标、多屏位置、Dock 行为。
- 无 API key 的初始引导。

## 不建议做的事

- 不建议在现有 WPF 项目里硬改 `TargetFramework`。
- 不建议为了快速显示窗口而复制全部 XAML。
- 不建议把用户私有 `UserData` 打进 macOS 包。
- 不建议未经验证就声明功能完全一致。
