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
  "id": "classic",
  "kind": "ruleset",
  "version": "0.1.0",
  "title": "Classic first-edition rules",
  "requires": [],
  "provenance": "Game mechanics adapted from Open Game Content under the OGL 1.0a; see PROVENANCE.md"
}
```

Every field is required and unknown fields are errors. IDs are lowercase
letters, digits and single hyphens, starting with a letter. Versions are
`MAJOR.MINOR.PATCH`. A `requires` entry is `{ "id": "classic", "version":
"^0.1.0" }`; ranges are an exact version, `^1.2.0` (same major; same minor
below 1.0.0), `~1.2.0` (same minor), space-separated comparators
(`>=1.0.0 <2.0.0`) or `*`.

Every other `.json` file in the module directory is a definition file: a JSON
object whose `type` field names its definition type. Other files (media,
`PROVENANCE.md`) are not read as definitions.

### Kinds

| Kind | Contains | May require |
| --- | --- | --- |
| `ruleset` | Attributes, derived values, races, classes and level tables, checks, conditions, item types, spells and abilities, monsters, the combat procedure and character creation | assets modules only; usually nothing |
| `extension` | Additions to a ruleset (classes, spells, monsters) and declared patches to its definitions (house rules) | exactly one ruleset, plus extensions and assets |
| `assets` | Logical asset IDs mapped to files: wall sets, backdrops, portraits, combat icons, sounds and music | other assets modules only |
| `campaign` | Areas, maps, events, encounters, NPCs, shops, variables, the starting party rules and the start location | exactly one ruleset, one or more assets modules, any extensions |

No module may require a campaign, and a resolved module set contains at most
one ruleset. A kind tells the validator what is allowed. It does not
create a different loader.

### Identity and references

- Each definition has an ID that is local to its module (`fighter`), with
  the definition type known from where it's used.
- Another module's definition is referenced as `module:id` (`classic:fighter`).
  A module may reference only modules it lists in `requires`. Transitive
  dependencies can't be referenced: a campaign that names `classic:` must
  require `classic` itself.
- `requires` entries take an ID and a version range. The resolver picks one
  version per ID: breadth-first from the module being loaded, each ID gets the
  highest available version that satisfies the ranges known when it is first
  required. A later range that excludes it is a conflict error; the resolver
  does not backtrack. Requirement cycles are errors.
- Required modules are found in search directories: `--modules <dir>`
  arguments together with the `modules` list of the nearest `goldbox.json`
  (paths relative to it), or, with neither, the module's sibling directories.
  A search directory may itself be a module or contain module directories.
  Directories have no precedence: the same ID and version in two of them is an
  error.
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

Stats are the ruleset's **attributes** (rolled scores) and **derived values**
(expressions over other stats, with inferred types), plus the built-ins
`level`, `class` and `race`. **Tracks** are the pools that go down and up in
play (hit points, fatigue, magic points, a dying value): each has a maximum
(an expression, or one the creature brings), an optional floor, restore cap
and starting value, and is read as `self.<id>` and `self.max_<id>`. No pool is
built in; a ruleset declares its own, and one may be built from class level
gains. Stat IDs form one namespace across the module
set. **Modifiers** from races, conditions and equipped items add to a stat or
to a check's roll. A **check** compares a roll, plus its modifiers, with a
target. A **monster** names the class and level whose tables it uses, and its
`stats` replace derived values. Attributes may declare a `default` for
creatures that have none.

Each definition file holds one definition: `type`, `id` and the type's
fields. Definition IDs use lowercase letters, digits and underscores, so they
can appear in expressions. `goldbox schema` lists every type with its fields
and an example, and the expression functions.

State changes come from a fixed vocabulary of **operations** implemented in
C#: deal damage, heal, apply or remove a condition (conditions carry stat and
check modifiers, so a timed condition is a timed modifier), make a further
check, and later move, grant XP or items, set a variable, and so on. Each operation
takes expression arguments. Rulesets and campaigns choose and combine
operations; they can't define new ones.

**Expressible, not distributed.** The engine's job is to express rule
systems, not to ship them. The repository distributes only rulesets whose
source license allows it, with provenance (such as `modules/classic`, from OGL
content). Other systems, including current commercial editions, must be
writable as ruleset data that a user supplies, with no new code. That claim is
checked with small original fixture rulesets in the tests that are shaped like
those systems (ascending AC and ability modifiers, proficiency ranks, other
action economies) but copy none of their text or tables. Core features stay
edition-neutral and are exercised by more than one ruleset shape.

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

Combat is built so that no die convention is assumed:

- **Actions, not attacks or spells.** An action costs budget (any named
  budget the combat definition declares: one action, standard + move, three
  actions), picks a target, may make a check, and runs operations for the
  check's outcome. Attacks, spells, heals and aimed shots are all actions.
  Creatures list the actions they can take as *uses* that supply the action's
  parameters (a monster's bite damage) or take them from equipment (a
  weapon's damage); a spell names the use its casting performs. Casting in
  combat (which spells a creature can cast, and what casting costs) isn't
  wired yet, so today spells are defined and checked but not cast.
- **Checks give outcome tiers.** A check is a roll, an optional bonus and a
  target, rolled high or under, with ordered tiers that read the roll and
  margin. That expresses natural-20 criticals, degrees of success by margin,
  percentile specials and fumbles, and 3d6 roll-under; `roll_count` gives
  dice-pool successes. Actions branch on tier names, not on hit or miss.
  An operation can make another check, such as a defender's parry.
- **Pools are declared, not built in.** Damage and heal act on a named
  track, or the combat's default one; costs that spend a pool are operations
  on it (an action can spend fatigue to hit harder).
- **Services resolve, facts record.** The loop and operations change state
  in one place (the combat runner); each change is recorded as a fact, and
  the facts in order are the transcript. Reactions will subscribe to facts
  rather than being special cases in the loop.
- **Kit and rules.** Core owns the mechanism (turn order, budgets, targeting,
  durations, when operations run); ruleset data owns every number and word.
  Core names no class, stat, condition or die size.

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
  initial value; undeclared variables fail validation. Expressions read them
  as `campaign.var.<name>`.

An area's map is written as text that agents can read and diff, in the same
notation `goldbox map render` prints: `+` corners, horizontal edges of two
characters (`--` wall, two spaces open, `DD` door, `SS` secret door) and
vertical edges of one (`|`, space, `D`, `S`). Cell features and entry points
are listed by `[x, y]`, x running east and y south from 0. Each event is a
definition whose `kind` picks its fields (`goldbox schema events`).
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
each, the identity being the Engine's bundle identity: a hash of the module's
files that is the same for a directory, a bundle and a container) and the
campaign state. They are files for the CLI and named slots in Engine
persistence for the Game; `goldbox play --store` reads and writes the same
slots.
Command *n* of a campaign rolls on its own random scope, so a game saved and
resumed rolls exactly as one played straight through. Loading a save under a different module set is
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
| `goldbox module pack <path>` | Validate a module and export it as an independent content container (through the Engine's `rusty pack-content`). |

Golden transcripts from `goldbox play` and `goldbox sim` are the main
regression checks for module and rules behavior.

## Engine integration

- **Content during development.** `modules/` is the product content root, and
  each module directory in it is declared as a `RustyEngineContentBundle`.
  The Game opens the bundles for the selected campaign and its requirements
  with `Content.OpenBundle`, and Core loads definitions from the bundle files
  through the same loader the CLI uses on directories. `rusty dev` reloads
  bundle edits without a restart.
- **Input.** The DOM claims one declared payload intent for its actions; keys
  map to declared digital intents for moving and choosing. Play commands are
  the CLI's text commands, so the play part of a Game session is a valid
  `goldbox play` script (the party itself is made in the Game).
- **Persistence.** Saves go through `ProductStateStore` in one Engine
  persistence scope, as the same JSON `goldbox play --save` writes.
- **Releases.** `rusty build --pack <dir> --compress` ships the product with
  its content as one zstd-compressed `.rpak`.
- **Independent module export.** `rusty pack-content <module-dir> --output
  <file> [--compress]` packs one module into an Engine content container.
  An installed container opens at run time as an ordinary bundle with
  `ProductContentBundle.OpenContainer`, so ruleset, asset and campaign modules
  ship and install separately. Replace an installed container by renaming a
  new file into place, never by rewriting it while open. Don't write a local
  container format or archive reader.
- **The CLI and Engine services.** The CLI sets `RustyEngineToolHost` and
  creates the Engine service set in process with `EngineTestHost.Create()`:
  no renderer, input or lifecycle, but the same `Random` (and content
  container) services as the running product, so a seed and script reproduce
  the same rolls in both. Don't swap in a local RNG.

## Non-goals

- A visual editor, or human-facing authoring UI.
- Loading C# code from modules.
- A general-purpose scripting language. Expressions plus fixed operations come
  first; anything larger needs a concrete module that can't be written without
  it.
- Importing UA/Dungeon Craft data files or keeping compatibility with them.
- Save migration across module versions.
