# Provenance: classic ruleset

## Open Game Content

This module's definition files (every `.json` file except `module.json`) are
Open Game Content under the Open Game License 1.0a; see `LICENSE-OGL.txt`,
which includes the section 15 copyright notice. Nothing in this module is
designated Product Identity.

## Sources

Game mechanics were adapted from Chapters I to III of the updated 2nd edition
core rules whose per-chapter copyright notices appear in section 15 of
`LICENSE-OGL.txt`. That work designates those chapters Open Game Content. Nothing was taken from its Product Identity: chapters IV to VI, its
artwork, its title and trademarks, or its variable experience point rule.

| Definitions | Source (book page) |
| --- | --- |
| `attributes/`, `tables/str_*`, `dex_*`, `con_*`, `wis_save` | Chapter I, ability score tables (pp. 1–3) |
| `races/` and `tables/race_movement` | Chapter I, character races (pp. 3–8) |
| `classes/`, `tables/thac0`, `tables/saving_throws` | Chapter I, fighter, cleric, magic user and thief (pp. 10–27) |
| `items/` | Chapter I, equipment and armour (pp. 31–34) |
| `creation/standard` (rolls, starting gold) | Chapter I (pp. 1, 30) |
| `spells/` | Chapter II, cleric and magic user level 1 spells; descriptions are reworded |
| `checks/`, `combat/standard`, `tables/surprise_segments` | Chapter III, combat (pp. 125–128) |

Monsters (`monsters/`) are original content written for this module. They use
the Chapter III rule that monsters attack and save as fighters of an
equivalent level. Size (`derived/size`, `tables/race_size`) expresses the
source's "damage vs small or medium / large" distinction: item damage reads
`target.size`.

## Simplifications

These are deliberate differences from the source, to keep the first ruleset
small:

- Exceptional strength (18.01 to 18.00) is not modelled; strength 18 uses the
  plain 18 row.
- Only the fighter, cleric, magic user and thief classes are included, up to
  level 10, and only humans, dwarves, elves and halflings. There are no
  multi-classed characters and no racial level limits.
- The dwarf and halfling constitution bonus applies to saves against spells
  and wands, but not to saves against poison (which share the death,
  paralysis and poison table).
- The wisdom mental saving throw bonus (`wis_save`) is available as a derived
  value but isn't added to any check automatically.
- Armour movement limits, thief skills, turning undead and spell effects are
  not modelled yet.
