# Accepted reference manifest

This manifest is the durable provenance for the representative art-direction
sample. The accepted set was inspected once at a display-sized view and again
at original resolution. The display view checks silhouette, focal hierarchy,
crop safety, and paired readability; the original view checks brush/paper
texture, edges, and alpha. The images are source-side references until the
future `blackapple-art` integrator writes runtime asset definitions.

## Tool and settings

* Tool: built-in Codex `image_gen` path, as required by the image-generation
  workflow.
* Model, sampler, seed, and internal generation settings: not exposed by the
  built-in result; no values are guessed here.
* Output policy: each selected result was copied from the tool's
  `$CODEX_HOME/generated_images/01a10504-8cef-7023-8122-0923f1e56a82/` output
  directory into `references/`. The tool output remains at its original path;
  the workspace copy is the project-preserved artifact.
* Source inputs: no supplied PDF page, map, cover, or excluded third-party
  illustration was used. The glamour image used only the accepted original
  truth image in this directory as its edit target to lock geometry.

## Accepted set

| Logical ID | Workspace file | Tool output file | Prompt | Media facts | Inspection decision |
| --- | --- | --- | --- | --- | --- |
| `asset.wylda-portrait` → `wylda_portrait` | [references/wylda_portrait.png](references/wylda_portrait.png) | `exec-f4145615-d30a-4e90-a335-f0777676bc5d.png` | [wylda-portrait-square-correction-v2.md](prompts/wylda-portrait-square-correction-v2.md) | 1024×1536 RGB, opaque; portrait picture candidate | Accepted after judge-directed reframe: painted ink contours and wash, auburn braid, teal coat, brass token, central face, and a crown-safe centered square crop read at display and original size. |
| `asset.blackapple-village` → `blackapple_village` | [references/blackapple_village.png](references/blackapple_village.png) | `exec-c3a07133-4777-4cee-99ef-44d3fb3ad2ce.png` | [blackapple-village-v4.md](prompts/blackapple-village-v4.md) | 1672×940 RGB, opaque; landscape picture candidate | Accepted: painted village plate with two helpers, pigs, well, deadwood, horseshoes, forest edge, and ringed Faehill; no readable text; crop-safe at display and original size. |
| `asset.ibix-double-figure` → `ibix_double_figure` | [references/ibix_double_figure.png](references/ibix_double_figure.png) | `exec-e3a3dba8-9697-4a16-85b8-5ac901e6bafe.png` | [ibix-double-figure-child-scale-correction-v2.md](prompts/ibix-double-figure-child-scale-correction-v2.md) | 1024×1536 RGBA, alpha extrema 0–254; one-frame figure candidate | Accepted after judge-directed proportion correction: one compact child-shaped-at-distance goatfolk, shell mask, horns, feet, gold ankle thread, clean transparent corners, and readable silhouette. Later one-frame sheet brief uses `faces: "left"`, `height: 1.6`, and a bottom-centre anchor. |
| `asset.brugh-reception-truth` → `brugh_reception_truth` | [references/brugh_reception_truth.png](references/brugh_reception_truth.png) | `exec-f1e5fcea-5596-4985-837c-d41b32c6bcdc.png` | [brugh-reception-truth.md](prompts/brugh-reception-truth.md) | 1672×941 RGB, opaque; 16:9 picture candidate | Accepted geometry base: arch left, oval mirror right, central table, rear doorway, ceiling beams, attendant, and floor-stone line are legible; damp/slate/moss truth treatment reads at both sizes. |
| `asset.brugh-reception-glamour` → `brugh_reception_glamour` | [references/brugh_reception_glamour.png](references/brugh_reception_glamour.png) | `exec-fe3964b2-f4fa-4f3d-82a4-1b85269252aa.png` | [brugh-reception-glamour.md](prompts/brugh-reception-glamour.md) | 1672×941 RGB, opaque; 16:9 paired picture candidate | Accepted edit from the truth image: the same camera and landmarks remain aligned while surfaces become polished marble, ivory paneling, court textiles, silver, and cold theatrical light. |

The two Brugh files are a paired reference, not two maps. A later runtime
author must keep one underlying room geometry and select the picture by the
member's persisted `glamour`/`truth` projection.

## Judge-directed corrections

The independent #9300 image review found two bounded presentation issues in the
first accepted set: the centered square crop clipped the crown/top hair in
`wylda_portrait`, and the Ibix double read as a tall adult proportion even
though the canon calls for a child-shaped figure at a distance. The other three
references passed and were not regenerated.

The original accepted files remain preserved as the correction inputs:

* Wylda pre-correction: `metadata/candidates/corrections/wylda_portrait-pre-correction.png`, copied from original tool output `exec-679ff9c3-ec6b-4537-9544-cc456eaded43.png` under [wylda-portrait-v6.md](prompts/wylda-portrait-v6.md), SHA-256 `a9b8ba32dcc5fcd6299ce1ce27b3b60c32c79caff66faf06c0432fcc0ba6d97a`.
* Ibix pre-correction: `metadata/candidates/corrections/ibix_double_figure-pre-correction.png`, copied from original tool output `exec-c492f223-6a10-4578-aa34-d5f9cb1dd191.png` under [ibix-double-figure-v3.md](prompts/ibix-double-figure-v3.md), SHA-256 `90a89879c46876394f4dfc573d7426cd6d40b3fd9f34b1cf6e063906a146a344`.

Each exact edit prompt was written before its built-in `image_gen` call and
kept beside the source and output:

* Wylda first bounded edit: [wylda-portrait-square-correction.md](prompts/wylda-portrait-square-correction.md), output `exec-2b864032-cabd-4831-812b-0a76d80641da.png`, preserved as `metadata/candidates/corrections/wylda_portrait-square-correction-output.png`, SHA-256 `7c5e2891088bcc37b1c2ab44a08a47afa26fa12d6b76923e73f4eef9322c8ecf`. It preserved identity and style but left the crown too close to the centered crop edge, so it was not accepted.
* Wylda final bounded edit: [wylda-portrait-square-correction-v2.md](prompts/wylda-portrait-square-correction-v2.md), input the preserved first edit, output `exec-f4145615-d30a-4e90-a335-f0777676bc5d.png`, and accepted workspace copy SHA-256 `23a8f396e0de4ad4870b0a9f12b3c02300d5ff701a66c04e696547d13f8f493c`. Display and original inspection show the full crown/top hair, face, and brass token inside the centered square crop with wall headroom; identity, room landmarks, palette, and ink-and-wash treatment remain coherent.
* Ibix first bounded edit: [ibix-double-figure-child-scale-correction.md](prompts/ibix-double-figure-child-scale-correction.md), output `exec-e2beaf73-9551-45ad-b97a-0e820aa8fa0e.png`, preserved as `metadata/candidates/corrections/ibix_double_figure-child-scale-correction-output.png`, SHA-256 `6dc75d3eb503b0baba2dd9549d10b113a344572e851400257bb7d3a744101bde`. It preserved true alpha and motifs but remained too close to the tall adult source, so it was not accepted.
* Ibix final bounded edit: [ibix-double-figure-child-scale-correction-v2.md](prompts/ibix-double-figure-child-scale-correction-v2.md), input the preserved first edit, output `exec-e3a3dba8-9697-4a16-85b8-5ac901e6bafe.png`, and accepted workspace copy SHA-256 `bac596b5639b50f7efebc3647dbcd4fa15f8d2a7818e7183edb02bd6bb4348cc`. Display and original inspection show one compact child-shaped-at-distance goatfolk with the mask, horns, cloven hooves, and single gold ankle thread intact. PIL inspection reports RGBA 1024×1536, alpha extrema 0–254, zero-alpha corners, and no opaque matte; it remains a transparent source-side cutout.

The two corrections used no PDF page, map, cover, or excluded illustration as
input. Tool outputs remain in the built-in generated-images directory and the
workspace copies above are the durable review artifacts. The bounded two-edit
limit was reached for each affected reference; no further regeneration is
planned without a new concrete judge finding.

## Revision and rejection record

The rejected files are retained under `metadata/rejected/` so the prompt
workflow's concrete corrections remain auditable. They are not part of the
accepted reference set and must not be exported as runtime art.

* Wylda v1–v5 rendered as photographic fantasy portraits. The v6 prompt led
  with a painted plate, demanded visible contour/crosshatching and simplified
  planes, and produced the accepted ink-and-wash portrait. The earlier image
  candidates are `metadata/rejected/wylda_portrait-v1-rejected.png`,
  `metadata/candidates/wylda_portrait-v2-candidate.png`,
  `metadata/candidates/wylda_portrait-v4-candidate.png`, and
  `metadata/rejected/wylda_portrait-v5-rejected.png`.
* Village v1–v3 drifted into realistic castle/mountain scenes or inserted
  signs and labels. The v4 prompt used the accepted portrait/room medium,
  named the modest pig village and ordinary helping action, and explicitly
  barred writing; it produced the accepted plate. Earlier outputs are in
  `metadata/rejected/` and `metadata/candidates/`.
* Figure v1–v2 produced two raven/ibis-like subjects and a matte or scenery
  background. The v3 prompt removed the ambiguous creature name, required one
  goat-headed humanoid, and repeated true-alpha/no-background constraints; it
  produced the accepted figure. Earlier outputs are in
  `metadata/rejected/ibix_double_figure-v1-rejected.png` and
  `metadata/rejected/ibix_double_figure-v2-rejected.png`.
* One truth-room attempt was invalid because the prompt read failed before the
  prompt files had been copied into this worktree. It is preserved as
  `metadata/rejected/brugh_reception_truth-v0-prompt-read-error.png` and is
  not a reference. The corrected truth prompt produced the accepted geometry
  base above.

This record captures visual decisions and rights-relevant input status. It is
not a dated progress report; temporal workflow messages and judge discussion
belong in Den.
