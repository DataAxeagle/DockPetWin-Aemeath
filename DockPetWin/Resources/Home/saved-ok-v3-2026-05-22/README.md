# Desktop Pet Home Asset Library v21

This folder is the finalized asset pack for the desktop pet home scene.

## Files

- `background/room.png`: final room background, with the large center background rug removed.
- `objects/`: furniture/object transparent PNG layers.
- `characters/`: character action transparent PNG layers, using the same character scale.
- `anchors.json`: placement, z-index, interaction anchors, and binding rules.
- `preview/final-layout.png`: final composed preview with all furniture.
- `preview/state-contact-sheet.png`: interaction-state preview sheet.
- `docs/asset-spec.md`: asset specification and interaction logic.
- `animations/`: lightweight character and object animation frames with `animation-manifest.json`.

## Layer Rules

- Background is the base layer.
- Furniture/object layers are placed above background.
- Character action layers are placed above furniture unless a specific interaction requires overlap.
- Character actions should keep the same scale. Do not resize one action independently.

## Current Interactions

- `sleep_bed_anchor_slot`: sleep on bed.
- `drink_tea_anchor_slot`: drink tea beside the tea table.
- `study_desk_chair_back_anchor`: sit at the study desk, back facing viewer, overlapping the desk front edge.
- `play_game_anchor_slot`: sit on the TV rug area facing the screen.
- `read_sofa_anchor_slot`: read near the sofa and coffee table.

## Animation

See `animations/animation-manifest.json` for frame lists and fps.

Recommended bindings:

- Sleeping: `animations/characters/sleep_breath/`
- Study: `animations/characters/study_read_page/` + `animations/objects/study_desk_page_flip/`
- Gaming: `animations/characters/play_game_idle/`, `play_game_happy/`, `play_game_bad/` + `animations/objects/gaming_station_tetris/`
- Sofa reading: `animations/characters/read_sofa_idle/`
- Tea drinking: `animations/characters/drink_tea_idle/`
