# Rusty Goldbox campaign authoring kit (draft 0.1)

This kit is a copyable starting point for an agent authoring a campaign
module. It is shipped inside `goldbox`, so an installed author can discover it
without a repository checkout, this chat, or a private skill:

```text
goldbox authoring list --json
goldbox authoring show workflow
goldbox authoring copy workflow --out .goldbox/authoring
```

The kit is deliberately marked **draft**. It preserves useful observations
from the bounded #9297 Blackapple village trials and remains subject to a fresh
#9307 transfer, revision, and different-ruleset trial. It makes no model,
strategy, quality, or performance ranking claim and does not claim that a
complete campaign or every fight is automatically tested.

## One workflow

1. Fill `brief.md`. Record preferences, audience, duration, ruleset, tone, art
   direction, exact route outcomes, and any expected facts that a later check
   may use. Do not leave numeric assertions in a hidden evaluator-only oracle.
2. Establish `canon.md` before workers write scenes. Name the campaign,
   modules, source and licence, canonical IDs, local IDs, variables, and
   interfaces. Treat the canon owner as the only owner of shared IDs and
   state.
3. Split work with `chapter.md`, `encounter.md`, and `art.md`. Every worker
   gets a bounded input slice, disjoint files, entry and exit conditions,
   quest-state reads and writes, dependencies, and an allowed change scope.
4. Run `goldbox schema` for the current format. Author runtime JSON under the
   workspace's explicit module directories; keep canon, prompts, source art,
   accepted art, and reports in their editable roots. Do not maintain a second
   schema in these notes.
5. Validate, inspect, play, and simulate with the CLI. Keep the first failing
   diagnostic, every repair receipt, changed-file rationale, command, seed,
   and result. State when a checkpoint ends a headless run.
6. Use `workspace build` and `workspace export` only after the editable source
   is reviewable. The generated staging tree and Engine containers are outputs;
   the editable source remains the source of truth.
7. Revise from files and receipts with `revision.md`. Re-read current canon
   and report contradictions before editing. A stale handoff is evidence to
   resolve, not a reason to add a revision fence or orchestration service.

## Resource map

| Resource | Use | Status |
| --- | --- | --- |
| `brief` | User brief and visible route acceptance | draft |
| `canon` | Shared story, IDs, variables, and ownership | draft |
| `chapter` | Area, scene, map, event, and quest-state contract | draft |
| `encounter` | Ruleset-aware encounter tuning and replay | draft |
| `art` | Style references, original assets, and drift review | draft |
| `handoff` | Single-agent and multi-agent transfer packet | draft |
| `workflow` | Coordinator sequence and merge discipline | draft |
| `revision` | Repair, contradiction, and later revision record | draft |

Copy the individual resources when a worker needs only a bounded slice. Copy
the whole directory when starting a new authoring workspace. Keep the copied
files immutable inputs for that run and create a new run directory for a new
attempt.

## Technical boundary

The kit describes authoring decisions; it does not define a competing module
format. Use `goldbox schema --json`, `goldbox schema module --json`, and the
relevant definition topic for exact fields and examples. Use
`goldbox workspace inspect`, `goldbox workspace build`, and
`goldbox workspace export` for the current workspace contract. When a command
reports a module, file, JSON path, and rule, repair that reported input and
retain its receipt.

The initial kit covers the workflow and handoffs. The Blackapple production
campaign, full art set, independent drift calibration, and final fresh transfer
remain later work in the parent task sequence.
