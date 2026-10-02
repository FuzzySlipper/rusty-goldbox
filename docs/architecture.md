# Product architecture

> The product decides. The Engine guarantees.

This describes what exists. [design.md](design.md) describes the intended
design (Core, the `goldbox` CLI, modules).

```text
modules/<id>/module.json + definition files
  -> RustyGoldbox.Core (load, resolve, check, evaluate; dice through Engine Random)
  -> RustyGoldbox.Cli (`goldbox`: parse arguments, host Engine services, print text or --json)

modules/<id>/ staged as Engine content bundles (or packed as containers)
  -> RustyGoldbox.Game (C#: ModuleLibrary -> Core; GameSession; SessionProjection)
  -> Rusty.Engine safe SDK
  -> SDK-generated bind entry point and ABI
  -> packaged Rust host, input, persistence, UI transport and browser shell
  -> DOM companion
```

## Owners

| Path or service | Responsibility |
| --- | --- |
| `src/RustyGoldbox.Core/Modules/ManifestReader.cs` | Reading and checking one `module.json`; the manifest field list |
| `src/RustyGoldbox.Core/Modules/ModuleSearchPaths.cs` | Search directories from `--modules` and `goldbox.json` |
| `src/RustyGoldbox.Core/Modules/ModuleResolver.cs` | Version selection, load order, cycles and kind rules |
| `src/RustyGoldbox.Core/Modules/ModuleSource.cs` | Where a module's files come from (`DirectoryModuleSource` on disk), and its content identity |
| `src/RustyGoldbox.Core/Modules/BundleModuleSource.cs` | A module in an Engine content bundle or container |
| `src/RustyGoldbox.Core/Modules/ModuleCatalog.cs` | The candidate modules a load picks requirements from: module directories and installed containers in the search directories |
| `src/RustyGoldbox.Core/Modules/PngImage.cs` | Checking an asset image is a PNG the renderer admits (8-bit RGBA) and reading its size |
| `src/RustyGoldbox.Core/Modules/InstalledModules.cs` | Installed module containers: the module library directory, file names, opening the containers in a directory |
| `src/RustyGoldbox.Core/Modules/DefinitionFiles.cs` | Finding and parsing a module's definition files |
| `src/RustyGoldbox.Core/Definitions/` | Definition types and their fields (`DefinitionTypes`, the `schema` source), and checking one file against its type (`DefinitionReader`) |
| `src/RustyGoldbox.Core/Expressions/` | Expression lexer, parser, values, functions and the language reference |
| `src/RustyGoldbox.Core/Rules/RuleSetBuilder.cs` | Cross-module checks: references and `requires` visibility (and asset kinds), stats, table rows, expression types, modifiers, asset images and wall-set frames |
| `src/RustyGoldbox.Core/Rules/ExpressionChecker.cs` | Expression type checking against a rule set |
| `src/RustyGoldbox.Core/Rules/RuleSet.cs` | The checked definitions of a module set, lookups and ad hoc compilation |
| `src/RustyGoldbox.Core/Rules/Evaluator.cs` | Evaluating expressions, stats with modifiers, and checks |
| `src/RustyGoldbox.Core/Rules/Creature.cs` | A creature an expression reads, and reading one from JSON |
| `src/RustyGoldbox.Core/Rules/DiceRoller.cs` | Dice from an Engine random stream, with a record of each roll |
| `src/RustyGoldbox.Core/Characters/Character.cs` | A character's state, and the creature view expressions read |
| `src/RustyGoldbox.Core/Characters/CharacterRules.cs` | Creating characters (attributes, race, class checks, level-1 hit points, gold), giving them a portrait asset, and gaining levels in one class or several, from character-creation, advancement, race and class data |
| `src/RustyGoldbox.Core/Characters/CharacterFile.cs` | The character JSON file, and refusing one made under a different module set |
| `src/RustyGoldbox.Core/Characters/CharacterSheet.cs` | A character's computed stats |
| `src/RustyGoldbox.Core/Definitions/OperationTypes.cs` | The operation vocabulary and its fields (the `schema operations` source) |
| `src/RustyGoldbox.Core/Combat/CombatField.cs` | A combat definition's field: cells, distance, neighbours and where each side starts |
| `src/RustyGoldbox.Core/Combat/CombatRunner.cs` | The fixed combat loop: surprise, initiative, turns, budgets, the choice policy, checks and operations, condition durations, defeat |
| `src/RustyGoldbox.Core/Combat/Combatant.cs` | A creature in a fight and the uses it can take (from class, monster and equipment data) |
| `src/RustyGoldbox.Core/Combat/CombatFact.cs` | What happened in a fight, in order: the transcript |
| `src/RustyGoldbox.Core/Campaigns/AreaMap.cs` | Area grids with edge walls: parsing the map text and drawing it |
| `src/RustyGoldbox.Core/Campaigns/CampaignState.cs` | Campaign play state: position, variables, fired triggers, pending menu, party, gold, inventory |
| `src/RustyGoldbox.Core/Campaigns/CampaignRunner.cs` | The play command surface: movement, triggers, event chains, fights, status |
| `src/RustyGoldbox.Core/Campaigns/SaveFile.cs` | Saves, and refusing one made under a different module set |
| `src/RustyGoldbox.Core/Campaigns/SaveSlots.cs` | Named save slots in Engine persistence, shared by the Game and `goldbox play --store` |
| `src/RustyGoldbox.Core/Definitions/EventTypes.cs` | The event kind vocabulary and its fields (the `schema events` source) |
| `src/RustyGoldbox.Core/Modules/ModuleLoader.cs` | Entry point: load a module and everything it requires into a `ModuleSet` |
| `src/RustyGoldbox.Core/Modules/ModuleScaffold.cs` | Writing a new module's starting manifest |
| `src/RustyGoldbox.Cli/` | `goldbox` argument parsing (`GoldboxCli`, `SchemaCommand`, `EvalCommand`, `InspectCommand`, `CharacterCommand`, `SimCommand`, `MapCommand`, `PlayCommand`, `PackCommand`), module loading with the Engine content service (`ModuleSets`), the Engine tool host with seeded dice (`EngineDice`), and text/JSON output (`Output`) |
| `src/RustyGoldbox.Game/RustyGoldboxProduct.cs` | Lifecycle callbacks, opening the module bundles, publishing the projection |
| `src/RustyGoldbox.Game/GameCommands.cs` | The input boundary: key intents and checked `goldbox.command.v1` payloads to session commands |
| `src/RustyGoldbox.Game/ModuleLibrary.cs` | The product's module bundles: listing campaigns and loading a module set from them |
| `src/RustyGoldbox.Game/GameSession.cs` | What the player is doing: screen, open module set, party being made, the running campaign, its log |
| `src/RustyGoldbox.Game/SessionProjection.cs` | The `rusty.goldbox.session` debug projection, and copying JSON into an Engine `UiValue` |
| `src/RustyGoldbox.Game/Presentation/AreaMesh.cs` | First-person geometry from an area map: inward-facing wall and door quads per cell edge, floors and ceilings, UVs from the wall set's frames |
| `src/RustyGoldbox.Game/Presentation/SceneView.cs` | The Engine scene in the view window: the first-person area (mesh, wall-set texture, camera at the party, backdrop sprite) or the combat scene, and admitting module art once per asset content |
| `src/RustyGoldbox.Game/FightReplay.cs` | Playing a resolved fight back fact by fact: track values, defeats and the acting combatant as each fact shows |
| `src/RustyGoldbox.Game/Presentation/CombatScene.cs` | The combat screen's scene: a floor field (the fight's combat field when it has one), side-view figures as spherical billboards (party left facing right, foes right facing left) standing on their cells and moving as the fight's moves show, attack animations for the actor, defeated figures leaving |
| `src/RustyGoldbox.Game/Presentation/SpriteArt.cs` | A sprite asset as an Engine sprite atlas (frames sized in cells, pivot on its anchor), figures billboarded around the vertical axis, animation playbacks, and the mirror scale that faces a figure the other way |
| `src/RustyGoldbox.Game/RustyGoldbox.Game.csproj` | Product entry, UI root, the module bundles, input intents and key mappings, projection identity |
| `src/ui/main.js` | DOM debug readout: renders the session projection and claims `goldbox.command` intents |
| `modules/` | First-party module sources; `goldbox.json` makes it the workspace search directory |
| `modules/classic/` | The first ruleset: first-edition rules from OGL content, with `PROVENANCE.md` and `LICENSE-OGL.txt` |
| `modules/placeholder-art/` | Placeholder assets (logical IDs to files) |
| `modules/sample-crypt/` | The sample campaign |
| `tests/RustyGoldbox.Tests/` | Core and CLI checks against temporary module directories, golden transcripts (`Golden/`), and original fixture rulesets shaped like other systems (`Fixtures/ascend`: ascending AC, criticals, standard and move budget, multiclass levels, feats, point buy; `Fixtures/degrees`: ancestry, heritage, background, boosts, proficiency ranks, three actions, four degrees of success, basic saves; `Fixtures/percentile`: d100 roll-under, specials, fumbles, active parry; `Fixtures/pools`: d10 success pools with rerolls, cancelling ones and botches, open-ended damage; `Fixtures/degrees-trial`: a one-room campaign on degrees for making characters in the Game; `Fixtures/ascend-trial`: a one-room campaign opening with a fight on ascend's field) |
| Engine SDK/runtime | Generated interop, update/input admission, UI transport, host, renderer and browser shell |

## Module loading

`ModuleLoader.Load` reads the root manifest, finds search directories, scans
them for candidate modules, resolves one version per required ID, orders the
set so each module follows what it requires, checks kind rules and then checks
every definition file. It reads files through a `ModuleSource`: module
directories, and Engine bundles and containers (`BundleModuleSource`). Given
the Engine content service (the CLI loads inside a tool-host call), the
search also opens every installed `.rpak` directly in a search directory, and
the module path may be one; the loader disposes what it opened once the set
is read. A container that doesn't open is named in "not found" messages. The
Game loads from its bundles and module library through the overload that
takes a root source and the available ones. Two copies of one module version
are ambiguous only when their content identities differ. Files are
read in path order either way, so both see definitions in the same order.

A module's content identity is the Engine's bundle identity: SHA-256 over each
file's relative path, a zero byte and that file's SHA-256, in path order. A
directory computes it on first use; a bundle or container already carries it
(`ProductContentBundle.Identity`). The same files give the same identity
however they are stored, so a save made from source directories loads in the
Game and the other way round. Every problem becomes a `ModuleDiagnostic` with a rule
ID, module, file, JSON path and a message that says how to fix it. Loading
reports all problems it can find rather than stopping at the first.

Definition files are read and checked against their type as each module
loads. Once every manifest and file is clean, `RuleSetBuilder` checks the set
as a whole and produces the `RuleSet`; a file-level error stops that stage so
it doesn't reappear as every reference to the broken definition. Expressions
are compiled once there; `Evaluator` only evaluates them.

Dice need Engine `Random`, whose calls are confined to a host callback.
Commands that roll (`eval`, `character new`, `character level`) create the
Engine tool host (`RustyEngineToolHost`, `EngineTestHost.Create()`), open a
stream seeded from `--seed` and do their work inside one `Call`.

## Combat

Tracks (hit points and any other pools) are ruleset definitions; `Creature`
holds each one's current value and, where the track doesn't compute its
maximum, the creature's own maximum. `Evaluator` resolves maxima, floors,
restore caps and starting values. Engine `Track` was considered and not used:
its maximum is a stored stat, while these are expressions.

`CombatRunner` runs one fight between sides of `Combatant`s. It first rolls
surprise, by side (reading the side's lead, the member `surprise_lead` ranks
highest, against the other side's) or by creature (each creature against
each enemy, so only some may be surprised). Each round it
rolls initiative (by side or by creature, from the combat definition), then
each creature's turn: start-of-turn condition operations, a skip if a
condition prevents actions, actions while the turn's budget (an expression,
worked out at the turn's start) lasts, then end-of-turn condition operations
and durations. The choice is a deterministic policy: the first use in the
creature's list it can afford and that has a target its `valid_target`
accepts, aimed at the candidate its `prefer` ranks highest, or by default the
enemy with the least left on the combat's track, the ally missing the most of
it, or the first fallen ally. An action's check gives a tier; the action's
operations for that tier run, then its `always` operations. Defeated
creatures take no turns; with the combat's `downed_conditions`, those that
had no turn in a round still run their conditions and count them down at the
round's end. Every change is a `CombatFact`, with the dice that produced it.

`goldbox sim combat` builds the sides from character files and an encounter
and runs the fight inside the Engine tool host; run k uses random scope
`goldbox.sim.<k>`, so any run repeats from its seed.

## Campaigns

`CampaignRunner` owns play: it takes one command at a time (`forward`,
`back`, `left`, `right`, `around`, `choose <n>`, `look`, `status`) and
returns `PlayFact`s. Moving checks the edge on that side; entering a cell
runs its event if the facing and once-only rules allow. An event chain runs
until a menu waits for a choice, the chain ends, or the adventure does. A
combat event fights the party against an encounter with `CombatRunner`, and
the party keeps the damage. `CampaignRunner` gives command *n* a dice stream
scoped `goldbox.play.<n>` from the campaign's seed, which a save records with
the command count; that is what makes a resumed save roll as an unbroken run.

A save is the JSON `SaveFile` writes. `goldbox play` keeps it in a file, or
with `--store <dir>` in a `SaveSlots` slot of that Engine persistence root;
the Game uses the same slots (scope `goldbox-saves`), so either loads the
other's saves.

## Characters

`CharacterRules` reads everything from data: the character-creation
definition's attribute order, roll and assignment, the race's adjustments,
limits and classes, the class's requirements and per-level `hp` expressions
(which build the track marked `from_levels`), and starting gold. Every other
track starts at its `start` or maximum. Levels come from the advancement
definition when there is one (experience by character, each level in a chosen
class) and otherwise from the class's own `xp` table; a class with no levels
left leaves the next level waiting for another class. Features fill the
grants of creation, the advancement and the class level in that order. A
character file stores its choices, its tracks' current values (and the level
track's maximum), each level's class, gain and features, and the module IDs
and versions it was made under. Reading it needs each of those
modules loaded at a compatible version; extra modules, such as a campaign that
requires the ruleset, are fine. Derived values are never stored; the sheet
computes them.

Candidate modules whose manifests have errors are skipped during resolution
and named in "not found" messages; their own errors are reported when they
are validated directly.

## Game lifecycle and data flow

The installed runtime loads the product assembly through its SDK-generated bind
entry point and constructs it with `ProductCreateContext`. The product opens
its UI stream through `IEngineContext.Ui`.

The product content root is `modules/`, and the project declares every
directory there as a `RustyEngineContentBundle` named after it. `ModuleLibrary`
opens all bundles, and every container in the module library
(`InstalledModules.DefaultDirectory`), for each listing or load and disposes
them afterwards, so under `rusty dev` an edited or newly installed module is
seen on the next open without a restart. A library container that doesn't
open becomes a note on the title screen.
A release (`rusty build --pack`) carries the same bundles inside its container.

The product uses the Engine's `realtime` lifecycle at 30 steps a second, so
sprite animations and fight playback move between inputs. Each step applies
input, lets the fight playback advance, advances animations, and republishes
only when something changed. Agent playtests can hold time with the Engine's
`action-driven` time mode. Input is the
`goldbox.command` intent with `goldbox.command.v1` payloads that the DOM
claims (`{ "action": ..., fields }`: refresh, open, roll, equip, drop, begin,
play, save, load, quit), plus digital intents mapped from keys: arrows and
WASD move and turn, X turns around, L looks, digits choose menu options.
Payloads come from the page, so `GameCommands` checks every field once and
turns a bad one into a note; a key intent acts on a key press or on a UI claim
with a positive value. Every rule decision is Core's. `GameSession` holds the screen (title, party, play), the loaded
module set, the party being rolled and the `CampaignRunner`. Opening a
campaign takes a `seed` from the payload or, without one, picks one from the
clock: roll *n* of character creation uses
scope `goldbox.character.<n>` and the game starts from the same seed, so a
session replays from it. Play commands are the same text commands `goldbox
play` scripts use.

`SceneView` draws play in a window at the top left of the screen
(`SceneView.Window`; the Engine measures camera viewports and sprite
placement from the lower left, and a sprite's placement is within the
camera's viewport). The area becomes one generated mesh: each wall, door or
secret door on a cell edge is a quad facing into that cell (back faces are not
drawn), and each cell has a floor and a ceiling, textured from the area's
wall set frames or a plain material. The mesh is rebuilt only when the area
(or its module's content) changes. The camera stands at the party's cell
centre, half a cell up, at the facing's yaw (north is the Engine's zero yaw).
When the party's cell has a backdrop, it shows over the view as a sprite
fitted to the window. A cell's prop stands at the cell's centre as a
cylindrical billboard playing its idle animation; while its `hidden`
condition holds (`CampaignRunner.IsTrue` against the campaign variables) it is
published invisible. The area mesh and props are released only after a
publish has stopped showing them.

Sprites stand in the scene as cylindrical billboards (`SpriteArt`): the atlas
frames carry their world size (a world-sized sprite's quad is its frame
size), and the pivot is the anchor pixel's bottom edge. Art faces one way, and
a negative X scale on the published transform mirrors a figure to face the
other; the Engine refuses mirrored atlas UVs. Playback advances only during
an Engine update; in the `demand` lifecycle steps come only on input, so
animated sprites need the `realtime` lifecycle. Figures
are unpublished before their atlas and texture are released. Textures are admitted from the module's bundle or
container once per asset content. The Engine's default lights light the
scene.

When a play command's facts include a fight (`FightFact`, which carries each
combatant's side, monster or class, and start on the combat's track), the
session switches to the combat screen and a `FightReplay` shows its facts one
beat at a time. The scene draws each combatant with the sprite its monster or
class has a `figure` for, under a camera straight on and 30 degrees down;
figures are spherical billboards so they stay upright under that camera.
Continue (a button, Enter or Space) skips to the end, then returns to play.
Play commands wait until then.

Portraits and icons reach the projection as asset IDs (a party member's
`portrait`, a combatant's `icon`) but aren't drawn yet: the DOM can't show
bundle images, and viewport sprites draw in every composition view, so a
roster strip beside the view isn't possible on the pinned Engine
(rusty-engine #9129).

After each update that changed something, and on `Start` and `Restart`, the
product shows the scene and publishes `rusty.goldbox.session`: the screen, status, notes,
campaigns, the party, and in play the position, the player-view map, the
waiting menu and the latest log lines. The DOM renders it and holds no state.

## Build and host

`Directory.Build.props` holds the shared compiler settings and pins one
immutable SDK/runtime pair. The `rusty` command installs it into its shared
cache (`rusty install`), supplies its package source to restores, and runs the
product on its runtime (`rusty dev`, which owns staging, watching, worker
replacement and serving). `rusty build` stages CoreCLR through
`StageRustyEngineCoreClrProduct`; `rusty build --aot` runs
`VerifyRustyEngineAot` for explicit fidelity/release checks. Generated
bindings and the bind entry point are ignored output. The `engine-pair`
workflow advances the pin only after the product builds and serves on the new
pair.

Before adding a mechanism, check the installed safe SDK and the owners above.
A missing Engine capability is an upstream request, not another local host,
transport, scheduler or renderer.
