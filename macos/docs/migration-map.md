# Windows 到 macOS 迁移映射

## 当前 Windows 项目事实

Windows 主项目入口：

```text
D:\桌面\重要文件同步\my code\桌面宠物\DockPetWin-main\DockPetWin-main\DockPetWin\DockPetWin.csproj
```

当前项目配置包含：

- `TargetFramework=net8.0-windows`
- `UseWPF=true`
- `UseWindowsForms=true`
- `ApplicationIcon=Resources\App\pet-app-icon.ico`

这说明当前项目是 Windows 专用桌面应用，不具备直接输出 macOS 的条件。

## 文件迁移建议

| 当前文件/目录 | 迁移策略 |
| --- | --- |
| `Core/Agents` | 优先迁入 `DockPet.Core`，移除 UI 依赖后复用 |
| `Core/Settings` | 迁入 `DockPet.Core`，调整默认 UserData 路径 |
| `Core/Assets` | 迁入 `DockPet.Core`，资源路径改为跨平台路径 |
| `Core/HomeLife` | 迁入 `DockPet.Core`，UI 点位由 Avalonia 层提供 |
| `Core/Reminder` | 迁入 `DockPet.Core`，通知展示由平台层实现 |
| `MainWindow.xaml(.cs)` | 用 Avalonia `PetWindow.axaml(.cs)` 重做 |
| `HomeWindow.xaml(.cs)` | 用 Avalonia `HomeWindow.axaml(.cs)` 重做 |
| `SettingsWindow.xaml(.cs)` | 用 Avalonia `SettingsWindow.axaml(.cs)` 重做 |
| `AgentChatWindow.xaml(.cs)` | 用 Avalonia `ChatWindow.axaml(.cs)` 重做 |
| `UI/Tray/TrayIconController.cs` | 改为 macOS 菜单栏/TrayIcon 适配 |
| `Platform/TaskbarGeometry.cs` | 改为 `Platform/Mac/ScreenGeometryService` |
| `Launcher` | macOS 不沿用，改 `.app` 启动 |

## 平台接口建议

后续拆 Core 时建议定义这些接口：

```csharp
public interface IUserDataPathProvider
{
    string UserDataRoot { get; }
}

public interface IDesktopSurface
{
    IReadOnlyList<DisplayInfo> GetDisplays();
    Rect GetSafeWorkingArea(DisplayInfo display);
}

public interface IAppMenuService
{
    void ShowChat();
    void ShowSettings();
    void ShowHome();
    void Restart();
    void Exit();
}

public interface IPetNotificationService
{
    void ShowBubble(string text, bool requiresAck);
}
```

## 数据路径建议

macOS 版不要把可变用户数据写在 `.app` 内部。建议：

```text
~/Library/Application Support/DockPetWin-Aemeath/UserData/
```

发布包内只放默认模板：

```text
Resources/UserDataTemplate/
```

首次启动时复制模板，之后更新应用时只替换程序和默认资源，不覆盖用户自己的 `UserData`。
