# Locations and routes

The campaign has five logical areas even when the runtime renderer uses more
than one map per area: `area.blackapple`, `area.environs`,
`area.brugh.l1`, `area.brugh.l2`, and `area.brugh.l3`. Source keys remain
visible in this document so an author can trace a scene back to Release 21.
The source maps are licensed material, but this repository does not copy the
PDF or its excluded illustrations. Runtime maps can be newly authored while
preserving the useful relationships and keyed-room IDs.

## Route shape

```text
North road -> Blackapple hub <-> environs / forest
                    |                    |
             Figwort Manor         Hen's Teeth
                    \              /
                     -> C1 Coatroom -> C2/C4/C5 -> L1/L2/L3
                                               -> C27 return mirrors
```

The party may revisit Blackapple after every safe exit. The Figwort and Hen's
Teeth mirrors are entry points only. The C27 vault is the return route. A
failed optional encounter can alter trust or resources but does not delete the
Brugh route.

## Blackapple hub (`area.blackapple`)

### Source key and digital disposition

| Key | Source location | Adaptation use | Services / evidence |
| --- | --- | --- | --- |
| B1 | North Road Guardhouse | **Retain, simplify.** Establishes the toll, village boundary, and first ordinary interaction. | Entry, directions, no forced combat. |
| B2 | Agatha's Potions and Powders | **Retain, convert.** Use a guarded shop with clearly labelled side effects. | Healing, sleep, anti-undead preparation, optional clue. |
| B3 | Goodall's Fine Trading | **Retain, convert.** Appraise and sell gems/items; make the family connection explicit. | Economy, evidence about Amelia/Bernard, reward after actual return. |
| B4 | The Jolly Fox Inn | **Retain as hub.** Revisit between expeditions. | Safe rest, food, rumours, Alistair and Sir Ruprecht, XP advancement after authored story rewards. |
| B5 | Village Square | **Retain as public consequence space.** Fires, patrols, markets, and the final public choice occur here. | Visible trust changes, crowd pressure, public exposure of a double. |
| B6 | Merchants' Guild | **Retain as optional economy scene.** The guild wants glowwood profit. | Job offer; changes `forest_stance` if accepted or refused. |
| B7 | Hazard's Quality Goods | **Retain as supplies shop.** Keep practical gear and a readable inventory. | Buy travel gear; no unique plot gate. |
| B8 | Pigman Jack's Homestead | **Retain, adapt.** Jack points to wild dogs and has a recoverable wereboar problem. | Wild-dog quest, farm trust, noncombat cure or containment. |
| B9 | Chapel of St. Ludann | **Retain as social clue.** The chapel is where Arthur's public misbehavior becomes village evidence. | Priory relationship, sanctuary, fictional confession or prayer. |
| B10 | St. Ludann Priory | **Retain, convert.** Clerics patrol and can provide care or containment. | Temple services, rest, cure path, humane double containment. |
| B11 | Tobler's Knick Knacks and Taxidermy | **Retain as optional moral investigation.** Do not let necromancy displace the child mystery. | Item appraisal, cemetery clue, optional exposure of Tobler. |
| B12 | Cemetery | **Retain as optional night scene.** Use for Tobler, Agatha, rats, or a clue about the village's fear of the dead. | Evidence and risk; no required undead clear. |
| B13 | Parents of the “Wicked Children” | **Retain as investigation anchor.** Give each household one memory, object, or sibling witness. | `evidence.parent_suspicion`, child identities, mirror rhyme. |
| B14 | Faehill | **Retain as visible landmark and warning.** It is a ringed mound, not a dungeon entrance. | Forest covenant, curse warning, cu-sidhe encounter. |
| B15 | Fairy Ruins | **Retain as optional discovery.** Billy Blurtweed observes from the ruined citadel. | Faehill history, respect/exploitation choice, no hidden Brugh entrance. |
| B16 | Figwort Manor | **Retain as family and mirror location.** Dinner, private requests, Wylda recruitment, and the entry mirror happen here. | Family stance, mirror route, possible return scene. |
| B17 | Smithson the Leper | **Retain with care adaptation.** Place Smithson at a distance by the south road and make approach/medical support explicit. | Warning about digging, disease-care choice, no surprise contact trap. |

### Hub services and economy

The Jolly Fox is the default safe base. Its shop and rest hooks use existing
`fifth-srd` currency and recovery definitions. XP awards use the ruleset's
immediate advancement; there is no paid training service in this adaptation. Agatha and
Hazard are normal shops; Goodall buys high-value items under a campaign-defined
limit and can identify magical goods. The priory is the default temple and
recovery owner. The campaign uses ordinary gold and item costs; the source's
songbird-egg joke at the Hen's Teeth is a local fiction detail, not a new
currency unless a later authoring task explicitly adds one.

## Environs (`area.environs`)

| Key | Source location | Disposition and route role |
| --- | --- | --- |
| A1 | Wild Dog Lair | **Retain as optional quest.** Kill, drive off, or calm the dogs. A cu-sidhe clue connects the pack to Faehill. Reward is food/coin/trust, not a mandatory level. |
| A2 | Dr. Livinius' Sanitarium | **Retain as sensitive investigation.** Stop an unsafe operation, expose the ibix, return an escaped patient, or leave with evidence. Provide an exit before medical-harm content. |
| A3 | Hen's Teeth Tavern | **Retain as forest hub and alternate mirror.** Safe lodging, cheap food, faun patrons, Flynn, Periwinkle, and the midnight portal. |
| A4 | Shrine of Confession | **Retain, adapt consent.** The shrine can cure poison/disease after a fictional confession. A party may decline and leave. The anti-charm item is optional. |
| A5 | Tenpenny Wood | **Retain as ecology choice.** Glowwood, stirges, and the treant test whether the party protects living trees or takes quick profit. |

The environs supports a loop of departure, encounter, retreat, and return. A
party that is hurt can use a safe rest in Blackapple or Hen's Teeth. The
forest's stronger creatures are narrated as hazards or negotiation opportunities
unless the fifth-srd conversion demonstrates a fair level 1–3 encounter.

## Brugh Level 1 (`area.brugh.l1`)

| Key | Source room | Disposition and scene contract |
| --- | --- | --- |
| C1 | Coatroom | **Retain as entry.** Mirror arrival, attendant, cloak choice, and one-way entry. After the party leaves, the attendant covers the mirror. |
| C2 | Reception Lounge | **Retain as first paired perception scene.** Palace lounge and shabby animal-filled room share geometry. Crocodile prop can animate only as a deliberate encounter. |
| C3 | Royal Balcony | **Retain as optional overlook.** Locked access and view of C12 establish the Elf Lord's surveillance; avoid making lockpicking mandatory. |
| C4 | Game Room | **Retain as social/combat choice.** Darts and bad sportsmanship test whether the party accepts court rules, cheats, or leaves. Tapestry material is rewritten as original art direction. |
| C5 | Library | **Retain as clue and secret route.** The Weadley volume sequence can reveal a passage to Level 3. The source's map prop becomes a readable clue, not a copied image. |
| C6 | Fungal Bluehouse | **Retain as exploration and child scene.** Stay on paths for a quiet approach; leaving them risks ward-pixie danger. Giles is here. |
| C7 | The White Lady | **Retain as paired horror.** Glamour presents a courtly lady; truth presents a ghoul. Make the scene skippable and keep the pearl reward optional. |
| C8 | Guest Room | **Retain as conditional safe rest.** The court may offer a room after favorable interaction; resting here advances time and can change court reaction. |
| C9 | Guest Room | **Retain as second conditional rest / rescue staging room.** Give it a distinct clue or quiet route so it is not duplicate filler. |
| C10 | Ambassador of the Mushroom People | **Retain as peaceful diplomacy.** The ambassador can explain fungi and court etiquette; violence closes the Mushroom People route. |
| C11 | A Prince Among Frogs | **Retain as optional deception.** The frog's false promise is a consent-forward joke and poison lesson, not a required kiss or incapacitation trap. |

## Brugh Level 2 (`area.brugh.l2`)

| Key | Source room | Disposition and scene contract |
| --- | --- | --- |
| C12 | Ballroom | **Retain as spectacle and route.** The palace/animal versions share the same dance space. The party may observe, perform, or leave. |
| C12a | Musician's Station | **Retain as Ursula's child scene.** Music is a clue; rescue can be quiet during a performance. |
| C13 | Dining Hall | **Retain as central social encounter.** The Elf Lord and Moth-in-Water host dinner. Food choices, questions, refusal, and reaction determine court stance. |
| C13a | Pit Opening | **Retain as visible threat.** It is a court punishment route and a warning, not a random death switch. The current digital area owner supports the whole-party throw; the source's single-member result becomes a focused rescue vignette with whole-party movement, and failed intervention throws the party into C18. |
| C14 | Server Area | **Retain as Philip's child scene and vertical route.** The dumbwaiter and server clues support stealth. |
| C15 | Smoking Room | **Retain as quiet lore scene.** Paintings and unused armor become optional clues and gear; no required fight. |
| C16 | Servant Quarters | **Retain as empty staging area.** Bernard's sling and Ursula's doll are tangible confirmation objects. |
| C17 | Pool of Love and Drowning | **Retain as risk/reward exploration.** A glamour pool and foul frog pool share geometry. The moonstones are optional; retreat is valid. |

## Brugh Level 3 (`area.brugh.l3`)

| Key | Source room | Disposition and scene contract |
| --- | --- | --- |
| C18 | The Pit | **Retain as setback/recovery.** A fall can hurt or separate the party, but the secret exit and a careful encounter keep rescue possible. |
| C19 | False Door Trap | **Retain as readable hazard.** Signal the contact danger and allow gloves, caution, or healing to matter. |
| C20 | Cold Storage | **Retain as optional bargain.** The imprisoned cold creature can be freed by breaking the salt line, with a clear consequence and no forced fight. |
| C21 | Kitchen | **Retain as Stevie's child scene.** Use food, distraction, and cook relationship for a noncombat route. |
| C21a | Dumbwaiter (Level 3) | **Retain as vertical route.** Use the source's weight concern as an authored party-size check; document the maximum in the runtime definition. |
| C22 | Pantry | **Retain as small optional hazard.** The tiny black pudding is a surprise that can be contained without clearing Level 3. |
| C23 | Wine Cellar | **Retain as Arthur's child scene.** The enchanted wine is a clue or hazard; rescuing Arthur opens a useful mirror route, but it is never required for C27 or for a partial/evidence-only escape. |
| C24 | Moth-in-Water's Chamber | **Retain as optional character clue.** A locked, dusty room can reveal the jester's private life and a bargaining token. |
| C25 | Elf Lord's Chamber | **Retain as high-risk optional confrontation.** It can expose court vulnerability, but the campaign must remain completable without entering. |
| C26 | Dungeon | **Retain as Master Ned rescue.** Pair glamour meadow and truth torture-room descriptions, provide a safe release route, and make Ned an optional companion. |
| C27 | Treasure Vault | **Retain as exit and choice.** The four mirrors are the only return mechanism. Mirrors 1 and 3 lead to the two mundane portals; mirror 2 is a self-confrontation; mirror 4 is closed or labelled as an unavailable future route. The party may leave with any mix of rescued children and evidence, without requiring Arthur or another named child. |

## Paired perception contract

The Brugh's geometry and underlying facts are shared by all members. A room
with a contrast has two descriptions, each tagged `glamour` or `truth`; it does
not have two separate event chains. The current minimum paired set is C1, C2,
C6/C7, C11, C12, C17, C26, and C27. Other rooms may use a single description
with subtle perception cues. The per-member mode is resolved by the
Engine-backed `fifthsave_int` rule in [premise.md](premise.md); member-keyed
storage belongs to each character; text views and the ordinary member control
select presentation over the same campaign position and routes.

## Art and map treatment

The runtime may ship original maps and illustrations under `blackapple-art`.
Map labels may preserve C1–C27 for source traceability, but the campaign does
not copy the source PDF's map images. Paired glamour/truth art uses identical
landmarks, door locations, and traversable geometry so a player can share clues
across party members. Art prompts and production evidence belong with the asset
module and Den tasks, not in the canon's temporary progress history.
