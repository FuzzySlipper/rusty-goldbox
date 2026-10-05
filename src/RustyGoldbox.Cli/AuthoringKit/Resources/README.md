# Rusty Goldbox campaign authoring kit (revision 1.0)

This kit is a copyable starting point for an agent authoring a campaign
module. It is shipped inside `goldbox`, so an installed author can discover it
without a repository checkout, this chat, or a private skill:

```text
goldbox --help
goldbox authoring list --json
goldbox authoring show workflow --json
goldbox authoring show worked-example --json
goldbox authoring copy --all --out .goldbox/authoring --json
```

The kit is ready for use as a files-first authoring packet. It preserves
observed authoring lessons as generic guidance and makes no model, strategy,
quality, or performance ranking claim. The complete worked example is a
text-only Lantern campaign; it does not claim that every fight or media route
is automatically tested.

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
   Dispatch disjoint area owners only after shared canon, IDs, and entry/exit
   contracts exist. If the available harness cannot spawn roles, mark the
   work same-session and ask the owning coordinator to dispatch it; never call
   same-session work independent.
4. Start discovery with global `goldbox --help`, then use
   `goldbox schema <topic> --json` for the current format. Author runtime JSON
   under the workspace's explicit module directories; keep canon, prompts,
   source art, accepted art, and reports in their editable roots. Do not
   maintain a second schema in these notes.
5. Validate, inspect, play, and simulate with the CLI. Keep the first failing
   diagnostic, every repair receipt, changed-file rationale, command, seed,
   and result. State when a checkpoint ends a headless run.
6. Keep the minimal useful artifacts: editable canon, prompts and source art;
   authored runtime module source; scripts; and receipts. Use `workspace build`
   and `workspace export` only after the editable source is reviewable. The
   generated staging tree and Engine containers are derived outputs, not
   editable source.
7. Revise from files and receipts with `revision.md`. Re-read current canon
   and report contradictions before editing. A stale handoff is evidence to
   resolve, not a reason to add a revision fence or orchestration service.

## Resource map

| Resource | Use | Status |
| --- | --- | --- |
| `kit` | Index, discovery, and staged workflow | ready |
| `brief` | User brief and visible route acceptance | ready |
| `canon` | Shared story, IDs, variables, ownership, and rights | ready |
| `chapter` | Area, scene, map, event, and quest-state contract | ready |
| `encounter` | Ruleset-aware tuning, manual combat, traces, and repair | ready |
| `art` | Style references, original assets, size-aware review, and drift | ready |
| `judge-individual` | Fresh original-file image review and targeted corrections | ready |
| `judge-batch` | Original-file and contact-sheet drift comparison | ready |
| `handoff` | Single-agent and multi-agent transfer packet | ready |
| `workflow` | Coordinator sequence, dispatch, and merge discipline | ready |
| `revision` | Repair, contradiction, and evidence-layer record | ready |
| `worked-example` | Complete original Lantern campaign and export path | ready |

Copy an individual resource when a worker needs only a bounded slice. Copy the
whole directory when starting a new authoring workspace. Keep copied files as
immutable inputs for that run and create a new run directory for a new attempt.

## Technical boundary

The kit describes authoring decisions; it does not define a competing module
format. Start with global `goldbox --help`, then use
`goldbox schema <topic> --json` for exact fields and examples. Use
`goldbox workspace inspect`, `goldbox workspace build`, and
`goldbox workspace export` for the current workspace contract. When a command
reports a module, file, JSON path, and rule, repair that reported input and
retain its receipt.

The kit includes individual and batch image judges. Give each fresh judge the
brief, original candidates and references, declared display-size views, and
current schema notes. Keep generator assessments and calibration expectations
out of the judge inputs. Record unavailable evidence as uncertain; a visual
verdict does not establish runtime format validity.

The `worked-example` resource is a complete original Lantern text campaign and
keeps its source canon, prompts, art notes, runtime modules, scripts, receipts,
and derived exports distinct. For combat authoring, `encounter.md` and
`docs/combat-authoring.md` retain the manual-combat, data-authored
custom-behavior, AI-debug trace, and refusal-repair recipes; their Classic
fixture routes remain labeled separately from an ordinary campaign route.
