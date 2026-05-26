# 爱弥斯相关索拉里斯探索抓取记录

抓取日期：2026-05-24

来源目录：<https://wiki.kurobbs.com/mc/catalogue/list?fid=1287&sid=1291>

## 目标

检查“索拉里斯探索”官方目录中是否存在可补充爱弥斯人设和剧情理解的文字资料。用户明确要求图片不用读取，因此本次只处理页面文本。

## 方法

1. 使用 Chrome CDP 打开官方目录。
2. 提取目录页中的 36 个词条链接。
3. 按爱弥斯现有资料中的地点、组织、机体和剧情关键词筛选。
4. 读取 7 个强相关词条的页面文字，跳过图片。
5. 将可用于人设和剧情理解的内容写入 `world.md`，并保存来源与指纹。

## 已读取词条

- 八方水土·罗伊冰原·冰原地表
- 纪世通鉴：隧者
- 八方水土·拉海洛 Vol.4
- 八方水土·拉海洛 Vol.3
- 八方水土·拉海洛 Vol.2
- 八方水土·拉海洛 Vol.1
- 纪世通鉴：拉海洛序篇

## 未纳入词条

黎那汐塔、黑海岸、瑝珑今州、人文特刊和通史总集条目与当前爱弥斯资料关联较弱，暂不纳入。后续若新增剧情把这些区域或组织变成直接上下文，再另行补充。

## 保存位置

- 摘要：`world.md`
- 来源索引：`sources/aemeath-solaris-exploration-sources.md`
- 原文指纹：`raw/aemeath-solaris-exploration-fingerprints.jsonl`

## 存储原则

`raw/aemeath-solaris-exploration-fingerprints.jsonl` 只保存标题、来源 URL、页面文字长度、sha256 和抓取时间，不保存官方页面全文。
