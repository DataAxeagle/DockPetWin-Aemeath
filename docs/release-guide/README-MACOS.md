# DockPetMac Aemeath macOS 使用说明

## 适用包

下载 `DockPetMac-Aemeath-v2026.05.27.zip` 后完整解压。解压后应看到：

```text
DockPetMac.app
UserData
README-MACOS.md
```

请尽量让 `DockPetMac.app` 和 `UserData` 保持同级。这个包已经内置爱弥斯资源、人设知识库、默认称呼“漂泊者”和干净 API 配置模板。

## 打开方式

1. 解压 zip。
2. 右键 `DockPetMac.app`，选择“打开”。
3. 第一次看到系统安全提示时选择继续打开。

如果仍然提示无法打开，可以在终端执行：

```bash
xattr -dr com.apple.quarantine /path/to/DockPetMac.app
```

把 `/path/to/DockPetMac.app` 换成你自己的应用路径。

## API 配置

第一次使用 AI 对话、联网搜索或需要模型参与的功能前，需要配置 API。

- DeepSeek: <https://platform.deepseek.com/usage>
- Tavily: <https://app.tavily.com/home>

也可以手动编辑：

```text
UserData/Agents/settings.local.json
```

## 更新并保留本地数据

`UserData` 保存 API、称呼、聊天记录、长期记忆、小屋配置和资源选择。

更新方式：

1. 退出正在运行的 DockPetMac。
2. 备份旧版本里的 `UserData`。
3. 解压新版。
4. 把旧版本 `UserData` 复制到新版文件夹，和新的 `DockPetMac.app` 放在同级。
5. 运行新版 `DockPetMac.app`。

## 当前说明

macOS 版是从 Windows 主项目迁移出来的版本。角色、人设、资源、API 配置和本地数据结构会尽量保持一致，但由于 macOS 的窗口置顶、菜单栏、安全权限、多屏位置和 Dock 行为与 Windows 不同，少量平台细节可能还需要继续修复。
