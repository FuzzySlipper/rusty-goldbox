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
Combat uses a live tactical screen. Party members wait for player choices by
default: choose a movement path, action and target, then finish the turn when
ready. Eligible members can use automatic control instead. Animation controls
only advance presentation of committed results; they cannot choose a waiting
player action. Saves can retain a battle at a pending decision.
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

Retained campaign canon lives beside its editable content. The
[Blackapple Brugh source](campaigns/blackapple-brugh/README.md) records its
story, characters, source adaptation, and licence. Shared canon has one editor;
chapter and art authors use its stable IDs and handoff contracts. These
documents describe the story rather than holding runtime game state.
Its [art bible](campaigns/blackapple-brugh/art/ART_BIBLE.md) defines original
ink-and-wash references, subject continuity, paired-room geometry and intended
runtime slots. Exact prompts and provenance remain beside the editable art.

`goldbox` is the authoring CLI. From the repository root:

```bash
dotnet run --project src/RustyGoldbox.Cli -- --help
dotnet run --project src/RustyGoldbox.Cli -- module new ruleset my-rules
dotnet run --project src/RustyGoldbox.Cli -- module validate modules/my-rules
dotnet run --project src/RustyGoldbox.Cli -- module deps modules/my-rules --json
```

`goldbox.json` makes `modules/` the workspace search directory, so new modules
go there and required modules are found there. Every command accepts `--json`.

An editable authoring workspace keeps canon, reference and accepted art,
prompts, and scripts outside runtime module directories. Create or inspect it
through the CLI:

```bash
dotnet run --project src/RustyGoldbox.Cli -- workspace new my-campaign
dotnet run --project src/RustyGoldbox.Cli -- workspace inspect my-campaign --json
dotnet run --project src/RustyGoldbox.Cli -- workspace build my-campaign --json
dotnet run --project src/RustyGoldbox.Cli -- workspace export my-campaign --json
dotnet run --project src/RustyGoldbox.Cli -- schema workspace --json
```

The optional `authoring` object in `goldbox.json` lists each owned module source
directory and the generated staging/export locations. The existing top-level
`modules` array remains the dependency search path. Workspace inspection
reports both, so a copied workspace can be resumed from its files.
Build validates the explicit authored modules and replaces their generated
staging tree; export independently packs each staged module through the pinned
Engine packer. Canon, prompts and source art remain editable outside the
runtime modules. See [authoring-workspaces.md](docs/authoring-workspaces.md).

The installed CLI also carries a draft authoring kit. `goldbox authoring list
--json` discovers its resources, `goldbox authoring show workflow` prints the
workflow, and `goldbox authoring copy --all --out my-campaign/prompts --json`
copies the brief, canon, chapter, encounter, art, individual/batch image judges,
handoff and revision templates.
The kit points to `goldbox schema` for format details and works outside this
repository. Its broader campaign workflow remains a draft until the full
demonstration and fresh-agent trials have tested it.

The format is described by the tool itself, and rules can be tried against a
module:

```bash
dotnet run --project src/RustyGoldbox.Cli -- schema
dotnet run --project src/RustyGoldbox.Cli -- schema class
dotnet run --project src/RustyGoldbox.Cli -- schema item --json
dotnet run --project src/RustyGoldbox.Cli -- schema events --json
dotnet run --project src/RustyGoldbox.Cli -- module inspect modules/classic
dotnet run --project src/RustyGoldbox.Cli -- eval "self.thac0" --module modules/classic --context '{"self": {"class": "fighter", "level": 5}}'
dotnet run --project src/RustyGoldbox.Cli -- eval --check attack --module modules/classic --seed 7 --context '{"self": {"class": "fighter", "level": 5, "str": 17}, "target": {"monster": "ogre"}}'
```

Characters are JSON files made and advanced under a module set:

```bash
dotnet run --project src/RustyGoldbox.Cli -- character new --module modules/classic --class fighter --race dwarf --name Brom --seed 11 --out brom.json
dotnet run --project src/RustyGoldbox.Cli -- character new --module modules/fifth-srd --class fighter --race human --feature soldier,savage_attacker,defense --equipment longsword,chain_mail --out ada.json
dotnet run --project src/RustyGoldbox.Cli -- character level brom.json --module modules/classic --xp 5000 --seed 3
dotnet run --project src/RustyGoldbox.Cli -- character show brom.json --module modules/classic
dotnet run --project src/RustyGoldbox.Cli -- eval --check save_spell --module modules/classic --context '{"self": "@brom.json", "target": {"monster": "skeleton"}}'
dotnet run --project src/RustyGoldbox.Cli -- character new --module modules/sample-crypt --class fighter --race human --name Ada --portrait placeholder-art:fighter_portrait --out ada.json
dotnet run --project src/RustyGoldbox.Cli -- character new --module modules/universal-d100 --creation staged --feature staged_soldier --name Rook --seed 4 --out rook.json
dotnet run --project src/RustyGoldbox.Cli -- character skills rook.json --module modules/universal-d100 --skill sword=profession:100+personal:90,shield=profession:50,dodge=profession:40,brawl=profession:30,bow=profession:30
dotnet run --project src/RustyGoldbox.Cli -- character new --module modules/scifi-2d6 --name Vance --lifepath prior_history --career marine --terms 1 --skill-table service,service,service --benefit cash --seed 3 --out vance.json
dotnet run --project src/RustyGoldbox.Cli -- character milestone ruth.json --module modules/fate-condensed --raise fight --feature deadeye
dotnet run --project src/RustyGoldbox.Cli -- character improve rook.json --module modules/universal-d100 --seed 7
```

When a character-creation definition names a `lifepath`, `--career` chooses
the career for each term (one choice repeats with `--terms`),
`--skill-table` supplies the visible table choice for each actual configured
skill-die roll (one choice repeats when the policy asks for more rolls), and
`--benefit` chooses cash or material for each mustering-out roll (one choice
also repeats).
The saved character keeps its age, term choices, roll totals and results in
`career_terms`; `goldbox character show --json` and the Game party projection
expose that ledger.

Derived definitions may set `show_on_sheet: true`. The member projection then
evaluates and exposes that named value in `derived`; the member sheet renders
the same list. This is an authored display flag and uses the existing
evaluator, so adding a derived value does not require a named stat in C#.

Campaigns play from command scripts, and save and resume:

```bash
dotnet run --project src/RustyGoldbox.Cli -- map render entrance --module modules/sample-crypt
dotnet run --project src/RustyGoldbox.Cli -- play --campaign modules/sample-crypt --party ada.json,brom.json --seed 1 --script tests/RustyGoldbox.Tests/Fixtures/scripts/crypt.script --save game.json
dotnet run --project src/RustyGoldbox.Cli -- play --campaign modules/sample-crypt --load game.json --script more.script
```

For manual battle scripts, add `--combat-control manual`. `combat inspect`
prints stable actor/action/target IDs, budgets and legal paths; use those IDs
with `combat action`, `combat move`, `combat end-turn` and `combat decide`.
`combat auto-step` assists the current manual turn through Core without
changing the member's saved control preference.
Inspecting choices consumes no dice. A selected spell commits any random
resource price once, and saves retain it; an unaffordable committed price
leaves resources unchanged so the actor can choose another action.
`goldbox schema live-combat --json` describes the command surface. Saves retain
the current decision, and `--trace` explains authored AI choices without
changing the rolls. [Authoring tactical combat](docs/combat-authoring.md)
shows how to add reusable profiles and data-only enemies; the original
`tactical-expedition` campaign permits up to twelve members.

The sample crypt's outfitter is at `[1,2]` in the entrance. Shops list guarded
stock and carried gear with prices: `buy <n>`, `sell <n>` and `leave` work in
scripts and the Game's shop buttons. A stock entry can name a numeric campaign
or area variable holding a nonnegative whole scalar count; each successful buy
decrements it, while omitted stock is unlimited. Declared currency balances stay
on characters and purchases join party inventory; selling equipped gear removes
it from its wearer. Each item names its payment currency, and the ruleset
declares resale in `economy.sell_fraction` (`goldbox schema economy`). A shop's
optional `buying` policy requires a currency, defaults its fraction to the
ruleset `sell_fraction` (0.5 in classic and fifth-srd), and may name a numeric
cash `balance` or `max_value`; omitted balance means unlimited cash and omitted
maximum means no value limit. `goldbox schema events` describes these fields
and the input diagnostics identify `$.items[i].stock` or `$.buying.*`. Areas can mark secret
doors with `SS`; `search [direction]` uses the area's search check and saves
discovered edges. `DD` edges may be declared as locked doors with key, pick,
force or event mechanisms; `open`, `pick` and `force` use those declarations
and saves keep opened doors and an open shop.

Between conversations, services and fights, `equip <member> <item-id>` moves
one carried item onto that member; `unequip <member> <item-id>` returns one
equipped copy to party inventory. The member sheet's Equipment table offers
the same choices. Class and race restrictions still apply, and saves retain
both equipped and carried copies. A shop can override its buying rate and
use a numeric campaign or area variable for its remaining cash with `buying`;
`goldbox schema events` includes the policy and an example.

Carried consumables use `use <member> <item-id>` with a one-based active member
and a local or qualified item ID. One carried copy is consumed and the item's
existing scene operations run outside combat and pending menu, shop, temple or
training interactions; an equipped copy cannot be used. The item schema's
minimal form is:

```json
{ "type": "item", "id": "healing_draught", "name": "Healing draught", "kind": "consumable", "cost": 5, "currency": "gold", "weight": 1, "use": { "operations": [{ "op": "heal", "track": "hit_points", "amount": "1d8 + 1" }] } }
```

`use.duration_days` is a nonnegative expression evaluated once on use with
the selected member as `self`; only authored rest and training advance
fictional `ElapsedDays`. An applied condition stores its absolute expiry in
the saved character's `condition_expiry` map. Combat rounds, transient
condition values and turn hooks are rejected for this campaign-time use.
`goldbox schema item --json` prints the fields and example, and errors identify
paths such as `$.use.operations[0].track` and `$.use.duration_days`.

Campaign `give` and `take` events name an `item` and an optional positive
`count` (one by default). Giving adds carried copies. Taking removes carried
copies first, then equipped ones; it follows `on_refused` and removes nothing
if the party lacks the full count. Guards and menus can use
`carried(item.id == 'dagger') > 0` for possession or `carried(item.kind == 'gear')`
for a count. This includes the active party's equipment and excludes absent
NPCs. The sample crypt's altar consumes its offered dagger.

Scene `check` events roll a declared check for a numbered active member and
follow its success or failure branch. Scene `effect` events apply explicit
track damage/healing and durable condition changes through the existing
character state. Authors supply recovery and defeat routing; timed or
turn-hook conditions belong to combat. `party_size()` reads the live active
count in campaign expressions, so an authored lift or passage can respond to
recruitment and dismissal. See `goldbox schema events` and
`goldbox schema expressions` for the fields and examples.

`spell_reward` is the data event for a ruleset-selected lesson. It may name a
positive `member` or award every active member. For each member it uses the
ruleset's castable eligibility, keeps the highest level on an active class list,
then filters known spells and draws once among that level's unknown spells.
The selected definition is added to the ordinary known list, so casting,
preparation and save/load use it. No eligible spell emits a visible no-op fact
and consumes no random draw. Gate a one-time reward with the authored event
trigger or variable path; `spell_reward` has no generic once flag. Its minimal
authoring form is `{ "type": "event", "id": "scroll_reward", "kind": "spell_reward", "text": "A lesson takes hold.", "next": "road" }`.

For scripted runs, `--fail-on-refusal` converts a refusal into a diagnostic and
exit code 1. Otherwise refusals remain transcript facts, which keeps a script
readable while allowing authors to choose whether a refusal should fail a
headless check.

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
| `src/RustyGoldbox.Game/` | Engine product: module bundles and installed modules, input intents, save slots, the first-person and combat scenes (`Presentation/`), presentation of committed combat facts and the session projection over Core |
| `src/ui/` | DOM panels: `main.js` mounts the panel frame and claims intents; `panels/` and `screens/` render the projection, `layout.js` picks the arrangement, `look.js` holds the stylesheet and skins |
| `modules/` | First-party module sources: the `classic` ruleset, `placeholder-art` assets and the `sample-crypt` campaign. Also the Game's content root: each directory is a content bundle |
| `campaigns/` | Retained editable campaign source, including shared story canon and source attribution |
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

For scenes perceived differently by party members, author a `perception` event
with `scope`, `check`, `success_mode` and `failure_mode`, then a text event with
matching `views`. `goldbox schema events --json` describes both and includes
examples. `view <member>` in scripted play, or the member sheet's view control
in the Game, selects the member's text and picture over the shared area. Results
belong to the existing character and survive save/load and NPC dismissal.
Use `reset: true` on an expedition-entry event when the same scope must be
rolled again; ordinary same-scope room events preserve the result.

Illustrated image and sheet assets can declare `"sampling": "linear"`;
omitting it keeps nearest sampling for pixel art. `goldbox schema media --json`
lists the modes and examples. Engine scene textures and DOM pictures use the
same authored policy, with isolated sheet frames and named regions. Validation
identifies one-pixel linear crops that the pinned Engine cannot represent and
suggests nearest sampling or a wider crop.
