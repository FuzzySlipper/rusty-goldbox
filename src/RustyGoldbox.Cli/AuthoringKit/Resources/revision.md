# Revision and repair record (draft 0.1)

Use one copy for each later attempt. Keep source, generated outputs, and
receipts distinct so a fresh author can tell what was actually changed.

## Revision header

```yaml
run: <new run id>
parent_run: <prior run id or null>
source_baseline: <revision or source tree hash>
canon_baseline: <canon path and sha256>
prompt_revision: draft-0.1
reason: <observed failure, changed brief, or deliberate content revision>
```

## Steps

1. Read the current brief, canon, contracts, source, accepted/rejected art,
   and previous receipts. Do not resume from a chat summary alone.
2. List the intended files and player-visible outcome. Keep the change within
   the owning slice; propose shared changes to the coordinator.
3. Re-run the old validation or route command before editing when it is still
   meaningful. Preserve its output.
4. Edit the source, record the diagnostic and rationale, and rerun the same
   command. Keep a receipt for every repair loop, including a second repair.
5. Re-run dependent chapter, encounter, art, build, export, and visible checks
   as applicable. Record what each layer proves.
6. Write a new handoff with changed files, assumptions, unresolved items, and
   one next command. Keep the old run intact for comparison.

## Contradictions

If the current source disagrees with a copied handoff, name the disagreement:

```text
canon ID / local ID:
file and JSON path:
old claim:
current evidence:
affected entry/exit state:
decision owner:
next command:
```

Reject a stale assumption until the coordinator makes a deliberate canon
decision. Resolve ordinary edits through ownership and current files; do not
add revision numbers, staleness fences, proposal layers, or an orchestration
service to the runtime.

## Closeout evidence

Report separately:

- source and licence/provenance state;
- module validation and schema/inspect receipts;
- workspace staging and independent exports;
- seeded headless play and encounter simulation;
- visible ordinary-control/player evaluation;
- independent review findings and the exact reviewed source revision.

The prompt revision remains **draft** until it is retested by the later fresh
transfer, revision, parallel, and different-ruleset trial. A successful local
copy or one valid route does not change that status.
