# Environs service independent-judge handoff

Task scope: #9305 environs service. This handoff contains six original wide context pictures, exact prompts, native raw copies, and per-asset provenance. The images are source-side originals only; no pack/export/runtime acceptance is claimed.

All six actual outputs are native `1672x941` RGB PNGs from the built-in Codex `image_gen` path. Each `raw/` copy and its matching `originals/` copy are byte-identical. The PNG `caBX` metadata chunk and the untouched native tool output path are retained in each JSON record.

| Runtime ID | Original | Raw | SHA-256 |
| --- | --- | --- | --- |
| `environs_hen_teeth` | `originals/environs_hen_teeth.png` | `raw/environs_hen_teeth-exec-019e99f4.png` | `2b296b4a17ce8a693a3bab8f05a3498f12672cd4db98a9b5dd39b8eb31555d4a` |
| `environs_sanitarium` | `originals/environs_sanitarium.png` | `raw/environs_sanitarium-exec-230de8ce.png` | `228e16772e74718308bc5592c5a74e6d0e95a596f40c4b41e401b81e3a0efbaa` |
| `environs_shrine_confession` | `originals/environs_shrine_confession.png` | `raw/environs_shrine_confession-exec-72709b70.png` | `25839b0415ef872d8206953d21c448d3e57aaa3fce1968f5a62f884e63b2567e` |
| `environs_faehill` | `originals/environs_faehill.png` | `raw/environs_faehill-exec-3132729c.png` | `1fc75a721c40b67ff47fca33acd38c93fb759d28e7e105beca8c2492ade8a9bf` |
| `environs_fairy_ruins` | `originals/environs_fairy_ruins.png` | `raw/environs_fairy_ruins-exec-969c2fbe.png` | `aaf627e736a109d6ea00accc43f7c9909485322daa3d10a4ca3a2fc8c8e2e765` |
| `environs_wild_dog_lair` | `originals/environs_wild_dog_lair.png` | `raw/environs_wild_dog_lair-exec-b35f5bfc.png` | `1c4564724e224111070f1eb357e820a8fa3dc2992fde545fd61a13469c24e41b` |

Exact prompts are in `prompts/<runtime_id>.md`, with hashes and native tool paths in `metadata/<runtime_id>.json`. `metadata/manifest.json` is the collection index; `metadata/palette-references.json` records the inspected Tenpenny forest and Blackapple village palette references and confirms they were not passed as generation inputs. `metadata/source-evidence.md` records the Release 21/canon evidence and the explicit PDF-image exclusion.

Selection was bounded: six originals, zero targeted corrections, no programmatic pixel edits/crop/resize/Pillow. The path-handoff receipt for the initial stray root directory is at `/tmp/goldbox-9305-environs-service/path-handoff-friction.txt`.

The complete authorized-tree checksum list is `metadata/file-manifest.tsv`; a temp copy for the independent judge is `/tmp/goldbox-9305-environs-service/JUDGE_HANDOFF.md`.
