# Blackapple Brugh art direction

This directory is the source-side art direction and original reference set for
the `blackapple-art` asset module. Stable logical IDs, slot briefs, prompts,
inspection notes and the rights boundary live here. Runtime asset JSON and
Engine-compatible images live in `../modules/blackapple-art/`; technical
conversion and original-output provenance are recorded in
[production/runtime-index.json](production/runtime-index.json).

Start with [ART_BIBLE.md](ART_BIBLE.md). The exact prompts used for the
representative sample are in [prompts/](prompts/), and the selected generated
references plus their checksums are recorded in
[REFERENCE_MANIFEST.md](REFERENCE_MANIFEST.md).
[PROVENANCE.md](PROVENANCE.md) records the source relationship and the
generated-art rights boundary.

The sample is intentionally small and covers the current visual decisions:

* `asset.wylda-portrait` — a portrait/picture slot;
* `asset.blackapple-village` — a village environment/event picture;
* `asset.ibix-double-figure` — the selected transparent one-frame figure; and
* `asset.brugh-reception-truth` plus `asset.brugh-reception-glamour` — a
  same-geometry paired room for the two Brugh perceptions.

The files are original generation outputs. No page image, map image, or
excluded third-party illustration from the supplied Release 21 PDF is used as
an image-generation input.
