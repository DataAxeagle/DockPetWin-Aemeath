# Execution

## 执行过程

1. 读取 `project-harness-bootstrap` skill，确认这是 `桌面宠物` 下的轻量子项目。
2. 检查 Windows 主项目入口和 `.csproj` 配置。
3. 创建 macOS port 试验目录。
4. 写入迁移路线、功能对齐、文件映射和变更记录。
5. 检查本机 .NET 环境和 Avalonia 模板。
6. 创建最小 .NET 解决方案、Core 类库和 mac 壳控制台原型。

## 验证

本机 `.NET SDK` 为 8.0.419。

`dotnet new list Avalonia` 未找到 Avalonia 模板，本机 NuGet 缓存也未发现 Avalonia 包。

已验证：

- 目标目录创建成功。
- 迁移文档和 changes 记录已写入。
- 最小解决方案和项目文件已创建。
- `dotnet build src/DockPet.Aemeath.Mac/DockPet.Aemeath.Mac.csproj -o C:\tmp\DockPetWin-Aemeath-mac-build-test` 成功，0 warning / 0 error。
- 直接运行 `C:\tmp\DockPetWin-Aemeath-mac-build-test\DockPet.Aemeath.Mac.dll` 成功输出爱弥斯默认身份、默认称呼、UserData 路径、首次 API 引导和平台能力清单。
- 安装 `Avalonia.Templates 12.0.3`。
- 创建 `DockPet.Aemeath.Avalonia` 项目，并引用 `DockPet.Core`。
- `dotnet build src/DockPet.Aemeath.Avalonia/DockPet.Aemeath.Avalonia.csproj -o C:\tmp\DockPetWin-Aemeath-avalonia-build-test --no-restore` 成功，0 warning / 0 error。
- `dotnet publish src/DockPet.Aemeath.Avalonia/DockPet.Aemeath.Avalonia.csproj -c Release -r osx-arm64 --self-contained true -p:UseAppHost=true -o output/publish/osx-arm64` 成功。
- 已创建干净 `.app` 结构：`output/app-clean/DockPetWin-Aemeath.app/Contents/MacOS/DockPet.Aemeath.Avalonia` 存在，且 `Contents/MacOS/osx-arm64` 不存在。
- 已压缩测试包：`output/DockPetWin-Aemeath-macOS-arm64-prototype.zip`。
- 新增 `scripts/package-macos.ps1`。
- 执行 `scripts/package-macos.ps1` 成功生成两套测试包：
  - `output/release/DockPetWin-Aemeath-osx-arm64-prototype.zip`
  - `output/release/DockPetWin-Aemeath-osx-x64-prototype.zip`
- 已验证两套 `.app` 中 `Contents/MacOS/DockPet.Aemeath.Avalonia` 均存在。

## 注意事项

第一次模板 restore 和 build 需要读取用户 NuGet 配置；普通沙箱里读取 `C:\Users\asus\AppData\Roaming\NuGet\NuGet.Config` 被拒绝，因此构建验证使用了授权后的 `dotnet build`。

macOS `.app` 已按官方结构手动创建，但未签名、未公证，也尚未在真实 Mac 上运行验证。Windows 上 cross-publish 生成的可执行文件可能需要在 macOS 上执行 `chmod +x` 后才能直接启动。

## 后续风险

- 需要真实 macOS 环境验证透明窗口、菜单栏、Dock 附近贴边、多屏和打包。
- 如果要使用 Avalonia 模板，可能需要安装模板或确认本机已有模板；这属于新依赖/工具链操作，应先征得用户同意。
