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
| `checks/`, `actions/`, `combat/standard` | Chapter III, combat (pp. 125–128) and movement (p. 123) |

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
- A dual-classed character may call on its old class before the new class
  passes its level (`former <member> on` in play); from then until the
  adventure ends it earns no experience. Experience it earned earlier in the
  adventure is kept rather than forfeited, and the whole campaign counts as
  one adventure. Starting money for a multi-classed character is the wealthiest
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
- Combat is on a field of 10 ft squares, 10 by 6, with diagonal steps
  counting as one. Melee reaches 10 ft (one square), as the source says
  (p. 128). A combatant out of reach closes into combat, moving its movement
  rate in feet per round (p. 123) and attacking the next round (p. 128), or
  charges: double movement ending in reach and +2 to hit, then not again for
  10 rounds (the defender's longer weapon, set weapons and the lost dexterity
  bonus aren't modelled). A creature leaving melee draws a parting blow at +4
  from each foe it leaves (p. 128). Living monsters flee once down to a quarter
  of their hit points, a stand-in for the source's morale rules, and get away
  at the field's edge; mindless undead never flee. Characters don't flee, and
  the fighting retreat isn't used. Spell ranges
  are the spells' own in squares (magic missile 6 + level, sleep 3 + level,
  bless 6, cure light wounds by touch), and an attack or spell needs a clear
  line of sight. Missile fire takes -2 to hit for each range increment beyond
  the first (short bow 50 ft, p. 34), out to ten increments, where the
  source sets no maximum; it isn't randomised among the melee as the source
  says, and ammunition isn't counted. Fighters use a bow they carry. Fighters make one attack per round at
  every level.
- Spells cast in combat spend a slot of their level, from tracks whose
  maximum adds the slots of every class the character has. Magic users and
  clerics memorise a copy per slot from the spells they know (a magic user's
  spell book, a cleric's chosen prayers) and cast each copy once; the sample
  crypt's rest restores slots and memorised spells together, without the
  source's study time. Sleep affects up to 2d4 living
  creatures of 4 hit dice or fewer, weakest first, rather than the source's
  count by hit dice band. Magic missile's missiles each go to the weakest foe
  in range, so they spread once one falls; the caster doesn't choose.
- Monsters' hit points are their hit dice rolled as written, with no minimum.
- In play, felled monsters' experience is shared evenly among the characters
  still standing. Experience for treasure brought home (p. 124) and the
  bonus for a high prime requisite are not awarded yet.
- Armour movement limits, thief skills and turning undead are not modelled yet.
- Shops buy carried gear at half its listed cost (`economy/standard.json`).
  This is an original sample resale policy, not a rule adapted from the source.

Training (`advancement/standard.training`) adapts Chapter III, p. 118:
1,500 gold per current total level and 1d4 weeks. The source leaves “per level”
unspecified; this module uses the current total character level, including
multi-class levels. Each payment buys one class level. Tutor suitability and
performance-based durations are not modelled.

Natural recovery (`resting/natural`) adapts Chapter III, p. 124: one hit point
per uninterrupted day. Constitution delays/weekly bonuses, coma and mandatory
post-injury convalescence are not modelled in this policy.
