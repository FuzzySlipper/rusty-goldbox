# Blackapple Brugh selected production art brief

This is the selected production brief for #9305. It names the art that the
campaign needs to ship and the consumers that make each asset necessary. It is
not a catalogue of every possible NPC portrait, monster figure, decorative
prop, or future UI treatment.

The brief is bounded by `../ART_BIBLE.md`, the canonical runtime and scene
contracts in `../../canon/`, and the Engine media contract. The source PDF's
artwork and page images are excluded. New images are original generated
adaptation art and carry the asset module's existing provenance and licence.

The runtime has one event `picture` field. It does not provide an NPC identity
plate beside that picture, and it does not show seven portraits at once. A
scene must therefore carry its identity in its one picture, its authored text,
and its named objects. The only mandatory recruit portraits in this brief are
Wylda and Master Ned because they are reachable party members. Elf Lord and
Moth-in-Water recur in the selected dining and court pictures; they do not need
separate portrait or figure assets. The seven children have distinct names,
objects, and scene identities; child portraits are not a required projection
feature and are not selected here.

## Settled visual direction

Use original illustrated dark-fairytale ink and wash: hand-inked contours,
warm paper grain, restrained watercolour/gouache, readable silhouettes, and
clear foreground, middle distance, and background. Blackapple uses lantern
amber, ochre, russet, muted cream, moss, and olive. The Brugh uses blue slate,
blue-green, verdigris, moon ivory, wet black, bruised violet, and iron grey.
Warm village light and unsettling fae cold must remain legible when the image
is reduced to its ordinary display size.

Blackapple identity is low timber and limewash, muddy lanes, tools, deadwood,
warm windows, horseshoes, black-apple seed or leaf clues, and the ringed
Faehill. Wylda is a lean nineteen-year-old mortal with an auburn loose braid,
weathered teal coat, cream shirt, brass mirror token, and practical satchel.
Brugh identity repeats broken oval mirrors, seven gold threads, blue-green
stone, damp seams, theatrical court objects, and stable door, arch, table,
stair, and window landmarks. Ibix doubles are child-shaped at a distance,
with a shell-like mask, hoof hint, and wrong stillness; they are non-graphic.

No image contains lettering, UI, logos, watermarks, source illustrations, PDF
page art, copied maps or floorplans, photorealism, 3D rendering, glossy game
art, anime, chibi proportions, retro pixel art, or a living-artist imitation.
Truth and glamour share camera, doors, exits, object positions, and landmarks;
only the perception and material reading changes.

## Selected slots and native display sizes

All runtime PNGs are 8-bit RGBA and use `sampling: "linear"`. Packaging may
add an all-255 alpha channel to an opaque RGB source, but may not resize, crop,
composite, paint, or otherwise edit the pixels.

| Selected use | Slot and native target | Ordinary-context requirement |
| --- | --- | --- |
| Village, forest, and Brugh event picture | `media: "image"`, `slot: picture`; 1672 x 941 target (the accepted village image is 1672 x 940) | Whole image remains readable around 320 x 180; one focal action and quiet margins; no identity plate or second picture. |
| Recruit portrait | `media: "image"`, `slot: picture`, `tags: ["portrait"]`; 1024 x 1536 | Wylda and Ned remain identifiable in the ordinary party portrait at about 48 x 71 CSS pixels and the member sheet portrait at 64 x 96 CSS pixels; crown, eyes, shoulders, and identity cue stay readable in the full vertical frame. |
| Ibix double | `media: "sheet"`, `slot: figure`; one 1024 x 1536 transparent frame, `faces: "left"`, bottom-centre anchor | `height: 0.75` Engine cells; feet and full child-scale silhouette remain visible. |
| Village material atlas | `media: "image"`, `slot: wall_set`; 1024 x 1024 target, four 512 x 512 regions | Named `wall`, `door`, `floor`, `ceiling` regions tile without seams or bleed in `area.blackapple`. |
| Forest material atlas | `media: "image"`, `slot: wall_set`; 1024 x 1024 target, four 512 x 512 regions | Named regions tile without seams or bleed in `area.environs`; forest remains living and readable. |
| Brugh material atlas | `media: "image"`, `slot: wall_set`; selected source is 1254 x 1254 with four 627 x 627 regions | Same structural set serves `brugh_l1`, `brugh_l2`, and `brugh_l3`; ordinary wall-view validation is part of runtime export. |
| Campaign title | `media: "image"`, `slot: picture`; 1600 x 400 | Village edge, forest, Faehill, and black-apple cue leave quiet space for the existing title/start controls. No baked lettering. |
| Skin | Existing `darkfairytale` theme with Blackapple palette | Palette configuration only; no local CSS renderer, panel image, frame image, button image, font, audio, or video is selected. |

Any later human/world figure would be a separate scoped need. No human figure
is selected here. If one becomes necessary, its initial target is `0.85` to
`0.9` Engine cells with headroom and must be labelled **unverified until
rendered**; the earlier 1.5–1.8 cell targets are retired because they clip in
the current one-cell ceiling view.

## Stable selected IDs

These IDs are the exact logical/runtime names for the selected collection.
`asset.<hyphenated-id>` is the source-side logical name; the second name is the
runtime ID used by module definitions.

| Logical ID | Runtime ID and slot | Selected consumer and reuse rule |
| --- | --- | --- |
| `asset.wylda-portrait` | `wylda_portrait`, portrait picture | Existing accepted reference; Wylda party card, manor conversation, and entry preparation only. |
| `asset.master-ned-portrait` | `master_ned_portrait`, portrait picture | Mandatory recruit portrait for C26 rescue and the optional companion sheet; no human figure is required. |
| `asset.blackapple-village` | `blackapple_village`, wide picture | Existing accepted outdoor village anchor for arrival, square, market/road, parents, and public consequence when that shared exterior is materially correct. |
| `asset.tenpenny-forest` | `tenpenny_forest`, wide picture | Selected forest-path anchor for A5 and compatible ordinary forest travel; it is not the Hen's Teeth, Faehill, ruins, shrine, or Brugh. |
| `asset.ibix-double-figure` | `ibix_double_figure`, one-frame figure sheet | Existing accepted generic figure for all seven doubles at a distance; identity remains in the named child/double record, scene, and object. `height: 0.75` cells is pending ordinary rendering verification. |
| `asset.brugh-reception-truth` | `brugh_reception_truth`, wide picture | Existing accepted C2 truth picture. It is not a generic Brugh room. |
| `asset.brugh-reception-glamour` | `brugh_reception_glamour`, wide picture | Existing accepted C2 glamour picture; same arch, mirror, table, doorway, camera, and exits as truth. |
| `asset.brugh-materials` | `brugh_materials`, wall set | Selected shared Brugh structural atlas. Its 1254 x 1254 regions are `wall [0,0,627,627]`, `door [627,0,627,627]`, `floor [0,627,627,627]`, `ceiling [627,627,627,627]`; ordinary wall-view validation is required before runtime export. |
| `asset.blackapple-village-materials` | `blackapple_village_materials`, wall set | Selected village atlas for `area.blackapple`; low timber/limewash wall, village door, muddy/stone floor, timber eave. |
| `asset.environs-forest-materials` | `environs_forest_materials`, wall set | Selected forest atlas for `area.environs`; living-tree boundary, rough gate, leaf/soil floor, branch canopy. |
| `asset.blackapple-title` | `blackapple_title`, wide title picture | Selected title image; no lettering, logo, or UI. The accepted village picture is not silently cropped into this role. |

The selected source images retain their original prompts, tool metadata, source
hashes, runtime hashes, and judge records in their existing provenance files
and temporary/Den receipts. This brief keeps only the selected ID and its
consumer contract.

## Selected village and forest contexts

These are the service and route pictures needed for the opening and return
experience. They are distinct where a different room identity matters; the
outdoor village and forest anchors are reused only for the rows named here.

| Runtime ID | Consumers | Brief and reuse boundary |
| --- | --- | --- |
| `blackapple_village` | B1/B5/B6/B13/B17 outdoor approach, arrival, public square, market/road, parents | Existing accepted amber village exterior. Indoor service rooms use their own selected picture below. |
| `blackapple_jolly_fox` | B4 safe village hub, rest, rumours | Warm village tavern interior; not the forest tavern. |
| `blackapple_figwort_manor` | B16 dinner, Wylda recruitment, Figwort mirror, Arthur return | Fashion-conscious manor room with the oval mirror landmark. |
| `blackapple_priory` | B9/B10 care, double containment, all living return triage | Plain clean sanctuary and care room with a visible safe exit. |
| `blackapple_goodall_shop` | B3 Goodall trading and Amelia/Bernard evidence/reward | Appraisal counter and practical goods; inventory code is authored text/object, not baked lettering. |
| `blackapple_agatha_shop` | B2 potions, poison, and recovery service | Apothecary shelves and safe bottles; effects are authored text. |
| `blackapple_hazard_shop` | B7 supplies | Practical gear counter; no treasure-hoard substitute. |
| `blackapple_tobler_shop` | B11 optional investigation | Restrained taxidermy workroom; no graphic display. |
| `blackapple_cemetery` | B12 optional night evidence | Quiet cemetery path with no required undead spectacle. |
| `environs_hen_teeth` | A3 forest tavern, Flynn, Periwinkle, alternate mirror | Separate forest tavern interior and mirror landmark; not `blackapple_jolly_fox`. |
| `environs_sanitarium` | A2 Livinius investigation and Arthur-double evidence | Non-graphic clinical room with a clear exit and holding space. |
| `environs_shrine_confession` | A4 optional cure | Consent-forward shrine, moonlit owl, and leave-safe path. |
| `environs_faehill` | B14 forest warning and ringed mound | Faehill is a visible landmark, not a Brugh entrance. |
| `environs_fairy_ruins` | B15 old-court evidence and Billy Blurtweed | Ruined citadel and stone shelter; no copied source map. |
| `environs_wild_dog_lair` | A1 optional wild-dog route | Natural den and path; generic fifth-srd animals can remain ruleset-owned. |
| `tenpenny_forest` | A5 Tenpenny Wood and compatible forest travel | Existing selected forest anchor; no reuse for a service interior or Brugh room. |

## Selected paired Brugh pictures

The following are the required paired pictures. Each truth/glamour pair is one
camera and one room geometry, with the same exits and landmarks. The minimum
contract requires C1, C2, C6/C7, C11, C12, C17, C26, and C27; the dining pair
is also selected because it carries the central court reaction and the Amelia/
Bernard identities.

| Truth runtime ID | Glamour runtime ID | Consumer | Required visual identity |
| --- | --- | --- | --- |
| `brugh_coatroom_truth` | `brugh_coatroom_glamour` | C1 | One-way entry mirror, attendant, cloak hooks, and portal cover; damp threshold versus polished court welcome. |
| `brugh_reception_truth` | `brugh_reception_glamour` | C2 | Existing accepted pair: arch left, oval mirror right, long table centre, rear doorway. |
| `brugh_bluehouse_truth` | `brugh_bluehouse_glamour` | C6 | Giles, fixed fungal paths, greenhouse arch, and ward-pixie risk. |
| `brugh_white_lady_truth` | `brugh_white_lady_glamour` | C7 | Courtly lady versus non-graphic ghoul reading; optional encounter and exit remain visible. |
| `brugh_frog_prince_truth` | `brugh_frog_prince_glamour` | C11 | Deceptive promise and poison lesson; consent-forward, no required kiss. |
| `brugh_ballroom_truth` | `brugh_ballroom_glamour` | C12 | Same dance floor, musician sightline, court/animal spectacle, and exits. |
| `brugh_dining_truth` | `brugh_dining_glamour` | C13 | Elf Lord and Moth-in-Water recur here; Amelia/Bernard clues, food, questions, and all four court reactions are event states over this one pair. |
| `brugh_pool_truth` | `brugh_pool_glamour` | C17 | Paired water hazard, moonstones, and retreat path without graphic drowning. |
| `brugh_dungeon_truth` | `brugh_dungeon_glamour` | C26 | Master Ned rescue; glamour meadow and truth punishment room share doorway and safe release route. |
| `brugh_treasure_vault_truth` | `brugh_treasure_vault_glamour` | C27 | Four mirror positions, the two mundane returns, self-confrontation, and sealed/future fourth; partial/evidence-only exit remains clear. |

No other room receives a speculative truth/glamour pair. Its ordinary view is
the selected Brugh wall set plus its authored map geometry, text, and named
objects. This keeps C1–C27 coherent without showing a wrong-room picture.

## C1–C27 selected coverage

The table records the visual treatment for every keyed room. `brugh_materials`
means the shared structural atlas rendered over that room's own map geometry;
the room identity comes from the geometry, text, and named object listed in
the final column. This is an intentional structural-family reuse, not a
generic backdrop substitution.

| Scene | Selected visual treatment | Scene identity / named object |
| --- | --- | --- |
| C1 Coatroom | `brugh_coatroom_truth/glamour` + `brugh_materials` | Entry mirror, attendant, cloak choice, one-way portal. |
| C2 Reception Lounge | `brugh_reception_truth/glamour` + `brugh_materials` | First paired perception scene; crocodile is an explicit optional prop/encounter. |
| C3 Royal Balcony | `brugh_materials` with balcony map geometry and text | Railing, locked access, and ballroom overlook; no ballroom picture substitution. |
| C4 Game Room | `brugh_materials` with game-room geometry and text | Darts/game table, court etiquette, and leave route. |
| C5 Library | `brugh_materials` with library geometry and text | Weadley volume sequence and secret Level 3 passage; no copied map prop. |
| C6 Fungal Bluehouse | `brugh_bluehouse_truth/glamour` + `brugh_materials` | Giles, fungal paths, and ward-pixie warning. |
| C7 White Lady | `brugh_white_lady_truth/glamour` + `brugh_materials` | Courtly/ghoul paired reading, skippable horror, optional pearl. |
| C8 Guest Room | `brugh_materials` with the C8 room geometry and text | Favorable rest, Moth route, and first distinct guest-room clue. |
| C9 Guest Room | `brugh_materials` with the C9 room geometry and text | Second rest/rescue staging room and distinct clue; not a duplicate C8 picture. |
| C10 Mushroom Ambassador | `brugh_materials` with mushroom-garden geometry and text | Peaceful diplomacy, fungus explanation, Giles clue. |
| C11 Prince Among Frogs | `brugh_frog_prince_truth/glamour` + `brugh_materials` | Deception/poison lesson and consent-forward joke. |
| C12 Ballroom | `brugh_ballroom_truth/glamour` + `brugh_materials` | Spectacle, observation, animal/court reading, musician sightline. |
| C12a Musician's Station | `brugh_materials` with musician geometry and text | Ursula's timpani rhythm and quiet performance rescue. |
| C13 Dining Hall | `brugh_dining_truth/glamour` + `brugh_materials` | Elf Lord, Moth-in-Water, food, Amelia's inventory code, Bernard's family clue, and four reactions. |
| C13a Pit Opening | `brugh_materials` with pit-ledge geometry and text | Focused single-member rescue vignette; failed intervention moves the whole party to C18. |
| C14 Server Area | `brugh_materials` with server geometry and text | Philip, service phrases, and dumbwaiter opening. |
| C15 Smoking Room | `brugh_materials` with smoking-room geometry and text | Optional lore, paintings, and armour; no required fight. |
| C16 Servant Quarters | `brugh_materials` with servant geometry and text | Bernard's sling and Ursula's cloth doll as evidence objects. |
| C17 Pool of Love and Drowning | `brugh_pool_truth/glamour` + `brugh_materials` | Paired water hazard, moonstone choice, and retreat. |
| C18 The Pit | `brugh_materials` with pit geometry and text | Recoverable setback, secret exit, and tentacle-worm negotiation; no child status is erased. |
| C19 False Door Trap | `brugh_materials` with false-door geometry and text | Readable contact hazard, caution/glove route, and healing consequence. |
| C20 Cold Storage | `brugh_materials` with cold-storage geometry and text | Trapped outsider, salt line, bargain, and clear exit. |
| C21 Kitchen | `brugh_materials` with kitchen geometry and text | Stevie, food distraction, pie/recipe clue, noncombat route. |
| C21a Dumbwaiter | `brugh_materials` with vertical-shaft geometry and text | Current party-size/weight check and safe arrival edge. |
| C22 Pantry | `brugh_materials` with pantry geometry and text | Optional small pudding hazard and contained retreat. |
| C23 Wine Cellar | `brugh_materials` with cellar geometry and text | Arthur, enchanted wine, cellar clue, and mirror route. |
| C24 Moth-in-Water's Chamber | `brugh_materials` with chamber geometry and text | Moth's private clue and bargaining token; recurring Moth identity comes from C13 dining art. |
| C25 Elf Lord's Chamber | `brugh_materials` with chamber geometry and text | Optional confrontation/evidence; recurring Elf Lord identity comes from C13 dining art. |
| C26 Dungeon | `brugh_dungeon_truth/glamour` + `brugh_materials` | Master Ned rescue, non-graphic truth, and safe release. |
| C27 Treasure Vault | `brugh_treasure_vault_truth/glamour` + `brugh_materials` | Four mirror outcomes and the only Brugh exit; no named child is required. |

## Seven rescue identities and return outcomes

Each child is distinguished in the single room picture when one is selected,
the authored scene text, and a named object or memory. There is no separate
child portrait requirement and no simultaneous seven-portrait view.

| Child scalar | Scene and selected identity cue | Double treatment |
| --- | --- | --- |
| `child_arthur_figwort` | C23 cellar: wine duty, Wylda's private nickname, and `gold_thread`; family handoff uses `blackapple_figwort_manor`. | `double_arthur_figwort` uses `ibix_double_figure`; sanitarium mask/impossible-memory evidence identifies it. |
| `child_amelia_goodall` | C13 dining: protective stance and Goodall inventory code; handoff uses `blackapple_goodall_shop`. | `double_amelia_goodall` uses the same figure; poison-mushroom/isolation evidence is text and named object state. |
| `child_bernard_goodall` | C13/C16: familiar counting game and `bernard_sling`; handoff uses `blackapple_goodall_shop`. | `double_bernard_goodall` uses the same figure; obedience/counting failure identifies it. |
| `child_giles_weadley` | C6/C10: fungus knowledge, liqueur/ambassador clue, and `gold_thread`. | `double_giles_weadley` uses the same figure; deadwood/forest-smell reaction identifies it. |
| `child_philip_anvil` | C14: service phrase and remembered smith rhythm. | `double_philip_anvil` uses the same figure; needless helpful traps identify it. |
| `child_ursula_cooke` | C12a/C16: timpani beat and cloth doll. | `double_ursula_cooke` uses the same figure; mimicry fails the village-bell rhythm. |
| `child_stevie_leeford` | C21: pie, recipe phrase, and small assigned task. | `double_stevie_leeford` uses the same figure; stolen food and small fires identify it. |

Thread removal sets the named child to `freed`; family handoff sets it to
`returned`. The matching double scalar remains separate. A double's exposure,
containment, release, or removal never supplies a child rescue image or state.

| Outcome | Selected context and variation |
| --- | --- |
| `ending.lost_in_brugh` | Last valid Brugh structural view or paired picture; never a village victory picture, and no child/double record is rewritten. |
| `ending.court_bound` | Brugh structural view and the court identity established in C13 dining; explicit informed service choice, children remain named as captive/freed/unknown. |
| `ending.elf_bargain` | C13 dining and C27 vault pair, then the selected route backdrop; named children, privacy/object/forest terms remain in text. |
| `ending.full_return` | `blackapple_priory` triage, then `blackapple_figwort_manor`, `blackapple_goodall_shop`, or `blackapple_village` by family/route; all seven handoffs are named in text. |
| `ending.partial_return` | Same triage and family backdrops; returned, freed-but-not-returned, captive, and unknown records remain distinct. |
| `ending.truth_without_return` | `blackapple_priory` or `blackapple_village` with a named Brugh evidence object; no rescue claim is implied. |
| `epilogue_doubles_contained` | `blackapple_priory` and the seven named double records; all must be `contained`. |
| `epilogue_masks_escaped` | `blackapple_village` plus `tenpenny_forest` or `environs_faehill`; every escaped/exposed/masked/unknown double is named. |

The other additive epilogues (`epilogue_public_truth`,
`epilogue_protected_account`, `epilogue_truth_verified`, `epilogue_no_proof`,
and `epilogue_court_terms`) are text/menu variations over these selected
contexts, not new art requirements.

## Production and judge gates

1. Save the exact prompt, tool output path, model/settings metadata, logical
   ID, source dimensions, reference paths, and original-generated status before
   each image call. The source PDF and excluded source illustrations are never
   inputs.
2. Inspect the original at native size and at its ordinary display size. A
   pair is judged together for fixed camera, landmarks, exits, body placement,
   and material-only truth/glamour drift. A single room is judged for its own
   map geometry and named object; no generic wrong-room picture passes.
3. Check the single event picture path in the ordinary product. Check Wylda
   and Ned in the actual party portrait projection, each selected wall atlas
   in its area, and Ibix in the one-cell wall view. A source image alone is not
   acceptance evidence.
4. Validate RGBA mode, dimensions, alpha, sampling, frame count, face,
   anchor, and wall-region bounds; run the module validator and pinned Engine
   packer. Record source/runtime SHA-256 and pixel equality for technical
   packaging.
5. For a concrete independent-judge failure, allow at most two targeted
   corrections for that asset, preserve every attempt and prompt, and stop
   after the second. Do not regenerate for taste or broaden the selected set.

The selected references, prompts, raw outputs, and temporary/Den judge receipts
remain the provenance record. This document records only the selected
production IDs and durable consumer constraints; runtime export still requires
the gates above.
