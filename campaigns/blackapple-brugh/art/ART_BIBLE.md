# Blackapple Brugh art bible

## Purpose and authority

This is the reusable visual contract for the Blackapple Brugh adaptation. It
supports the later `blackapple-art` integrator and independent asset workers;
it does not define runtime state or replace the campaign canon. The story
anchors come from the retained Release 21 adaptation in `../canon/`. The
visual treatment, subject identity choices, and all generated images below are
original adaptation work.

The direction is **illustrated dark fairytale in ink and wash**, with a warm
human village set against an unsettling cold fae court. It is not retro pixel
art. The medium is a recurring surface and lighting language; recurring
Blackapple, Wylda, and Brugh motifs are a separate identity system so the
same subjects can be carried into a later style or a different campaign.

## Style system

### Medium and linework

* Hand-inked contours with varied pressure: firm silhouettes, fine interior
  marks, and occasional broken dry-brush edges.
* Watercolour and diluted gouache washes on visible warm paper grain. Let a
  few paper breaks remain in large quiet areas; do not turn every surface into
  noise.
* Painterly colour shapes stay subordinate to a readable silhouette and a
  clear focal action. Details support play readability at a small display size.
* Natural storybook proportions: adults roughly 7 heads tall, children small
  and recognisable, and figures grounded by a visible foot/ground contact. No
  chibi distortion, fashion illustration elongation, or anatomy that makes an
  interactive figure hard to read.

### Palette

The palette is a guide, not a demand for flat swatches. Values and temperature
must remain legible when an image is scaled down.

| Role | Colour family | Use |
| --- | --- | --- |
| Blackapple warmth | lantern amber, ochre, russet, muted cream | windows, bread, wood, village welcome, human skin under firelight |
| Living edge | moss green, lichen, muted olive | hedges, forest covenant, worn cloth, quiet clues |
| Mortal shadow | charcoal ink, slate, umber | linework, dusk, barns, soil, safe contrast |
| Fae cold | blue slate, blue-green, verdigris, moon ivory | Brugh stone, mirrors, court light, glamour's distant beauty |
| Truth accent | wet black, bruised violet, iron grey | damp masonry, tarnish, mould, revealed mechanisms; keep horror non-graphic |
| Story signal | black apple near-black with a small burgundy glint; thread gold | apples/seeds and the seven ankle threads as sparse repeatable clues |

Village images may carry a small amount of fae blue at the forest or mirror
edge. Brugh images may carry a warm memory of amber or russet, but the court
should never become a second cosy village.

### Lighting and mood

* Blackapple: low sun, dusk, or lantern pools with long soft shadows; warmth
  gathers around people and practical work while the forest edge stays cool.
* Forest/Faehill: overcast green-grey or moonlit blue with a few intentional
  amber fireflies, shrine candles, or windows.
* Brugh glamour: cold moonlight polished through mirror glass, clean ivory,
  blue-green marble, silver reflections, and ornamental distance. It is
  beautiful enough to invite trust and strange enough to unsettle.
* Brugh truth: the same light direction and room geometry, but damp slate,
  worn plaster, moss, soot, tarnished metal, animal traces, and practical
  clutter. Use implication and texture rather than gore.
* Contrast is a clue. Never use a full-screen colour wash that erases object
  identity or makes the two modes look like different maps.

### Composition

* Establish a strong foreground/midground/background read and one primary
  focal subject. Keep a quiet margin for event text or UI crops.
* Use doorway, mirror, window, stair, table, and arch landmarks as repeatable
  anchors. In paired scenes, their positions, proportions, and silhouettes are
  locked; only material, decoration, surface condition, and perception cues
  change.
* Use portrait-safe central head-and-shoulder framing for faces. Use a clear
  silhouette and floor contact for figures. Avoid tiny faces in wide scenes.
* Wide event pictures should read at roughly 320 px wide. Portraits should
  read at the UI card size. A later integrator can crop or resize, but the
  source image must keep the focal subject away from the crop edge.

### Materials

Use tactile, grounded surfaces: pig-farm timber, limewashed plaster, old
iron, damp stone, cracked mirror silver, wool, linen, brass, moss, leaf litter,
and paper. Fae surfaces can be immaculate in glamour and worn in truth, but
both versions must share the same physical landmarks.

## Recurring visual identity

These anchors are independent of the ink-and-wash medium. If a later campaign
changes medium, preserve the anchors unless a deliberate adaptation says
otherwise.

### Blackapple village and forest

* A remote pig-farming settlement: low timber and limewashed buildings, broad
  eaves, muddy lanes, practical tools, stacked deadwood, and warm windows.
* Horseshoes over doors and small black-apple seed or leaf marks recur as
  local folk details. They are clues, never a logo or repeated border.
* The old forest begins close to the village. Living green is dense and
  watchful; fallen wood is handled respectfully. Faehill is a ringed mound and
  landmark, not a generic dungeon mouth.
* The village's human palette is amber/moss/slate. Cold blue-green appears at
  mirror edges, deep woods, or places where the old court is felt.

### Wylda Figwort

Wylda is an original visual anchor for `npc.wylda-figwort`, separate from the
source fact that she is nineteen, the oldest Figwort child, a romantic
self-described adventurer, and a skilled thief. Depict her as a lean young
adult with dark auburn hair in a loose braid, an alert oval face, a weathered
teal travel coat over a cream shirt, a narrow brass mirror token, and a
practical satchel. Her expression is curious and conspiratorial rather than
brooding. Keep her capable and recognisably mortal; she is not an elf, a
princess caricature, or a sexualised child. The coat and brass token should
recur in portraits and event scenes, with sensible wear changes.

### The Brugh court

* Broken oval mirrors, seven fine gold threads, blue-green stone, damp seams,
  and theatrical court objects are the shared visual vocabulary.
* The Elf Lord's court is a collection of old ceremony and improvised
  entertainment: long tables, costumes, animal guests, music, games, and
  dangerous etiquette. Avoid generic gothic castles.
* The same room can be beautiful or shabby depending on a member's persisted
  `glamour`/`truth` mode. The difference is perceptual presentation, not a
  second location or a second set of exits.
* Ibix doubles are child-shaped at a distance but should never be portrayed as
  a real child being graphically harmed. A shell-like mask, hoof hint, wrong
  stillness, or mismatched gesture can be an evidence cue when the scene calls
  for one.

## Anti-targets

Every generation prompt includes these constraints as appropriate:

* retro pixel art, pixelated sprites, 8-bit/16-bit game art, or forced nearest
  filtering;
* photorealism, 3D render, glossy concept-art finish, plastic materials,
  vector flatness, anime/manga styling, chibi proportions, or generic trading
  card fantasy;
* imitation of a living artist or named modern illustrator;
* copied, traced, collaged, or edited source PDF pages, maps, covers, or any
  excluded third-party illustration;
* logos, lettering, captions, UI, watermarks, signatures, borders, or invented
  map labels in the image;
* randomised room geometry, changed door/window locations, or a glamour/truth
  pair that cannot be aligned;
* graphic child harm, gore, torture detail, sexualisation, or real-world
  diagnostic imagery.

## Asset and slot briefs

The current Engine media contract is the source of these requirements:
`picture` accepts an image or sheet; `figure` requires a sheet with `faces`
(`left` or `right`) and `height`; `wall_set` requires an image with `wall` and
`door` pixel regions and may also provide `floor` and `ceiling`. The upcoming
smooth-sampling work in #9313 must preserve these media kinds and atlas
boundaries while allowing these illustrated images to scale smoothly. No
runtime asset JSON is added by this art-direction task because #9305 owns the
later integrator.

| Logical ID | Runtime ID | Intended slot | Source brief and target | Transparency / layout |
| --- | --- | --- | --- | --- |
| `asset.wylda-portrait` | `wylda_portrait` | `picture` with `portrait` tag | 1024×1536 portrait-safe source; full crown/top hair, face, and brass token remain readable with headroom in the central square crop | opaque paper background; no frame or animation |
| `asset.blackapple-village` | `blackapple_village` | `picture` for event/backdrop | landscape-safe village square at 16:9 composition; keep focal action in the center 70% for current event-picture cropping | opaque; one image, no animation |
| `asset.ibix-double-figure` | `ibix_double_figure` | `figure` after one-frame sheet wrapping | 1024×1536 transparent full-body reference with a compact child-shaped-at-distance silhouette (large head, short limbs); later JSON uses `frame_size: [1024, 1536]`, `faces: "left"`, `height: 1.6`, and a bottom-centre anchor | true alpha; one frame, no animation; anchor at the feet |
| `asset.brugh-reception-truth` | `brugh_reception_truth` | `picture` for `scene.brugh_search` | 16:9 paired room; damp reception lounge, arch left, oval mirror right, long table centre, doorway rear | opaque; exact landmark geometry is shared with glamour |
| `asset.brugh-reception-glamour` | `brugh_reception_glamour` | `picture` for `scene.brugh_search` | same camera, crop, arch, mirror, table, and doorway as truth; polished courtly perception only | opaque; exact landmark geometry is shared with truth |
| `asset.brugh-wall-set` | `brugh_wall_set` | `wall_set` for later first-person areas | author as a single illustrated atlas image with named pixel regions `wall`, `door`, optionally `floor`, `ceiling`; use a 4:1 or larger atlas so borders stay readable | opaque RGBA image; no sheet frames; regions must remain inside image and not overlap |

The generated sample deliberately exercises pictures and a transparent figure.
The wall-set row is a handoff contract for #9305: it must not be faked by
stretching a picture or by adding an unowned renderer. Smooth filtering in
#9313 should be checked at native display size and at a smaller crop for
adjacent-frame/atlas bleeding, alpha halos, and line clarity.

No animation, audio, video, or font dependency is selected by this art bible.
If a later asset needs animation, keep the same figure sheet contract and
specify frame count, `frame_size`, `faces`, `anchor`, `height`, and animation
frames in its asset brief before generation.

## Reuse and edit workflow

1. Start from this bible and the relevant subject identity block.
2. Use the stable logical ID and slot brief in the prompt. State what is
   shared geometry and what is allowed to vary.
3. For a recurring subject, use an accepted reference image as a visual
   reference or edit target only when that preserves the subject invariant;
   record the exact reference path and edit prompt beside the output.
4. For a glamour/truth pair, generate one geometry-locked base, inspect it,
   then edit only material, condition, and perception cues. Do not ask a
   second independent generation to redraw the room.
5. Inspect at intended display size and original resolution. Reject an image
   when focal readability, transparency, identity, or geometry drifts even if
   its surface style is attractive.
6. A different campaign may reuse the medium grammar while changing the
   Blackapple identity anchors and palette. It must retain its own source
   rights and provenance; these references are not a generic licensed pack.

## Reference-set decision

The accepted sample and inspection observations are recorded in
[REFERENCE_MANIFEST.md](REFERENCE_MANIFEST.md). Prompt files are kept beside
the images so a later worker can reproduce the intended visual decisions
without access to this conversation or to the original source PDF.
