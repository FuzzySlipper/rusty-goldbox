# Rusty Goldbox design

Rusty Goldbox is a Gold Box-style RPG engine with authoring, in the spirit of
Unlimited Adventures / Dungeon Craft
([grannypron/uaf](https://github.com/grannypron/uaf)). A player runs a
**campaign module** that is built on a **ruleset module** and dressed by one or
more **asset modules**. All three are authored, validated and exported
independently.

It is a downstream [Rusty Engine](https://github.com/FuzzySlipper/rusty-engine)
product. The Engine provides lifecycle, input, rendering, spatial queries,
deterministic random, content bundles and containers, persistence and UI
transport. Rusty Goldbox owns the module format, rules interpretation, the
campaign runtime and the authoring CLI.

UA/Dungeon Craft is a reference for features and behavior. We don't port its
code or copy its data formats. If anything specific is adapted, record where it
came from in the commit and in the module's provenance.

## Audience and priorities

For the near and middle term, **the author is an agent**. An agent creates
modules by writing JSON files and checks them with the `goldbox` CLI. It
"plays" them through scripted, headless runs that produce readable transcripts.
A visual editor and the full graphical presentation come later. Nothing built
now should assume them, and nothing should stand in their way.

In priority order:

1. A module format that an agent can read, write and diff without help.
2. A CLI that explains the format, validates modules with precise errors,
   evaluates rules, simulates combat and plays campaigns from scripts.
3. A rules and campaign runtime that the CLI and the Engine product both share.
4. Independent module export and cross-module references.
5. Presentation: the first-person view, combat view and UI. This comes later.

## Repository layout

| Path | Owns |
| --- | --- |
| `src/RustyGoldbox.Core/` | Module format and loading, the expression language, ruleset interpretation, characters, combat, the campaign/event runtime and save data. No host and no UI. |
| `src/RustyGoldbox.Game/` | The Engine `IEngineProduct`: opens module bundles, runs Core under Engine update and input, persists saves and publishes projections. |
| `src/RustyGoldbox.Cli/` | The `goldbox` authoring and testing CLI over Core. |
| `src/ui/` | DOM companion: a debug readout before presentation exists. |
| `modules/` | First-party module sources, one directory per module. |
| `tests/` | Focused checks: module fixtures, expression cases and golden play transcripts. |

This is the intended shape; [architecture.md](architecture.md) records what
exists. Planned work and its order are tracked as tasks in the
`rusty-goldbox` Den project, not in this document.

## Modules

A module is a directory with a `module.json` manifest and typed JSON
definition files. The directory layout is a convention for people and agents.
Meaning comes from the manifest and each file's declared type, not from paths.

```json
{
  "format": 1,
  "id": "osric",
  "kind": "ruleset",
  "version": "0.1.0",
  "title": "OSRIC-flavoured core rules",
  "requires": [],
  "provenance": "Adapted from the OSRIC SRD (OGL 1.0a); see PROVENANCE.md"
}
```

### Kinds

| Kind | Contains | Typical requires |
| --- | --- | --- |
| `ruleset` | Attributes, derived values, races, classes and level tables, checks, conditions, item types, spells and abilities, monsters, the combat procedure and character creation | none |
| `extension` | Additions to a ruleset (classes, spells, monsters) and declared patches to its definitions (house rules) | one ruleset |
| `assets` | Logical asset IDs mapped to files: wall sets, backdrops, portraits, combat icons, sounds and music | none |
| `campaign` | Areas, maps, events, encounters, NPCs, shops, variables, the starting party rules and the start location | exactly one ruleset, any extensions, one or more asset modules |

A kind tells the validator what is allowed. It does not create a different
loader.

### Identity and references

- Each definition has an ID that is local to its module (`fighter`), with
  the definition type known from where it's used.
- Another module's definition is referenced as `module:id` (`osric:fighter`).
  A module may reference only modules it lists in `requires`. Transitive
  dependencies can't be referenced: a campaign that names `osric:` must
  require `osric` itself.
- `requires` entries take an ID and a version range. The resolver picks one
  version per ID, and any conflict is an error.
- Load order is dependency order. Two modules defining the same qualified ID
  is impossible by construction. Changing another module's definition takes an
  explicit `patch` entry in an `extension` or `campaign`. A patch names its
  target and the fields it replaces, and validation reports every patch it
  applied.
- Asset references are logical IDs (`stonecrypt:wall/stone`), never file
  paths. Swapping one art module for another works as long as the new one
  provides the same IDs. `goldbox module validate` lists any that are missing.

### Rules as data

A ruleset has to express both AD&D-style descending AC/THAC0 and 3.5e-style
ascending AC and bonus stacking without new code. A ruleset is therefore data
plus a small **expression language**:

- arithmetic, comparisons, boolean logic, `if`/`then`/`else`, `min`/`max`/
  `floor`, and dice (`2d6+1`, `1d20`);
- reads of the evaluation context (`self.str`, `target.ac`, `self.level`,
  `campaign.var.gate_open`) and table lookups (`table(thac0, self.class,
  self.level)`);
- no assignment, loops, user-defined functions or side effects. Expressions
  are type-checked when a module loads, so an unknown stat or table fails
  validation, not play.

State changes come from a fixed vocabulary of **operations** implemented in
C#: deal damage, heal, apply or remove a condition, modify a stat for a
duration, move, grant XP or items, set a variable, and so on. Each operation
takes expression arguments. Rulesets and campaigns choose and combine
operations; they can't define new ones.

**Plugin boundary.** Modules are the plugins. They are data only and load at
runtime with no compilation. New *primitive* behavior, such as an operation,
an expression function or a combat-procedure hook, is added to Core in this
repository and becomes available to every module. That is a deliberate choice:
NativeAOT can't load assemblies, and data-only modules stay safe to share,
validate and diff. Add a primitive when a real ruleset needs one. Prefer one
general operation over a ruleset-specific special case, but don't build a
universal effect system ahead of a concrete need.

The combat procedure (rounds, initiative, the per-turn action budget and
movement) is a fixed C# loop with ruleset-supplied formulas and budgets. That
covers 1e one-action rounds and 3.5e standard/move/swift actions without
making the loop itself scriptable.

### Campaign content

- **Areas.** Gold Box maps are cell grids with walls *on cell edges*: each cell
  side has a wall, door or opening type, and a cell is never just "solid".
  Areas can be dungeon levels or overland maps. A cell can carry zone tags, a
  backdrop and an event trigger with a facing or once-only flag. Areas have
  named entry points.
- **Events.** These are UA-style event chains, written as data. Event types
  include text, question/menu, combat, treasure, give/take item, shop,
  temple, training, rest, NPC join/leave, set/test variable, teleport, a
  conditional branch, chain-to, and end adventure. Each event names its
  successors through outcome branches (`onYes`, `onWin`, `onFlee` …). Any
  branch can be guarded by an expression.
- **Variables** are global or area-scoped. Each is declared with a type and an
  initial value; undeclared variables fail validation.
- **Encounters** reference ruleset monsters, with counts, placements and
  surprise rules.

## Runtime

Core holds one mutable owner per domain: party and characters, the campaign
position and variables, the active event, and the active combat. Commands in
(move, turn, choose option, combat action) produce state changes and an
observation record. The CLI and the Game product drive the same command
surface. The CLI prints observations; the Game publishes them as projections
and, later, presentation.

Every random draw goes through Engine `Random` with an explicit seed, so a
seed and a command script fully reproduce a run. A transcript records seeds,
commands, rolls and outcomes.

**Saves** record the resolved module set (ID, version and content identity for
each) and the campaign state. Loading a save under a different module set is
refused with a message that names the differences. Save migration is out of
scope until a real module release needs it.

## CLI

`goldbox` is how agents author and test modules. Every command accepts
`--json` and returns structured results. Errors name the module, file, JSON
path and the rule that failed. Module directories resolve through
`--modules <dir>` and a workspace `goldbox.json`.

| Command | Purpose |
| --- | --- |
| `goldbox schema [type]` | Print a definition type's fields, an example and the available operations and expression functions. This is the agent's format reference. |
| `goldbox module new <kind> <id>` | Scaffold a module. |
| `goldbox module validate <path>` | Check the manifest, types, references across `requires`, expression types, asset coverage and patches. |
| `goldbox module inspect <path> [selector]` | Show resolved definitions after dependencies and patches. |
| `goldbox module deps <path>` | Show the resolved dependency graph and versions. |
| `goldbox eval <expr> --module … [--context …]` | Evaluate an expression or check, for example a level-5 fighter's THAC0 or a saving throw against a given spell. |
| `goldbox character new\|level …` | Create or advance a character under a ruleset and print the derived sheet. |
| `goldbox map render <area>` | Print an area as text: edge walls, doors, triggers and entry points. |
| `goldbox sim combat --encounter … --party … --seed N [--runs K]` | Run a headless combat, or K of them, and report outcomes and distributions. |
| `goldbox play --campaign … --seed N [--script file]` | Play from a command script or stdin and emit a transcript. |
| `goldbox module pack <path>` | Export a module as an independent container. **Blocked on Engine.** See below. |

Golden transcripts from `goldbox play` and `goldbox sim` are the main
regression checks for module and rules behavior.

## Engine integration

- **Content during development.** Each module that the Game should see is
  declared as a `RustyEngineContentBundle` under the product content root.
  The Game opens the bundles for the selected campaign and its requirements
  with `Content.OpenBundle`, and Core loads definitions from the bundle files.
  `rusty dev` reloads bundle edits without a restart.
- **Releases.** `rusty build --pack <dir> --compress` ships the product with
  its content as one zstd-compressed `.rpak`.
- **Independent module export: Engine gap.** An Engine container holds a
  single whole Product, and bundles are declared when the
  product is built. Two things are missing:
  1. packing a standalone content collection (one module) into the Engine
     container format;
  2. opening an independently installed container at runtime as a bundle.

  Distinct ruleset, asset and campaign exports depend on both (requested
  upstream as rusty-engine Den task #9040). Without them, modules are
  distributed as source directories and staged as bundles. Do not write a local container format or
  archive reader as a substitute.
- **The CLI and Engine services.** The CLI needs Engine `Random` (and later
  content admission) outside a running host. `Rusty.Engine.Testing.EngineTestHost`
  provides the Engine service set in process. The CLI uses it only if the
  Engine supports that for tools rather than just test projects; otherwise it
  uses an Engine-supported in-process host. Don't swap in a local RNG.

## Non-goals

- A visual editor, or human-facing authoring UI.
- Loading C# code from modules.
- A general-purpose scripting language. Expressions plus fixed operations come
  first; anything larger needs a concrete module that can't be written without
  it.
- Importing UA/Dungeon Craft data files or keeping compatibility with them.
- Save migration across module versions.
