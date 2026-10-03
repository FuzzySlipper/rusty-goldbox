# Provenance: scifi-2d6 ruleset

## Open Game Content

This module's definition files (every `.json` file except `module.json`) are
Open Game Content under the Open Game License 1.0a; see `LICENSE-OGL.txt`,
which includes the section 15 copyright notices. Nothing in this module is
designated Product Identity.

The rules are adapted from the Cepheus Engine System Reference Document
(Samardan Press, 2016), whose text is designated Open Game Content except for
the titles of products published by Samardan Press and the trademarks
"Cepheus Engine" and "Samardan Press". This module uses neither trademark
outside the section 15 notice and makes no claim of compatibility with them.
It contains no closed content from products published by Mongoose Publishing
or Far Future Enterprises and is not affiliated with or endorsed by either.

The SRD text was read from https://ce.riftroamers.com/ (an HTML edition of
the SRD; the PDF and DOCX editions are at DriveThruRPG, product 186894).

## Sources

| Definitions | Source (Cepheus Engine SRD) |
| --- | --- |
| `attributes/`, `creation/standard` (six characteristics, 2D6 each, assigned) | Chapter 1: Character Creation |
| `derived/*_dm` (DM = characteristic / 3, rounded down, − 2) | Characteristic Modifiers |
| `derived/` skills (unskilled −3) and `features/` careers | Chapter 2: Skills; Chapter 1 careers |
| `checks/` (2D6 + skill + DM, 8+; Effect is the margin) | Chapter 2: Skills (checks); Chapter 5: Attack |
| `actions/`, `reactions/dodge`, `conditions/dodging` (dodging gives the attacker −1 and the dodger −1 on its own checks until the next round) | Chapter 5: Attack, Reactions, Dodging |
| `conditions/injury`, `overflow`, `spill_*`, `tracks/`, `combat/standard` (damage = weapon dice + Effect − armour, at least 1 at Effect 6+; Endurance first, then Strength or Dexterity; unconscious with two characteristics at 0) | Chapter 5: Damage, Armor, Damage Results |
| `items/` (dagger 1D6, blade 2D6, cutlass 3D6, revolver and auto pistol 2D6, rifle 3D6; jack 1, mesh 5) | Chapter 4: Equipment |
| `currencies/credits` | Chapter 4: Equipment pricing already used by the item definitions |

The careers' skill packages and the two NPCs are original examples written
for this module; the career names are the SRD's.

## Simplifications

- Characters don't go through careers term by term (qualification,
  survival, skills and benefits): a career gives a fixed package of skill
  levels. Mental characteristics aren't used in combat.
- Damage always comes off Endurance first; past that, the higher of Strength
  and Dexterity takes it. A wounded or seriously wounded state has no effect
  short of unconsciousness.
- Range bands, cover, stance, aiming, minor actions, dynamic initiative and
  parrying aren't modelled; everyone is in range of everyone. A dodge costs
  the dodger a reaction instead of initiative.
