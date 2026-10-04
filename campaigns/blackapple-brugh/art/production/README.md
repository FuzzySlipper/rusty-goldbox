# Blackapple art source and runtime packaging

Selected collection and exact consumers are in `art-inventory.md`.
`runtime-index.json` links included runtime definitions to their native source
images. Batch directories retain prompts, original images, raw generated
outputs, rejected attempts and correction lineage. Editable material is
excluded from playable module containers; only the asset module's accepted
media, definitions and notices are packed.

Opaque RGB sources receive an all-255 alpha channel with FFmpeg, without
resizing, cropping or changing their RGB planes. Existing RGBA cutouts are
copied unchanged. Each `metadata/runtime-packing.json` records native sizes,
source/runtime hashes and an exact decoded RGB comparison. The conversion is:

```bash
ffmpeg -v error -i <source>.png -vf format=rgba -frames:v 1 <runtime>.png
```

All selected media use `sampling: "linear"`. Portraits occupy the full
vertical source frame. The Ibix is a single transparent frame with a
bottom-centre anchor and `height: 0.75` Engine cells, fitting the world's
one-cell ceiling. Atlases declare exact integer wall, door, floor and ceiling
regions; no external image processor invents a renderer or container identity.

Truth/glamour images are judged together with fixed room landmarks and are
presented through Core's persisted per-member perception. Image/source
judgments, ordinary Engine observations, captures and temporal experiment
reports are retained in Den. A packed image and a source judgment alone do
not establish ordinary player-visible acceptance.
