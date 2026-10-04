# The Blackapple Brugh

This directory is the retained campaign source for the Rusty Goldbox adaptation
of *The Blackapple Brugh*, 1st Edition, Release 21. The editable workspace
contains canonical story documents, original art and its generation provenance,
runtime module data, and scripted routes. Runtime data refers to the stable IDs
here; the canon describes that data without maintaining a second story state.

The source is Kyle Hettinger's 2020–2021, 2023 release supplied as
`KH1-The-Blackapple-Brugh-r21.pdf`. Its SHA-256 is
`87e22e0d0bbdb66c54010d4485df05e92d0237b0234c2dffa24298250d4f6de5`.
The source notice places text, maps, floorplans, diagrams, charts, and forms
under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). The
source's other artwork is used with permission and is excluded from this
adaptation. The original PDF and its illustrations are not bundled here.
See [PROVENANCE.md](PROVENANCE.md) and the complete
[license text](LICENSE-CC-BY-SA-4.0.txt).

The campaign depends on the repository's `fifth-srd` ruleset (SRD 5.2.1). The
source adventure's Basic Fantasy statistics are conversion references only;
they are not a second ruleset and are not copied into the campaign source.
The campaign is authored for four to six active characters beginning at level
1 and reaching level 3. Its intended first run is about 8–12 hours over 6–10
sessions. Rusty Goldbox platform support for larger parties, including 12
members, is a separate product capability and does not change this balance
target.

The module split is `blackapple-brugh` (campaign), `blackapple-art`
(asset module), and `blackapple-fae` (data-only extension). There is no campaign-specific
ruleset: the campaign requires the existing `fifth-srd` module, while
`blackapple-fae` may carry campaign-owned monster, NPC, encounter, check, or
condition definitions that use that ruleset. These module IDs follow
the repository's lowercase-hyphen rule. Runtime definition IDs use the
repository's lowercase-underscore rule, so the canon's dotted IDs are logical
names mapped deliberately in the implementation; for example,
`area.brugh.l1` maps to the `brugh_l1` area definition and
`scene.mirror_entry` maps to `scene_mirror_entry`.

## Canon files

* [canon/README.md](canon/README.md) — stable IDs, ownership, state contract,
  and reading order.
* [canon/premise.md](canon/premise.md) — creative brief, audience, tone,
  ruleset conversion, sensitive material, and play loop.
* [canon/beats.md](canon/beats.md) — acts, quests, scene beats, choices,
  recovery, and progression.
* [canon/characters.md](canon/characters.md) — factions, recurring NPCs,
  children, doubles, voices, goals, secrets, and relationships.
* [canon/locations.md](canon/locations.md) — the village, environs, and all
  three Brugh levels, with services and routes.
* [canon/endings.md](canon/endings.md) — outcome rules, alternative endings,
  and returned-child/double accounting.
* [canon/source-mapping.md](canon/source-mapping.md) — source facts, page
  mapping, rights boundary, and the C1–C27 disposition table.
* [canon/scene-matrix.md](canon/scene-matrix.md) — feature-to-scene matrix
  checked against the current Core, CLI, and Game owners.
* [canon/runtime-contract.md](canon/runtime-contract.md) — exact module,
  area, event, variable and parallel-worker interfaces.
* [art/ART_BIBLE.md](art/ART_BIBLE.md) — style, identities, reference images,
  original generation prompts and independent judging requirements.

## Edit and export

Use the installed CLI's `goldbox authoring list`, `show` and `copy` commands
for reusable prompts and templates. `goldbox schema --json` is authoritative
for the runtime JSON vocabulary. The nearest `goldbox.json` supplies the
ruleset dependency search path and the three authored module directories.

```bash
goldbox workspace inspect campaigns/blackapple-brugh --json
goldbox workspace build campaigns/blackapple-brugh --json
goldbox workspace export campaigns/blackapple-brugh --json
```

The build validates runtime modules and stages them under `.goldbox/staged/`;
export writes one Engine container per module into `exports/`. Canon, source
art, generation prompts and scripts remain editable source and are excluded
from those containers. The required `fifth-srd` ruleset is distributed
independently with its own licence; it is not folded into an adaptation module.
Generated staging, party files, saves and exports stay ignored.

For a repeatable four-member CLI party, run `scripts/create-party.sh`, with
`GOLDBOX_CLI=/path/to/goldbox` if needed. `PARTY_DIR` chooses the generated
output directory and `RULESET_PATH` can point to an installed ruleset
container. Ordinary Game creation uses the campaign's authored party limits.

## Editing contract

One canon editor owns shared changes. Runtime authors may propose a correction
or a new concrete implementation need, but they should preserve the IDs and
the distinction between source fact, adaptation decision, and unresolved
runtime question. Temporary progress, review notes, test transcripts, and
implementation status belong in Den rather than in this directory.

The required narrative spine is the revisitable Blackapple hub, its
fairy-haunted forest and Faehill, the mirror crossing, the three-level Brugh,
the seven captive children and their ibix doubles, and the final return. The
party must have viable social, investigative, stealth, exploration, retreat,
and combat approaches. No ending requires killing the Elf Lord.

The source's dual Brugh perception is retained as a per-member mode. On each
entry, every active member receives one deterministic perception resolution;
the resulting `glamour` or `truth` mode is stored for that expedition and is
shown through the ordinary member view control. Every member shares the same
map, routes and consequences. Changing views or rooms never rerolls perception;
the authored entry event explicitly resets it for a new expedition.
