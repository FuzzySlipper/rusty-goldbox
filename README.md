# Rusty Tales

An **offline, data-driven RPG toolkit and runtime** built on
[Rusty Engine](https://github.com/FuzzySlipper/rusty-engine) — inspired by
Unlimited Adventures / Dungeon Craft, Wizardry, and classic 16-bit JRPGs.

Players run **campaign modules** built on **ruleset modules** and dressed by
**asset modules**, each authored, validated, and distributed independently as
Engine content containers (`.rpak`). Authors (human or AI agents) build
adventures as readable JSON + expressions, verify them headlessly with the
`tales` CLI, and play or host them in the desktop/browser runtime.

- **Three combat presentations out of the box:** angled 3D `tactical` grid
  (Gold Box style), eye-level `first_person` lineup (Wizardry / Dragon Quest
  style), and `side_view` formations (JRPG style), plus fixed `starting_party`
  or custom character creation.
- **Modular genre starter kits:** scaffold a complete, validated, playable
  workspace in one command (`tactical-dungeon`, `first-person-crawler`,
  `classic-jrpg`).
- **Low-friction authoring:** multi-definition JSON array files, `extends`
  inheritance, `patch` overrides, compact `scene` dialogue graphs with static
  reachability/softlock checking, and scoped schema discovery
  (`tales schema --module`).
- **Data-driven rules:** rulesets live in JSON + a statically type-checked
  expression language (shipping with open-licensed `classic`, `fifth-srd`,
  `three-action`, `fate-condensed`, `scifi-2d6`, and `universal-d100` rulesets,
  plus original genre kits).

See [docs/design.md](docs/design.md) for the module format and runtime contract,
and [docs/architecture.md](docs/architecture.md) for current code owners.

---

## Quick start (players & local dev)

Requires the .NET 10 SDK, `curl`, and `tar` on Linux x64 (Clang and zlib
development headers for NativeAOT). Install the Rusty Engine CLI once:

```bash
curl -fsSL https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.sh | bash
```

Then from this repository:

```bash
rusty status
rusty install
rusty dev --port 8787
```

Open the URL printed by `rusty dev`. `Directory.Build.props` sets
`RustyTales.Game` as the default project, so `rusty dev` and `rusty build` need
no `--project` flag. Module edits reload as content bundles without a restart.

### Playing & controls
- **Exploration:** arrows or WASD move and turn, `X` turns around, `L` looks,
  digits choose menu options, `/` focuses the collapsible command bar.
- **Combat:** supports `tactical`, `first_person`, and `side_view` modes, with
  per-character manual or automatic control and mid-battle save/resume.
- **Multiplayer:** host from the title screen's **Play together** panel and
  share the invite; the host runs Core, players vote on menus and control their
  own characters in combat.
- **Dev console:** under `rusty dev --live-debug`, `rusty-live-debug` exposes
  `tales.help`, `tales.campaigns`, `tales.start`, `tales.play`, `tales.goto`,
  `tales.tp`, `tales.event`, `tales.heal`, `tales.combat-view`, and `tales.quit`.

---

## Quick start (authoring a campaign)

The `tales` CLI is self-describing and every command supports `--json`.

### 1. Scaffold from a genre starter kit

```bash
# List available genre kits (tactical-dungeon, first-person-crawler, classic-jrpg)
dotnet run --project src/RustyTales.Cli -- schema kits

# Scaffold a complete workspace (scoped ruleset, starter art, campaign, smoke script, kit guide)
dotnet run --project src/RustyTales.Cli -- workspace new my-tale --kit classic-jrpg --id my-tale --title "My Tale"

# Inspect only this campaign's active stats, actions, items, spells, monsters, and asset slots
dotnet run --project src/RustyTales.Cli -- schema --module my-tale/modules/my-tale
```

### 2. Validate, playtest headlessly, and iterate

```bash
# Validate and stage all authored modules in the workspace
dotnet run --project src/RustyTales.Cli -- workspace build my-tale

# Run a seeded headless playtest (fails on any refused command)
dotnet run --project src/RustyTales.Cli -- play \
  --campaign my-tale/modules/my-tale \
  --modules my-tale/modules \
  --script my-tale/scripts/smoke.txt \
  --seed 1 --fail-on-refusal

# Simulate combat balance over 200 seeded runs
dotnet run --project src/RustyTales.Cli -- sim combat \
  --module modules/classic --party ada.json,brom.json --encounter ogre --runs 200
```

Play live against your working checkout without installing:

```bash
TALES_WORKSPACES=$PWD/my-tale rusty dev
```

### 3. Pack, install, and distribute modules

```bash
# Export independent .rpak containers for every module in the workspace
dotnet run --project src/RustyTales.Cli -- workspace export my-tale

# Install directly into the Game's module library (Title screen -> Refresh picks it up)
dotnet run --project src/RustyTales.Cli -- workspace install my-tale

# Publish to GitHub Releases or fetch a published campaign with its dependencies
dotnet run --project src/RustyTales.Cli -- module release modules/my-campaign --repo alice/my-campaign
dotnet run --project src/RustyTales.Cli -- module get github:alice/my-campaign
dotnet run --project src/RustyTales.Cli -- module updates --install
```

The Game's title screen also includes a **Modules** browser to preview, install,
update, and remove published module repositories. Installed containers live in
`$TALES_MODULE_LIBRARY` (default `$XDG_DATA_HOME/rusty-tales/modules`, or
`~/.local/share/rusty-tales/modules`).

---

## Authoring building blocks

Discover exact schemas and examples directly from the CLI:

```bash
dotnet run --project src/RustyTales.Cli -- --help
dotnet run --project src/RustyTales.Cli -- schema                  # overview of all definition types & topics
dotnet run --project src/RustyTales.Cli -- schema scene            # compact multi-step NPC dialogue & hub graphs
dotnet run --project src/RustyTales.Cli -- schema patch            # override fields on a required module's definitions
dotnet run --project src/RustyTales.Cli -- schema events --json    # event kinds (shop, rest, temple, combat, check, ...)
dotnet run --project src/RustyTales.Cli -- schema media --json     # image, sheet, sampling, audio & slot rules
dotnet run --project src/RustyTales.Cli -- schema live-combat      # suspended manual combat script commands
dotnet run --project src/RustyTales.Cli -- authoring list --json   # embedded campaign-authoring workflow & templates
```

- **Grouped definition files:** any `.json` file in a module may hold a single
  definition object or a JSON array of definitions (`$[i].field` diagnostics).
- **Inheritance & overrides:** any non-patch definition may set `"extends"` to
  inherit and override fields from another visible definition of the same type
  (setting an optional field to `null` removes it); `extension` and `campaign`
  modules may override required modules' definitions with `"type": "patch"`.
- **Compact scenes & dialogues:** `"type": "scene"` writes branching NPC
  conversations, stateful topics, skill checks, item grants, and service
  handoffs as one readable graph that lowers deterministically into validated
  events with reachability, menu-softlock, and loop checking.
- **Saves & slots:** saves use Engine `SaveSlots` (`.runtime/persistence` under
  `rusty dev`; reachable from the CLI via `tales play --store .runtime/persistence`).
- **Additional docs:** [docs/authoring-workspaces.md](docs/authoring-workspaces.md),
  [docs/combat-authoring.md](docs/combat-authoring.md), and
  [docs/ui.md](docs/ui.md).

---

## Verification & release builds

```bash
dotnet test tests/RustyTales.Tests
dotnet run --project src/RustyTales.Cli -- module validate modules/sample-crypt
rusty build --project src/RustyTales.Game/RustyTales.Game.csproj
```

Release & NativeAOT fidelity builds:

```bash
rusty build --project src/RustyTales.Game/RustyTales.Game.csproj --pack release --compress
rusty build --project src/RustyTales.Game/RustyTales.Game.csproj --aot
```

To advance the pinned Rusty Engine SDK/runtime pair in `Directory.Build.props`:

```bash
rusty update
rusty build --project src/RustyTales.Game/RustyTales.Game.csproj
```

---

## Repository shape

| Path | Responsibility |
| --- | --- |
| `src/RustyTales.Core/` | Module format and loading, `patch`/`extends` composition, `scene` lowering, expressions, rule evaluation, characters, combat, campaigns, saves, and genre starter kits |
| `src/RustyTales.Cli/` | The `tales` authoring, validation, simulation, playtest, and distribution CLI |
| `src/RustyTales.Game/` | Engine product: module bundles, live-debug commands, input intents, save slots, 3D exploration & multi-style combat scenes (`Presentation/`), and session projection |
| `src/ui/` | DOM companion panels (`main.js`, `panels/`, `screens/`, `layout.js`, `look.js`) |
| `modules/` | First-party rulesets (`classic`, `fifth-srd`, `three-action`, `fate-condensed`, `scifi-2d6`, `universal-d100`), `placeholder-art`, `tactical-bestiaire`, `sample-crypt`, and `tactical-expedition` |
| `tales.json` | Workspace module search directories |
| `tests/RustyTales.Tests/` | Core, CLI, kit, scene, and golden transcript tests (`Golden/`, `Fixtures/`) |
| `Directory.Build.props` | Pinned Rusty Engine SDK/runtime version |
| `docs/` | [design.md](docs/design.md), [architecture.md](docs/architecture.md), [ui.md](docs/ui.md), [authoring-workspaces.md](docs/authoring-workspaces.md), [combat-authoring.md](docs/combat-authoring.md), [agent-review/](docs/agent-review/README.md) |

Read [AGENTS.md](AGENTS.md) before changing anything.
