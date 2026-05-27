# src

这里是 macOS 版试验源码。

当前已创建一个可构建的最小 .NET 解决方案：

```text
src/
├── DockPet.Core/
├── DockPet.Aemeath.Mac/
├── DockPet.Aemeath.Avalonia/
└── DockPet.Aemeath.Mac.sln
```

当前状态：

- `DockPet.Core`：放跨平台默认设置、UserData 路径、首次 API 引导文案和平台能力映射。
- `DockPet.Aemeath.Mac`：暂时是控制台原型，用于验证 Core 能被 mac 壳引用。
- `DockPet.Aemeath.Avalonia`：真正的 Avalonia 桌宠壳原型，当前支持透明无边框置顶窗口、拖拽、右键菜单、爱弥斯展示图和首次 API 引导。

下一步是在 `DockPet.Aemeath.Avalonia` 内继续接入设置窗口、聊天窗口、小屋窗口和菜单栏图标。
