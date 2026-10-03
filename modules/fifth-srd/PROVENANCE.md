# Provenance: fifth-srd ruleset

## Licence and attribution

This work includes material from the System Reference Document 5.2.1
("SRD 5.2.1") by Wizards of the Coast LLC, available at
https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative
Commons Attribution 4.0 International License, available at
https://creativecommons.org/licenses/by/4.0/legalcode.

The licence's full text is in `LICENSE-CC-BY-4.0.txt`. This module's
definition files (every `.json` file except `module.json`) adapt the SRD's
rules into data and are released under the same licence. The source is the
English SRD 5.2.1 PDF from https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf.

## Sources

| Definitions | Source (SRD 5.2.1) |
| --- | --- |
| `attributes/`, `derived/*_mod`, `derived/proficiency`, `creation/` (standard array; point cost with 27 points) | Playing the Game: The Six Abilities, Proficiency; Character Creation |
| `advancement/standard` (300, 900, 2,700, 6,500 XP) | Character Advancement |
| `classes/` (Fighter, Rogue, Cleric, Wizard to level 5: hit points, Second Wind, Extra Attack, Sneak Attack, spell slots, subclass at level 3, Ability Score Improvement at 4) | Classes |
| `features/` (Acolyte, Criminal, Sage and Soldier backgrounds; Alert, Tough and Savage Attacker; Archery, Defense and Dueling; Champion, Thief, Life Domain and Evoker; Ability Score Improvement) | Character Origins, Feats, Classes |
| `races/` (Human, Dwarf with Dwarven Toughness, Elf, Halfling) | Character Origins: Species |
| `checks/`, `conditions/` (d20 attack rolls against AC with Advantage and Disadvantage, Critical Hits doubling dice, saving throws against a spell save DC, Prone, Dodging, death saving throws, massive damage) | Playing the Game; Combat; Rules Glossary |
| `combat/standard` (initiative, action, Bonus Action, movement and Reaction; 5-foot squares; Opportunity Attacks) | Combat; Rules Glossary |
| `spells/` and their actions (Fire Bolt, Sacred Flame, Magic Missile, Guiding Bolt, Cure Wounds, Healing Word, Shield, Scorching Ray, Fireball) | Spells |
| `items/` (weapons, armor and the shield, with their damage and Armor Class) | Equipment |
| `monsters/` (Goblin Warrior, Bandit, Skeleton, Zombie with Undead Fortitude, Wolf with Pack Tactics, Ogre) | Monsters; Animals |

The encounters are original groupings of SRD creatures.

## Simplifications

- Only levels 1 to 5 of four classes, one subclass each, and a handful of
  spells are included. Weapon Mastery, Action Surge, Tactical Mind, Cunning
  Action, Channel Divinity, Arcane Recovery, multiclassing requirements,
  skills and tool proficiencies are not modelled. Savage Attacker,
  Thief and Evoker features have no effect here.
- A background gives a fixed +2/+1 to two of its three abilities, and the
  origin feat is chosen separately (the SRD ties one feat to each
  background). Hit points use the fixed value after level 1.
- Spellcasters prepare as many level 1 to 3 spells as they have slots; the
  wizard's Shield is a reaction it takes when targeted (the SRD casts it when
  hit) while it has a level 1 slot. Fireball affects every enemy; each
  creature rolls its own damage.
- Advantage comes from a Prone or Glowing (Guiding Bolt) target and Pack
  Tactics; Disadvantage from a Dodging target. Sneak Attack needs an ally
  within 5 feet of the target (not Advantage), once per turn.
- Creatures fight on a 60 by 40 foot field; ranged attacks have no long range
  or cover. Monsters use their stat block's attack bonuses and average hit
  points.
