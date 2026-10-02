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
| class `equipment`, race `multiclass_equipment` | Chapter I, armour and weapons permitted to each class (pp. 11, 18, 20, 25) and the races' multi-class restrictions (pp. 4–7) |
| `advancement/standard`, race `multiclasses`, class level `hp` division | Chapter I, multi-classing and dual-classing (pp. 27–28) and permitted class options (pp. 4–7) |
| `spells/` | Chapter II, cleric and magic user level 1 spells; descriptions are reworded |
| `checks/`, `actions/`, `combat/standard` | Chapter III, combat (pp. 125–128) |

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
  racial level limits.
- Multi-classed characters attack and save with the most favourable of their
  classes' tables (`class_min`). The source states that rule for monsters
  with the abilities of several classes (p. 129); it doesn't say so for
  characters. Experience is divided evenly in whole points, rounding down.
  Dwarves follow the more restrictive class's armour and weapon limits and
  elves and halflings the less restrictive; the source's narrower rule that
  thieving abilities only work in thief armour is not modelled.
- A dual-classed character's old class doesn't work at all until the new
  class passes its level, rather than working at the cost of the adventure's
  experience. Starting money for a multi-classed character is the wealthiest
  of its classes', as the source says (p. 30).
- The dwarf and halfling constitution bonus applies to saves against spells
  and wands, but not to saves against poison (which share the death,
  paralysis and poison table).
- The wisdom mental saving throw bonus (`wis_save`) is available as a derived
  value but isn't added to any check automatically.
- Hit points gained at a level are at least 1 (`max(1, ...)`), so a low
  constitution penalty can't leave a character with 0 hit points. The source
  doesn't state a minimum.
- Surprise costs the surprised side its first whole round, rather than one
  or two segments.
- Combat has no positions or movement, and fighters make one attack per round
  at every level.
- Spells cast in combat spend a slot of their level, from tracks whose
  maximum adds the slots of every class the character has; a character
  knows a list of spells rather than memorising them each day, and the
  sample crypt's rest restores slots. Sleep affects up to 2d4 living
  creatures of 4 hit dice or fewer, weakest first, rather than the source's
  count by hit dice band; magic missile fires all its missiles at one target.
- Monsters' hit points are their hit dice rolled as written, with no minimum.
- Armour movement limits, thief skills and turning undead are not modelled yet.
