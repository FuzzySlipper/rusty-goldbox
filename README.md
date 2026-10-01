# Rusty Template

A minimal Rusty Engine product: C# owns a counter, a DOM button sends an
`increment` intent, and Engine transports the counter projection to the UI.
The packaged Engine owns the host, input, update loop, and browser shell.

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
rusty dev --project src/RustyTemplate.Game/RustyTemplate.Game.csproj --port 8787
```

Open the URL printed by the host. The Increment button changes the counter.
`rusty dev` runs the pinned pair's runtime: CoreCLR loads the product, and
changes to declared C#, UI, or content inputs rebuild and reload it. See
`rusty dev --help` for `--bind-host`, `--live-debug`, and `--debugger`.

`Directory.Build.props` pins the exact SDK/runtime pair. `rusty install`
downloads it once into the shared Engine cache, and later builds and runs work
offline. No Engine source checkout is required.

To adopt the newest published pair deliberately:

```bash
rusty update
rusty build --project src/RustyTemplate.Game/RustyTemplate.Game.csproj
```

`rusty update` lists the release notes to read; include the changed
`Directory.Build.props` in the resulting source change. This repository's
`engine-pair` workflow does the same every six hours: it moves the pin only
after the product builds and serves on the new pair, and otherwise opens an
`engine-pair-update` issue with the build output and the notes to read. For an explicit
NativeAOT fidelity/release check:

```bash
rusty build --project src/RustyTemplate.Game/RustyTemplate.Game.csproj --aot
```

## Repository shape

| Path | Responsibility |
| --- | --- |
| `src/RustyTemplate.Game/` | Ordinary safe C# product, counter state, and product metadata |
| `src/ui/main.js` | DOM presentation and semantic input |
| `content/` | Product-authored content root |
| `Directory.Build.props` | Matched Engine SDK/runtime pin |
| `docs/architecture.md` | Current ownership and data flow |
| `docs/ui.md` | DOM companion contract |
| `docs/agent-review/` | Reusable review workflow and lane packets |

The SDK generates the product's bind entry point inside its ordinary build;
there is no composition project. The Engine runtime supplies the host and
browser shell. Product metadata, input intents, content/UI
roots, and projection identity live in the ordinary `.csproj`.

## Start a product from this template

1. Rename the C# directory/project, namespace, and entry type together, and use
   the new project path with `rusty dev` and `rusty build`.
2. Set the product ID/title and UI projection stream/contract in the project
   file. Keep the C# stream/contract constants aligned. Define semantic intents
   there and keep their C#/DOM callers aligned.
3. Replace the counter domain and DOM UI with the product's behavior. Add
   authored data under `content/` and load it through Engine services. Keep
   documentation outside `src/ui/`; every file there is staged as a web asset.
4. Customize `AGENTS.md` and the architecture owner map for the actual product.
   Add a Den project or donor contract only if the new project uses one.
5. Keep the generic review lanes, adding concrete owner pointers and relevant
   task-specific questions as described in [the review guide](docs/agent-review/README.md).

Read [AGENTS.md](AGENTS.md) before extending the product. Keep instructions
about current behavior and ownership; exact dependency identities belong in
configuration, and task status belongs in the task system.
