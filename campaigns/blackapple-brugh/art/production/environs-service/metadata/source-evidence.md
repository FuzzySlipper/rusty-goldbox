# Environs service source evidence

The six prompts were based on the supplied Release 21 text and the campaign canon. Relevant source text was read from `/tmp/blackapple-r21.txt`; no source PDF image was opened or passed to image generation.

- A1 Wild Dog Lair: `/tmp/blackapple-r21.txt:237-261`; `canon/locations.md:66`; inventory row `art-inventory.md:118`.
- A2 Dr. Livinius Sanitarium: `/tmp/blackapple-r21.txt:262-376`; `canon/locations.md:67`; inventory row `art-inventory.md:114`.
- A3 Hen's Teeth: `/tmp/blackapple-r21.txt:380-454`; `canon/locations.md:68`; inventory row `art-inventory.md:113`.
- A4 Shrine of Confession: `/tmp/blackapple-r21.txt:455-489`; `canon/locations.md:69`; inventory row `art-inventory.md:115`.
- B14 Faehill: `/tmp/blackapple-r21.txt:1115-1152`; `canon/locations.md:46`; inventory row `art-inventory.md:116`.
- B15 Fairy Ruins/Billy: `/tmp/blackapple-r21.txt:1153-1185`; `canon/locations.md:47`; inventory row `art-inventory.md:117`.

The accepted/current palette references were inspected before generation: `production/candidates/tenpenny_forest-candidate.png` and `references/blackapple_village.png`; hashes and the no-input policy are in `palette-references.json`.

All six tool outputs are untouched RGB PNGs at native 1672x941. Each raw copy and selected `originals/` copy has equal bytes and SHA-256; the embedded native PNG metadata chunks remain in both copies.
