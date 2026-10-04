# Encounter tuning and replay contract (draft 0.1)

Use this file for each authored encounter or encounter family. The encounter
owner chooses values in module data; Core interprets the ruleset and the CLI
drives reproducible checks.

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
use `goldbox schema encounter`, `goldbox schema combat`, and the relevant
ruleset topics for exact fields.

## Checks

1. Validate the containing module and its dependencies.
2. Inspect the encounter and combat definitions.
3. Run a seeded single simulation with `goldbox sim combat` and preserve its
   transcript, rolls, seed, and exit status.
4. Run the declared party profiles with a small set of seeds chosen in the
   brief. Compare the outcome to the stated target and explain any deviation.
5. Play the surrounding scene with ordinary commands so the encounter is
   reachable and its aftermath updates the declared chapter state.

Do not claim that a simulation proves the full campaign, balance in every
party, or every fight path. A successful schema check, simulation, headless
play, and visible player evaluation are separate evidence layers.

## Repair path

Keep the first failing diagnostic and each repair receipt. If the error names a
missing definition or invalid field, fix the owning module source and rerun
the schema-guided validation. If a seed exposes an undesirable result, state
the target and adjust authored data or the encounter contract; do not add a
hard numeric cap or a local random source. If the scene cannot reach the
encounter, repair the chapter event/area contract and report the dependency to
the coordinator.

The final handoff includes changed files, seeds and transcripts, assumptions,
unresolved balance questions, and the next command. Manual combat recipes and
the final shipped kit belong to the later integration tasks; this draft does
not replace them.
