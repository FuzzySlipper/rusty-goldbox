# Blackapple Brugh provenance and licence boundary

This directory is the durable provenance record for the planned
`blackapple-brugh` campaign module and its companion data and asset modules.
The canon is an adaptation plan; it does not make a runtime module or ship a
copy of the source publication.

## Source

* **Title:** *The Blackapple Brugh*, 1st Edition, Release 21 (`KH1`)
* **Author:** Kyle Hettinger
* **Copyright notice:** © 2020–2021, 2023 Kyle Hettinger
* **Reviewed filename:** `KH1-The-Blackapple-Brugh-r21.pdf`
* **Reviewed copy SHA-256:**
  `87e22e0d0bbdb66c54010d4485df05e92d0237b0234c2dffa24298250d4f6de5`
* **Public source:** [Basic Fantasy downloads](https://www.basicfantasy.org/downloads.html)
* **Source licence:** Creative Commons Attribution-ShareAlike 4.0
  International (CC BY-SA 4.0), for the source's textual materials, maps,
  floorplans, diagrams, charts, and forms as identified in its front-matter
  notice.

The supplied source's second PDF page says that most other artwork is the
property of the original artists and is used with permission. Those images are
outside the source licence and are excluded from this repository and every
future module export. The source PDF itself, its page images, and its excluded
illustrations are not copied into the campaign directory.

The complete official licence text is retained at
[LICENSE-CC-BY-SA-4.0.txt](LICENSE-CC-BY-SA-4.0.txt). The source section map,
page convention, and C1–C27 dispositions are in
[canon/source-mapping.md](canon/source-mapping.md).

## Required source attribution

The source publication credits the following people and materials. These are
preserved as source credits; they are not claims that the credited artists,
playtesters, or proofers worked on the Rusty Goldbox adaptation:

* Maps: Scott Abraham and Kyle Hettinger.
* Cover art: Vasily Ermolaev.
* Other artwork: Burger Babylon, Hieronymus Bosch, John Fredericks, Denis
  McCarthy, Colin Richards, Piotr Klimkowicz, Jonas Campe, S. Ender Thiel,
  Andy “ATOM” Taylor, W.F. Wakeman, and Andreas Blanckenstein.
* Playtesters: Heidi Hettinger and O.B. Lama.
* Proofing: Seven, James Lemon, Scott Abraham, Mike West, and Alan Vetter.

## Adaptation and modification notice

The following notice must accompany any exported or shared Blackapple Brugh
adaptation data and prose:

> This work is adapted from *The Blackapple Brugh*, 1st Edition, Release 21,
> by Kyle Hettinger, © 2020–2021, 2023. The source's textual materials, maps,
> floorplans, diagrams, charts, and forms are licensed under the Creative
> Commons Attribution-ShareAlike 4.0 International License. This adaptation
> changes the rules conversion to the Rusty Goldbox `fifth-srd` ruleset,
> reorganizes scenes for headless digital play, adds persistent per-child and
> per-double consequence records, defines an Engine-backed per-member glamour
> check, adapts the C13 pit reaction to a whole-party area position, and adds
> original recovery, accessibility, ending, and presentation decisions. The
> source's excluded artwork is not included. The adaptation is licensed under
> CC BY-SA 4.0; see `LICENSE-CC-BY-SA-4.0.txt` for the complete licence.

The adaptation notice identifies changes without claiming that the source
author endorses Rusty Goldbox or its rules conversion. Any source-derived
definition or prose added to a module must retain this notice or a reasonable
equivalent attribution in its module provenance.

## What is adapted and what is original

The retained source relationship includes the Blackapple village, its forest
and Faehill environs, the mirror rescue mystery, the Brugh's three-level room
sequence, the seven children and ibix doubles, the Elf Lord's court, and the
source's keyed locations. These relationships are paraphrased and mapped in
the canon; source mechanics and numeric stat blocks are converted rather than
copied.

Rusty Goldbox additions include:

* the `fifth-srd` level 1–3 conversion and its module contracts;
* the module split and stable logical/runtime IDs;
* the source-to-scene beat structure, recovery paths, and return accounting;
* explicit per-member `glamour`/`truth` semantics using a prospective
  `fifthsave_int` check;
* complete child and double state vocabularies and the primary-ending versus
  additive-epilogue contract;
* the whole-party C13/C13a digital treatment required by the current Core area
  position owner;
* non-graphic sensitive-content handling, opt-out/leave paths, and original
  paired palace/underground presentation direction; and
* any original campaign prose, data, maps, geometry, code, or art created for
  Rusty Goldbox.

These additions and any adapted material are released under CC BY-SA 4.0 in
the campaign module. `fifth-srd` remains a separately licensed required
ruleset module. It is not relicensed by this notice.

## Planned module boundaries

| Module | Kind | Contents and rights boundary |
| --- | --- | --- |
| `blackapple-brugh` | campaign | Campaign start, areas, events, scenes, child/double records, and adapted prose. CC BY-SA 4.0 with this provenance. |
| `blackapple-fae` | extension | Only concrete source-specific data needed by the campaign, such as converted creatures, NPCs, encounters, conditions, or the `fifthsave_int` check shape. CC BY-SA 4.0 with this provenance. It is not a ruleset or code module. |
| `blackapple-art` | assets | Original art and presentation assets with their own source notices. No excluded source illustration, page image, or unlicensed derivative is accepted. |
| `fifth-srd` | required ruleset | Existing repository ruleset dependency under its own module licence and provenance. |

The campaign references assets by logical IDs and references only modules in
its manifest `requires` list. It does not carry C# or other executable rules.
If a new primitive is required for a source-faithful behavior, the Core task
and its schema/tests own that change; the module remains data.

## Licence summary for maintainers

The source-derived and original adaptation portions of this campaign use the
same CC BY-SA 4.0 licence so that ShareAlike obligations remain clear. Keep
the complete licence file, this attribution record, and the source mapping
with every module export. Do not export the source PDF or any excluded artwork.
When adding a new source-derived definition, record its source section and
printed page in [canon/source-mapping.md](canon/source-mapping.md), describe
the modification, and update this record if the module boundary changes.
