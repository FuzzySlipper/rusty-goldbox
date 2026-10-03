# Rusty Goldbox

A Gold Box-style RPG engine with authoring, in the spirit of Unlimited
Adventures / Dungeon Craft, built as a downstream
[Rusty Engine](https://github.com/FuzzySlipper/rusty-engine) product. Players
run campaign modules built on ruleset modules and dressed by asset modules.
See [docs/design.md](docs/design.md) for the module format, runtime and CLI.
Planned work is tracked in the `rusty-goldbox` Den project.

## Setup

The supported runtime pair targets Linux x64. Install the .NET 10 SDK, `curl`
and `tar`. NativeAOT also needs the platform compiler/linker prerequisites
(Clang and zlib development headers on Linux). Get the Engine's `rusty`
command once:

```bash
curl -fsSL https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.sh | bash
```

Then, from this repository:

```bash
rusty status
rusty install
rusty dev --project src/RustyGoldbox.Game/RustyGoldbox.Game.csproj --port 8787
```

Open the URL printed by the host. `rusty dev` runs the pinned pair's runtime:
CoreCLR loads the product, and changes to C#, UI or content inputs rebuild and
reload it; module edits reload as content bundles without a restart. See
`rusty dev --help` for `--bind-host`, `--live-debug` and `--debugger`.

The first-person view draws in the top-left window and the panel sits beside it. Open a campaign, roll a party (and give it
equipment and spells), then play with the buttons, a typed command, or the keys: arrows
or WASD move and turn, X turns around, L looks, digits choose menu options.
A fight plays back on its own screen; Enter, Space or the button skips it and
then returns to the corridor.
Saves go to named slots in the Engine persistence root, which `rusty dev`
keeps in `.runtime/persistence`; `goldbox play --store .runtime/persistence`
loads and writes the same slots.

A release is one compressed container plus the managed files beside it, with
every module bundle inside:

```bash
rusty build --project src/RustyGoldbox.Game/RustyGoldbox.Game.csproj --pack release --compress
```

Modules also ship and install on their own, as Engine content containers.
`goldbox module pack` validates a module and packs it; `--install` puts it in
the Game's module library (`$GOLDBOX_MODULE_LIBRARY`, else
`$XDG_DATA_HOME/rusty-goldbox/modules`, else
`~/.local/share/rusty-goldbox/modules`), where the Game offers it beside its
own modules without a rebuild:

```bash
dotnet run --project src/RustyGoldbox.Cli -- module pack modules/sample-crypt --install
```

The CLI reads installed containers too: any `.rpak` in a search directory is
a candidate module, and a command's module path may be one.

`--pack` and `rusty pack-content` are commands of the pinned pair's own
`rusty` (`~/.cache/rusty-engine/pairs/<pin>/runtime-pack/bin/rusty`); an older
bootstrap `rusty` on `PATH` may not have them until it is refreshed with the
install script above. `goldbox module pack` uses the pair's copy when it is
installed.

## Authoring modules

`goldbox` is the authoring CLI. From the repository root:

```bash
dotnet run --project src/RustyGoldbox.Cli -- --help
dotnet run --project src/RustyGoldbox.Cli -- module new ruleset my-rules
dotnet run --project src/RustyGoldbox.Cli -- module validate modules/my-rules
dotnet run --project src/RustyGoldbox.Cli -- module deps modules/my-rules --json
```

`goldbox.json` makes `modules/` the workspace search directory, so new modules
go there and required modules are found there. Every command accepts `--json`.

The format is described by the tool itself, and rules can be tried against a
module:

```bash
dotnet run --project src/RustyGoldbox.Cli -- schema
dotnet run --project src/RustyGoldbox.Cli -- schema class
dotnet run --project src/RustyGoldbox.Cli -- module inspect modules/classic
dotnet run --project src/RustyGoldbox.Cli -- eval "self.thac0" --module modules/classic --context '{"self": {"class": "fighter", "level": 5}}'
dotnet run --project src/RustyGoldbox.Cli -- eval --check attack --module modules/classic --seed 7 --context '{"self": {"class": "fighter", "level": 5, "str": 17}, "target": {"monster": "ogre"}}'
```

Characters are JSON files made and advanced under a module set:

```bash
dotnet run --project src/RustyGoldbox.Cli -- character new --module modules/classic --class fighter --race dwarf --name Brom --seed 11 --out brom.json
dotnet run --project src/RustyGoldbox.Cli -- character level brom.json --module modules/classic --xp 5000 --seed 3
dotnet run --project src/RustyGoldbox.Cli -- character show brom.json --module modules/classic
dotnet run --project src/RustyGoldbox.Cli -- eval --check save_spell --module modules/classic --context '{"self": "@brom.json", "target": {"monster": "skeleton"}}'
dotnet run --project src/RustyGoldbox.Cli -- character new --module modules/sample-crypt --class fighter --race human --name Ada --portrait placeholder-art:fighter_portrait --out ada.json
dotnet run --project src/RustyGoldbox.Cli -- character milestone ruth.json --module modules/fate-condensed --raise fight --feature deadeye
dotnet run --project src/RustyGoldbox.Cli -- character improve rook.json --module modules/universal-d100 --seed 7
```

Campaigns play from command scripts, and save and resume:

```bash
dotnet run --project src/RustyGoldbox.Cli -- map render entrance --module modules/sample-crypt
dotnet run --project src/RustyGoldbox.Cli -- play --campaign modules/sample-crypt --party ada.json,brom.json --seed 1 --script tests/RustyGoldbox.Tests/Fixtures/scripts/crypt.script --save game.json
dotnet run --project src/RustyGoldbox.Cli -- play --campaign modules/sample-crypt --load game.json --script more.script
```

The sample crypt's outfitter is at `[1,2]` in the entrance. Shops list guarded
stock and carried gear with prices: `buy <n>`, `sell <n>` and `leave` work in
scripts and the Game's shop buttons. Declared currency balances stay on
characters and purchases join party inventory; selling equipped gear removes it
from its wearer. Each item names its payment currency, and the ruleset declares
resale in `economy.sell_fraction` (`goldbox schema economy`),
and `goldbox schema events` describes the shop format. Saves keep an open shop.

Campaign `give` and `take` events name an `item` and an optional positive
`count` (one by default). Giving adds carried copies. Taking removes carried
copies first, then equipped ones; it follows `on_refused` and removes nothing
if the party lacks the full count. Guards and menus can use
`carried(item.id == 'dagger') > 0` for possession or `carried(item.kind == 'gear')`
for a count. This includes the active party's equipment and excludes absent
NPCs. The sample crypt's altar consumes its offered dagger.

With `--store <dir>`, `--load` and `--save` name save slots in that Engine
persistence root instead of files, such as the Game's under `rusty dev`:

```bash
dotnet run --project src/RustyGoldbox.Cli -- play --campaign modules/sample-crypt --store .runtime/persistence --load slot-1 --script more.script --save slot-2
```

Fights run headless from a seed; one run prints the transcript, more print
distributions:

```bash
dotnet run --project src/RustyGoldbox.Cli -- sim combat --module modules/classic --party ada.json,brom.json --encounter crypt_guard --seed 3
dotnet run --project src/RustyGoldbox.Cli -- sim combat --module modules/classic --party ada.json,brom.json --encounter ogre --runs 200
```

Combat definitions can use the default grid or shared `field.mode: "zones"`
cells, where several creatures may share a zone and same-zone distance is
zero. Combat events can anchor each side with `party_start` and
`monsters_start`, force a side's opening surprise with `surprise`, and route a
fleeing side through `on_flee` (with `flee_on_draw` for round-limit draws).
Rulesets that use Fate-style popcorn order set
`initiative_mode: "elective"` and may provide an `initiative_score` expression;
the schema command documents these fields and the resulting transcript records
each actor's next-choice fact.

## Tests

```bash
dotnet test tests/RustyGoldbox.Tests
```

The `ci` workflow runs these tests, builds the Game and validates every
module under `modules/` on each push to `main` and each pull request.

## Engine pin

`Directory.Build.props` pins the exact SDK/runtime pair. `rusty install`
downloads it once into the shared Engine cache; later builds and runs work
offline. No Engine source checkout is required.

To adopt the newest published pair deliberately:

```bash
rusty update
rusty build --project src/RustyGoldbox.Game/RustyGoldbox.Game.csproj
```

`rusty update` lists the release notes to read; commit the changed
`Directory.Build.props` with any product changes they call for. The
`engine-pair` workflow does the same every six hours: it moves the pin only
after the product builds and serves on the new pair, and otherwise opens an
`engine-pair-update` issue with the build output and the notes to read.

For an explicit NativeAOT fidelity/release check:

```bash
rusty build --project src/RustyGoldbox.Game/RustyGoldbox.Game.csproj --aot
```

## Repository shape

| Path | Responsibility |
| --- | --- |
| `src/RustyGoldbox.Core/` | Module format and loading (from directories or Engine bundles), definition types, expressions, rule evaluation, characters, combat, campaigns and saves |
| `src/RustyGoldbox.Cli/` | The `goldbox` authoring CLI |
| `src/RustyGoldbox.Game/` | Engine product: module bundles and installed modules, input intents, save slots, the first-person and combat scenes (`Presentation/`), fight playback and the session projection over Core |
| `src/ui/main.js` | DOM debug readout and intent claims |
| `modules/` | First-party module sources: the `classic` ruleset, `placeholder-art` assets and the `sample-crypt` campaign. Also the Game's content root: each directory is a content bundle |
| `goldbox.json` | Workspace: module search directories |
| `tests/RustyGoldbox.Tests/` | Core and CLI checks, golden transcripts (`Golden/`) and original fixture rulesets (`Fixtures/`) |
| `Directory.Build.props` | Engine SDK/runtime pin |
| `docs/design.md` | Design: module format, runtime, CLI, Engine boundary |
| `docs/architecture.md` | Current owners and data flow |
| `docs/ui.md` | DOM companion contract |
| `docs/evidence/` | Screenshots that record what a presentation change looked like |
| `docs/agent-review/` | Review workflow and lane packets |

Read [AGENTS.md](AGENTS.md) before changing anything.

Temples offer numbered services through `serve <service> <member>` and `leave`.
Service expressions read the chosen character as `self` and campaign variables;
`heal` names its track and `remove_condition` names a condition. The campaign
runner uses existing character tracks, conditions and pooled balances in each service's declared currency;
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

`character level --trained` is an authoring assertion that training happened
outside the campaign, for importing or preparing a character file. In play,
only the paid `train` command gains a training-required level.

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
