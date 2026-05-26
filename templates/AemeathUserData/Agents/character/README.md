# Character 目录说明

这个目录保存爱弥斯每次对话都应该优先读取的短设定。这里不放大量剧情原文，也不放每天发生的事情。

## 文件用途

- `00_identity.md`：人物身份、桌宠身份边界、爱弥斯核心设定。
- `01_voice.md`：语气、性格、表达习惯、不要怎么说。
- `02_relationship.md`：漂泊者和爱弥斯的关系。当前默认：漂泊者 = 当前用户 = 本项目语境下的漂泊者。
- `03_memory_rules.md`：记忆系统怎么读、怎么保存、什么不能编造。
- `04_examples.md`：典型回复示例。想让她某类场景说得更像，就补这里。
- `05_lore.md`：常驻世界观短锚点。
- `06_knowledge_rules.md`：如何按需读取 `knowledge/`。
- `07_story_perspective.md`：剧情叙述视角，规定漂泊者就是剧情中的漂泊者，聊剧情时默认用共同经历而不是第三方介绍。
- `08_factual_grounding.md`：事实接地规则，规定剧情、世界观、歌曲、歌词含义等硬设定必须查资料，不能靠人设语气胡编。
- `09_current_state.md`：当前对话状态硬锚点。只覆盖旧摘要中的客观人设/世界观/剧情阶段/能力状态冲突，不覆盖漂泊者的个人偏好、日常记忆、小屋事件或近期任务。
- `_template.md`：新增角色或重建角色包时的模板。

## 修改建议

- 改“爱弥斯是谁”：先改 `00_identity.md`，再看是否同步 `knowledge/characters/aemeath.md`。
- 改“爱弥斯和漂泊者是什么关系”：改 `02_relationship.md`。
- 改“聊剧情时是否把漂泊者带入为漂泊者”：改 `07_story_perspective.md`，必要时同步 `02_relationship.md` 和 `04_examples.md`。
- 改“哪些内容必须查资料、哪些可以按人设发挥”：改 `08_factual_grounding.md` 和 `06_knowledge_rules.md`。
- 改“当前时间点、当前身体状态、旧摘要冲突覆盖规则”：改 `09_current_state.md`。
- 改“说话声音、性格、撒娇/认真比例”：改 `01_voice.md` 和 `04_examples.md`。
- 改“她应该记住什么、怎么确认已保存”：改 `03_memory_rules.md`。

长期官方资料不要直接塞进这里，放到 `knowledge/`；日常聊天和小屋生活不要塞进这里，放到 `memory/`、`conversations/`、`home-life/`。
