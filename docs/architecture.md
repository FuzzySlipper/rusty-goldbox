# Product architecture

> The product decides. The Engine guarantees.

This describes what exists. [design.md](design.md) describes the intended
design (Core, the `goldbox` CLI, modules).

```text
modules/<id>/module.json + definition files
  -> RustyGoldbox.Core (load, resolve, check, evaluate; dice through Engine Random)
  -> RustyGoldbox.Cli (`goldbox`: parse arguments, host Engine services, print text or --json)

RustyGoldbox.Game (C#)
  -> Rusty.Engine safe SDK
  -> SDK-generated bind entry point and ABI
  -> packaged Rust host, input, UI transport and browser shell
  -> DOM companion
```

The Game does not use Core yet.

## Owners

| Path or service | Responsibility |
| --- | --- |
| `src/RustyGoldbox.Core/Modules/ManifestReader.cs` | Reading and checking one `module.json`; the manifest field list |
| `src/RustyGoldbox.Core/Modules/ModuleSearchPaths.cs` | Search directories from `--modules` and `goldbox.json` |
| `src/RustyGoldbox.Core/Modules/ModuleResolver.cs` | Version selection, load order, cycles and kind rules |
| `src/RustyGoldbox.Core/Modules/DefinitionFiles.cs` | Finding and parsing a module's definition files |
| `src/RustyGoldbox.Core/Definitions/` | Definition types and their fields (`DefinitionTypes`, the `schema` source), and checking one file against its type (`DefinitionReader`) |
| `src/RustyGoldbox.Core/Expressions/` | Expression lexer, parser, values, functions and the language reference |
| `src/RustyGoldbox.Core/Rules/RuleSetBuilder.cs` | Cross-module checks: references and `requires` visibility, stats, table rows, expression types, modifiers |
| `src/RustyGoldbox.Core/Rules/ExpressionChecker.cs` | Expression type checking against a rule set |
| `src/RustyGoldbox.Core/Rules/RuleSet.cs` | The checked definitions of a module set, lookups and ad hoc compilation |
| `src/RustyGoldbox.Core/Rules/Evaluator.cs` | Evaluating expressions, stats with modifiers, and checks |
| `src/RustyGoldbox.Core/Rules/Creature.cs` | A creature an expression reads, and reading one from JSON |
| `src/RustyGoldbox.Core/Rules/DiceRoller.cs` | Dice from an Engine random stream, with a record of each roll |
| `src/RustyGoldbox.Core/Characters/Character.cs` | A character's state, and the creature view expressions read |
| `src/RustyGoldbox.Core/Characters/CharacterRules.cs` | Creating characters (attributes, race, class checks, level-1 hit points, gold) and gaining levels, from character-creation, race and class data |
| `src/RustyGoldbox.Core/Characters/CharacterFile.cs` | The character JSON file, and refusing one made under a different module set |
| `src/RustyGoldbox.Core/Characters/CharacterSheet.cs` | A character's computed stats |
| `src/RustyGoldbox.Core/Definitions/OperationTypes.cs` | The operation vocabulary and its fields (the `schema operations` source) |
| `src/RustyGoldbox.Core/Combat/CombatRunner.cs` | The fixed combat loop: surprise, initiative, turns, budgets, the choice policy, checks and operations, condition durations, defeat |
| `src/RustyGoldbox.Core/Combat/Combatant.cs` | A creature in a fight and the uses it can take (from class, monster and equipment data) |
| `src/RustyGoldbox.Core/Combat/CombatFact.cs` | What happened in a fight, in order: the transcript |
| `src/RustyGoldbox.Core/Campaigns/AreaMap.cs` | Area grids with edge walls: parsing the map text and drawing it |
| `src/RustyGoldbox.Core/Campaigns/CampaignState.cs` | Campaign play state: position, variables, fired triggers, pending menu, party, gold, inventory |
| `src/RustyGoldbox.Core/Campaigns/CampaignRunner.cs` | The play command surface: movement, triggers, event chains, fights, status |
| `src/RustyGoldbox.Core/Campaigns/SaveFile.cs`, `ModuleIdentity.cs` | Saves, and refusing one made under a different module set |
| `src/RustyGoldbox.Core/Definitions/EventTypes.cs` | The event kind vocabulary and its fields (the `schema events` source) |
| `src/RustyGoldbox.Core/Modules/ModuleLoader.cs` | Entry point: load a module and everything it requires into a `ModuleSet` |
| `src/RustyGoldbox.Core/Modules/ModuleScaffold.cs` | Writing a new module's starting manifest |
| `src/RustyGoldbox.Cli/` | `goldbox` argument parsing (`GoldboxCli`, `SchemaCommand`, `EvalCommand`, `InspectCommand`, `CharacterCommand`, `SimCommand`, `MapCommand`, `PlayCommand`), the Engine tool host with seeded dice (`EngineDice`), and text/JSON output (`Output`) |
| `src/RustyGoldbox.Game/RustyGoldboxProduct.cs` | Lifecycle callbacks and the status projection |
| `src/RustyGoldbox.Game/RustyGoldbox.Game.csproj` | Product entry, content/UI roots, projection identity and host defaults |
| `src/ui/main.js` | DOM status readout and projection subscription |
| `modules/` | First-party module sources; `goldbox.json` makes it the workspace search directory |
| `modules/classic/` | The first ruleset: first-edition rules from OGL content, with `PROVENANCE.md` and `LICENSE-OGL.txt` |
| `modules/placeholder-art/` | Placeholder assets (logical IDs to files) |
| `modules/sample-crypt/` | The sample campaign |
| `content/` | Product-authored data |
| `tests/RustyGoldbox.Tests/` | Core and CLI checks against temporary module directories, golden transcripts (`Golden/`), and original fixture rulesets shaped like other systems (`Fixtures/ascend`: ascending AC, criticals, standard and move budget; `Fixtures/percentile`: d100 roll-under, specials, fumbles, active parry) |
| Engine SDK/runtime | Generated interop, update/input admission, UI transport, host, renderer and browser shell |

## Module loading

`ModuleLoader.Load` reads the root manifest, finds search directories, scans
them for candidate modules, resolves one version per required ID, orders the
set so each module follows what it requires, checks kind rules and then checks
every definition file. Every problem becomes a `ModuleDiagnostic` with a rule
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

`CombatRunner` runs one fight between sides of `Combatant`s. Each round it
rolls initiative (by side or by creature, from the combat definition), then
each creature's turn: start-of-turn condition operations, a skip if a
condition prevents actions, then actions while the turn's budget lasts. The
choice is a simple deterministic policy: the first use in the creature's list
it can afford and that has a target, aimed at the enemy with the least left on the combat's track or the ally
missing the most of it. An action's check gives a tier; the
action's operations for that tier run, then its `always` operations. Timed
conditions count down at the end of the round. Every change is a
`CombatFact`, with the dice that produced it.

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
the party keeps the damage. `goldbox play` gives command *n* a dice stream
scoped `goldbox.play.<n>` from the campaign's seed, which a save records with
the command count; that is what makes a resumed save roll as an unbroken run.

## Characters

`CharacterRules` reads everything from data: the character-creation
definition's attribute order, roll and assignment, the race's adjustments,
limits and classes, the class's requirements and per-level `hp` expressions
(which build the track marked `from_levels`), and starting gold. Every other
track starts at its `start` or maximum. A character file stores its choices,
its tracks' current values (and the level track's maximum), its level gains,
and the module IDs and versions it was made under. Reading it needs each of those
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

The product uses Engine's default `demand` lifecycle: updates run when there
is input or work, not on a fixed clock, which suits a turn-based game. On
`Start` and `Restart` it publishes a `rusty.goldbox.status` projection with a
`status` string. The DOM displays it and holds no state. No input intents are
declared yet.

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
