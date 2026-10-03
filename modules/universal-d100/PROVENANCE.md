# Provenance: universal-d100 ruleset

## ORC Notice

This product is licensed under the ORC License held in the Library of
Congress at TX000 [number tbd] and available online at various locations
including www.chaosium.com/orclicense, www.azoralaw.com/orclicense,
www.gencon.com/orclicense and others. All warranties are disclaimed as set
forth therein. (This is the notice as Chaosium's ORC Content Document gives
it.) The licence text is in `LICENSE-ORC.txt`.

## Attribution

This product is based on the following Licensed Material: "Basic Roleplaying:
Universal Game Engine", copyright © 2023 Chaosium Inc., by Jason Durall and
Steve Perrin, based on the Basic Roleplaying system created by Steve Perrin,
Steve Henderson, Warren James, Greg Stafford, Sandy Petersen, Ray Turney and
Lynn Willis. Source: the Basic Roleplaying ORC Content Document
(https://www.chaosium.com/content/orclicense/BasicRoleplaying-ORC-Content-Document.pdf).

If you use our Licensed Material in your own works, please credit us as
follows: "Universal d100" ruleset module of Rusty Goldbox, by the Rusty Goldbox
contributors.

## Reserved Material

Basic Roleplaying, BRP, Chaosium and the Powered by BRP logo are trademarks of
Chaosium Inc. and are not licensed; they appear here only in the attribution
above, and this module is not endorsed by Chaosium. This module designates no
Reserved Material of its own.

## Expressly Designated Licensed Material

None.

## Sources

| Definitions | Source (Basic Roleplaying ORC Content Document) |
| --- | --- |
| `attributes/`, `creation/standard` (3D6, SIZ and INT 2D6+6; human limits) | Ch. 2 Creating a Character: Step One, Characteristics |
| `tracks/hit_points`, `derived/major_wound`, `dm_d4`, `dm_d6` | Derived Characteristics: Hit Points, Major Wounds, Damage Modifier Table |
| `derived/` skills (base chances: Sword, Axe and Spear 15%, Dagger and Mace 25%, Bow 10%, Brawl 25%, Dodge DEX×2, Shield 15%) | Skill list base chances; weapon and shield tables |
| `checks/` (d100 at or under the skill; critical at 1/20, special at 1/5, fumble at the highest 1/20 of the failure chance) | Ch. 6 Combat: Levels of Success and Failure |
| `actions/` (an attack is dodged, or parried with a shield; each defence after the first in a round at −30%; a critical does maximum damage and ignores armour; a missile weapon adds half a positive damage modifier) | Combat Actions, Parry, Dodge, Attack and Defense Matrix, Damage Modifier |
| `conditions/` (armour subtracts from damage; a single wound of half the hit points or more is a major wound: the character fights on for rounds equal to its remaining hit points; at 2 or fewer hit points it is unconscious) | Armor, Minor Wounds, Major Wounds, Hit Points |
| `items/` (broadsword, short sword, battle axe, dagger, light mace, short spear, self bow, heater shield, soft and hard leather, ring armour, half plate, with their damage, armour points and skill penalties) | Weapons and Armor tables |
| `monsters/wolf`, `bear` | Ch. 11 Creatures: Natural Creatures (average values) |

`monsters/bandit` and the four professions' skill spreads are original
examples written for this module.

## Simplifications

- Skill points aren't spent at creation: a profession grants a fixed,
  combat-heavy spread of skill bonuses instead of the source's professional and
  personal skill points. Skill category modifiers aren't used.
- The Attack and Defense Matrix is reduced to levels: the defence's level
  (critical 3, special 2, success 1) is taken from the attack's; a difference
  of 3 is a critical (maximum damage, armour ignored), 1 or 2 a normal hit.
  Special results (impale, bleed, crush, knockback), damage to weapons and
  shields, fumble tables, and parrying with a weapon aren't modelled.
- A major wound puts the creature in shock instead of rolling on the Major
  Wound Table; there is no Luck roll or characteristic loss.
- Everyone acts once a round in DEX order; movement, engagement and hit
  locations aren't used. There is no experience or skill improvement.
