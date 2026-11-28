# Rusty Tales

An **offline, data-driven RPG toolkit, authoring workbench, and runtime** built on
[Rusty Engine](https://github.com/FuzzySlipper/rusty-engine) — inspired by
Unlimited Adventures / Dungeon Craft, Ultima, Wizardry, and classic 16-bit JRPGs.

Players run **campaign modules** built on **ruleset modules** and dressed by
**asset modules**, each authored, validated, and distributed independently as
Engine content containers (`.rpak`). Authors (human or AI agents) build
adventures as readable JSON + expressions, inspect and playtest them in the
local **Tales Workbench**, verify them headlessly with the `tales` CLI, and play
or host them in the desktop/browser runtime.

- **Mixed 2D & 3D exploration:** mix classic 3D first-person edge-wall dungeons
  with Ultima-style 2D overhead auto-tiled towns and overworlds (`tileset` +
  multi-layer `tilemap` supporting `4bit`, `wall_top`, `blob47`, and
  coordinate-seeded `variants`) inside the same campaign.
- **Three combat presentations & canonical spatial grids:** orthographic 3D
  `tactical` grid (Gold Box / SKALD style with click-to-preview pathfinding,
  click-to-target reticles, and floating combat text), eye-level `first_person`
  lineup (Wizardry / Dragon Quest style), and `side_view` formations (JRPG
  style). All map types (`AreaMap`, `ResolvedTilemap`, `CombatField`) share
  `IGridTopology` + `GridSearch`, supporting both tilemap-driven tactical
  obstacles/sight/costs and Ultima IV–style `"layout": "from_area"` projection
  from 3D dungeon corridors/doorways or 2D overhead slices.
- **Tales Workbench (`tales workbench`):** local Studio SPA for inspecting
  navigation flow, 2D auto-tiled maps, event/scene graphs, Art & Assets
  (tileset 3×3 auto-tile seam tester, animated sprites, wall sets, audio, UI
  skins), agent-readable review pins (`tales review`), an embedded **Puppets**
  tab (`puppet-studio`), and a browser-streamed **Playtest** tab with deep-jump
  and quick-pinning.
- **Modular genre starter kits & first-party showcases:** scaffold a validated
  workspace in one command (`tactical-dungeon`, `first-person-crawler`,
  `classic-jrpg`), or explore bundled showcases (`sample-crypt`,
  `tactical-expedition`, `wizardry-delve`, `crystal-keep`, `ultima-vale`).
- **Low-friction authoring & pure-.NET Core:** multi-definition JSON arrays,
  `extends` inheritance, `patch` overrides, compact `scene` dialogue graphs,
  scoped schema discovery (`tales schema --module`), standalone `puppet-studio`
  baked sheet export (`tales puppet`), and pure-.NET deterministic RNG/rules
  with zero Engine dependency in `RustyTales.Core`.

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
- **Exploration:** arrows or WASD move and turn in 3D first-person areas, or
  step cardinally (north/east/south/west) in 2D overhead tilemap areas; `X`
  turns around, `L` looks, digits choose menu options, `/` focuses the
  collapsible command bar, `F9` toggles the in-game Workbench bridge.
- **Combat:** supports `tactical`, `first_person`, and `side_view` modes, with
  click-to-preview / click-to-confirm tactical movement, click-to-target
  reticles with click-site error feedback, floating combat text over sprites and
  portraits, per-character manual or automatic control, and mid-battle
  save/resume.
- **Character Forge & Stable:** displays rolled scores alongside class/race
  requirements and persists ruleset-bound characters across campaigns with
  fail-open import.
- **Multiplayer:** host from the title screen's **Play together** panel and
  share the invite; the host runs Core, players vote on menus and control their
  own characters in combat.
- **Dev console:** under `rusty dev --live-debug`, `rusty-live-debug` exposes
  `tales.help`, `tales.campaigns`, `tales.start`, `tales.play`, `tales.goto`,
  `tales.tp`, `tales.event`, `tales.heal`, `tales.combat-view`, and `tales.quit`.

---

## Quick start (authoring & Workbench Studio)

The `tales` CLI is self-describing and every command supports `--json`.

### 1. Open Tales Workbench (visual Studio + streamed Playtest)

```bash
# Inspect any campaign or asset module in the local Studio UI (default http://127.0.0.1:4173/)
dotnet run --project src/RustyTales.Cli -- workbench modules/ultima-vale

# Optional: pass a puppet-studio character workspace for the embedded Puppets tab
dotnet run --project src/RustyTales.Cli -- workbench modules/ultima-vale --puppets ../puppet-studio/examples/poselock-trio
```

Workbench includes **Flow Overview**, **2D Area Maps** (auto-tiled canvas +
reachability), **Events & Scenes**, **Art & Assets** (including the Tileset
3×3 auto-tile seam tester), **Puppets** (embedded `puppet-studio serve` +
one-click sheet export), **Review Queue**, **Diagnostics**, and a live
browser-streamed **Playtest** tab (`rusty dev --output stream --live-debug`).

### 2. Scaffold from a genre starter kit

```bash
# List available genre kits (tactical-dungeon, first-person-crawler, classic-jrpg)
dotnet run --project src/RustyTales.Cli -- schema kits

# Scaffold a complete workspace (scoped ruleset, starter art, campaign, smoke script, kit guide)
dotnet run --project src/RustyTales.Cli -- workspace new my-tale --kit classic-jrpg --id my-tale --title "My Tale"

# Inspect only this campaign's active stats, actions, items, spells, monsters, and asset slots
dotnet run --project src/RustyTales.Cli -- schema --module my-tale/modules/my-tale
```

### 3. Validate, review, puppet-export, and playtest headlessly

```bash
# Validate and stage all authored modules in the workspace
dotnet run --project src/RustyTales.Cli -- workspace build my-tale

# Read human review pins & puppet spatial flags for agent workflows
dotnet run --project src/RustyTales.Cli -- review list modules/ultima-vale --json

# Export a rigged puppet-studio character as a tight-union multi-clip sprite sheet
dotnet run --project src/RustyTales.Cli -- puppet export-sheet ../puppet-studio/examples/poselock-trio/hero_sword \
  --module modules/ultima-vale --clips idle,attack,recoil:hit --height 160 --json

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

### 4. Pack, install, and distribute modules

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
dotnet run --project src/RustyTales.Cli -- schema tileset          # 2D tile palettes, auto-tiling & combat tile traits
dotnet run --project src/RustyTales.Cli -- schema scene            # compact multi-step NPC dialogue & hub graphs
dotnet run --project src/RustyTales.Cli -- schema patch            # override fields on a required module's definitions
dotnet run --project src/RustyTales.Cli -- schema events --json    # event kinds (shop, rest, temple, combat, check, ...)
dotnet run --project src/RustyTales.Cli -- schema media --json     # image, sheet, sampling, audio & slot rules
dotnet run --project src/RustyTales.Cli -- schema puppets --json   # puppet-studio workspace contract & export workflow
dotnet run --project src/RustyTales.Cli -- schema live-combat      # suspended manual combat script commands
dotnet run --project src/RustyTales.Cli -- authoring list --json   # embedded campaign-authoring workflow & templates
```

- **Grouped definition files:** any `.json` file in a module may hold a single
  definition object or a JSON array of definitions (`$[i].field` diagnostics).
- **Inheritance & overrides:** any non-patch definition may set `"extends"` to
  inherit and override fields from another visible definition of the same type
  (setting an optional field to `null` removes it); `extension` and `campaign`
  modules may override required modules' definitions with `"type": "patch"`.
- **2D Tilemaps & Ultima IV contextual combat:** `area` definitions support
  `"view": "overhead"` with multi-layer auto-tiled `tilemap`s (`4bit`,
  `wall_top`, `blob47`, `variants`), `encounter`s support both authored
  `tilemap` battlefields (driving passability, movement cost, and line of sight)
  and `"layout": "from_area"` to project surrounding 3D dungeon doorways or 2D
  overhead slices directly onto the tactical grid.
- **Compact scenes & dialogues:** `"type": "scene"` writes branching NPC
  conversations, stateful topics, skill checks, item grants, and service
  handoffs as one readable graph that lowers deterministically into validated
  events with reachability, menu-softlock, and loop checking.
- **Saves & slots:** saves use Core's `IStateStore` (`SaveSlots` and
  `CharacterStableStore`, backed by Engine persistence in `.runtime/persistence`
  under `rusty dev` and reachable from the CLI via `tales play --store`).
- **Additional docs:** [docs/authoring-workspaces.md](docs/authoring-workspaces.md),
  [docs/combat-authoring.md](docs/combat-authoring.md), and
  [docs/ui.md](docs/ui.md).

---

## Verification & release builds

```bash
dotnet test tests/RustyTales.Tests
dotnet run --project src/RustyTales.Cli -- module validate modules/ultima-vale
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
| `src/RustyTales.Core/` | Pure-.NET module format and loading, `patch`/`extends` composition, `scene` lowering, `Grids/` (`IGridTopology`, `GridSearch`, `AreaCombatProjector`), 2D `Tilemap`/`AutoTiler`, expressions, rule evaluation, deterministic RNG (`DeterministicRandom`), characters/stable, combat, campaigns, saves, authoring graph/review store, and genre starter kits |
| `src/RustyTales.Cli/` | The `tales` authoring, validation, Workbench Studio (`Workbench/`), `puppet-studio` bridge (`PuppetCommand`, `PuppetStudioSession`), review pin, simulation, playtest, and distribution CLI |
| `src/RustyTales.Game/` | Engine product: thin Engine adapters, module bundles, live-debug commands, input intents, save slots, 3D & 2D overhead exploration, multi-style combat scenes (`Presentation/`), and session projection |
| `src/ui/` | DOM companion panels (`main.js`, `panels/`, `screens/`, `layout.js`, `look.js`, `combatOverlay.js`, `workbench.js`) |
| `modules/` | First-party rulesets (`classic`, `fifth-srd`, `three-action`, `fate-condensed`, `scifi-2d6`, `universal-d100`, `wizardry-rules`, `jrpg-rules`), art packs (`placeholder-art`, `tactical-bestiaire`, `wizardry-art`, `jrpg-art`), and playable showcases (`sample-crypt`, `tactical-expedition`, `wizardry-delve`, `crystal-keep`, `ultima-vale`) |
| `tales.json` | Workspace module search directories |
| `tests/RustyTales.Tests/` | Core, grid/tilemap, puppet, Workbench, CLI, kit, scene, and golden transcript tests (`Golden/`, `Fixtures/`) |
| `Directory.Build.props` | Pinned Rusty Engine SDK/runtime version |
| `docs/` | [design.md](docs/design.md), [architecture.md](docs/architecture.md), [ui.md](docs/ui.md), [authoring-workspaces.md](docs/authoring-workspaces.md), [combat-authoring.md](docs/combat-authoring.md), [agent-review/](docs/agent-review/README.md) |

Read [AGENTS.md](AGENTS.md) before changing anything.
