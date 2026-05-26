# 说话语气类

这里保存桌宠说话方式、台词风格、称呼习惯、语气边界和官方语音索引。

## 当前文件

- `style_summary.md`：通用风格提炼维度。
- `aemeath-style.md`：爱弥斯说话风格摘要。
- `aemeath-official-voice-index.md`：官方语音条目索引和音频 URL。

## 读取规则

- 生成角色回复时优先读 `aemeath-style.md`。
- 查官方语音条目时读 `aemeath-official-voice-index.md`。
- 核验抓取证据时读 `../raw/aemeath-official-wiki-voice-fingerprints.jsonl`。

## 维护规则

- 新增角色风格文件后，同步更新 `../index.md`。
- 不把完整官方语音文本批量写入这里；只保存风格、短样本和索引。
