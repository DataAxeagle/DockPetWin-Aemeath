# DockPetWin Aemeath Windows 使用说明

## 适用包

下载 `DockPetWin-Aemeath-Windows-v2026.05.27.zip` 后完整解压，双击运行：

```text
DockPetWin.exe
```

不要在压缩包预览窗口里直接运行，也不建议放到 `Program Files`。程序需要在同级目录读写 `UserData`。

## 第一次启动

- 默认角色是爱弥斯，默认称呼是“漂泊者”。
- 未填写 AI API 时，桌宠本体、右键菜单、设置、基础提醒和小屋基础资源仍可打开。
- 要使用 AI 对话、长期记忆提取、联网搜索等能力，需要在设置里填写 API。

API 申请地址：

- DeepSeek: <https://platform.deepseek.com/usage>
- Tavily: <https://app.tavily.com/home>

## 更新并保留本地数据

`UserData` 保存你的 API、称呼、聊天记录、长期记忆、小屋配置、提醒和资源选择。

更新方式：

1. 退出正在运行的 DockPetWin。
2. 备份旧版本里的 `UserData`。
3. 解压新版到新文件夹。
4. 把旧版本 `UserData` 复制到新版文件夹。
5. 运行新版 `DockPetWin.exe`。

只想全新开始时，才删除或重命名 `UserData`。

## 常见问题

如果 Windows SmartScreen 提示未知发布者，这是因为当前分享包没有代码签名。确认来源可信后选择继续运行。

如果设置或记忆没有保存，优先检查软件目录是否有写入权限，以及是否在压缩包预览窗口里直接运行。
