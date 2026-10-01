# Product architecture

> The product decides. The Engine guarantees.

This describes what exists. [design.md](design.md) describes the intended
design (Core, the `goldbox` CLI, modules).

```text
RustyGoldbox.Game (C#)
  -> Rusty.Engine safe SDK
  -> SDK-generated bind entry point and ABI
  -> packaged Rust host, input, UI transport and browser shell
  -> DOM companion
```

## Owners

| Path or service | Responsibility |
| --- | --- |
| `src/RustyGoldbox.Game/RustyGoldboxProduct.cs` | Lifecycle callbacks and the status projection |
| `src/RustyGoldbox.Game/RustyGoldbox.Game.csproj` | Product entry, content/UI roots, projection identity and host defaults |
| `src/ui/main.js` | DOM status readout and projection subscription |
| `content/` | Product-authored data |
| Engine SDK/runtime | Generated interop, update/input admission, UI transport, host, renderer and browser shell |

## Lifecycle and data flow

The installed runtime loads the product assembly through its SDK-generated bind
entry point and constructs it with `ProductCreateContext`. The product opens
its UI stream through `IEngineContext.Ui`.

The product uses Engine's default `demand` lifecycle: updates run when there
is input or work, not on a fixed clock, which suits a turn-based game. On
`Start` and `Restart` it publishes a `rusty.goldbox.status` projection with a
`status` string. The DOM displays it and holds no state. No input intents are
declared yet.

## Build and host

`Directory.Build.props` pins one immutable SDK/runtime pair. The `rusty`
command installs it into its shared cache (`rusty install`), supplies its
package source to restores, and runs the product on its runtime (`rusty dev`,
which owns staging, watching, worker replacement and serving). `rusty build`
stages CoreCLR through `StageRustyEngineCoreClrProduct`; `rusty build --aot`
runs `VerifyRustyEngineAot` for explicit fidelity/release checks. Generated
bindings and the bind entry point are ignored output. The `engine-pair`
workflow advances the pin only after the product builds and serves on the new
pair.

Before adding a mechanism, check the installed safe SDK and the owners above.
A missing Engine capability is an upstream request, not another local host,
transport, scheduler or renderer.
