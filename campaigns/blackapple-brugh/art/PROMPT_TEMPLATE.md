# Reusable Blackapple art prompt

Use this structure for every later asset. Keep the style block stable and
change only the subject, scene, slot, and explicit invariants. The
`Use case` slug follows the image-generation workflow taxonomy.

```text
Use case: illustration-story
Asset type: <picture, portrait, figure, wall set, or paired event image>
Logical asset ID: <asset.* ID and runtime ID>
Primary request: <one concrete subject/action from the campaign canon>
Input images: <none for an original generation; list each local reference by role for an edit>
Scene/backdrop: <Blackapple village, forest/Faehill, or named Brugh room>
Subject: <recurring identity block plus the per-asset action>
Style/medium: original illustrated dark-fairytale ink-and-wash; hand-inked varied contours, watercolour/gouache washes, paper grain, readable storybook silhouettes; no living-artist imitation
Composition/framing: <display-safe crop, focal placement, locked geometry if paired>
Lighting/mood: <warm village amber or cold fae glamour/truth treatment>
Color palette: <palette roles from ART_BIBLE.md>
Materials/textures: <wood, limewash, moss, mirror glass, damp stone, brass, cloth>
Text (verbatim): "" (no text in the image)
Constraints: original work; preserve logical landmarks and recurring identity; no source image input; clean edges and readable silhouette at display size
Avoid: retro pixel art; pixelated/nearest-filtered look; photorealism; 3D render; glossy generic fantasy; anime/chibi; vector flatness; logos; lettering; watermark; copied or traced PDF/map/art; graphic child harm or gore
```

For a transparent figure, add `transparent background with genuine alpha; no
ground plane, shadow blob, frame, or halo; feet fully visible; later one-frame
sheet anchor at the feet`. For a paired edit, add `change only materials,
surface condition, palette temperature, and perception cues; keep camera,
landmarks, door/window/mirror locations, table silhouette, subject count,
pose, and crop unchanged`.
