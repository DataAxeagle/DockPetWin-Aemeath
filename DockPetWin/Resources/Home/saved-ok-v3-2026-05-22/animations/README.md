# Animation Assets

This folder adds lightweight animation frames on top of the finalized v21 home asset pack.

## Manifest

Use `animation-manifest.json` as the source of truth. It lists frame paths, fps, looping rules, and recommended state bindings.

## Character Animations

- `characters/sleep_breath/`: sleeping breathing loop.
- `characters/study_read_page/`: subtle seated reading loop for the study desk character.
- `characters/play_game_idle/`: normal controller hand/body movement.
- `characters/play_game_happy/`: happy/winning game reaction.
- `characters/play_game_bad/`: frustrated/losing game reaction.
- `characters/read_sofa_idle/`: sofa reading loop with a small page turn cue.
- `characters/drink_tea_idle/`: tea drinking idle loop.

All character frames keep the same action canvas size within each animation set. Do not resize individual frames inside one animation.

## Object Animations

- `objects/gaming_station_tetris/`: animated TV/gaming-station frames with a simple falling-block screen sequence.
- `objects/study_desk_page_flip/`: study desk frames with the open book flipping pages.

The study desk page flip is an object-layer animation, not a character-layer animation. Use it together with `characters/study_read_page/`.

## Preview Strips

`previews/` contains horizontal strips for fast visual checking. They are not runtime assets unless the consuming app wants sprite sheets.

`previews/gifs/` contains animated GIF previews for review only. Use the PNG frame folders for runtime.

## Note On Blinking

Do not fake blinking by drawing rough eye lines over existing sprites. If precise blinking is required later, generate dedicated blink frames from the source character reference and replace the relevant PNG frames as a clean redraw pass.
