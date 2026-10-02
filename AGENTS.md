# Rusty Goldbox agent guidance

Rusty Goldbox is a Gold Box-style RPG engine with authoring, in the spirit of
Unlimited Adventures / Dungeon Craft. Players run **campaign modules**, which
are built on **ruleset modules** and dressed by **asset modules**. Every module
is authored, validated and exported independently. It is a downstream Rusty
Engine product.

> The product decides. The Engine guarantees.

## Start here

Read [docs/design.md](docs/design.md) before changing the module format,
module identity and references, the expression language or operations, the
runtime command surface, saves, or the Engine boundary. Planned work, its
order and status live in the `rusty-goldbox` Den project; keep repository
docs durable and put plans and status there. [README.md](README.md) covers setup and `rusty` commands. Before
you change the Engine boundary, read the Engine's
[C# SDK guide](https://github.com/FuzzySlipper/rusty-engine/blob/main/docs/csharp-sdk.md)
and check what you need against the **pinned** package (its release notes and
API surface), not Engine source at another revision.

[docs/architecture.md](docs/architecture.md) records what exists; when you
add or move an owner, update it and `README.md` in the same change.

## Who the author is

For the near and middle term, the person authoring modules is an **agent
using the CLI**, not someone working in a visual editor. So:

- Every authoring capability ships as a `goldbox` CLI command before anything
  else. Commands accept `--json`. Errors name the module, file, JSON path and
  the rule that was broken, and a reader should be able to fix the problem
  from the error alone.
- The format must be discoverable from the tool. When you add a definition
  type, operation or expression function, `goldbox schema` must describe it,
  with an example.
- Behavior is checked by scripted headless runs (`goldbox play`,
  `goldbox sim`) and golden transcripts. Don't build visual or
  human-interactive UI unless a task explicitly asks for it.

## Ownership

| Path | Owns |
| --- | --- |
| `src/RustyGoldbox.Core/` | Module format and loading, expressions, operations, ruleset interpretation, characters, combat, campaign/event runtime and save data. No host or UI code. |
| `src/RustyGoldbox.Cli/` | `goldbox` commands. Thin: parse arguments, call Core, print results. No rules logic. |
| `src/RustyGoldbox.Game/` | The Engine product: module bundles, Engine update/input, persistence and projections over Core. |
| `src/ui/` | DOM companion. Observes projections and submits intents; it owns no game state. |
| `modules/<id>/` | First-party module sources, each with `module.json` and provenance. |
| `tests/` | Focused checks and golden transcripts. |

`Rusty.Engine` owns lifecycle and update admission, input, rendering and
resources, spatial queries, deterministic random, content bundles and
containers, persistence primitives, UI transport and host integration. Search
the safe SDK before adding a mechanism. The SDK generates the bind entry point
and interop under ignored `obj/`, and product code stays safe C#.

There is one Engine-admitted update path. Don't add another loop, clock,
scheduler, renderer or state authority. All randomness goes through Engine
`Random` with an explicit seed.

## Module rules

- Modules are data: JSON plus the expression language. No module carries or
  loads C# code. New primitive behavior (an operation, an expression function,
  a combat hook) goes into Core, and only when a real module needs it.
- A module references only the modules listed in its own `requires`, using
  `module:id`. Changing another module's definition takes an explicit `patch`.
  Asset references are logical IDs, never file paths.
- Rules belong in ruleset data, not in C#. If Core code mentions a specific
  class, spell, stat name or edition, that's a bug unless it's a fixture.
- Record provenance for any module content adapted from a published game or
  SRD, and use only open-licensed sources.
- The engine must be able to express rule systems the repository doesn't
  distribute. Prove a new Core capability with more than one ruleset shape,
  using original fixture rulesets in `tests/` shaped like other systems; never
  add copied commercial rules text or tables. UA/Dungeon Craft is a reference for
  features, not a source of code or formats.
- When a definition type changes, update the first-party modules and the
  golden transcripts that use it in the same change.

## Product style

Write ordinary readable C#: explicit composition, direct methods, and one
clear mutable owner per domain. Keep operations thin: read, decide, apply,
publish. Use nullable types, file-scoped namespaces, and `internal`/`sealed`
by default. Method bodies should be normal multi-line code, not dense
one-liners.

Avoid ceremony. Don't add any of the following without a concrete,
task-owned failure it prevents:

- revision/staleness fences, proposal/acceptance or "admission" layers,
  repeated hashing, whole-state rollback, or defensive re-validation of
  first-party state;
- hard numeric caps or "bounded" limits that aren't game rules;
- frameworks, generic buses, service locators, reflection discovery, or
  interfaces with one implementation;
- compatibility readers for formats that never shipped.

Validate untrusted input once, where it enters: module loading in Core, CLI
arguments, and save loading. After that, trust it. Prefer plain names (load,
check, apply) over vocabulary like admit, authority or fence.

## Engine dependencies and gaps

`Directory.Build.props` holds the exact SDK/runtime pin. `rusty install`
installs it, `rusty status` reports it, and `rusty update` moves it
deliberately: read the release notes it lists, then run the checks below.
Don't make an adjacent Engine checkout a build dependency, and don't modify
one as part of work here.

If a mechanism is missing, verify the safe API, name the blocked behavior and
upstream owner, and file or link one narrow Engine request when that's
authorized. Stop that slice and continue independent work. Track open gaps as
tasks in the `rusty-goldbox` Den project, not here.

Engine mechanisms this product relies on:

- **Engine services in the CLI.** `RustyGoldbox.Cli` sets
  `RustyEngineToolHost`, so `EngineTestHost.Create()` gives `goldbox` the
  product's in-process service set (no renderer, input or lifecycle). Seeded
  `Random` draws match the running product's. Don't substitute a local RNG.
- **Module bundles and containers.** The Game's content root is `modules/`,
  one `RustyEngineContentBundle` per module directory, opened with
  `Content.OpenBundle`. `rusty pack-content <module> --output <file>` packs
  one module; `ProductContentBundle.OpenContainer` opens an installed one as a
  bundle. Core reads either through `BundleModuleSource`, and a module's
  content identity is the Engine's bundle identity. Don't write a local
  container format, archive reader or second identity hash.
- **Persistence.** Saves are `SaveSlots` over `ProductStateStore` in the
  Engine persistence root; the CLI reaches the same slots through the tool
  host's `PersistenceRoot`. Don't write save files from the Game.

## Verification

Run what covers the change. The checks are:

```bash
dotnet test tests/RustyGoldbox.Tests
dotnet run --project src/RustyGoldbox.Cli -- module validate modules/<id>
rusty build --project src/RustyGoldbox.Game/RustyGoldbox.Game.csproj
```

Golden transcripts live in `tests/RustyGoldbox.Tests/Golden/`. After an
intended behaviour change, regenerate them with `GOLDBOX_UPDATE_GOLDEN=1 dotnet
test tests/RustyGoldbox.Tests`, read the diff, and commit it with the change.

`rusty dev --project src/RustyGoldbox.Game/RustyGoldbox.Game.csproj` is the
edit-run loop for the product. `rusty build … --aot` is an explicit
NativeAOT fidelity check, not a routine gate. Keep claims separate: a build,
a validated module, a passing transcript and a visible interaction each prove
something different. Add a check for a specific new behavior. Don't add broad
coverage gates.

## Review and work hygiene

Use [docs/agent-review/README.md](docs/agent-review/README.md). Every change
gets an Engine-reuse and an existing-product-reuse check. The runtime-trust
lane is the place to push back on ceremony.

Preserve unrelated edits. Keep generated output and installed artifacts
ignored. Don't reset, force-push or change adjacent repositories. Commit or
push only when asked or when the active task authorizes it. Report what
changed, the checks you ran and any concrete limitations.
