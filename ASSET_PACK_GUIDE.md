# DockPetWin 资源包指南

这份文档说明如何为 DockPetWin Aemeath 制作或替换桌宠资源包。资源包主要控制桌面上的角色姿态、走路动画和显示尺寸；小屋里的家具与动作点位则由 `UserData\Home` 的本地配置控制。

## 资源包放在哪里

用户资源包目录：

```text
UserData\AssetPacks
```

首次启动或生成分享包时，应用通常会准备这些资源包：

- `my-pink-character`：爱弥斯分享版默认资源包。
- `default-lizz`：上游 DockCat 默认栗子资源包参考。
- `huihui-pet`：示例资源包。
- `my-pet`：空模板资源包，适合放入自己的素材测试。

`DefaultCat` 和 `HuihuiCat` 是程序内置参考素材目录，文件夹名和内部素材应保持不动。用户自定义内容建议放到 `UserData\AssetPacks`，不要直接改内置资源。

## 推荐目录结构

```text
my-pet
├─ manifest.json
├─ animations
│  └─ walk
│     ├─ walk_01.png
│     ├─ walk_02.png
│     ├─ walk_03.png
│     └─ walk_04.png
└─ poses
   ├─ dialogue
   │  └─ stand.png
   ├─ held
   │  └─ held.png
   ├─ resting
   │  ├─ idle_01.png
   │  └─ idle_02.png
   └─ transition
      ├─ stretch.png
      └─ yawn.png
```

PNG 必须带真正的透明 alpha 通道。图片里画出来的棋盘格不等于透明背景。

## manifest.json 示例

```json
{
  "id": "my-pet",
  "name": "My Pet",
  "author": "Your Name",
  "canvas_width": 1254,
  "canvas_height": 1254,
  "default_anchor": { "x": 0.5, "y": 0.88 },
  "poses": {
    "resting": "poses/resting",
    "held": "poses/held",
    "dialogue": "poses/dialogue",
    "transition": "poses/transition"
  },
  "display_sizes": {
    "held": { "width": 650, "height": 1236 }
  },
  "animations": {
    "walk": {
      "fps": 3,
      "frames": [
        "animations/walk/walk_01.png",
        "animations/walk/walk_02.png",
        "animations/walk/walk_03.png",
        "animations/walk/walk_04.png"
      ]
    }
  }
}
```

字段说明：

- `id`：资源包唯一 ID，建议使用英文、小写和连字符，例如 `my-pet`。
- `name`：设置界面展示名称。
- `author`：作者名。
- `canvas_width` / `canvas_height`：素材默认画布尺寸。
- `default_anchor`：角色贴地锚点，`x` 和 `y` 都是 0 到 1 之间的比例。
- `poses`：各类静态姿态目录。
- `display_sizes`：特殊姿态显示尺寸覆盖，目前常用于 `held`。
- `animations.walk`：走路动画设置。

## 姿态素材

资源包可以提供这些姿态：

- `resting`：休息姿态，可以放一张或多张 PNG。
- `held`：鼠标拖动时的抱起姿态。
- `dialogue`：对话或提示时使用的站立姿态。
- `transition`：休息和散步之间的过渡姿态，例如伸懒腰、打哈欠。
- `animations\walk`：走路序列帧。

如果某一类资源缺失、路径写错或图片加载失败，DockPetWin 会对缺失类别回退使用默认资源。这样可以先做一部分素材，再逐步补齐。

## 走路动画

最稳定的方式是直接提供透明 PNG 序列帧：

```text
animations\walk\walk_01.png
animations\walk\walk_02.png
animations\walk\walk_03.png
animations\walk\walk_04.png
```

然后在 `manifest.json` 中写入：

```json
"animations": {
  "walk": {
    "fps": 3,
    "frames": [
      "animations/walk/walk_01.png",
      "animations/walk/walk_02.png",
      "animations/walk/walk_03.png",
      "animations/walk/walk_04.png"
    ]
  }
}
```

`fps` 控制走路帧播放速度。角色动作偏慢可以调高，动作太急可以调低。

## 可选：从绿幕视频抽帧

如果只有绿幕 MP4，也可以让资源包指向视频：

```json
"animations": {
  "walk": {
    "fps": 3,
    "video": "animations/walk/walk.mp4",
    "video_frame_count": 4,
    "frames": []
  }
}
```

这个功能需要用户电脑的 `PATH` 中能找到 `ffmpeg` 和 `ffprobe`。DockPetWin 不会捆绑它们。工具可用时，应用会从视频中抽帧、去绿幕，并把结果缓存到：

```text
UserData\VideoCache
```

对外发布资源包时，仍建议直接提供透明 PNG 序列帧，避免新用户因为缺少本机工具导致动画无法生成。

## 抱起姿态尺寸

如果 `held` 图片比默认画布更高或更宽，可以单独设置显示尺寸：

```json
"display_sizes": {
  "held": { "width": 650, "height": 1236 }
}
```

省略时，应用会使用 `canvas_width` 和 `canvas_height` 显示抱起姿态。

## 小屋动作和点位

桌面资源包不直接控制小屋里的动作位置。小屋中的人物点位、家具点位和本地覆盖配置通常在：

```text
UserData\Home
```

常见本地配置：

- `placements.local.json`：人物动作位置覆盖，例如床上休息、沙发旁读书、游戏区动作点。
- `furniture.local.json`：家具位置覆盖，例如沙发、地毯、茶几、电视柜等。

这类 `.local.json` 适合用户自己微调，不建议提交到公共仓库，除非它们是分享包必须携带的默认点位。

## 制作建议

- 保持每个姿态的角色脚底或身体锚点一致，否则状态切换时会跳动。
- 图片边缘留出适度透明边距，避免拖动或缩放时被裁切。
- 同一资源包内尽量统一画布尺寸。
- 走路帧的角色大小、脚底高度和朝向要稳定。
- 文件名建议使用英文、数字、下划线或连字符，避免路径兼容问题。
- 对外分享前，先在设置里切换到该资源包，确认休息、散步、拖动和对话都能正常显示。

## 常见问题

### 设置里看不到我的资源包

检查资源包文件夹是否在 `UserData\AssetPacks` 下，并确认根目录存在 `manifest.json`。

### 切换后还是显示默认角色

通常是 manifest 路径写错、图片损坏或对应类别没有素材。DockPetWin 会自动回退默认资源，所以看起来像没有切换。打开设置里的资源包状态信息，优先检查缺失项。

### 透明背景变成白底或黑底

图片没有真正的 alpha 通道，或者导出时被合成到底色。请重新导出为带透明通道的 PNG。

### 走路动画不播放

检查 `animations.walk.frames` 是否写了正确的相对路径，或确认 `video` 模式所需的 `ffmpeg` / `ffprobe` 能在命令行中直接运行。

### 更新软件会不会覆盖我的资源包

只要保留旧的 `UserData`，自定义资源包就会保留。更新时不要删除 `UserData\AssetPacks`。
