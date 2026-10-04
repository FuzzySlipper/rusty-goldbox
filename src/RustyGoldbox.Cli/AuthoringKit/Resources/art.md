# Art direction and drift review (draft 0.1)

Use this file before generating or accepting campaign art. The app supports
different visual styles; the asset owner records the chosen style and uses
logical asset IDs in module data.

## Brief and rights

```yaml
style: <illustrated dark fairytale, pixel, ink, or other>
palette: <colours and contrast>
shape_language: <silhouette and line choices>
lighting: <lighting and atmosphere>
camera_or_crop: <portrait, environment, prop, or scene framing>
sampling: <nearest for pixel art, linear for smoothly scaled illustration>
source_references:
  - source: <url or local reference record>
    role: <positive target or anti-target>
    rights: <licence or reference-only status>
```

Record an original generation or drawing prompt, the tool and date, output
hash, and any transformation that was applied. Do not copy excluded source
illustrations into the product or use them as image-edit inputs. Keep the full
licence, attribution, and provenance with an adapted module where required.

## Ownership and layout

The art owner writes only the assigned source, accepted/rejected, provenance,
and asset-definition roots. The scene owner chooses when a logical asset is
shown; the art owner does not edit scene state. Pair shared-geometry images
when the same room needs a glamour/truth comparison. Record framing and
geometry so a drift judge can compare them without a hidden reference.

Use `goldbox schema media` for asset fields and sampling modes. Use the pinned
Engine-backed renderer and existing media paths; do not add a second decoder,
renderer, or asset identity mechanism.

## Independent review

Prepare a small calibration set with known style and identity matches, a
controlled mismatch, and a shared-geometry pair. A fresh judge receives the
brief, references, and candidate outputs, then records separate scores for
style, subject identity, composition/geometry, readability, and licence
provenance. Preserve the prompt, input hashes, judge instructions, output,
and disagreement notes. The judge's task is drift review, not a claim about
model performance.

## Repair path

Reject an output with a concrete reason and keep it in the rejected root. If
the problem is style drift, update the style brief or prompt and generate a
new output; do not silently edit the accepted image. If the issue is a wrong
sampling mode, correct the asset definition and rerun the visible check. If a
reference's rights are unclear, exclude it and record the unresolved source
question before acceptance.
