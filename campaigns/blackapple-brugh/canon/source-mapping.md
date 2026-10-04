# Source mapping, rights, and adaptation boundary

## Source identity

The source is the supplied Release 21 file. The repository records a portable
filename and hash so the canon does not depend on a workstation attachment
path:

* **Title:** *The Blackapple Brugh*, 1st Edition, Release 21
* **Author:** Kyle Hettinger
* **Copyright notice:** © 2020–2021, 2023 Kyle Hettinger
* **Source filename:** `KH1-The-Blackapple-Brugh-r21.pdf`
* **SHA-256:** `87e22e0d0bbdb66c54010d4485df05e92d0237b0234c2dffa24298250d4f6de5`
* **Public source:** [Basic Fantasy downloads](https://www.basicfantasy.org/downloads.html)
  (the source publication is listed as KH1, *The Blackapple Brugh*, 1e Release
  21; the exact hash above identifies the reviewed copy).
* **Page numbering:** the adventure and map pages are printed 1–47; the PDF
  has 49 pages including two front-matter pages. Tables below use printed page
  numbers. Add two to obtain a PDF viewer page number.

The second PDF page (front matter, without a printed page number) states that
all textual materials, maps, floorplans,
diagrams, charts, and forms are distributed under CC BY-SA 4.0. It separately
states that most other artwork belongs to original artists and must be removed
before redistribution. This campaign carries the complete license in
`../LICENSE-CC-BY-SA-4.0.txt`; its source and modification notices are in
`../PROVENANCE.md`.

## Immutable source facts

The adaptation keeps these facts unless a later, explicit canon decision marks
the change:

* Blackapple is a remote village of 408 on the edge of an old forest. The local
  history connects its pig farming and live-tree taboo to an old fairy bargain
  involving Ludann, a squirrel, and a black apple.
* A jealous Elf Lord once ruled the region cruelly, was banished underground,
  and now lives in the Brugh beneath the Faehill. The source presents this as
  local history and leaves its oldest truth uncertain.
* Seven children followed the mirror rhyme. The Elf Lord took them through a
  mirror, charmed them into service, and sent seven ibix doubles back in their
  forms. The real children and their Brugh duties are listed in
  [characters.md](characters.md).
* The source's entry mirrors are at Figwort Manor (B16) and the Hen's Teeth
  Tavern (A3). The C1 mirror is opened by the Elf Lord and is an entry only;
  the C27 mirrors are the exits.
* The Brugh has three keyed levels and a persistent dual illusion. Immediately
  on entry, each adventurer makes one save versus Spells with the character's
  Intelligence bonus added; a failed save sees glamour and a successful save
  sees reality throughout that expedition. The adaptation implements this
  source save rule through the fifth-srd-shaped `fifthsave_int` check described
  in [premise.md](premise.md).
* The source's return effects include one day passing outside and a possible
  belief that the expedition was a dream. The adaptation retains the time
  compression but keeps persistent state and recovery explicit.

Source mechanics and numeric stat blocks are not immutable campaign facts. The
campaign uses `fifth-srd` and a data-only `blackapple-fae` extension where
source-specific creatures or checks are required.

## Source section disposition

`retain` means the relationship and useful fiction remain. `adapt` means the
fiction remains but rules, sensitivity, or presentation changes. `optional`
means it is available as a meaningful route but is not required for the core
rescue. `omit` means the adaptation does not carry that source material.

### Introduction and environs

| Source section | Pages | Disposition | Canon destination |
| --- | ---: | --- | --- |
| Introduction, Blackapple history, recent events | 1–2 | retain/adapt | [premise.md](premise.md), `beat.arrival`, `beat.parent_interviews` |
| Wandering monsters outside Blackapple | 3 | adapt/optional | `scene.environs_hooks`; use fifth-srd encounters and retreat rules |
| A1 Wild Dog Lair | 3–4 | retain/optional | `quest.wild-dog-lair`, `npc.pigman-jack`, `npc.cu-sidhe` |
| A2 Dr. Livinius' Sanitarium | 4–6 | retain/adapt | `beat.sanitarium`; avoid graphic or required brain-surgery content |
| A3 Hen's Teeth Tavern | 7–8 | retain | `beat.hen_teeth`, `area.environs`, `scene.mirror_entry` |
| A4 Shrine of Confession | 8–9 | retain/adapt | `quest.confession-shrine`; consent-forward confession and cure |
| A5 Tenpenny Wood | 9 | retain/optional | `quest.tenpenny-wood`, `faction.forest-covenant` |

### Blackapple village

| Source section | Pages | Disposition | Canon destination |
| --- | ---: | --- | --- |
| Arrival, law-and-order, random encounters | 10–11 | retain/adapt | `beat.arrival`, `beat.first_help`, `beat.child_misbehavior` |
| News, rumours, and general knowledge | 11–12 | retain/adapt | `beat.parent_interviews`, `scene.hub_investigation`; rumours become guarded evidence |
| B1 North Road Guardhouse | 12 | retain | `area.blackapple`, `beat.arrival` |
| B2 Agatha's Potions and Powders | 12–13 | retain/adapt | `npc.agatha`, shop and recovery service |
| B3 Goodall's Fine Trading | 13–14 | retain/adapt | `npc.ms-goodall`, child evidence and economy |
| B4 The Jolly Fox Inn | 14–16 | retain | safe hub, rest, rumours, hireling leads |
| B5 Village Square | 16 | retain | public trust and double-resolution scene |
| B6 Merchants' Guild | 16 | optional/adapt | `quest.tenpenny-wood`, economy pressure |
| B7 Hazard's Quality Goods | 16–17 | retain/adapt | practical supplies shop |
| B8 Pigman Jack's Homestead | 17 | retain/adapt | `npc.pigman-jack`, dogs, recoverable infection |
| B9 Chapel of St. Ludann | 17 | retain/adapt | priory relationship and safe confession |
| B10 St. Ludann Priory | 17–18 | retain/adapt | care, temple service, double containment |
| B11 Tobler's Knick Knacks and Taxidermy | 18 | retain/optional | `npc.tobler`, cemetery clue and moral investigation |
| B12 Cemetery | 18 | retain/optional | optional night evidence; no required undead clear |
| B13 Parents of the “Wicked Children” | 19 | retain | child identity, sibling witness, mirror rhyme |
| B14 Faehill | 19–20 | retain/adapt | `beat.faehill_watch`, forest warning; no dig entrance |
| B15 Fairy Ruins | 20 | retain/optional | `npc.billy-blurtweed`, old-court clue |
| B16 Figwort Manor | 20–22 | retain/adapt | family dinner, Wylda, entry mirror |
| B17 Smithson the Leper | 22 | retain/adapt | respectful disease scene and warning |

### Brugh overview and denizens

| Source section | Pages | Disposition | Canon destination |
| --- | ---: | --- | --- |
| Entry and exit rules | 23 | retain/adapt | `beat.portal_opening`, `beat.treasure_exit` |
| Illusions in the Brugh | 23 | retain/adapt | `state.glamour_truth`, `fifthsave_int`, character perception and text views |
| Elf Lord and Moth-in-Water | 24–25 | retain/adapt | `npc.elf-lord`, `npc.moth-in-water` |
| Brugh elves and missing children | 25 | retain/adapt | factions and seven child/double records |

### C1–C27 keyed rooms

This table is the required room-by-room disposition. The optional `a` keys are
included because they hold routes or child scenes even though the source uses
them as sub-areas.

| Key | Source room | Page | Disposition | Adaptation purpose |
| --- | --- | ---: | --- | --- |
| C1 | Coatroom | 26 | retain/adapt | one-way entry, attendant, cloak choice, portal coverage; the adaptation opens the prepared external mirror for ten minutes at midnight |
| C2 | Reception Lounge | 26 | retain/adapt | first palace/underground paired description and crocodile prop |
| C3 | Royal Balcony | 27 | optional/retain | surveillance, ballroom overlook, optional lock route |
| C4 | Game Room | 27 | retain/adapt | social game, court etiquette, optional fight |
| C5 | Library | 28 | retain/adapt | Weadley clue and secret Level 3 passage |
| C6 | Fungal Bluehouse | 29 | retain/adapt | Giles rescue, fungal paths, forest-like court clue |
| C7 | The White Lady | 29–30 | retain/adapt | paired glamour/ghoul scene; skippable horror |
| C8 | Guest Room | 30 | optional/retain | court-favor rest and rescue staging |
| C9 | Guest Room | 30 | optional/adapt | second rest/staging room with distinct clue |
| C10 | Ambassador of the Mushroom People | 30 | retain/adapt | peaceful diplomacy and mushroom faction |
| C11 | A Prince Among Frogs | 30 | optional/adapt | deceptive promise and poison lesson; no required kiss |
| C12 | Ballroom | 31 | retain/adapt | spectacle, animals, paired perception, observation route |
| C12a | Musician's Station | 32 | retain/adapt | Ursula rescue and music clue |
| C13 | Dining Hall | 32–33 | retain/adapt | Elf Lord dinner, questioning, reaction, child clues; immediate attack, unfavorable, favorable, and very favorable reactions remain distinct in the digital scene |
| C13a | Pit Opening | 33 | retain/adapt | visible threat and court punishment warning; a single-member result becomes a focused rescue vignette while the whole party stays in the Core area position, and failed intervention throws the party into C18 |
| C14 | Server Area | 33 | retain/adapt | Philip rescue and dumbwaiter access |
| C15 | Smoking Room | 34 | optional/retain | quiet lore, optional equipment and paintings |
| C16 | Servant Quarters | 34 | retain/adapt | Bernard's sling and Ursula's doll evidence |
| C17 | Pool of Love and Drowning | 34 | optional/adapt | paired water hazard, moonstone choice, retreat |
| C18 | The Pit | 35 | retain/adapt | setback, damage, secret exit, recovery route |
| C19 | False Door Trap | 35 | retain/adapt | readable contact hazard and caution choice |
| C20 | Cold Storage | 35 | optional/adapt | trapped outsider bargain and clear salt-line choice |
| C21 | Kitchen | 36 | retain/adapt | Stevie rescue, food distraction, noncombat route |
| C21a | Dumbwaiter (Level 3) | 36 | retain/adapt | vertical shortcut with authored weight/party check |
| C22 | Pantry | 36 | optional/adapt | tiny pudding hazard, no mandatory clear |
| C23 | Wine Cellar | 37 | retain/adapt | Arthur rescue and cellar guard clue |
| C24 | Moth-in-Water's Chamber | 37 | optional/retain | jester's private clue and bargaining token |
| C25 | Elf Lord's Chamber | 37 | optional/retain | high-risk confrontation or evidence, never required |
| C26 | Dungeon | 37 | retain/adapt | Master Ned rescue and paired torture/meadow presentation |
| C27 | Treasure Vault | 38–39 | retain/adapt | four mirror outcomes, treasure choice, only exit; it permits partial/evidence-only escape and does not require Arthur or another specific child to be rescued |

### New monsters and maps

| Source material | Pages | Disposition |
| --- | ---: | --- |
| Brugh Elf, Mushroom Men, Ward Pixies | 40–41 | adapt into `blackapple-fae` only where a keyed scene needs them; use fifth-srd actions/checks and original descriptions |
| Maps 1–4c | 42–47 | retain spatial relationships, redraw or author new maps; do not copy the PDF images |
| Source artwork and credited third-party illustrations | 1–2 and throughout | omit; create original `blackapple-art` assets |

## Deliberate adaptation changes

1. Basic Fantasy combat, classes, saving-throw tables, currencies, and monster
   numbers become fifth-srd data. No Basic Fantasy rule text is copied into the
   campaign.
2. The source's arbitrary Mirror #4 destinations become an unavailable or
   clearly labelled future route. The demonstration campaign promises only the
   two known returns and the self-adversary mirror.
3. The source's leprosy, sanitarium surgery, torture imagery, and despair magic
   receive content notes, opt-out exits, and non-graphic conditions. These
   changes preserve the mystery while reducing surprise real-world harm.
4. The source's party-wide GM narration becomes paired player-facing
   descriptions keyed by each member's persisted `glamour_truth` mode. The
   mode is resolved by the Engine-backed `fifthsave_int` check once on entry;
   it is not a hash or visual toggle.
5. Return rewards and consequences are calculated per child and per double.
   Source rewards for Arthur or the Goodall children are not paid for a generic
   “children rescued” flag.
6. The source references external adventures such as the Dark Temple only as
   omitted background. They do not become hidden dependencies of this campaign.

### Vault treasure conversion

C27 keeps its three separate, once-only treasure choices and four mirror
outcomes. Coin/gem amounts are converted to 471, 1,354 and 800 fifth-srd gold.
The first chest also supplies the source gold-and-obsidian bracers, a 240-gold
saleable treasure item. The second supplies the source emerald-set gold ring,
a 1,020-gold treasure item. Both can be traded through Goodall's actual fee
and cash limit. The third chest's gloves become protective work gloves usable
on C19's contact trap; the source's unwanted pinching/slapping effect is omitted.

The Wand of Moss Oak's hostile resurrection, Potion of Control Giant and
cursed centipede rod are omitted from this low-level rescue adaptation. They
are not represented by unusable spell items or substituted gold threads.
Resurrection, giant control and cursed-item activation are not promised paths
in the selected campaign. This is an explicit object-level adaptation; it
does not change the seven children, mirror routes, named return rewards or
required combat, investigation and recovery choices.

## Attribution and modification record

This campaign is adapted from the CC BY-SA 4.0 text and map material of Kyle
Hettinger's Release 21. Maps are credited in the source to Scott Abraham and
Kyle Hettinger. The source lists cover art by Vasily Ermolaev; artwork by Burger
Babylon, Hieronymus Bosch, John Fredericks, Denis McCarthy, Colin Richards,
Piotr Klimkowicz, Jonas Campe, S. Ender Thiel, Andy “ATOM” Taylor, W.F.
Wakeman, and Andreas Blanckenstein; playtesters Heidi Hettinger and O.B. Lama;
and proofing by Seven, James Lemon, Scott Abraham, Mike West, and Alan Vetter.
Those credits are preserved as source publication credits. The listed artwork
is not included in this campaign.

Original Rusty Goldbox campaign additions and modifications are released under
CC BY-SA 4.0 as described by `../PROVENANCE.md`. Ruleset material remains in
the separately licensed `fifth-srd` module. Original runtime art is authored
for `blackapple-art` and must carry its own rights notice; it may not reuse the
excluded source illustrations.
