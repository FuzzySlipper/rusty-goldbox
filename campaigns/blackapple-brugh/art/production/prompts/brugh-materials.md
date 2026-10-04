# Brugh material atlas candidate

## Generation scope

- Proposed logical ID: `asset.brugh-materials` → `brugh_materials`
- Intended slot: `wall_set`
- Canvas target: exact square 1:1 atlas; four equal 2×2 quadrants
- Source inputs: none; generate an original adaptation material sheet from this
  written brief only.

## Exact generation prompt

Create an original square 1:1 painted material atlas for an illustrated
dark-fairytale first-person Brugh room. The canvas must be a clean 2×2 grid of
four equal material quadrants with no gutters: at exact 1024×1024 output size,
each quadrant is 512×512 and the seams meet at the exact centre. Each
quadrant must be filled edge to edge by one continuous tileable-looking
material, with no object extending across a quadrant boundary and no empty
margin.

Top-left quadrant: rough damp blue-slate stone wall, worn mortar seams,
moss-darkened patches, subtle ink contours. Top-right quadrant: one old closed
wooden door, front-facing and filling its quadrant, vertical dark timber boards,
iron hinges and a simple worn latch, no opening and no handle text. Bottom-left
quadrant: damp stone flag floor, irregular slate slabs, shallow seams, small
water-darkened edges and moss in joints. Bottom-right quadrant: dark timber
ceiling, broad aged beams and shadowed boards, restrained blue-green and umber
wash.

Keep all four materials coherent with the Blackapple Brugh art bible: hand-inked
contours, watercolour and diluted gouache wash on warm paper grain, charcoal,
slate, umber, moss, verdigris, bruised-violet and restrained moon-ivory accents.
The sheet is a technical material atlas, not a room illustration. Do not add
characters, animals, furniture, mirrors, candles, props outside the one closed
door, scenery, perspective vanishing points, labels, words, letters, numbers,
UI, icons, logos, watermarks, borders, framing lines, or a decorative title.
Do not make it retro pixel art, photorealistic, 3D rendered, glossy, anime, or
an imitation of a living artist. The only visible division is the exact clean
meeting of the four material quadrants; do not draw a black grid or cross.
Return one opaque square painted atlas suitable for named pixel regions
`wall`, `door`, `floor`, and `ceiling`.

## Candidate checks

1. Output is square and the content is visibly four equal 2×2 quadrants with
   the specified material in each exact position.
2. Quadrants meet without gutters, labels, borders, subjects, or UI; no
   quadrant content crosses the centre seams.
3. Style and palette match the Brugh truth-room material language while each
   quadrant remains distinguishable for linear cropped regions.
