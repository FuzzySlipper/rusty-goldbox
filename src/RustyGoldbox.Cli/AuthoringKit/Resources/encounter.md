# Encounter tuning and replay contract (draft 0.1)

Use this file for each authored encounter or encounter family. The encounter
owner chooses values in module data; Core interprets the ruleset and the CLI
drives reproducible checks. The CLI is the authoring and debugging surface:
the ruleset owns legal actions, costs, ranges, targets, movement and checks;
an authored behavior profile chooses among those legal uses.

## Inputs and ownership

```yaml
encounter_id: <module-local id>
chapter: <chapter id>
owner: <worker name>
ruleset: <module id and version range>
party_profiles:
  - name: <level and party shape>
    files: [<character files>]
targets:
  difficulty: <target and rationale>
  duration: <round or time target>
  resources: <intended attrition>
  escape_or_social: <allowed alternative>
```

Record the player-visible stakes, location, entry conditions, available
choices, and intended outcomes. Name the definition IDs and logical assets;
use `goldbox schema encounter`, `goldbox schema combat`,
`goldbox schema combat-behavior`, and `goldbox schema live-combat` for exact
fields and commands.

## Checks

Use the module directory containing the encounter as `<campaign>` and its
dependency directory as `<modules>`. Preserve the command, complete JSON
stdout, stderr, and status for every check.

1. Validate the containing module and its dependencies:

   ```text
   goldbox module validate <campaign> --modules <modules> --json
   ```

2. Inspect the encounter, selected combat, and any authored behavior. `--trace`
   is read-only and includes the authored guard, priority, selected steps,
   alternatives, and source path:

   ```text
   goldbox module inspect <behavior-module> combat-behavior --trace \
     --modules <modules> --json
   goldbox module inspect <campaign> <campaign-id>:<encounter-id> --trace \
     --modules <modules> --json
   ```

3. Run a seeded simulation with an explicit combat definition. A campaign can
   resolve more than one combat definition; omitting `--combat` is a repairable
   diagnostic, not a successful simulation:

   ```text
   goldbox sim combat --module <campaign> --modules <modules> \
     --party <character.json>,... --encounter <campaign-id>:<encounter-id> \
     --combat <ruleset-or-extension:combat-id> --seed <n> --trace --json
   ```

   Preserve the transcript, rolls, seed, trace and exit status. Repeat the
   exact command when checking seeded determinism and compare the complete JSON
   bytes. Treat a repeat as evidence for that command and seed; it does not
   establish full-campaign determinism or balance.

4. Run the declared party profiles with a small set of seeds chosen in the
   brief. Compare the outcome to the stated target and explain any deviation.
5. Play the surrounding scene with ordinary commands so the encounter is
   reachable and its aftermath updates the declared chapter state. Save a
   pending fight and load it again when persistence is part of the contract.

Do not claim that a simulation proves the full campaign, balance in every
party, or every fight path. A successful schema check, simulation, headless
play, save/resume check, and visible player evaluation are separate evidence
layers.

## Authored behavior and trace

Start with the CLI schema and the shipped combat-authoring guide:

```text
goldbox schema combat-behavior --json
goldbox schema action --json
goldbox schema combat --json
```

The behavior profile supplies guards or scores, `priority`, `commit` (`step`
or `plan`), ordered steps, target preference, destination preference and a
fallback. It cannot make an unavailable action legal, change its cost or
effect, or add a script loop. A compact authored profile, using IDs that exist
in the tactical example modules, is:

```json
{
  "type": "combat-behavior",
  "id": "skirmish",
  "name": "Skirmish at range",
  "rules": [
    {
      "when": "combat.distance <= 2",
      "priority": 20,
      "commit": "plan",
      "steps": [
        {
          "destination": { "kind": "away", "distance": "4" },
          "action": { "action": "tactical-bestiaire:withdraw" },
          "target": "enemy"
        }
      ],
      "fallback": "next"
    },
    {
      "priority": 10,
      "steps": [
        {
          "action": {
            "action": "classic:missile_attack",
            "damage": "1d6",
            "increment": "6"
          },
          "target": "enemy"
        }
      ],
      "fallback": "end-turn"
    }
  ]
}
```

Inspect the resolved profile and then run the same encounter with `--trace`:

```text
goldbox module inspect <behavior-module> combat-behavior --trace \
  --modules <modules> --json
goldbox sim combat --module <campaign> --modules <modules> \
  --party <character.json> --encounter <campaign-id>:<encounter-id> \
  --combat <ruleset-or-extension:combat-id> --seed 17 --trace --json
```

The simulation trace is a side-effect-free policy projection. Read the source
path, guard result, selected action, and alternative rejection reason together;
an exit code alone does not show that the intended policy ran. Observation and
trace do not roll dice. All checks and operations still use the Engine random
service, so do not add a local random source or infer an extra draw from a
read-only inspection.

The original tactical example is `tactical-bestiaire` attached to the original
`tactical-expedition` campaign. Its `skirmisher`, `duelist`, `warder`, and
`phase_watcher` profiles demonstrate movement, fallback, and resource-aware
choices, while the `classic` ruleset supplies legality. This is a Classic
ruleset example, so it is not evidence for a non-D&D ruleset. A ruleset adapted
under an open licence keeps that licence and its provenance; do not relabel an
adapted ruleset as an original fixture.

## Manual live combat

Start a campaign in manual mode. Ordinary scene commands include `forward`,
`back`, `left`, `right`, `around`, `search [direction]`, `open [direction]`,
`pick [direction]`, `force [direction]`, `choose <n>`, `look`, `view <member>`
and `status`. A menu choice must be made before walking into the tactical
gallery in this example:

```text
goldbox play --campaign <campaign> --modules <modules> \
  --party <character.json> --seed 17 --combat-control manual \
  --script <script> --trace --json
```

The script can be as small as:

```text
choose 2
forward
combat inspect
```

`combat inspect` does not advance the fight. It returns the phase, round,
active actor, legal actions, action costs, legal target IDs, offered movement
paths, tracks and any pending decision. Use those IDs and paths verbatim; do
not guess display names. A legal action from the tactical-gallery inspection
was:

```text
combat action side-1-member-1 classic:close --target side-2-member-1 --path 1,2;2,3;3,4;4,4;5,5;6,4;7,3;8,2
```

The path is a semicolon-separated list of `x,y` cells and must be one of the
moves offered for the action's first selected target. The shorter form without
`--path` lets the resolver use the offered move; use an explicit path when the
choice itself is what the test is checking. For an authored movement action,
the schema also exposes:

```text
combat move <actor-id> <action-id> <target-id> <x,y;x,y>
```

The remaining live commands are:

```text
combat control <actor-id> auto|manual
combat end-turn <actor-id>
combat decide <decision-id> [option-id]
combat auto-step
```

`combat auto-step` takes one automatic turn and restores manual control;
`combat control ... auto` is a persistent takeover. Resource prices and tracks
are part of `combat inspect`. An invalid command or path is refused before
movement, spending or random-price commitment. A legal spell's random price
is retained across save/resume; an unaffordable accepted choice reports that
state without casting or spending.

Save a pending fight after inspection, then resume it with the same live
decision:

```text
goldbox play --campaign <campaign> --modules <modules> \
  --party <character.json> --seed 17 --combat-control manual \
  --script pause.script --save pending.save --trace --json
goldbox play --campaign <campaign> --modules <modules> \
  --load pending.save --script resume.script --trace --json
```

`pause.script`:

```text
choose 2
forward
combat inspect
```

`resume.script` can begin with `combat inspect`; the loaded result should name
the same pending actor/decision before the next action is submitted. A save,
load, or trace result is evidence of persistence/projection only; assert the
phase, actor, target, resource and intended outcome in the JSON before calling
the run successful.

## Repair path

Keep the first failing diagnostic and each repair receipt. If the error names a
missing definition or invalid field, fix the owning module source and rerun the
schema-guided validation. If a simulation reports more than one combat
definition, add the named `--combat` ID and rerun; do not turn an exit-0
transcript with the wrong winner into a success. If a target or path is refused,
inspect again and replace it with an offered target/path. If a seed exposes an
undesirable result, state the target and adjust authored data or the encounter
contract; do not add a hard numeric cap or a local random source. If the scene
cannot reach the encounter, repair the chapter event/area contract and report
the dependency to the coordinator.

`--fail-on-refusal` turns a scripted scene or combat-command refusal into a
located `play.refusal` diagnostic and exit code 1. Without the flag, a refused
combat command remains a structured transcript result with
`combat.accepted: false`, its `reason`, and exit code 0. In either mode, assert
the diagnostic count, accepted flag, reason, pending phase, and intended
outcome; use the returned `combat inspect` guidance to choose the smallest
legal repair before claiming success.

The final handoff includes changed files, seeds and transcripts, assumptions,
unresolved balance questions, and the next command. This draft is a recipe and
does not certify a module, campaign, or authoring-kit release.
