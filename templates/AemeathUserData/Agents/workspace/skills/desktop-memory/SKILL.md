---
name: desktop-memory
description: 桌宠专用记忆保存流程。当用户说“保存记忆”“记住这个”“把刚才的对话存成记忆”时使用：读取本次对话可见上下文、当前会话压缩摘要和近30轮历史，提炼值得保存的候选记忆；用户确认长期/短期后必须调用 save_memory 真正写入。
---

# Desktop Memory

## 触发

用户表达以下任意意图时触发：

- 保存记忆
- 记住这个
- 把刚才的对话存成记忆
- 保存一下我们刚才说的

## 流程

1. 不只保存用户当前这一句话。
2. 读取当前对话框可见上下文、当前会话压缩摘要，以及近 30 轮历史对话。
3. 提炼真正值得保存的记忆：
   - 用户长期偏好、称呼、工作习惯、项目规则。
   - 当前任务的重要约定。
   - 后续复用价值高的事实。
4. 忽略寒暄、重复确认、一次性闲聊、无复用价值内容。
5. 如果用户没有明确长期/短期，先向用户展示候选记忆，并询问保存为长期记忆还是短期记忆。
6. 用户回复“长期/短期/1/2”后，必须调用 `save_memory` 工具写入对应记忆区。
7. 只有收到 `save_memory` 的 `ok=true` 工具结果后，才能告诉用户“已保存”，并必须把工具返回的真实路径展示给用户。
8. 严禁只凭自然语言编造 `memory/...` 路径；没有工具结果就不能声称保存成功。

## 存储位置

- 记忆根目录：`memory/`
- 记忆索引：`memory/MEMORY.md`
- 长期用户记忆：`memory/permanent/用户记忆/通用/摘要.md` 和 `原文.md`
- 长期人设记忆：`memory/permanent/人设记忆/爱弥斯/摘要.md` 和 `原文.md`
- 长期流程记忆：`memory/permanent/流程记忆/工具调用与坑/摘要.md` 和 `原文.md`
- 长期设置摘要：`memory/permanent/设置记忆/运行配置摘要/摘要.md`
- 短期聊天记忆：`memory/domains/用户记忆/聊天记忆/YYYY-MM-DD/摘要.md` 和 `原文.md`
- 桌宠项目进度：`memory/domains/项目进度/桌宠/YYYY-MM-DD/摘要.md`
- 小屋记忆摘要：`memory/domains/小屋记忆/YYYY-MM-DD/摘要.md`
- 对话记录：`conversations/YYYY-MM-DD.md`
- 对话摘要：`conversations/summaries/YYYY-MM-DD.md`
- 小屋摘要：`home-life/summaries/YYYY-MM-DD.md`
- 当前/最近会话摘要：`memory/summaries/current-session-summary.md`
- 压缩摘要归档：`memory/summaries/compressed/`

## 读取规则

- 每次启动自动读取 `memory/permanent/**/摘要.md`。
- 每次启动自动读取 `memory/summaries/current-session-summary.md`、最近的小屋摘要和最近的对话摘要。
- 短期记忆不默认全量注入，需要用户问起或任务需要时，用 `read_memory` 的 `type=short` 读取。
- 用户问记忆保存在哪里、有哪些记忆时，用 `list_memories`。

## 输出要求

候选记忆要简洁，优先输出 1-5 条。不要保存 API key、token、密码。