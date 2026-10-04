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
| `src/RustyGoldbox.Core/Definitions/` | Definition types and their fields (`DefinitionTypes`, the `schema` source), checking one file against its type (`DefinitionReader`), and media: what an asset is and which media each slot accepts (`Media`) |
| `src/RustyGoldbox.Core/Expressions/` | Expression lexer, parser, values, functions and the language reference |
| `src/RustyGoldbox.Core/Rules/RuleSetBuilder.cs` | Cross-module checks: references and `requires` visibility (and that an asset fits its media slot), stats, table rows, expression types, modifiers, asset images and wall-set frames |
| `src/RustyGoldbox.Core/Rules/ExpressionChecker.cs` | Expression type checking against a rule set |
| `src/RustyGoldbox.Core/Rules/RuleSet.cs` | The checked definitions of a module set, lookups and ad hoc compilation |
| `src/RustyGoldbox.Core/Rules/Evaluator.cs` | Evaluating expressions, stats with modifiers, and checks |
| `src/RustyGoldbox.Core/Rules/Creature.cs` | A creature an expression reads, and reading one from JSON |
| `src/RustyGoldbox.Core/Rules/DiceRoller.cs` | Dice from Engine Random, with a record of each roll; scoped streams for continuous commands and keyed draws for persisted live battles |
| `src/RustyGoldbox.Core/Characters/Character.cs` | A character's state, including the persisted term-career ledger, and the creature view expressions read |
| `src/RustyGoldbox.Core/Characters/CharacterRules.cs` and `LifepathRules.cs` | Creating characters (attributes, race, class checks, level-1 hit points, declared currency balances, staged profession/personal skill choices and data-driven term careers), giving them a portrait asset, applying milestones and skill improvements, and gaining levels in one class or several, from character-creation, lifepath, advancement, race and class data; lifepath throws use the caller's Engine-backed dice and apply table choices to the existing attributes, stat bonuses, balances and equipment |
| `src/RustyGoldbox.Core/Characters/CharacterFile.cs` | The character JSON file, including staged skill-choice progress and lifepath age, choices, rolls and results, and refusing one made under a different module set |
| `src/RustyGoldbox.Core/Characters/CharacterSheet.cs` | A character's computed stats |
| `src/RustyGoldbox.Core/Definitions/OperationTypes.cs` | The operation vocabulary and its fields (the `schema operations` source) |
| `src/RustyGoldbox.Core/Combat/CombatField.cs` | A combat definition's field with an encounter's terrain: grid or shared-zone cells, distance, neighbours, what can be entered and at what cost, line of sight, and where each side starts |
| `src/RustyGoldbox.Core/Combat/CombatRunner.cs` and its partials | The live combat owner and shared resolver: surprise and forced surprise, placement, rolled or elective initiative, turns, budgets, legal choices, commands, flee rules, checks and operations, condition durations and defeat; automatic runs drive this same owner |
| `src/RustyGoldbox.Core/Combat/LiveCombat.cs` | Combat command, observation, pending-decision and continuation data, including stable combatant references |
| `src/RustyGoldbox.Core/Definitions/CombatBehavior.cs` | Checked reusable behavior profiles: finite action-use sequences, guards, scores, targets and destination preferences |
| `src/RustyGoldbox.Core/Combat/CombatBehaviorController.cs` | Module-authored policy selection over the combat owner's legal choices |
| `src/RustyGoldbox.Core/Combat/Combatant.cs` | A creature in a fight and the uses it can take (from class, monster and equipment data) |
| `src/RustyGoldbox.Core/Combat/CombatFact.cs` | What happened in a fight, in order: the transcript |
| `src/RustyGoldbox.Core/Campaigns/AreaMap.cs` | Area grids with edge walls, doors and secret doors: parsing, canonical edge keys and drawing |
| `src/RustyGoldbox.Core/Campaigns/CampaignState.cs` | Campaign play state: position, campaign and per-area variables, discovered secret edges, opened doors, fired triggers, pending menu, shop or combat, party and inventory; characters own declared currency balances and equipment |
| `src/RustyGoldbox.Core/Campaigns/CampaignRunner.cs` and its partials | The play command surface: movement, secret search, locked-door opening, triggers, event chains, fights, combat start anchors and surprise overrides, flee routing, status and shops; trading changes the existing character balances, party inventory and equipment; `CurrencyLedger` owns pooled payments and splits |
| `src/RustyGoldbox.Core/Campaigns/CampaignRunner.Inventory.cs` | Give/take events and the existing party item stores used by removal and shop offers; `carried()` reads those items without owning another inventory |
| `src/RustyGoldbox.Core/Campaigns/PendingCombatState.cs` | The suspended combat event, participant definition/party references, initial presentation metadata and the combat owner's primitive continuation |
| `src/RustyGoldbox.Core/Campaigns/SaveFile.cs` | Saves, and refusing one made under a different module set |
| `src/RustyGoldbox.Core/Campaigns/SaveSlots.cs` | Named save slots in Engine persistence, shared by the Game and `goldbox play --store` |
| `src/RustyGoldbox.Core/Definitions/EventTypes.cs` | The event kind vocabulary and its fields (the `schema events` source), including combat placement, surprise and flee branches |
| `src/RustyGoldbox.Core/Modules/ModuleLoader.cs` | Entry point: load a module and everything it requires into a `ModuleSet` |
| `src/RustyGoldbox.Core/Modules/ModuleScaffold.cs` | Writing a new module's starting manifest |
| `src/RustyGoldbox.Cli/` | `goldbox` argument parsing (`GoldboxCli`, `SchemaCommand`, `EvalCommand`, `InspectCommand`, `CharacterCommand`, `SimCommand`, `MapCommand`, `PlayCommand`, `PackCommand`), module loading with the Engine content service (`ModuleSets`), the Engine tool host with seeded dice (`EngineDice`), and text/JSON output (`Output`) |
| `src/RustyGoldbox.Game/RustyGoldboxProduct.cs` | Lifecycle callbacks, opening the module bundles, publishing the projection |
| `src/RustyGoldbox.Game/PlayerSettings.cs` | The player's volumes, picked skin, interface scale and layout, kept between runs in their own persistence scope and loaded through the session's checks |
| `src/RustyGoldbox.Game/GameCommands.cs` | The input boundary: key intents and checked `goldbox.command.v1` payloads to session commands |
| `src/RustyGoldbox.Game/ModuleLibrary.cs` | The product's module bundles: listing campaigns with the extensions each may add, the skins of assets modules, and loading a module set from them |
| `src/RustyGoldbox.Game/GameSession.cs` | What the player is doing: screen, open module set, party being made, the running campaign, its log |
| `src/RustyGoldbox.Game/UiImages.cs` | Module images granted to the DOM panels as Engine UI images, one per asset content, by URL |
| `src/RustyGoldbox.Game/SessionProjection.cs` | The `rusty.goldbox.session` debug projection, including visible lifepath career/table choices and the resulting term ledger, and copying JSON into an Engine `UiValue` |
| `src/RustyGoldbox.Game/Presentation/AreaMesh.cs` | First-person geometry from an area map: inward-facing wall and door quads per cell edge, floors and ceilings, UVs from the wall set's frames |
| `src/RustyGoldbox.Game/Presentation/SceneView.cs` | The Engine scene in the view window: the first-person area (mesh, wall-set texture, camera at the party, backdrop sprite) or the combat scene, and admitting module art once per asset content |
| `src/RustyGoldbox.Game/FightReplay.cs` | Presentation of already committed fight facts by stable combatant ID: track values, defeats and the acting combatant as each fact shows |
| `src/RustyGoldbox.Game/Presentation/CombatScene.cs` | The combat screen's scene: a floor field (the fight's combat field when it has one), side-view figures as spherical billboards (party left facing right, foes right facing left) standing on their cells and moving as the fight's moves show, attack animations for the actor, defeated figures leaving, and the field's terrain: each cell drawn with the sprite a `figure` gives its terrain key, else a grey block (impassable) or a low brown slab (rough ground) |
| `src/RustyGoldbox.Game/Presentation/GameAudio.cs` | Event sounds and music through Engine audio: clips opened once per asset, one-shot sounds on the Sfx bus, one looping Music voice following the campaign's music, bus volumes |
| `src/RustyGoldbox.Game/Presentation/PictureArt.cs` | Any picture-slot asset over the view window: a pixel-sized atlas sprite fitted to the window, playing a sheet's first animation |
| `src/RustyGoldbox.Game/Presentation/PlaybackFrames.cs` | Advancing sprite playbacks and noticing frame changes, so the scene republishes its snapshot to show them |
| `src/RustyGoldbox.Game/Presentation/SpriteArt.cs` | A sheet used as a figure, as an Engine sprite atlas (frames sized in cells, pivot on its anchor), figures billboarded around the vertical axis, animation playbacks, and the mirror scale that faces a figure the other way |
| `src/RustyGoldbox.Game/RustyGoldbox.Game.csproj` | Product entry, UI root, the module bundles, input intents and key mappings, projection identity |
| `src/ui/main.js` | Mounts the panel frame, subscribes to the session projection, claims `goldbox.command` intents and keeps the one presentation-only choice (which overlay the player opened) |
| `src/ui/layout.js` and `look.js` | The arrangement for the window's shape and its ratios as CSS variables; the stylesheet and a skin's overrides |
| `src/ui/panels/` | One renderer per play panel: status line, log, portraits, map (foes in combat), controls, overlays (game menu, member sheet, shop, temple, trainer) |
| `src/ui/screens/` | The title and party-creation screens |
| `modules/` | First-party module sources; `goldbox.json` makes it the workspace search directory |
| `modules/classic/` | The first ruleset: first-edition rules from OGL content, with `PROVENANCE.md` and `LICENSE-OGL.txt` |
| `modules/fate-condensed/` | Fate Condensed's conflict rules from the CC BY SRD, with `PROVENANCE.md` and `LICENSE-CC-BY-3.0.txt`: skills, stress and consequences, no races or classes |
| `modules/universal-d100/` | Basic Roleplaying's d100 rules from Chaosium's ORC Content Document, with `PROVENANCE.md` (ORC notices) and `LICENSE-ORC.txt`: rolled characteristics, skills by profession, dodge and shield parry, armour, major wounds |
| `modules/scifi-2d6/` | 2D6 science-fiction rules from the Cepheus Engine SRD (OGL), with `PROVENANCE.md` and `LICENSE-OGL.txt`: data-driven prior-history careers in `lifepath/`, no hit points; damage comes off Endurance, Strength and Dexterity, and the modifiers fall with them |
| `modules/fifth-srd/` | Fifth edition rules from SRD 5.2.1 (CC BY 4.0), with `PROVENANCE.md` and `LICENSE-CC-BY-4.0.txt`: four classes to level 5, death saves, Opportunity Attacks, spells and SRD monsters on a field |
| `modules/three-action/` | Three-action d20 rules adapted from Paizo's ORC-licensed Player Core, GM Core, Monster Core and NPC Core, with `PROVENANCE.md` (ORC notices) and `LICENSE-ORC.txt`: four degrees of success, the multiple attack penalty, raised shields and Shield Block, dying and wounded, four classes to level 5 |
| `modules/placeholder-art/` | Placeholder assets (logical IDs to files) |
| `modules/sample-crypt/` | The sample campaign |
| `modules/tactical-bestiaire/` and `modules/tactical-expedition/` | Original enemies and reusable tactics, an allied warder and a playable gallery campaign supporting up to twelve members; each module validates and packs independently |
| `tests/RustyGoldbox.Tests/` | Core and CLI checks against temporary module directories, golden transcripts (`Golden/`), and original fixture rulesets shaped like other systems (`Fixtures/ascend`: ascending AC, criticals, standard and move budget, multiclass levels, feats, point buy; `Fixtures/degrees`: ancestry, heritage, background, boosts, proficiency ranks, three actions, four degrees of success, basic saves; `Fixtures/lifepath`: original term-career data shape; `Fixtures/percentile`: d100 roll-under, specials, fumbles, active parry; `Fixtures/pools`: d10 success pools with rerolls, cancelling ones and botches, open-ended damage; `Fixtures/degrees-trial`: a one-room campaign on degrees for making characters in the Game; `Fixtures/ascend-trial`: a one-room campaign opening with a fight on ascend's field) |
| Engine SDK/runtime | Generated interop, update/input admission, UI transport, host, renderer and browser shell |

The original `Fixtures/tactical-zones` extension adds a shared-zone,
three-action economy. `Fixtures/tactical-zones-trial` makes it a playable
campaign for the same title/party/combat flow; packaged integration checks
use those modules through Engine containers and save slots.

## Module loading

`ModuleLoader.Load` reads the root manifest, finds search directories, scans
them for candidate modules, resolves one version per required ID (then any
added extensions, `ModuleSet.Extensions`, and what they require), orders the
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

`CombatRunner` owns one live fight between sides of `Combatant`s. It deploys each
side on the combat field, using the side's default edge or an encounter
event's `[x, y]` anchor. A field in `mode: "zones"` permits several creatures
to occupy one cell, making their distance 0; the ordinary grid keeps one
occupant per cell, reserved across all sides. Insufficient passable cells are
an encounter diagnostic rather than overlapping figures. It first rolls surprise, by side (reading the side's lead,
the member `surprise_lead` ranks highest, against the other side's) or by
creature (each creature against each enemy, so only some may be surprised),
unless the event forces `party` or `monsters` surprise for its configured
number of rounds.

Each round normally rolls initiative (by side or by creature, from the combat
definition). An `initiative_mode: "elective"` definition uses deterministic
popcorn order instead: the first listing member acts first, then the previous
actor chooses the next unacted member by the optional `initiative_score`
expression (highest score, listing order for ties), recording an
`initiative_choice` fact. A creature's turn runs start-of-turn condition
operations, a flee rule when one matches, a skip if a condition prevents
actions, actions while the turn's budget (an expression, worked out at the
turn's start) lasts, then end-of-turn condition operations and durations. The
choice is a deterministic policy: the first use in the creature's list it can
afford and that has a target its `valid_target` accepts, aimed at the candidate
its `prefer` ranks highest, or by default the enemy with the least left on the
combat's track, the ally missing the most of it, or the first fallen ally.
Where a creature's uses carry a `score`, it scores every such option against
its target and takes the highest instead. An explicit `flee` operation has the
same escaped result as a matching flee rule. A side is fled when all its
members have escaped; the event receives that outcome through `on_flee`, and
may treat a round-limit draw as flee with `flee_on_draw`.

An action's check gives a tier; the action's operations for that tier run, then
its `always` operations. Defeated creatures take no turns; with the combat's
`downed_conditions`, they still run their conditions and count them down at
their place in the order, or at the round's end when they have none (initiative
rolled each round leaves them out). Reactions resolve where their triggers
happen: as a mover leaves a reach, before an action's check against the
reactor, after an operation wounds it, and after an enemy's operation fells an
ally. Every change is a `CombatFact`, with the dice that produced it and stable
combatant IDs beside display names.

The owner advances until a manual decision or the outcome. `Observe` copies
the current phase, actor, budgets, targets and legal choices without rolling.
`Submit` checks a typed action, movement, end-turn or optional-decision command
and resumes the same resolver. Rules decide costs, range, sight and targets;
controller preferences do not restrict a human's legal choices. A committed
random target limit and an optional reaction or post-roll choice retain their
actual rolls and suspended operation data. `Capture` and `Restore` carry that
continuation without repeating deployment, surprise or completed actions.
`Run` is the automatic driver of this owner.

Each character can retain a manual or automatic control preference in its
save data. A campaign applies that preference when the next fight starts;
otherwise an NPC's explicit authored default or the caller's party default
applies. Changing control during a fight changes the existing combatant and
does not restore spent budgets. Behavior profiles propose legal commands and
retain their committed plan state in the combat continuation. Optional CLI
traces record the actual proposals, their source paths and alternatives.

`goldbox sim combat` builds the sides from character files and an encounter
and runs the fight inside the Engine tool host; run k uses random scope
`goldbox.sim.<k>`, so any run repeats from its seed.

## Campaigns

`CampaignRunner` owns play: it takes one command at a time (`forward`,
`back`, `left`, `right`, `around`, `search [direction]`, `choose <n>`, `look`, `status`,
`level <member>`, `former <member> on|off`) and returns `PlayFact`s. Moving checks the edge on that side; entering a cell
runs its event if the facing and once-only rules allow. An event chain runs
until a menu or live combat waits for a choice, the chain ends, or the
adventure does. A combat event creates a `CombatRunner` and suspends the
chain. The pending event retains the combat continuation and participant
references; Game and CLI observe it or submit commands through
`CampaignRunner`. Initial figure positions come from that initialized combat
owner. Only its terminal result synchronizes the party, grants rewards and
selects the event's outcome chain. A combat event can select its combat definition,
anchor both sides, force the surprised side, and route a flee outcome through
`on_flee` (including a round-limit draw when `flee_on_draw` is true). Felled
monsters' experience, and an experience event's, go to the party through `CharacterRules.Award`: shared among the
survivors or the whole party as the advancement's `experience_to` says,
taking levels that need no choice at once and leaving the rest for the
`level` command, whose choices `CharacterRules.AddExperience` takes all or
nothing. `CampaignRunner` gives command *n* a dice stream
scoped `goldbox.play.<n>` from the campaign's seed, which a save records with
the command count; that is what makes a resumed save roll as an unbroken run.

A save is the JSON `SaveFile` writes. `goldbox play` keeps it in a file, or
with `--store <dir>` in a `SaveSlots` slot of that Engine persistence root;
the Game uses the same slots (scope `goldbox-saves`), so either loads the
other's saves. Pending combat saves include the active turn, budgets,
conditions, controller state and optional decision. Live battles use Engine
keyed random draws with a persisted scope and draw index, so inspection,
rejected choices and save/load do not restart the battle's random sequence.

## Characters

`CharacterRules` reads everything from data: the character-creation
definition's attribute order, roll and assignment, optional staged skill-point
budgets/base expressions, the race's adjustments, limits and classes, the
class's requirements and per-level `hp` expressions (which build the track
marked `from_levels`), and starting gold. A staged creation is a two-step
flow: the first command saves the actual rolled attributes and chosen
profession, then CLI or Game choices spend the evaluated profession and
personal budgets over the listed derived skills. Every other track starts at
its `start` or maximum. Levels come from the advancement
definition when there is one (experience by character, each level in a chosen
class) and otherwise from the class's own `xp` table; a class with no levels
left leaves the next level waiting for another class. Features fill the
grants of creation, the advancement and the class level in that order. A
character file stores its choices, its tracks' current values (and the level
track's maximum), each level's class, gain and features, staged creation's
evaluated choice values, and the module IDs and versions it was made under. Reading it needs each of those
modules loaded at a compatible version; extra modules, such as a campaign that
requires the ruleset, are fine. Ordinary derived values are never stored; the
sheet computes them, while staged creation keeps its evaluated first-stage
values so a later choice uses the same Engine rolls.

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
claims (`{ "action": ..., fields }`: refresh, open, roll, skills, equip, spells, memorise, drop,
begin, play, continue, save, load, quit, volume, skin, layout-config (the player's panel
proportions), ui-scale and view-aspect), plus digital intents mapped from keys: arrows and
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

The party screen exposes every available creation in `creations`. A staged
creation is rolled with `roll` first; the party member then carries its actual
attributes and `skillPoints` budgets/base values in the projection. The DOM
submits a second `skills` action with profession and personal allocations, and
`GameSession` sends that choice to `CharacterRules` before the character can
enter the campaign. The character file persists the pending first-stage values
and the committed allocation.

`SceneView` draws play and combat in the DOM's view panel: it anchors its
camera to `hero` (`CameraView.SetViewportAnchor`), the UI anchors the panel's
inside under the same name, and the Engine keeps the camera's views over that
element through resizes with no product code; the camera's own viewport (the
whole window) applies only until a page anchors it. The vertical field of view
holds, so a wider panel sees more to the sides. The combat camera frames the
field to the view's shape (`CombatScene.PoseFor`): straight on and 30 degrees
down, it stands as close as it can with every corner of the field, and
figures standing on it, inside 90% of the frame, aimed so the field sits in
the frame's middle. The view's aspect comes from the panels (`view-aspect`)
until the Engine reports an anchored view's rectangle (rusty-engine #9361). A sprite's placement (an event
picture's) is within the camera's viewport. The area becomes one generated mesh: each wall, door or
secret door on a cell edge is a quad facing into that cell (back faces are not
drawn), and each cell has a floor and a ceiling, textured from the area's
wall set's regions or a plain material. The mesh is rebuilt only when the area
(or its module's content) changes. The camera stands at the party's cell
centre, half a cell up, at the facing's yaw (north is the Engine's zero yaw).
A picture covers the view while there is one: the latest event's
(`CampaignState.Picture`, until the party moves), else the cell's backdrop.
`PictureArt` makes any picture-slot asset a pixel-sized atlas sprite fitted
to the window (an image as one frame, a sheet with every frame) and plays a
sheet's first animation from the start each time a different picture shows. A cell's prop stands at the cell's centre as a
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
animated sprites need the `realtime` lifecycle. The renderer shows a
playback's frame as of the latest published snapshot, so each update
`PlaybackFrames` advances the playbacks showing (picture, props, combat
figures) and the scene publishes the same snapshot again when a frame changed. Figures
are unpublished before their atlas and texture are released. Textures are admitted from the module's bundle or
container once per asset content. The Engine's default lights light the
scene.

When a campaign suspends at a fight, the session switches to the combat screen.
Its projection exposes Core's active combatant, remaining budgets, legal
actions and pending optional decision. The player submits commands to that
same combat owner; enemies and party members using automatic control advance
through its automatic driver. Party members wait for manual choices by default.
The scene draws each combatant with the sprite its monster or class has a
`figure` for, under a camera straight on and 30 degrees down, framed to the
view (above); figures are spherical billboards so they stay upright under that
camera. Presentation consumes committed facts. Continue (a button, Enter or
Space) skips their presentation and does not resolve a waiting choice. The
session returns to play after Core finishes the fight and its outcome chain.
Campaign movement commands wait until then; saves include the pending fight.

Portraits and icons reach the panels as Engine UI images: `UiImages` opens an
asset's PNG from its module bundle once (`Ui.OpenImage`) and keeps the image for
the product's life. The projection carries any panel picture as one media
object beside the asset ID (`{ url, width, height, frame }`: a party member's
`portraitPicture`, the portrait chooser's `picture`, a combatant's
`iconPicture`, with a sheet's frame size and first animation), and the UI's
one `picture` function draws every kind, one ticker stepping the animated ones. The panels never see image bytes.

Audio goes through Engine audio (`GameAudio`), which the product host plays
natively. Each update it emits the sounds the latest play facts brought
(`MediaFact`) once each on the Sfx bus, keeps one looping voice on the Music
bus for the campaign's current music (replaced when it changes, stopped on
quit), and applies the session's music and sound volumes to their buses. A
clip is released only once its one-shot sounds have finished, as the Engine's
realization facts report.

Skins: the session lists the skins of installed assets modules
(`ModuleLibrary.Skins`) and keeps the player's pick with the module set it
came from; the projection's `skin` is that pick, else the open campaign's
`skin`, with its pictures as media objects from its own set.

After each update that changed something, and on `Start` and `Restart`, the
product shows the scene and publishes `rusty.goldbox.session`: the screen, status, notes,
campaigns, the party, and in play the position, the player-view map, the
waiting menu, the latest log lines, the event picture (a media object) and the
music playing. The DOM renders it and holds no state.

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

Temples offer numbered services through `serve <service> <member>` and `leave`.
Service expressions read the chosen character as `self` and campaign variables;
`heal` names its track and `remove_condition` names a condition. The campaign
runner uses existing character tracks, conditions and pooled balances in the service's declared currency;
`CampaignRunner.Temple.cs` owns these commands and `TrackOperations` shares
healing with combat. Saves retain an open temple and Game projections expose
prices for each member.

An advancement may declare `training: { "cost": "self.level * 20", "currency": "gold", "days": "2" }`.
Experience still accumulates, but levels wait for `train <member>` at a training
event, with the same class, feature and boost choices as `level`. One payment
buys one level; a failure to choose or pay leaves the named balance and time unchanged.
`CampaignRunner.Training.cs` uses `CharacterRules` for advancement and the
existing character balances for fees. Campaign state stores fictional `ElapsedDays`
and the open trainer in saves; this is game time, independent of Engine clocks.
Rulesets without training retain their existing immediate advancement.

A `resting` ruleset definition gives the period (`rounds`, `hours` or `days`)
and per-period `restore` track/amount pairs. Rounds reference the ruleset's
`combat` definition for its existing `round_seconds`. A rest event selects it with `resting` and a positive `periods`.
`CampaignRunner.Rest.cs` advances fictional campaign days and applies shared
track healing for every uninterrupted period. Optional `wandering.when` rolls
once per attempted period: a hit advances that period's time, grants no recovery,
ends rest and runs `wandering.event` (a combat event and its usual outcome chains).
Only a completed rest restores the event's full `tracks`, prepares spells and
chains to `next`. Rest without a policy retains immediate restoration.

NPC definitions contain fixed `character` data. Create a character with the CLI,
then `goldbox character npc <file> --module <path> --id <id> --out <npc.json>`
(also `--json`). `NpcFile` reuses `CharacterFile` to validate the template once
at module loading, supplying module stamps from its declared dependencies.
`join` and `dismiss` events reference the NPC definition; successful events
chain to `next`, refusals to `on_refused` (or end the chain). They enforce the
campaign's party maximum/minimum and never remove player characters.
`CampaignRunner.Party.cs` moves the same NPC character between `Party` and
`AbsentNpcs`; rejoining preserves wounds, balances and gear. Saves keep both lists
and NPC identity, rejecting duplicate identities at the save boundary. The Game
roster observes the existing party, with no separate NPC runtime or state.
