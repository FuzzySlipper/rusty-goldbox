# Provenance: placeholder-art

The images and sounds in this module are original placeholders made for Rusty
Goldbox: plain shapes and seeded colour noise drawn by a script (PIL), and
tones and filtered noise synthesised by a script (Python's wave module), not
derived from any other artwork or recording. Images are 8-bit RGBA PNGs, the
format the Engine renderer admits; sounds are 16-bit mono 22,050 Hz WAV.

| Asset | File | What it is |
| --- | --- | --- |
| `stone_wall` | `walls/stone.png` | A 256 x 64 wall set: stone wall, wooden door, flagstone floor and dark stone ceiling, 64 x 64 each |
| `hall` | `backdrops/hall.png` | A torchlit archway |
| `crypt` | `backdrops/crypt.png` | Coffins under vaults |
| `skeleton` | `sprites/skeleton.png` | A sprite sheet of 32 x 48 frames facing right: a four-frame idle sway and a three-frame sword attack |
| `fighter`, `cleric`, `magic_user`, `thief` | `sprites/<id>.png` | Hero sprite sheets of 32 x 48 frames facing right: a two-frame idle and a two-frame attack |
| `bones` | `sprites/bones.png` | A single 32 x 16 frame: a skull and scattered bones, for lying on a floor |
| `fighter_portrait`, `cleric_portrait`, `magic_user_portrait`, `thief_portrait` | `portraits/<class>.png` | 48 x 48 head-and-shoulders portraits matching the hero sprites |
| `skull` | `icons/skull.png` | A 24 x 24 skull icon |
| `pillar` | `sprites/pillar.png` | A single 32 x 48 frame: a stone pillar with capital and base, for combat terrain |
| `altar` | `pictures/altar.png` | A sheet of four 160 x 100 frames: a stone altar whose glow pulses (`glow`, 4 fps), an event picture |
| `bones_crunch` | `sounds/bones.wav` | Three short cracks, 0.6 seconds |
| `crypt_music` | `music/crypt.wav` | A 4-second low drone that loops without a seam |
| `battle_music` | `music/battle.wav` | A 2-second pulsing chord that loops without a seam |
| `stone_tile`, `stone_frame`, `stone_button`, `stone_title` | `skins/stone_*.png` | The `stone` skin's pictures: a 32 x 32 dark block tile, a 24 x 24 bevelled frame with corner studs (slice 8), a 12 x 12 button face (slice 4), and a 240 x 40 crest between two torches |
| `parchment_tile`, `parchment_frame`, `parchment_button` | `skins/parchment_*.png` | The `parchment` skin's pictures: a 32 x 32 speckled parchment tile, a 24 x 24 leather frame (slice 8) and a 12 x 12 button face (slice 4) |

`skins/stone.json` and `skins/parchment.json` are the two skins; the sample
crypt names `stone`.
