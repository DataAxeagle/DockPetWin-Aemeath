# 爱弥斯官方语音抓取记录

来源：库街区《鸣潮》官方 WIKI - 爱弥斯。

## 抓取结果

- 抓取日期：2026-05-24
- 页面标题：爱弥斯-鸣潮WIKI官网-鸣潮图鉴-库街区
- 页面更新时间：2026-02-26
- 语音条目总数：116
- 已读取展示文字条目数：116
- 分区：个性语音、战斗语音
- 聚合文本 SHA-256：9982491e334d9da92e0cf06564691178878198f6d03da733832e64f988c917e9

## 抓取方式

通过 Chrome 渲染官方 Wiki 页面后，读取 `.voice-item-layout` 条目；逐条点击语音条目，让页面在相邻 `.voice-item-input-container` 中展示文字；同时读取页面当前 `<audio>` 的官方音频地址。

## 落库文件

- `raw/aemeath-official-wiki-voice-fingerprints.jsonl`：每条语音的标题、分区、音频 URL、展示文字长度和展示文字 SHA-256 指纹。
- `raw/aemeath-official-wiki-voice-fingerprints.summary.json`：本次抓取的汇总校验。
- `quotes/aemeath-official-voice-index.md`：面向阅读的官方语音标题和音频索引。

## 文本保存策略

本次确实逐条读取了官方页面展示的语音文字，但不把 116 条官方完整台词批量复制进知识库。需要核验具体原句时，回到官方 Wiki 页面逐条展开，或用本目录中的指纹文件对比重新抓取结果。
