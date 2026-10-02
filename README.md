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
reload it. See `rusty dev --help` for `--bind-host`, `--live-debug` and
`--debugger`.

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
```

Campaigns play from command scripts, and save and resume:

```bash
dotnet run --project src/RustyGoldbox.Cli -- map render entrance --module modules/sample-crypt
dotnet run --project src/RustyGoldbox.Cli -- play --campaign modules/sample-crypt --party ada.json,brom.json --seed 1 --script tests/RustyGoldbox.Tests/Fixtures/scripts/crypt.script
```

Fights run headless from a seed; one run prints the transcript, more print
distributions:

```bash
dotnet run --project src/RustyGoldbox.Cli -- sim combat --module modules/classic --party ada.json,brom.json --encounter crypt_guard --seed 3
dotnet run --project src/RustyGoldbox.Cli -- sim combat --module modules/classic --party ada.json,brom.json --encounter ogre --runs 200
```

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
| `src/RustyGoldbox.Core/` | Module format and loading, definition types, expressions and rule evaluation; later characters, combat and campaigns |
| `src/RustyGoldbox.Cli/` | The `goldbox` authoring CLI |
| `src/RustyGoldbox.Game/` | Engine product: lifecycle and projections |
| `src/ui/main.js` | DOM debug readout |
| `modules/` | First-party module sources: the `classic` ruleset, `placeholder-art` assets and the `sample-crypt` campaign |
| `goldbox.json` | Workspace: module search directories |
| `tests/RustyGoldbox.Tests/` | Core and CLI checks, golden transcripts (`Golden/`) and original fixture rulesets (`Fixtures/`) |
| `content/` | Product content root; module bundles are staged from here |
| `Directory.Build.props` | Engine SDK/runtime pin |
| `docs/design.md` | Design: module format, runtime, CLI, Engine boundary |
| `docs/architecture.md` | Current owners and data flow |
| `docs/ui.md` | DOM companion contract |
| `docs/agent-review/` | Review workflow and lane packets |

Read [AGENTS.md](AGENTS.md) before changing anything.
