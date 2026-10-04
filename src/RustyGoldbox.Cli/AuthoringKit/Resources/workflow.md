# Single-agent and multi-agent workflow (draft 0.1)

This is the coordinator's copyable sequence. It is intentionally a practical
workflow rather than an orchestration service.

## Single author

1. Copy `brief.md`, fill the user-visible brief, and make exact route facts
   available to the author and reviewer.
2. Copy `canon.md`, choose module identities, rights, ruleset dependencies,
   local IDs, variables, and cross-scene interfaces.
3. Use `goldbox schema --json` and the relevant schema topics. Create or edit
   the workspace's runtime module directories; keep editable canon, prompts,
   scripts, source art, accepted art, and reports separate from generated
   staging and exports.
4. Write chapter contracts, encounter contracts, and art direction. Keep
   player-facing outcomes and unsupported gaps explicit.
5. Validate and inspect. Run seeded headless route scripts and encounter
   simulations. Preserve commands, seeds, stdout, stderr, exit codes, and
   every repair receipt.
6. Run `goldbox workspace build <workspace>` and then
   `goldbox workspace export <workspace>`. Review the included runtime files
   and each independent container. Test a staged/exported copy as a fresh
   input.
7. Record a handoff and use `revision.md` for the next pass.

## Coordinator with workers

The coordinator owns `brief.md`, `canon.md`, module identity, shared IDs,
variables, cross-chapter interfaces, and integration. Split only disjoint
paths:

| Worker | Receives | Owns |
| --- | --- | --- |
| planner | brief and canon slice | plan and proposed interfaces |
| chapter/area | chapter contract and relevant canon | its area/events/scripts |
| encounter | chapter state and ruleset schema | encounter/combat data and balance notes |
| art | art brief and references | source prompts, accepted/rejected records, asset definitions |
| validator/reader | immutable source and expected facts | receipts and findings, no author edits |

Every worker receives an entry state, exit state, dependencies, allowed
changes, and a next command. Workers preserve others' edits and return shared
changes as proposals. The coordinator merges chapter contracts in dependency
order, checks the canonical-to-local alias map, validates the full module set,
and runs an independent fresh reader or player evaluation.

## What the #9297 trials changed

- Exact state values and route aliases must be author-visible. A post hoc
  hidden oracle made a compact trial's trust value look like a semantic failure
  even though the brief only said “raises trust.”
- Include `--modules <dir>` in every play command and document reproducible
  sibling dependency staging.
- Keep a receipt for every repair. A valid second repair without its receipt is
  incomplete evidence.
- State whether a live checkpoint ends a headless run.
- Keep explicit canonical-to-local ID mapping and unsupported-gap prose.
- Preserve compact canon plus structured plan, planner/worker handoffs, and
  files-only continuation inputs as separate artifacts.

These are bounded observations for one brief and revision. They are not a
model or strategy ranking, and the kit remains draft until the later transfer
trial.

## Repair and closeout

When a worker reports a contradiction, name the affected source, module, JSON
path, canon ID, and state. Decide deliberately whether to update canon or
repair the worker slice, then rerun the smallest affected checks and the full
validation. When source, build, runtime, visible, and review evidence differ,
report each layer separately. Do not close the workflow on a first chapter,
plan, or generated image alone.
