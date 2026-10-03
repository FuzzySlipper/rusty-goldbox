# Provenance: fate-condensed ruleset

## Licence and attribution

This module's definition files (every `.json` file except `module.json`) adapt
the rules of Fate Condensed and are released under the Creative Commons
Attribution 3.0 Unported license; the licence's full text is in
`LICENSE-CC-BY-3.0.txt` (also at http://creativecommons.org/licenses/by/3.0/).
The attribution the source requires:

> This work is based on Fate Condensed (found at http://www.faterpg.com/), a
> product of Evil Hat Productions, LLC, developed, authored, and edited by PK
> Sullivan, Lara Turner, Leonard Balsera, Fred Hicks, Richard Bellingham,
> Robert Hanz, Ryan Macklin, and Sophie Lagacé, and licensed for our use under
> the Creative Commons Attribution 3.0 Unported license
> (http://creativecommons.org/licenses/by/3.0/).

Fate™ is a trademark of Evil Hat Productions, LLC. This module uses no Evil Hat
logo, artwork or setting, and is not endorsed by Evil Hat Productions.

The source is the CC BY SRD of Fate Condensed, from the official SRD download
at https://fate-srd.com/official-licensing-fate ("CC-BY SRDs"). Its rules were
adapted into data; no text was copied beyond skill names and short game terms.

## Sources

| Definitions | Source (Fate Condensed) |
| --- | --- |
| `attributes/` (the 19 skills, rated 0 to +8 on the ladder) | Skills, Skill List, The Adjective Ladder |
| `creation/pyramid` (one Great, two Good, three Fair, four Average, the rest Mediocre; three stunts) | Skills, Stunts |
| `tracks/` (three stress boxes, one more at Physique or Will Average/Fair, three more at Good/Great) | Stress and Consequences |
| `conditions/mild`, `moderate`, `severe`, `physical_hit`, `mental_hit`, `taken_out` (stress absorbs one shift a box; consequences absorb 2, 4 and 6; a hit that can't be absorbed takes you out) | Taking Harm, Stress, Consequences, Getting Taken Out |
| `checks/` (4dF plus a skill against the defender's 4dF plus a skill; fail, tie, success, success with style at three shifts or more) | Taking Action, Rolling the Dice; Outcomes |
| `actions/` (attack with Fight, Shoot or Provoke; create an advantage) | The Four Actions |
| `monsters/` (minor NPCs with a few stress boxes and no consequences; a supporting NPC with consequences) | Running the Game: NPCs |
| `features/` (stunts: +2 to an action with a skill, or an extra stress box) | Stunts |

The NPCs, their names and the stunts are original examples written for this
module in the shape the source describes.

## Simplifications

Fate is a narrative game; a combat module keeps its conflict arithmetic and
leaves out what needs a table to decide:

- Aspects, invokes, compels and fate points are not modelled. Create an
  advantage gives its creator +2 on its attacks for the next exchange (two
  with style), standing in for an aspect with free invokes.
- Turn order uses Notice (the Fate Core way) rather than the default elective
  ("popcorn") order, which needs players choosing who goes next.
- Conflicts have no zones: everyone can reach everyone. Ties and success with
  style give no boost. Defending is always with Athletics (physical) or Will
  (mental).
- A hit is absorbed with stress first, then the smallest consequence (or pair)
  that covers the rest. The extra mild consequence at Superb Physique or Will
  is not modelled.
- There is no advancement (milestones), no concession, and consequences don't
  recover on their own.
