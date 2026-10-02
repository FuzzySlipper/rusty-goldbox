# Product architecture

> The product decides. The Engine guarantees.

This describes what exists. [design.md](design.md) describes the intended
design (Core, the `goldbox` CLI, modules).

```text
modules/<id>/module.json + definition files
  -> RustyGoldbox.Core (load, resolve, check)
  -> RustyGoldbox.Cli (`goldbox`: parse arguments, print text or --json)

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
| `src/RustyGoldbox.Core/Modules/DefinitionFiles.cs` | Definition file checks and the known definition types |
| `src/RustyGoldbox.Core/Modules/ModuleLoader.cs` | Entry point: load a module and everything it requires into a `ModuleSet` |
| `src/RustyGoldbox.Core/Modules/ModuleScaffold.cs` | Writing a new module's starting manifest |
| `src/RustyGoldbox.Cli/` | `goldbox` argument parsing (`GoldboxCli`) and text/JSON output (`Output`) |
| `src/RustyGoldbox.Game/RustyGoldboxProduct.cs` | Lifecycle callbacks and the status projection |
| `src/RustyGoldbox.Game/RustyGoldbox.Game.csproj` | Product entry, content/UI roots, projection identity and host defaults |
| `src/ui/main.js` | DOM status readout and projection subscription |
| `modules/` | First-party module sources; `goldbox.json` makes it the workspace search directory |
| `content/` | Product-authored data |
| `tests/RustyGoldbox.Tests/` | Core and CLI checks against temporary module directories |
| Engine SDK/runtime | Generated interop, update/input admission, UI transport, host, renderer and browser shell |

## Module loading

`ModuleLoader.Load` reads the root manifest, finds search directories, scans
them for candidate modules, resolves one version per required ID, orders the
set so each module follows what it requires, checks kind rules and then checks
every definition file. Every problem becomes a `ModuleDiagnostic` with a rule
ID, module, file, JSON path and a message that says how to fix it. Loading
reports all problems it can find rather than stopping at the first.

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
