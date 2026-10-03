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

The SRD text was read from the primary HTML edition at
https://cepheus-srd.opengamingnetwork.com/cepheus-engine-srd/cepheus-engine-character-creation/
(the module's earlier provenance named a mirror). The complete OGL 1.0a text
and its section 15 notices are kept in `LICENSE-OGL.txt`.

## Sources

| Definitions | Source (Cepheus Engine SRD) |
| --- | --- |
| `attributes/`, `creation/standard` (six characteristics, 2D6 each, assigned) | Chapter 1: Character Creation, Characteristic Generation |
| `lifepath/prior_history` qualification, survival, commission, advancement, reenlistment, four-year terms, configured career/ageing dice, ageing and mustering-out tables | Chapter 1: Character Creation, Careers; Qualifying and the Draft; Terms of Service; Survival; Commission and Advancement; Aging; Re-enlistment and Retirement; Mustering Out Benefits |
| `lifepath/prior_history` personal, service, specialist and advanced table shapes and configured 1D6 skill/benefit selection | Chapter 1: Character Creation, Skills and Training |
| `derived/*_dm` (DM = characteristic / 3, rounded down, − 2) | Chapter 1: Characteristic Modifiers |
| `derived/` skills (unskilled −3) | Chapter 2: Skills |
| `checks/` (2D6 + skill + DM, 8+; Effect is the margin) | Chapter 2: Skills (checks); Chapter 5: Attack |
| `actions/`, `reactions/dodge`, `conditions/dodging` (dodging gives the attacker −1 and the dodger −1 on its own checks until the next round) | Chapter 5: Attack, Reactions, Dodging |
| `conditions/injury`, `overflow`, `spill_*`, `tracks/`, `combat/standard` (damage = weapon dice + Effect − armour, at least 1 at Effect 6+; Endurance first, then Strength or Dexterity; unconscious with two characteristics at 0) | Chapter 5: Damage, Armor, Damage Results |
| `items/` (dagger 1D6, blade 2D6, cutlass 3D6, revolver and auto pistol 2D6, rifle 3D6; jack 1, mesh 5) | Chapter 4: Equipment |
| `currencies/credits` | Chapter 4: Equipment pricing |

The lifepath contains four careers (Scout, Marine, Mercenary and Rogue) and
condenses their SRD career tables to the four skill stats and equipment
already distributed by this module. The names and rules rows are adapted OGC;
the derived skill IDs and the material item mappings are this module's
original implementation choices. The career policy explicitly records its dice
shapes, qualification modifier, natural outcomes, skill-roll counts, rank
benefit thresholds and material-roll modifier so Core does not supply
Cepheus-specific defaults. Career state and all roll evidence are product data,
not code in the module.

## Simplifications

- The module does not distribute the SRD's full 24-career catalogue, draft
  table, injury/mishap table, anagathics, pensions or ship/passage abstractions.
  Its four careers use the same term checks, rank progression, ageing rows,
  skill-table rolls and cash/material benefit procedure, while material rows
  map to the module's existing items, characteristics and skills.
- Ageing changes the six distributed characteristics and an ageing crisis ends
  prior history. Survival failure likewise ends the career; the core keeps the
  recorded term so the CLI and Game can show the failed roll. Mental
  characteristics remain outside combat modifiers.
- Range bands, cover, stance, aiming, minor actions, dynamic initiative and
  parrying aren't modelled; everyone is in range of everyone. A dodge costs
  the dodger a reaction instead of initiative.
