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

## Tests

```bash
dotnet test tests/RustyGoldbox.Tests
```

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
| `src/RustyGoldbox.Core/` | Module format and loading; later rules, combat and campaign runtime |
| `src/RustyGoldbox.Cli/` | The `goldbox` authoring CLI |
| `src/RustyGoldbox.Game/` | Engine product: lifecycle and projections |
| `src/ui/main.js` | DOM debug readout |
| `modules/` | First-party module sources |
| `goldbox.json` | Workspace: module search directories |
| `tests/RustyGoldbox.Tests/` | Core and CLI checks |
| `content/` | Product content root; module bundles are staged from here |
| `Directory.Build.props` | Engine SDK/runtime pin |
| `docs/design.md` | Design: module format, runtime, CLI, Engine boundary |
| `docs/architecture.md` | Current owners and data flow |
| `docs/ui.md` | DOM companion contract |
| `docs/agent-review/` | Review workflow and lane packets |

Read [AGENTS.md](AGENTS.md) before changing anything.
