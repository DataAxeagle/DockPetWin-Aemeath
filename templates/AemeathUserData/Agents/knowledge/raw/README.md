# 原始资料层

这个目录用于保存从官方页面或中文社区页面读取到的原始资料证据，包括来源 URL、抓取时间、页面标题、DOM 定位方式、条目索引、媒体 URL、文本长度、文本哈希和聚合校验信息。

## 存放原则

- `raw/` 保存“资料从哪里来、怎么读到、读到了哪些条目、如何回源核验”。
- `characters/`、`story/`、`quotes/` 保存二次整理后的人设、剧情摘要和台词风格。
- `sources/` 保存来源索引和官方页面列表；`world.md` 保存世界观和专有名词摘要。
- 完整官方故事原文和完整官方台词默认不批量常驻保存；若用户明确授权并提供文件，可以保存到 raw，并在 sources 中记录授权来源与使用边界。
- 如果确实需要证明逐条语音文字已读取，用 `text_length` 和 `text_sha256` 做指纹，不把原句复制进仓库。

## 当前 raw 类型

- 用户截图摘录：角色档案、角色故事截图中可见文字。
- 用户授权原文：`aemeath-yuanhangxing-full-script-user-authorized.txt`、`aemeath-zuoyequnxing-full-script-user-authorized.txt`、`aemeath-voice-lines-user-authorized.txt`、`aemeath-fleet-snowfluff-lyrics-user-authorized.md`。
- 用户补充拉海洛主线原文：`aemeath-rahel-mainline-user-provided-index.md` 和 8 个 `aemeath-rahel-mainline-*.txt` 文件；《远航星》《昨夜群星》沿用已有用户授权完整原文。
- 用户授权歌词索引：`aemeath-fleet-snowfluff-lyrics-user-authorized-index.md`。
- 用户确认整理稿：`aemeath-main-character-plot-user-approved.md`。
- 抓取记录：`*capture.md`。
- 指纹证据：`*fingerprints.jsonl`。
- 统计摘要：`*summary.json`。

## 读取顺序

先读 `../index.md` 定位分类，再读 `characters/`、`quotes/`、`story/`、`world.md` 等整理稿。只有当整理稿不足、需要核验原文或需要回源时，才读取本目录。

## 字段建议

- `source_url`：原始页面。
- `captured_at`：抓取日期。
- `section` / `title`：页面内分区和条目标题。
- `audio_url`：官方音频地址。
- `text_length`：展示文字的字符数。
- `text_sha256`：展示文字的 SHA-256 指纹。
- `text_storage`：文本保存策略。
