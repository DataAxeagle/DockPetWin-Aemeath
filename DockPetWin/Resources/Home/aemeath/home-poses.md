# 爱弥斯小屋室内姿态

这些素材来自附件像素风爱弥斯参考图生成，并已去除绿幕背景。默认用于 `HomeWindow` 小屋生活场景。

当前版本已经改为“人物去家具化 + 小屋坐标锚点”：姿态素材只保留爱弥斯本体和手持小物，不再包含床、桌子、椅子等家具；小屋窗口负责把她放到背景里对应家具附近。

## 姿态

- `idle_front.png`：站立待机。
- `sit_bed.png`：坐姿，放到背景床边锚点。
- `write_desk.png`：写字姿势，放到背景书桌锚点。
- `read_book.png`：读书。
- `drink_tea.png`：喝茶。
- `sleep_cushion.png`：睡觉姿势，放到背景床/软垫锚点。
- `walk_left.png`：向左走动。
- `walk_right.png`：向右走动。

## 预览与源图

- `aemeath-home-preview.png`：全部姿态预览。
- `aemeath-home-sprite-sheet-character-only-green.png`：当前使用的去家具化绿幕 sprite sheet。
- `aemeath-home-sprite-sheet-green.png`：上一版带家具的原始绿幕 sprite sheet，保留用于对比。

## 当前接入规则

`HomeWindow` 会优先加载本目录下的姿态素材，并根据小屋活动文案切换姿态：

- 写、纸条、desk -> `write_desk.png`
- 读、书、book -> `read_book.png`
- 茶、喝、tea -> `drink_tea.png`
- 睡、小睡、枕、cushion -> `sleep_cushion.png`
- 床、坐 -> `sit_bed.png`
- 走、散步、逛 -> `walk_left.png` / `walk_right.png`
- 站、idle -> `idle_front.png`

同时 `HomeWindow` 会把姿态移动到对应场景锚点：

- 床边：`sit_bed.png` / `sleep_cushion.png`
- 书桌：`write_desk.png`
- 沙发/客厅：`read_book.png`
- 茶桌/厨房：`drink_tea.png`
- 走廊/客厅路径：`walk_left.png` / `walk_right.png`
