# Blackapple Fae provenance and licence

`blackapple-fae` is a data-only extension for the `fifth-srd` ruleset. It is
adapted from Kyle Hettinger's *The Blackapple Brugh*, 1st Edition, Release 21
(`KH1`), copyright © 2020–2021, 2023 Kyle Hettinger. The reviewed source is
identified by SHA-256
`87e22e0d0bbdb66c54010d4485df05e92d0237b0234c2dffa24298250d4f6de5`.

The source's text, maps, floorplans, diagrams, charts, and forms are licensed
under the Creative Commons Attribution-ShareAlike 4.0 International License.
The full licence text is retained in `LICENSE-CC-BY-SA-4.0.txt`. Most source
illustrations are identified in the source as property of their original
artists and used with permission; no such illustration, page image, or
unlicensed derivative is included here.

This module is licensed under CC BY-SA 4.0. It converts the source creatures
to `fifth-srd` data, rephrases descriptions, and makes the digital combat and
content handling choices recorded below. The `fifth-srd` ruleset remains a
separate required module under its own CC-BY-4.0 notice. No Dungeon Craft or
other engine code or file format is included.

## Source conversion map

| Runtime definition | Source location | Conversion and change |
| --- | --- | --- |
| `check.fifthsave_int` | Entry and illusion rule, printed p. 23 | One `1d20` Intelligence save against 10. The bonus uses the existing `fifth-srd:int_mod` and grants proficiency through `class_max` only for Wizard and Rogue, the fifth-srd classes that have an applicable mental-save training shape here. It is invoked once per active member on entry and stores `truth` or `glamour` in Core; it is never hashed, alternated, or rerolled per room. |
| `check.careful_contact_dex` | Source contact hazards, including the Faehill warning and the false-door/pit approach, pp. 19, 23, 35 | A reusable fifth-srd-shaped Dexterity save against 10. It supports a cautious touch or approach and leaves the event author free to show a warning, retreat, or consequence. |
| `check.court_negotiation` | Elf Lord dinner and reaction, pp. 24, 32–33 | A Charisma-modified 1d20 check against 10 with a strong-success tier for courteous court play. The campaign event chooses the resulting favor or exit; it does not force combat. |
| `check.investigate_evidence` | Parent interviews, sanitarium, child objects and Brugh clues, pp. 2, 5–6, 19, 25, 34, 36 | An Intelligence-modified 1d20 check against 10 with a clear-evidence tier. Evidence remains explicit campaign scalar state, and a failed check does not remove the ordinary investigation route. |
| `check.fae_con_save` | Ward-pixie, ghoul-shape and tentacle-worm riders, pp. 29–30, 35, 41 | A data-only Constitution save whose DC is the acting creature's declared `spell_dc` and whose bonus is the target's existing fifth-srd `con_save`. This keeps the existing save math while making a source creature's effect DC explicit in a nested target save. |
| `check.pixie_dust_chance` | Ward Pixie, New Monsters, p. 41 | A real `1d4` check against 4, called after each successful claw; only the result 4 applies the short `pixie_dust` condition. |
| `condition.fae_poisoned` | Poisonous mushrooms, p. 13; the poisonous frog's kiss, p. 34; contact poison, p. 35 | A durable, mild digital state for the source's poison hazards. It applies `-2` to the existing `fifth-srd:con_save` and `-1` to the existing melee, finesse, ranged, and spell attack checks. These are original conversion magnitudes: the module does not reproduce the source's daily hit-point loss, twelve-hour incapacitation, or an exact SRD poison rule, and it has no timed hook or per-instance value. The shrine's authored cure removes this named condition. |
| `condition.forest_sickness` | Smithson's disease, p. 22; the Faehill curse and fever, p. 23; the shrine cure, p. 8 | A durable, mild digital state for a forest fever or disease hazard. It applies `-1` to the declared fifth-srd `con` and `cha` stats and `-1` to `fifth-srd:dex_save`, an explicit numeric adaptation rather than a reproduction of SRD disease, monthly deterioration, or clinical claims. It has no timed hook, on-apply operation, or per-instance field; the shrine removes this named condition. |
| `item.truth_token` contract (`blackapple-brugh:truth_token`) | Electrum closed-eye pendant at the A4 shrine, p. 8 | The environs-owned item may be a gear object with an equipped-only modifier `{ "check": "blackapple-fae:fifthsave_int", "value": "2" }`. This is a converted glamour ward that helps the existing Brugh truth/glamour check; it does not grant universal charm immunity or replace a condition cure. |
| `resting.long_rest` | Original digital recovery policy for the campaign's overnight safe-rest routes | One in-game day advances through Core's existing `resting` contract and recovers one hit point and one slot per named fifth-srd track per period; the event names its tracks and can finish with full-cap restoration. No new clock or runtime owner is introduced. |
| `monster.ibix_double` | Missing children and ibix doubles, pp. 2, 10, 25, 33 | Goatfolk double conversion with a fifth-srd fighter chassis. The headbutt reuses the existing hit operation and adds a short prone rider as a digital charge-like adaptation; disguise, evidence, and child status remain campaign scene data. |
| `monster.cu_sidhe` | Faehill, printed p. 19 | Powerful guardian conversion. It has a strong bite and movement, but its scene is an optional warning and negotiation. The campaign never forces this fight on a level 1–3 party; retreat and respect remain authored alternatives. |
| `monster.ward_pixie` | Fungal Bluehouse, pp. 29–30; New Monsters, p. 41 | Two-claw guardian with a real `pixie_dust_chance` nested 1d4 check (4 succeeds, so one-in-four) after each successful claw. `pixie_dust` is a short, non-graphic attack penalty rather than the source's self-harm instruction. The path encounter uses only one or two pixies for a normal four-to-six-member level 1–3 party. |
| `monster.white_lady` | The White Lady, pp. 29–30 | Ghoul-shape conversion with a three-part claw-and-bite action. A failed Constitution save can cause one round of `paralyzed`; the scene is skippable and the pearl reward is optional. |
| `monster.tentacle_worm` | The Pit, printed p. 35 | One pit guardian with six low-damage tentacle portions and a brief Constitution-save paralysis rider. The encounter is a setback with a secret exit and recovery/retreat route, rather than a required clear. |
| `monster.tiny_black_pudding` | Pantry, printed p. 36 | Tiny pantry hazard with one pseudopod. The source's corrosive threat is converted to a two-round, one-damage `acid_burn` condition; it is optional and containable. |
| `monster.court_guard` | Brugh court and dinner, pp. 32–33 | Optional fifth-srd fighter guard for a court escalation. Ordinary dinner negotiation never requires combat. |
| `monster.elf_lord` | Denizens of the Brugh, printed p. 24 | Optional high-level court confrontation, with existing fifth-srd magic-missile, melee, and move actions. The campaign's completion and endings never require killing him. |

`encounters/` deliberately uses smaller or single-creature recipes for the
low-level route. The source's seven doubles, five ward pixies, and court
population are narrative populations, not a promise to throw every creature
into one fight. The campaign supplies courtesy, investigation, stealth,
containment, and retreat alternatives around these recipes.

## Existing action and controller reuse

The extension reuses fifth-srd attacks, saves, movement, and Core's existing
combat-behavior interpreter. Each source creature has a short authored policy
that proposes its actual action and selects a nearby enemy through the common
legal-action resolver. Pixies, hounds, doubles and guards approach their target;
the pit worm and pantry pudding stay at their keyed hazard. The Elf Lord first
uses his existing prepared Magic Missile while resources permit, then his
dagger. Negotiation and avoidance remain campaign choices before these policies
are reached; no policy turns a friendly scene into a fight.

Wylda's automatic policy selects her equipped finesse weapon. Master Ned is a
brown talking war horse using original data-only `talking_warhorse` race and
`warsteed_companion` creation/class definitions. Three six-hit-point levels
give him 18 hit points, the race adds speed and natural armour, and the class
supplies his Hooves action directly. His body is not portable equipment. Ned's
policy selects that natural attack. A player may switch either companion to
manual control through the shared combat controls. Their prose, recruitment,
six-member limit and dismissal remain campaign-owned.

No duplicate combat loop, C# AI, random source, or Engine mechanism is added.

Source publication credits retained for attribution include maps by Scott
Abraham and Kyle Hettinger and the source's listed artists, playtesters, and
proofers; their artwork is excluded from this module.

Ordinary equipment resale is owned by the required `fifth-srd` ruleset's
`economy/standard` policy. This extension defines no economy and carries no
cross-module replacement definition. The campaign selects its timed-rest
policy through ordinary rest events.
