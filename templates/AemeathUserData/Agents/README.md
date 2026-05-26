# Agents 目录总览

这里是桌宠运行时可读的本地资料区。不要把所有内容混进一个文件；不同信息按用途放在不同目录。

## 常用入口

| 你想做什么 | 改/看哪里 | 说明 |
|---|---|---|
| 改爱弥斯的人物设定 | `character/00_identity.md`、`character/02_relationship.md` | 常驻短设定，影响每次对话的身份和关系判断 |
| 补官方人物设定依据 | `knowledge/characters/aemeath.md` | 长资料入口，放完整设定摘要和来源定位 |
| 改爱弥斯的语气性格 | `character/01_voice.md`、`character/04_examples.md` | 常驻语气规则和示例 |
| 补官方台词/语气分析 | `knowledge/quotes/aemeath-style.md` | 从官方语音、故事里提炼出的说话风格 |
| 补工具调用坑、任务流程、设置注意事项 | `memory/permanent/流程记忆/工具调用与坑/摘要.md` | 让之后少犯同类错误 |
| 看小屋做过什么 | `home-life/summaries/` | 先看摘要；明细在 `home-life/calendar/` |
| 看所有对话 | `conversations/` | 每日原始对话；摘要在 `conversations/summaries/` |
| 看保存过的记忆 | `memory/MEMORY.md` | 记忆总索引 |
| 看任务执行历史 | `tasks/README.md`、`tasks/scheduled-runs/index.jsonl` | 定时任务或工具任务记录 |

## 分层原则

- `character/`：短、常驻、强约束，告诉桌宠“你是谁、怎么说话、和漂泊者是什么关系”。
- `knowledge/`：长、可检索、讲出处，保存爱弥斯和鸣潮世界观资料。
- `memory/`：整理后的用户记忆、项目记忆、流程坑和当前摘要。
- `conversations/`：聊天原始记录和每日摘要。
- `home-life/`：小屋生活原始记录和每日摘要。
- `tasks/`：任务执行历史，不直接当人格记忆。
- `tool_outputs/`：工具产物归档，不直接常驻读取。
