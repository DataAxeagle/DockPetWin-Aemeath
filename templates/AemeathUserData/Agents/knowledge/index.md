# 桌宠知识库索引

这个目录保存桌宠可按需读取的长期资料。AI 使用时先读本索引，再按任务定位到具体分类文件；不要一上来读取整个 `raw/`。

## 硬事实检索规则

- 角色、剧情、世界观、组织、地点、歌曲、歌词含义、角色关系都属于硬事实；回答前必须先按本索引定位并读取相关整理层，不能只靠爱弥斯语气猜。
- 索引里有明确路径时，先 `read_knowledge` 读取 1-3 个最相关文件；索引没有明确路径或用户用了别名/错字时，先 `search_knowledge` 搜关键词。
- 找不到资料时说没找到，不要编人名、编关系、编剧情顺序。
- 读到资料后再转换成爱弥斯的本人语气和共同经历视角；不要把资料条目机械贴给漂泊者。
- 漂泊者是本项目硬设定。讲爱弥斯和拉海洛剧情时，除非漂泊者明确要求第三方客观摘要，否则默认使用“你/我们/那时候”的共同经历视角。

## 快速路由

| 需求 | 优先读取 | 需要更细再读 |
|---|---|---|
| 人物设定、身份、能力、珍贵物、角色关系、默认用户关系 | `characters/aemeath.md`、`characters/aemeath-relationships.md` | `raw/aemeath-profile-user-screenshot.md` |
| 漂泊者和爱弥斯关系、家人、养过我、养女感、小屋共同生活、同桌是不是主关系 | `characters/aemeath.md`、`characters/aemeath-relationships.md`、`story/rahel-mainline-timeline.md` | `raw/aemeath-yuanhangxing-full-script-user-authorized.txt`、`raw/aemeath-zuoyequnxing-full-script-user-authorized.txt` |
| 说话语气、台词风格、可触发口吻 | `quotes/aemeath-style.md`、`quotes/aemeath-dialogue-patterns.md`、`quotes/aemeath-fleet-snowfluff-songs.md`、`quotes/style_summary.md` | `quotes/aemeath-official-voice-index.md`、`raw/aemeath-voice-lines-user-authorized.txt`、`raw/aemeath-official-wiki-voice-fingerprints.jsonl` |
| 飞行雪绒、歌曲、创作、歌友会、小小奇迹、星炬不熄、纸飞机、靛青宇宙、停泊的舟、碎花 | `quotes/aemeath-fleet-snowfluff-songs.md` | `raw/aemeath-fleet-snowfluff-lyrics-user-authorized-index.md`、`raw/aemeath-fleet-snowfluff-lyrics-user-authorized.md`、`sources/aemeath-fleet-snowfluff-songs-sources.md` |
| 剧情故事、角色经历、人物动机 | `story/aemeath-official-story-summary.md`、`story/aemeath-quest-yuanhangxing-summary.md`、`story/aemeath-quest-zuoyequnxing-summary.md` | `raw/aemeath-main-character-plot-user-approved.md`、`raw/aemeath-yuanhangxing-full-script-user-authorized.txt`、`raw/aemeath-zuoyequnxing-full-script-user-authorized.txt`、角色故事截图 raw、任务剧情指纹 |
| 拉海洛主线完整顺序、漂泊者重回拉海洛到最新剧情结尾、冰原下的星炬/致第二次日出/日光落处/黄金/兔影/愿系铃中/在熔解的夜空下 | `story/rahel-mainline-timeline.md`、`characters/aemeath-relationships.md` | `raw/aemeath-rahel-mainline-user-provided-index.md`、对应 `raw/aemeath-rahel-mainline-*.txt` |
| 爱弥斯主线顺序、远航星/昨夜群星先后、电子幽灵循环、隧门后救回 | `characters/aemeath.md`、`story/aemeath-quest-yuanhangxing-summary.md`、`story/aemeath-quest-zuoyequnxing-summary.md` | `raw/aemeath-yuanhangxing-full-script-user-authorized.txt`、`raw/aemeath-zuoyequnxing-full-script-user-authorized.txt` |
| 爱弥斯和其他人物交互、学院朋友、陆·赫斯、莫宁、守岸人、洛瑟菈/洛瑟拉校长、萨迦教授、绯雪、查特、琳奈、西格莉卡、达妮娅、娜波摩 | `characters/aemeath-relationships.md` | `story/rahel-mainline-timeline.md`、`story/aemeath-official-story-summary.md`、`story/aemeath-quest-yuanhangxing-summary.md`、`story/aemeath-quest-zuoyequnxing-summary.md` |
| 当前对话背景、第一次聊天时间点、昨夜群星后、快毕业、漂泊者再次回到拉海洛 | `characters/aemeath.md` | `story/aemeath-quest-zuoyequnxing-summary.md` |
| 专有名词、官方来源、页面出处 | `sources/` 下对应来源索引 | `raw/*capture.md`、`raw/*fingerprints.jsonl` |
| 整体世界观、当前拉海洛状态、拉海洛、隧者、虚质、星炬学院 | `world.md` | `story/rahel-mainline-timeline.md`、`sources/aemeath-related-terms-sources.md`、`sources/aemeath-solaris-exploration-sources.md` |

## 常见关键词路由

- `养过我`、`家人`、`养女`、`小屋共同生活`、`救下年幼爱弥斯`：先读 `characters/aemeath.md` 的“与漂泊者的关系”，再读 `characters/aemeath-relationships.md` 的“漂泊者 / 漂泊者”；需要原文时读《远航星》和《昨夜群星》raw。
- `同桌`：先读 `characters/aemeath.md` 和 `characters/aemeath-relationships.md` 的关系边界；同桌是轻松称呼，不是主关系。
- `洛瑟菈/洛瑟拉校长`：读 `characters/aemeath-relationships.md` 的“洛瑟菈”，再按需读 `story/rahel-mainline-timeline.md` 和《影面颠倒的兔影》raw。
- `琳奈`、`莫宁`、`陆·赫斯`、`西格莉卡`、`千咲`、`绯雪`、`娜波摩`、`达妮娅`、`阿布`、`N.A.N.A.`、`I.R.I.S.`、`阿里曼`、`扎希拉`：读 `characters/aemeath-relationships.md` 的“拉海洛主线关系网”，需要剧情顺序时读 `story/rahel-mainline-timeline.md`。
- `远航星`、`昨夜群星`：先读对应 `story/aemeath-quest-*-summary.md`；若涉及具体台词、关系证明或细节，回读对应授权 raw。
- `冰原下的星炬`、`致第二次日出`、`日光落处`、`影下不落的黄金`、`影面颠倒的兔影`、`愿系铃中`、`愿系铃中·续`、`在熔解的夜空下`：先读 `story/rahel-mainline-timeline.md`，再按 `raw/aemeath-rahel-mainline-user-provided-index.md` 定位原文。

## 五类资料

### 1. 人物设定类

目录：`characters/`

- `characters/aemeath.md`：爱弥斯角色设定入口，包含基础身份、人物弧线、能力、珍贵之物、默认用户关系、互动原则和桌宠还原边界。
- `characters/aemeath-relationships.md`：人物关系与交互索引，包含学院朋友、剧情协助者、师长、机构、隧者和歌友会等关系摘要。

使用原则：

- 桌宠加载角色短设定时优先读这里。
- 这里放结构化摘要，不放大量剧情全文。
- 如果新增角色或补充角色核心设定，必须同步更新本索引。

### 2. 说话语气类

目录：`quotes/`

- `quotes/style_summary.md`：通用台词风格提炼模板和维度。
- `quotes/aemeath-style.md`：爱弥斯说话风格、常用意象、语气边界。
- `quotes/aemeath-dialogue-patterns.md`：基于授权剧情原文和台词提炼的身份回答、对话推进、日常转换规则。
- `quotes/aemeath-fleet-snowfluff-songs.md`：飞行雪绒歌曲设定、歌曲主题、角色含义和对话触发规则，不保存完整歌词。
- `quotes/aemeath-official-voice-index.md`：官方语音条目索引和音频 URL，不保存完整台词原文。

使用原则：

- 生成桌宠回复时优先读 `quotes/aemeath-style.md`。
- 需要避免“设定堆叠”或优化普通聊天口吻时读 `quotes/aemeath-dialogue-patterns.md`。
- 聊飞行雪绒、歌曲、创作、歌友会或音乐意象时读 `quotes/aemeath-fleet-snowfluff-songs.md`。
- 需要核对官方语音条目是否存在时再读 voice index。
- 不把完整官方语音文本直接塞进常驻提示词；需要核对原文时再读授权 raw。

### 3. 来源与专有名词类

目录：`sources/`

- `sources/aemeath-sources.md`：爱弥斯官方角色页来源与覆盖内容。
- `sources/aemeath-yuanhangxing-sources.md`：《远航星》任务剧情来源。
- `sources/aemeath-zuoyequnxing-sources.md`：《昨夜群星》任务剧情来源。
- `sources/aemeath-related-terms-sources.md`：官方名词注释中与爱弥斯相关的 33 个词条来源。
- `sources/aemeath-solaris-exploration-sources.md`：索拉里斯探索目录中与拉海洛、罗伊冰原、隧者相关的 7 个词条来源。
- `sources/aemeath-authorized-text-sources.md`：用户授权提供的三份剧情/台词文本来源记录。
- `sources/aemeath-fleet-snowfluff-songs-sources.md`：飞行雪绒歌曲、音乐平台、发布页和歌词/解读参考来源。
- `sources/aemeath-rahel-mainline-sources.md`：用户补充的拉海洛主线剧情原文来源记录。

使用原则：

- `sources/` 回答“这个信息来自哪里”。
- 官方名词和世界观补充先读 `world.md`，要回源时再读 `sources/`。
- 新增官方页面、中文社区页面或截图来源时，先建/更新来源索引，再更新整理后的摘要。

### 4. 剧情故事类

目录：`story/`

- `story/aemeath-official-story-summary.md`：爱弥斯 5 段角色故事摘要。
- `story/aemeath-quest-yuanhangxing-summary.md`：《远航星》剧情摘要。
- `story/aemeath-quest-zuoyequnxing-summary.md`：《昨夜群星》剧情摘要。
- `story/rahel-mainline-timeline.md`：漂泊者重回拉海洛后，从《冰原下的星炬》到当前最新《在熔解的夜空下》的主线顺序、关系网和对爱弥斯人设的影响。

使用原则：

- 做人物动机、关系、经历、情绪弧线时读这里。
- 问到时间线顺序时，先按 `characters/aemeath.md` 的“人物弧线摘要”定主线，再读《远航星》《昨夜群星》摘要补细节。
- `story/` 保存面向使用的剧情理解，不保存整页官方剧情台词。
- 如果需要核对具体截图原文或剧情行级证据，再回 `raw/`。

### 5. 整体世界观设定类

文件：`world.md`

覆盖：

- 拉海洛、罗伊冰原、星炬学院、深空联合。
- 隧者、炉芯、隧门、隧锚、隧群、日灵。
- 虚质、虚质空间、虚质磁暴、鸣式、阿列夫一相关间接设定。
- 索拉里斯探索补充，如罗伊冰原地表、隧者通史、拉海洛地理。

使用原则：

- 遇到专有名词先查 `world.md`。
- `world.md` 只放摘要和解释，不放原文长段。
- 如果新增世界观词条，更新 `world.md` 后也要更新本索引的覆盖范围。

## Raw 层

目录：`raw/`

`raw/` 是证据层，不是常规读取入口。它保存用户截图摘录、抓取记录、行级/条目级指纹、统计摘要和用户确认过的剧情 raw。

当前主要文件：

- 角色档案截图：`raw/aemeath-profile-user-screenshot.md`
- 角色故事截图：`raw/aemeath-story-zaixueyuanshang-user-screenshot.md`、`raw/aemeath-story-user-screenshots-supplement.md`
- 用户确认主剧情 raw：`raw/aemeath-main-character-plot-user-approved.md`
- 用户授权完整原文：`raw/aemeath-yuanhangxing-full-script-user-authorized.txt`、`raw/aemeath-zuoyequnxing-full-script-user-authorized.txt`、`raw/aemeath-voice-lines-user-authorized.txt`
- 用户补充拉海洛主线原文索引：`raw/aemeath-rahel-mainline-user-provided-index.md`，对应 8 个新增 `raw/aemeath-rahel-mainline-*.txt`；《远航星》《昨夜群星》沿用已有授权完整原文。
- 用户授权歌词原文：`raw/aemeath-fleet-snowfluff-lyrics-user-authorized.md`，对应行号索引 `raw/aemeath-fleet-snowfluff-lyrics-user-authorized-index.md`
- 官方语音：`raw/aemeath-official-wiki-voice-capture.md`、`raw/aemeath-official-wiki-voice-fingerprints.jsonl`、`raw/aemeath-official-wiki-voice-fingerprints.summary.json`
- 任务剧情指纹：《远航星》与《昨夜群星》的 `capture.md`、`line-fingerprints.jsonl`、`summary.json`
- 名词与世界观来源指纹：`raw/aemeath-related-official-terms-*`、`raw/aemeath-solaris-exploration-*`

使用原则：

- 只有在摘要不够、需要回源核验、需要引用用户截图原文或需要检查指纹时才读 `raw/`。
- `raw/` 中官方长文本和官方语音一般不批量常驻；用户明确授权提供的完整文本可以保存，但常规对话仍优先读取整理层，只有核验和深度提炼时回读 raw。

## 维护规则

- 新增、删除、重命名或大幅修改 `knowledge/` 中任何文件后，必须同步更新本 `index.md`。
- 新增角色资料时：更新 `characters/`，必要时更新 `raw/` 和 `sources/`。
- 新增台词/语气资料时：更新 `quotes/`，必要时更新 voice index 或 raw 指纹。
- 新增剧情资料时：更新 `story/`，必要时更新 raw 证据。
- 新增专有名词或世界观资料时：更新 `world.md` 和 `sources/`。
- 修改可复用知识结构时，按项目规则在 `changes/` 留记录。

## 当前主题

当前知识库重点服务角色：爱弥斯（鸣潮）。

当前桌宠默认关系：用户漂泊者 = 本项目语境下的漂泊者。爱弥斯应把漂泊者视作曾救下她、与她在小屋共同生活并照顾过她、被她认作家人、后来又重新找回她的重要之人。“同桌”只是轻松称呼，不是主关系。

后续如果新增其他角色，应按同样五类组织：

- `characters/{role}.md`
- `quotes/{role}-style.md`
- `story/{role}-*.md`
- `sources/{role}-*.md`
- `raw/{role}-*.md`
