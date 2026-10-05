# Worked example: Lantern at Lowwater

This is a complete, original, text-only campaign that can be rebuilt from this file. It demonstrates a tiny ruleset, an empty asset module, a campaign with a visible menu consequence, an ordinary character file, a saved waiting menu, a reload, and independent workspace exports. It uses the shipped `goldbox` CLI and a POSIX shell; `workspace export` invokes the pinned Engine pair's `rusty pack-content` tool. Run it with the published `goldbox` and a matching pinned packer already installed. If that pair is missing, install it from the configured Goldbox checkout with `rusty install --project <path-to-goldbox-checkout>` or from a supplied matching archive and its `.sha256` file with `rusty install --archive <pair.tar.gz>` before entering this standalone workspace.

The author-visible outcome is simple: Neri arrives at Lowwater quay, chooses whether to light the harbor lantern, and can resume that choice from a saved waiting menu. The choice writes `campaign.var.lantern_lit`; the light branch ends with three answering horns and the dark branch ends with the tide carrying the light away.

All names, prose, map data, IDs, numbers, and rules in this example are original. The ruleset contains no published rules text or tables. The asset module is intentionally empty because this example is text-only, so it has no missing image or audio dependency. If art is added later, put it in a separate assets module with `goldbox schema media --json`, add that module to the campaign `requires`, and reference logical asset IDs from scenes; do not add file paths to event data.

## 1. Create the workspace

Use a fresh directory. `goldbox workspace new` creates the editable roots and generated output roots; the manifest below then declares the three authored runtime modules.

```sh
set -eu
WORKSPACE="$(pwd)/lantern-at-lowwater"
goldbox workspace new "$WORKSPACE"
mkdir -p "$WORKSPACE/modules/lantern-rules/attributes" \
  "$WORKSPACE/modules/lantern-rules/derived" \
  "$WORKSPACE/modules/lantern-rules/creation" \
  "$WORKSPACE/modules/lantern-rules/currencies" \
  "$WORKSPACE/modules/lantern-art" \
  "$WORKSPACE/modules/tidehouse/areas" \
  "$WORKSPACE/modules/tidehouse/events" \
  "$WORKSPACE/modules/tidehouse/variables" \
  "$WORKSPACE/party" "$WORKSPACE/scripts" "$WORKSPACE/receipts"
```

Keep `canon`, `prompts`, and `art` as editable authoring material. The
three directories under `modules` are authored runtime module sources; `.goldbox/staged`
and `exports` are derived outputs. The scripts and receipts are the minimal
useful run record and do not become module source.

## 2. Write the complete source files

The following blocks are the full source of the example. They include each manifest, provenance file, ruleset definition, campaign content definition, and variable.

### `$WORKSPACE/goldbox.json`

```json
{
  "modules": [
    "modules"
  ],
  "authoring": {
    "modules": [
      "modules/lantern-rules",
      "modules/lantern-art",
      "modules/tidehouse"
    ],
    "staging": ".goldbox/staged",
    "exports": "exports"
  }
}
```

### `$WORKSPACE/modules/lantern-rules/module.json`

```json
{
  "format": 1,
  "id": "lantern-rules",
  "kind": "ruleset",
  "version": "0.1.0",
  "title": "Lantern rules",
  "requires": [],
  "provenance": "Original rules written for the Lantern at Lowwater worked example; no published rules text or tables."
}
```

### `$WORKSPACE/modules/lantern-rules/PROVENANCE.md`

```text
# Provenance: lantern-rules

All definitions are original to this worked example. No published rules text, table, or setting is copied.
```

### `$WORKSPACE/modules/lantern-rules/attributes/body.json`

```json
{
  "type": "attribute",
  "id": "body",
  "name": "Body",
  "min": 0,
  "max": 20,
  "default": 10
}
```

### `$WORKSPACE/modules/lantern-rules/attributes/mind.json`

```json
{
  "type": "attribute",
  "id": "mind",
  "name": "Mind",
  "min": 0,
  "max": 20,
  "default": 10
}
```

### `$WORKSPACE/modules/lantern-rules/derived/poise.json`

```json
{
  "type": "derived",
  "id": "poise",
  "name": "Poise",
  "value": "self.body + self.mind",
  "show_on_sheet": true
}
```

### `$WORKSPACE/modules/lantern-rules/currencies/marks.json`

```json
{
  "type": "currency",
  "id": "marks",
  "name": "Harbor marks"
}
```

### `$WORKSPACE/modules/lantern-rules/creation/standard.json`

```json
{
  "type": "character-creation",
  "id": "standard",
  "name": "Two-score standard",
  "default": true,
  "attributes": [
    "body",
    "mind"
  ],
  "method": "roll",
  "attribute_roll": "10",
  "assignment": "in-order"
}
```

### `$WORKSPACE/modules/lantern-art/module.json`

```json
{
  "format": 1,
  "id": "lantern-art",
  "kind": "assets",
  "version": "0.1.0",
  "title": "Lantern text-only assets",
  "requires": [],
  "provenance": "Original worked example intentionally uses no media; the empty asset module satisfies the campaign asset boundary without missing files."
}
```

### `$WORKSPACE/modules/lantern-art/PROVENANCE.md`

```text
# Provenance: lantern-art

This intentionally empty assets module supplies no media. The example uses text-only scenes and therefore has no art files to license or validate.
```

### `$WORKSPACE/modules/tidehouse/module.json`

```json
{
  "format": 1,
  "id": "tidehouse",
  "kind": "campaign",
  "version": "0.1.0",
  "title": "Lantern at Lowwater",
  "requires": [
    {
      "id": "lantern-rules",
      "version": "^0.1.0"
    },
    {
      "id": "lantern-art",
      "version": "^0.1.0"
    }
  ],
  "provenance": "Original compact campaign written for the Rusty Goldbox authoring worked example; all prose and outcomes are original."
}
```

### `$WORKSPACE/modules/tidehouse/PROVENANCE.md`

```text
# Provenance: tidehouse

The Lowwater setting, names, prose, map, event IDs, and variable are original to this worked example. It uses no adapted text, art, or audio.
```

### `$WORKSPACE/modules/tidehouse/campaign.json`

```json
{
  "type": "campaign",
  "id": "tidehouse",
  "name": "Lantern at Lowwater",
  "start": {
    "area": "quay",
    "entry": "start"
  },
  "party": {
    "min": 1,
    "max": 1
  },
  "intro": "arrival"
}
```

### `$WORKSPACE/modules/tidehouse/areas/quay.json`

```json
{
  "type": "area",
  "id": "quay",
  "name": "Lowwater quay",
  "map": [
    "+--+--+",
    "|     |",
    "+--+--+"
  ],
  "cells": [],
  "entries": {
    "start": {
      "at": [
        0,
        0
      ],
      "facing": "east"
    }
  }
}
```

### `$WORKSPACE/modules/tidehouse/variables/lantern_lit.json`

```json
{
  "type": "variable",
  "id": "lantern_lit",
  "value_type": "boolean",
  "initial": "false",
  "description": "Whether the quay lantern was lit."
}
```

### `$WORKSPACE/modules/tidehouse/events/arrival.json`

```json
{
  "type": "event",
  "id": "arrival",
  "kind": "text",
  "text": "At Lowwater, the harbor lantern is dark and the tide is turning.",
  "next": "arrival_choice"
}
```

### `$WORKSPACE/modules/tidehouse/events/arrival_choice.json`

```json
{
  "type": "event",
  "id": "arrival_choice",
  "kind": "menu",
  "text": "A bell rope hangs beside the lantern.",
  "options": [
    {
      "label": "Light the lantern",
      "next": "light_lantern"
    },
    {
      "label": "Leave it dark",
      "next": "leave_dark"
    }
  ]
}
```

### `$WORKSPACE/modules/tidehouse/events/light_lantern.json`

```json
{
  "type": "event",
  "id": "light_lantern",
  "kind": "set",
  "variable": "lantern_lit",
  "value": "true",
  "next": "lit"
}
```

### `$WORKSPACE/modules/tidehouse/events/lit.json`

```json
{
  "type": "event",
  "id": "lit",
  "kind": "end",
  "text": "The harbor answers with three small horns. The night route is open."
}
```

### `$WORKSPACE/modules/tidehouse/events/leave_dark.json`

```json
{
  "type": "event",
  "id": "leave_dark",
  "kind": "end",
  "text": "You leave the wick cold; the tide carries the last light away."
}
```

### Editable canon note

```sh
cat > "$WORKSPACE/canon/brief.md" <<'MD'
# Lantern at Lowwater

Original one-scene text campaign. Neri chooses whether to light the Lowwater quay lantern; the choice is recorded in campaign.var.lantern_lit.
MD

cat > "$WORKSPACE/prompts/README.md" <<'MD'
No generated art is used by this text-only example. If art is added later, keep prompts and source references here and keep logical asset IDs in module data.
MD

cat > "$WORKSPACE/art/references/README.md" <<'MD'
No image references are needed for this text-only example.
MD
```

## 3. Discover the format before checking the files

Run the shipped schema topics that cover this example and retain their structured outputs. The schema is the format authority; these notes do not replace it.

```sh
goldbox schema module --json > "$WORKSPACE/receipts/schema-module.json"
goldbox schema workspace --json > "$WORKSPACE/receipts/schema-workspace.json"
goldbox schema campaign --json > "$WORKSPACE/receipts/schema-campaign.json"
goldbox schema area --json > "$WORKSPACE/receipts/schema-area.json"
goldbox schema event --json > "$WORKSPACE/receipts/schema-event.json"
goldbox schema variable --json > "$WORKSPACE/receipts/schema-variable.json"
goldbox schema character-creation --json > "$WORKSPACE/receipts/schema-character-creation.json"
goldbox schema attribute --json > "$WORKSPACE/receipts/schema-attribute.json"
goldbox schema derived --json > "$WORKSPACE/receipts/schema-derived.json"
goldbox schema currency --json > "$WORKSPACE/receipts/schema-currency.json"
goldbox schema expressions --json > "$WORKSPACE/receipts/schema-expressions.json"
goldbox schema operations --json > "$WORKSPACE/receipts/schema-operations.json"
goldbox schema events --json > "$WORKSPACE/receipts/schema-events.json"
goldbox schema media --json > "$WORKSPACE/receipts/schema-media.json"
```

## 4. Validate, inspect, and create the party

Validate each authored module with its dependency directory. Inspecting the campaign shows the resolved definitions and stats; `module deps` records the dependency order.

```sh
goldbox module validate "$WORKSPACE/modules/lantern-rules" --modules "$WORKSPACE/modules" --json > "$WORKSPACE/receipts/validate-lantern-rules.json"
goldbox module validate "$WORKSPACE/modules/lantern-art" --modules "$WORKSPACE/modules" --json > "$WORKSPACE/receipts/validate-lantern-art.json"
goldbox module validate "$WORKSPACE/modules/tidehouse" --modules "$WORKSPACE/modules" --json > "$WORKSPACE/receipts/validate-tidehouse.json"
goldbox module deps "$WORKSPACE/modules/tidehouse" --modules "$WORKSPACE/modules" --json > "$WORKSPACE/receipts/deps-tidehouse.json"
goldbox module inspect "$WORKSPACE/modules/tidehouse" --modules "$WORKSPACE/modules" --json > "$WORKSPACE/receipts/inspect-tidehouse.json"
goldbox map render tidehouse:quay --module "$WORKSPACE/modules/tidehouse" --modules "$WORKSPACE/modules" --json > "$WORKSPACE/receipts/map-quay.json"

goldbox character new \
  --module "$WORKSPACE/modules/lantern-rules" \
  --name Neri \
  --attributes body=12,mind=9 \
  --seed 7 \
  --out "$WORKSPACE/party/neri.json" \
  --json > "$WORKSPACE/receipts/character-new.json"
goldbox character show "$WORKSPACE/party/neri.json" \
  --module "$WORKSPACE/modules/lantern-rules" \
  --json > "$WORKSPACE/receipts/character-show.json"
```

The expected generated party file is included here so a reader can see the handoff format. Normally it is produced by `character new`; do not hand-edit it before play.

### `$WORKSPACE/party/neri.json (generated)`

```json
{
  "format": 1,
  "name": "Neri",
  "modules": [
    {
      "id": "lantern-rules",
      "version": "0.1.0"
    }
  ],
  "creation": "lantern-rules:standard",
  "levels": [
    {
      "gain": 0
    }
  ],
  "experience": 0,
  "attributes": {
    "body": 12,
    "mind": 9
  },
  "tracks": {},
  "balances": {},
  "equipment": [],
  "conditions": []
}
```

The `character show` result reports Body 12, Mind 9, and the derived Poise 21. The ruleset is classless, so no class or race flags are needed.

## 5. Play both choices and save a real waiting menu

These scripts are the complete command files. The first two run the two visible choices from a new party.

### `$WORKSPACE/scripts/intro-and-light.script`

```text
choose 1
```

### `$WORKSPACE/scripts/intro-and-dark.script`

```text
choose 2
```

Create an empty script to stop at the intro menu. A save at this point contains `pending_menu: "tidehouse:arrival_choice"`; it is a live checkpoint, so the run is intentionally not ended.

```sh
: > "$WORKSPACE/scripts/pause-at-menu.script"
goldbox play \
  --campaign "$WORKSPACE/modules/tidehouse" \
  --modules "$WORKSPACE/modules" \
  --party "$WORKSPACE/party/neri.json" \
  --seed 7 \
  --script "$WORKSPACE/scripts/pause-at-menu.script" \
  --save "$WORKSPACE/party/pending-menu.save.json" \
  --fail-on-refusal --json > "$WORKSPACE/receipts/play-pause.json"
```

Resume that real pending menu without supplying a party again. The light branch writes the variable and ends the short adventure.

```sh
goldbox play \
  --campaign "$WORKSPACE/modules/tidehouse" \
  --modules "$WORKSPACE/modules" \
  --load "$WORKSPACE/party/pending-menu.save.json" \
  --script "$WORKSPACE/scripts/intro-and-light.script" \
  --save "$WORKSPACE/party/reopened-light.save.json" \
  --fail-on-refusal --json > "$WORKSPACE/receipts/play-reopen.json"
```

Run the uninterrupted light and dark branches too.

```sh
goldbox play --campaign "$WORKSPACE/modules/tidehouse" --modules "$WORKSPACE/modules" \
  --party "$WORKSPACE/party/neri.json" --seed 7 \
  --script "$WORKSPACE/scripts/intro-and-light.script" \
  --save "$WORKSPACE/party/light.save.json" --fail-on-refusal --json \
  > "$WORKSPACE/receipts/play-light.json"

goldbox play --campaign "$WORKSPACE/modules/tidehouse" --modules "$WORKSPACE/modules" \
  --party "$WORKSPACE/party/neri.json" --seed 7 \
  --script "$WORKSPACE/scripts/intro-and-dark.script" \
  --save "$WORKSPACE/party/dark.save.json" --fail-on-refusal --json \
  > "$WORKSPACE/receipts/play-dark.json"
```

Expected facts:

- The pause transcript ends at the menu with `ended: false`, position `tidehouse:quay [0,0]` facing east, and `pending_menu: tidehouse:arrival_choice` in the save.
- The reopened and uninterrupted light saves have the same runtime result: `ended: true`, `campaign.var.lantern_lit: true`, and the text “The harbor answers with three small horns.”
- The dark branch ends with `campaign.var.lantern_lit: false` and the text “You leave the wick cold; the tide carries the last light away.”
- The play command scripts contain no combat, image, audio, private fixture, or extra runtime-tool dependency; this example exercises the workspace-export path only.

## 6. Build and export each module independently

The workspace stages only the three paths listed under `authoring.modules`; canon, prompts, and references stay editable and outside runtime staging.

```sh
goldbox workspace inspect "$WORKSPACE" --json > "$WORKSPACE/receipts/workspace-inspect.json"
goldbox workspace build "$WORKSPACE" --json > "$WORKSPACE/receipts/workspace-build.json"
goldbox workspace export "$WORKSPACE" --json > "$WORKSPACE/receipts/workspace-export.json"
```

The export directory contains `lantern-rules-0.1.0.rpak`, `lantern-art-0.1.0.rpak`, and `tidehouse-0.1.0.rpak`. The campaign export is independent from the ruleset and asset exports; all three are loaded together by their declared logical dependencies.

## 7. Reopen the exported containers as a fresh input

This checks that the exported containers, rather than the source directories, are sufficient for validation, character creation, and play.

```sh
mkdir -p "$WORKSPACE/export-run"
goldbox module validate "$WORKSPACE/exports/tidehouse-0.1.0.rpak" \
  --modules "$WORKSPACE/exports" --json > "$WORKSPACE/receipts/export-validate.json"
goldbox character new \
  --module "$WORKSPACE/exports/lantern-rules-0.1.0.rpak" \
  --name NeriExport --attributes body=12,mind=9 --seed 7 \
  --out "$WORKSPACE/export-run/neri.json" --json > "$WORKSPACE/receipts/export-character-new.json"
goldbox character show "$WORKSPACE/export-run/neri.json" \
  --module "$WORKSPACE/exports/lantern-rules-0.1.0.rpak" \
  --json > "$WORKSPACE/receipts/export-character-show.json"
goldbox play \
  --campaign "$WORKSPACE/exports/tidehouse-0.1.0.rpak" \
  --modules "$WORKSPACE/exports" \
  --party "$WORKSPACE/export-run/neri.json" --seed 7 \
  --script "$WORKSPACE/scripts/intro-and-light.script" \
  --save "$WORKSPACE/export-run/light.save.json" \
  --fail-on-refusal --json > "$WORKSPACE/receipts/export-play.json"
```

The exported validation, character sheet, and play should each return `ok: true`; the exported play should end with the lantern-lit branch.

## 8. Repair evidence

Keep the first failed command and its repair beside the successful receipts. This deliberate route-script mistake chooses the light ending and then asks for `status` after the adventure has ended.

```sh
cat > "$WORKSPACE/scripts/light-with-post-end-status.script" <<'SCRIPT'
choose 1
status
SCRIPT

goldbox play --campaign "$WORKSPACE/modules/tidehouse" --modules "$WORKSPACE/modules" \
  --party "$WORKSPACE/party/neri.json" --seed 7 \
  --script "$WORKSPACE/scripts/light-with-post-end-status.script" \
  --save "$WORKSPACE/party/light-with-refusal.save.json" --fail-on-refusal --json \
  > "$WORKSPACE/receipts/play-post-end-refusal.json"
```

Expect exit 1, `ok: false`, and a `play.refusal` diagnostic naming line 2 and explaining that the adventure has ended. If running commands in a shell that stops on nonzero exits, run this expected failure separately and retain its exit status.

The repair uses the already supplied one-line light script, which removes the post-end command:

```sh
goldbox play --campaign "$WORKSPACE/modules/tidehouse" --modules "$WORKSPACE/modules" \
  --party "$WORKSPACE/party/neri.json" --seed 7 \
  --script "$WORKSPACE/scripts/intro-and-light.script" \
  --save "$WORKSPACE/party/repaired-light.save.json" --fail-on-refusal --json \
  > "$WORKSPACE/receipts/play-repaired-light.json"
```

Expect exit 0 and the same lantern-lit ending and save state as the uninterrupted light branch in section 5.

This is a route-script correction, not a runtime workaround. If a later change needs a new operation or definition field, first run the relevant schema topic and record the unsupported gap with its owning Core task.
