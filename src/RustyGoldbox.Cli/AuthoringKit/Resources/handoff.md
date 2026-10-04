# Authoring handoff packet (draft 0.1)

This packet is the minimum a fresh author needs to resume from files. It is a
work record, not a conversation transcript and not a hidden chain of thought.

## Header

```yaml
task: <task id or local run id>
campaign: <campaign id>
run_directory: <path>
brief: <path and sha256>
canon: <path and sha256>
inputs:
  - <path and purpose>
baseline: <source revision or explicitly local source>
tool: <published goldbox path and version>
application_assembly: <published application assembly identity or hash>
executable: <optional apphost or executable identity>
status: <draft, ready-for-review, blocked, or superseded>
```

The application assembly identifies the published CLI. An apphost or
executable identity is supplementary and cannot stand alone.

## Worker assignment

```yaml
role: <planner, chapter, encounter, art, validator, or reader>
reads:
  - <canon/reference slice>
writes:
  - <disjoint paths owned by this worker>
dependencies:
  - <task, module, or prior handoff>
entry_contract:
  - <state and files present on entry>
exit_contract:
  - <state and files guaranteed on exit>
allowed_changes:
  - <specific files, IDs, and variables>
shared_change_proposals:
  - <shared interface proposed for coordinator review>
```

The worker preserves unrelated edits, reads current files before editing, and
returns every changed path. A planner may propose IDs and state; only the
coordinator changes shared canon. A reader can report a contradiction without
editing the author output.

Dispatch area or chapter owners only after the shared canon, IDs, and entry and
exit contracts are ready. The coordinator leaves each owner's runtime slice to
that owner. A prepared prompt or planned command is not a dispatch, run, or
assessment receipt; record the actual receipt before describing it as one. If a
partial owner slice lacks a sibling definition, keep that as owner coordination
until assembly validation rather than inventing a shared reference.

## Results

```yaml
commands:
  - command: <exact command>
    seed: <seed or null>
    exit_code: <number>
    stdout: <receipt path>
    stderr: <receipt path>
repairs:
  - diagnostic: <first diagnostic>
    changed_files: [<paths>]
    rationale: <why this repair addresses the rule>
assumptions: [<explicit assumptions>]
unresolved: [<questions or unsupported capabilities>]
changed_files: [<all paths>]
next_command: <one executable next step>
```

Keep the first failing output and a receipt for every repair loop. State
whether a checkpoint is expected to end headless play. If a command needs
`--modules <dir>`, keep that argument in the copied command and record the
dependency staging used. If a seeded contradiction is found, identify the
canon ID, local ID, file, and state it affects before deciding whether canon
should change. Run short CLI checks directly in the foreground. A background
process without a complete completion receipt is unverified and must be rerun
before its result is recorded as successful.

## Merge and resume

The coordinator compares the packet with the current brief, canon, and source.
If a handoff is stale, stop at the contradiction, report it, and ask for a
deliberate canon update. Do not build a revision fence, admission layer, or
orchestration service. To resume, use the files and exact next command; never
rely on the previous agent's chat context.
