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
- Asset references are logical IDs (`stonecrypt:stone_wall`), never file
  paths. Swapping one art module for another works as long as the new one
  provides the same IDs. `goldbox module validate` lists any that are missing.
- An asset has a kind (`wall_set`, `backdrop`, `portrait`, `icon`), and a
  reference names the kind it needs: an area's `wall_set`, a cell's
  `backdrop`. Asset files are 8-bit RGBA PNGs, the format the Engine renderer
  admits, and validation reads each header to check that. A wall set is one
  image with named frames (pixel rectangles for `wall` and `door`, optionally
  `floor` and `ceiling`), so art of any size and layout fits. A sprite is a
  sheet of equal frames (any frame size and count) with the way it faces,
  the pixel it stands on, its height in cells and optional named animations
  (frames and fps, looping or once). A `figure` definition (in a campaign or
  extension, since rulesets carry no art) says which sprite draws a monster or
  a class, and optionally the icon that lists it. A character may have a
  `portrait` asset, chosen at creation and kept in its file and saves.

### Rules as data

A ruleset has to express both AD&D-style descending AC/THAC0 and 3.5e-style
ascending AC and bonus stacking without new code. A ruleset is therefore data
plus a small **expression language**:

- arithmetic (including `%` remainder), comparisons, boolean logic,
  `if`/`then`/`else`, `min`/`max`/`floor`, and dice (`2d6+1`, `1d20`);
- reads of the evaluation context (`self.str`, `target.ac`, `self.level`,
  `target.condition.prone`, `self.rolled.attack` (how often the creature has
  rolled that check since its latest turn began), `campaign.var.gate_open`, in a class's modifiers
  `class.level`, and in a check made during another check `outer.margin`) and
  table lookups (`table(thac0, self.class, self.level)`);
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
set. **Modifiers** from races, classes, features, conditions and equipped items
add to a stat or to a check's roll; a check modifier with `against` applies only
when its expression, which may read the target, holds (+2 against a shaken
foe). A **check** compares a roll, plus its modifiers,
with a target. A **monster** names the class and level whose tables it uses,
and its `stats` replace derived values. Attributes may declare a `default` for
creatures that have none.

A character records the class it took at each level and what the level track
gained, so it can hold levels in several classes: `self.level` is the total
and `self.class` the first class. Without an **advancement** definition each
class has its own experience table and a character keeps its one class
(first-edition style). An advancement with experience by character gives one
table for total level, and each new level goes to a class the player picks,
checked against the race and the class's requirements (3.5e style). A class's `equipment` expression (reading `item.id`, `item.kind`,
`item.weight` and `item.cost`) says what its members may equip, checked when
an item is equipped and when a character file loads; a race's
`multiclass_equipment` says whether a multi-classed character needs every
class to allow an item or any one. What works with the gear is a separate
question: `equipped(x)` counts the equipped items `x` holds for, so an
action's `available`, a reaction's `when` or a feature's modifier can need a
shield or forbid heavy armour. A class's
modifiers read `class.level`, the creature's level in that class, so
per-class progressions such as base attack and base saves add up across
classes. With experience **split** (first-edition multi-classing), a
character may start with several classes its race's `multiclasses` allow;
experience is divided evenly between them, each advances on its own table,
and expressions take the best table with `class_min(...)` and divide hit
points by `self.classes`. Under split experience a `class_change` expression
allows dual-classing: the old classes stop, and their modifiers and actions
wait until the new class passes them (`self.former_level`). A class level's
`hp` is rolled once and kept; its optional
`hp_bonus` is added as the character is now, so a constitution-style change
moves every level's hit points (3.5e), while rulesets without it keep each
level's gain as rolled (first edition).

What a character chooses beyond race and class is a **feature**: a
background, heritage, feat, class feature or ability increase, each with a
`kind` in the ruleset's own words, optional requirements (an expression read
as the character is with the level that grants it), modifiers and actions.
Choices are **grants** of a kind: character creation grants some to every new
character (a background and a heritage), the advancement grants others at
character levels its `when` expression picks (a feat every third level), and
a class level grants its own (bonus feats). The player names features in
order and each grant takes the next of its kind; each level records the
features chosen at it. A repeatable feature with an attribute modifier is an
ability increase. A grant may accept several kinds (a general feat slot that
also takes combat feats, while a warrior's bonus feat takes only combat
feats). A modifier can also raise a track's maximum (toughness). A race may
leave out its class list to allow any class, so an ancestry-and-class ruleset
needs no per-race patches when an extension adds a class. The advancement can
also grant **level boosts** (four attributes at set levels), each raising a
score by an amount its table gives for the current score. A character file
records the creation it was made with and each level's features and boosts;
loading it replays the levels from the scores before any level boost and
refuses a choice no level granted, a requirement missed at the level it was
taken, or a wrong number of boosts. When a level raises a track's maximum
(its gain, a toughness feat, a boosted stat), the current value rises with it.

A **character-creation** definition makes attribute scores by one method:
rolled (in order or arranged by the player's priority), a fixed array the
player arranges, point buy (a base, a budget and a table of each score's
cost), or boosts. Under boosts every attribute starts at a base, and the race,
the features creation grants (a background), the class and the creation
definition each list boosts, fixed or a choice among attributes, that raise
one attribute by the creation's boost; one source never boosts the same
attribute twice. A ruleset may offer several creation definitions and mark
one the default.

A condition may declare **values** with defaults (`{ "amount": 5 }`) that
`apply_condition` sets when it applies the condition ("ongoing 5"); its
modifiers and its start- and end-of-turn operations read them as
`condition.amount`, so a save that ends the condition is an end-of-turn check.
A combat definition may put its fights on a **field**: a grid of cells with
a distance metric (diagonal steps counting 1, or only straight steps). The
sides start at opposite edges; an action's `range` limits its targets to that
many cells, `combat.distance` and `combat.nearest` read how far apart
creatures are and `combat.sight` whether one sees the other, and the `move`
operation steps the actor toward its target (until `within` range and in
sight, so a caster stops at casting range) or away from it (until `beyond`
a distance) through free cells, so movement costs whatever budget the moving
action costs. The field declares kinds of **terrain** by one-character keys
(impassable, costing more movement to enter, blocking sight) and an
encounter lays them out as rows of text, so the same field can be a corridor
or a pillared hall. Movement takes the cheapest way round obstacles; an
action with a `range` also needs line of sight, while one without (moving
toward an enemy) doesn't. An action's `check_bonus` (and a check
operation's `bonus`) adjust its roll for range or a charge. A move may be a
careful withdrawal (`provokes: false`) that sets off no parting blows, and a
fleeing creature (`escape`) that runs out of room at the field's edge leaves
the fight, out but not felled. Creatures have no facing: Gold Box fights didn't
turn on it, and a rule about flanking or rear attacks can read positions.
Without a field everyone is in reach.

**Reactions** happen out of turn: a reaction names a trigger (an enemy
leaving its reach, an enemy's action about to resolve against it, an
enemy's operation wounding it, or an enemy felling one of its allies), spends a combat budget (usually a `reaction`
budget refilled each turn) and resolves its use against whoever triggered it.
Classes, monsters and features list the reactions they give. A `targeted`
reaction is an interrupt: it resolves before the action's check (a raised
shield), and an attacker it defeats doesn't act. Reactions don't set off
further reactions, except that a reaction marked `counter` may answer an
enemy's reaction (a shield raised against an attack of opportunity, a
counterspell); nothing answers a counter, so chains stop one level down.
Budgets start full so a creature can react before its
first turn.

A combat budget's `per_turn` is an expression, so a condition that lowers a
stat it reads takes actions away. Expressions evaluated during a fight
(budgets, initiative, actions, operations and condition hooks) read
`combat.round` and `combat.surprise_round`, so unsurprised creatures can act
with less in a surprise round (3.5e).

Each definition file holds one definition: `type`, `id` and the type's
fields. Definition IDs use lowercase letters, digits and underscores, so they
can appear in expressions. `goldbox schema` lists every type with its fields
and an example, and the expression functions.

State changes come from a fixed vocabulary of **operations** implemented in
C#: deal damage, heal, apply or remove a condition (conditions carry stat and
check modifiers, so a timed condition is a timed modifier), make a further
check, branch with `if`, and later move, grant XP or items, set a variable, and so on. Each operation
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
  weapon's damage); a spell names the use its casting performs and what
  casting spends from the caster's tracks. Per-day resources are tracks: spell
  slots of each level (whose maximum `spell_slots(level)` reads from the
  classes) or a single pool of power points. A character knows a list of
  spells, each on one of its classes' lists and payable at full resources;
  in combat it casts them first while it can pay, and a `rest` event restores
  the tracks it names. A class with `prepares_spells` memorises instead: the
  character holds a day's copies (one per casting, all payable together), each
  cast once, and a `rest` with `prepare` readies them again; without a chosen
  list it memorises its known spells in order as far as its slots go.
  Monsters list spells too, cast before their actions: paid from their own
  tracks (a monster's slots or power points are just tracks it is given) or
  a number of times a day, as innate powers. An action with `max_targets` affects that many of its
  candidates, the ones `prefer` ranks highest (sleep by hit dice); one with
  `portions` resolves that many times, each portion on the target it would
  pick then, so missiles move on once their target falls.
- **Checks give outcome tiers.** A check is a roll, an optional bonus and a
  target, rolled high or under, with ordered tiers that read the roll and
  margin. That expresses natural-20 criticals, degrees of success by margin,
  percentile specials and fumbles, and 3d6 roll-under; `roll_count` gives
  dice-pool successes, `roll_pool` adds dice that roll again and ones that
  cancel successes, and `roll_explode` gives open-ended totals. Actions branch on tier names, not on hit or miss.
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
  include text, question/menu, combat, treasure, experience, give/take item, shop,
  temple, training, rest, NPC join/leave, set/test variable, teleport, a
  conditional branch, chain-to, and end adventure. Each event names its
  successors through outcome branches (`onYes`, `onWin`, `onFlee` …). Any
  branch can be guarded by an expression.
- **Experience** comes from felled monsters and experience events, shared
  among the survivors (or the whole party, as the ruleset says). A level that
  needs no choice is taken at once; one that does (a feat, a boost, the class
  of the next level) waits until the player takes it with its choices. A
  dual-classed character may call on its former class before the new one
  passes it (`former <member> on`), and earns no more experience that
  adventure.
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
| `goldbox eval <expr> --module … [--context …]` | Evaluate an expression or check, for example a level-5 fighter's THAC0 or a saving throw against a given spell; a context creature can be a saved character (`{"self": "@brom.json"}`). |
| `goldbox character new\|level …` | Create or advance a character under a ruleset and print the derived sheet. |
| `goldbox map render <area>` | Print an area as text: edge walls, doors, triggers and entry points. |
| `goldbox sim combat --encounter … --party … --seed N [--runs K]` | Run a headless combat, or K of them, and report outcomes and distributions. |
| `goldbox play --campaign … --seed N [--script file]` | Play from a command script or stdin and emit a transcript. |
| `goldbox module pack <path> [--output <file> \| --install]` | Validate a module and export it as an independent content container (through the Engine's `rusty pack-content`). |

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
- **Presentation.** The Engine renderer draws everything from asset-module
  art, and the art is always flat images so a campaign can bring its own
  easily:
  - Navigation is real 3D: each area becomes one generated mesh textured
    from its wall set (the textures are effectively sprites on geometry),
    with a perspective camera at the party. A cell's backdrop shows as a
    picture over the view window.
  - Everything else is a billboard sprite: props such as chests and pillars,
    and monsters, standing in the 3D view, and every combatant in combat. A
    cell's `prop` names a sprite and, optionally, a campaign condition that
    hides it (an opened chest, a defeated guard).
    Combat uses side-view sprites under a perspective camera looking down at
    the field, the way Gold Box showed side-view figures in a pseudo-iso view.
  - Sprites are drawn facing one way and flipped horizontally for the other.
    There are no directional sprite sets and no meshes or mesh animation. A
    sprite may be an animated strip of frames.
  - The DOM draws text, menus and panels around the view. Module images can't
    be DOM images (bundles give no URLs), so pictures are renderer sprites.
- **Persistence.** Saves go through `ProductStateStore` in one Engine
  persistence scope, as the same JSON `goldbox play --save` writes.
- **Releases.** `rusty build --pack <dir> --compress` ships the product with
  its content as one zstd-compressed `.rpak`.
- **Independent module export.** `goldbox module pack <module-dir>` validates
  a module with its requirements, then packs it with the pinned pair's
  `rusty pack-content --compress` into `<id>-<version>.rpak`; `--install`
  writes it into the module library (`$GOLDBOX_MODULE_LIBRARY`, else
  `$XDG_DATA_HOME/rusty-goldbox/modules`, else
  `~/.local/share/rusty-goldbox/modules`). An installed container opens at run
  time as an ordinary bundle with `ProductContentBundle.OpenContainer`, so
  ruleset, asset and campaign modules ship and install separately. The Game
  offers the modules in its library beside its own bundles; the CLI finds
  `.rpak` files in its search directories and takes one as a module path.
  A container and a source directory of the same module version with the same
  content are one module; with different content they are ambiguous.
  Replace an installed container by renaming a new file into place (`rusty
  pack-content` does), never by rewriting it while open. Don't write a local
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
