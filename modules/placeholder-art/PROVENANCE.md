# Provenance: placeholder-art

The images in this module are original 1990s DOS VGA-style pixel art created for
Rusty Tales (generated locally on Strix Halo via Krea 2 Turbo with the
`civitai_pixelart_krea2_5x5` adapter at strength `0.85`, seed `1993`, then cut
out, downscaled with nearest/box filtering, and packed into 8-bit RGBA PNGs for
the Engine renderer). The sounds are original 16-bit mono 22,050 Hz WAVs
synthesised by script (Python's `wave` module).

| Asset | File | What it is |
| --- | --- | --- |
| `stone_wall` | `walls/stone.png` | A 512 × 128 wall set: stone wall, arched iron-bound door, flagstone floor and dark vaulted stone ceiling, 128 × 128 each |
| `hall` | `backdrops/hall.png` | A 320 × 200 pillared stone chamber with flickering braziers |
| `crypt` | `backdrops/crypt.png` | A 320 × 200 torchlit stone crypt corridor |
| `skeleton` | `sprites/skeleton.png` | A 256 × 192 sprite sheet of 64 × 96 frames facing right: a four-frame idle sway and a three-frame sword attack (`frame_count: 7`) |
| `fighter`, `cleric`, `magic_user`, `thief` | `sprites/<id>.png` | Hero sprite sheets of 64 × 96 frames facing right (256 × 96): a two-frame idle and a two-frame attack |
| `bones` | `sprites/bones.png` | A single 64 × 32 frame: a skull, scattered bones and dagger hilt for lying on a floor |
| `fighter_portrait`, `cleric_portrait`, `magic_user_portrait`, `thief_portrait` | `portraits/<class>.png` | 96 × 96 head-and-shoulders portraits matching the hero sprites |
| `skull` | `icons/skull.png` | A 32 × 32 skull icon |
| `pillar` | `sprites/pillar.png` | A single 64 × 96 frame: a carved stone pillar with capital and mossy base, for combat terrain |
| `altar` | `pictures/altar.png` | A 1280 × 200 sheet of four 320 × 200 frames: a candlelit stone altar with pulsing relic glow (`glow`, 4 fps), an event picture |
| `bones_crunch` | `sounds/bones.wav` | Three short cracks, 0.6 seconds |
| `crypt_music` | `music/crypt.wav` | A 4-second low drone that loops without a seam |
| `battle_music` | `music/battle.wav` | A 2-second pulsing chord that loops without a seam |
| `stone_tile`, `stone_frame`, `stone_button`, `stone_title` | `skins/stone_*.png` | The `stone` skin's pictures: a 64 × 64 dark stone block tile, a 24 × 24 bevelled frame with gold trim and corner rivets (slice 8), a 12 × 12 button face (slice 4), and a 320 × 48 carved stone nameplate |
| `parchment_tile`, `parchment_frame`, `parchment_button` | `skins/parchment_*.png` | The `parchment` skin's pictures: a 32 × 32 speckled parchment tile, a 24 × 24 leather frame (slice 8) and a 12 × 12 button face (slice 4) |

`skins/stone.json` and `skins/parchment.json` are the two skins; the sample
crypt names `stone`.
