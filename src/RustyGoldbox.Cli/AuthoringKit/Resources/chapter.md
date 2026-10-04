# Chapter and area contract (draft 0.1)

Use one copy per chapter, area, or bounded scene slice. The contract prevents
parallel authors from creating incompatible entrances, exits, or state names.

## Assignment

```yaml
chapter_id: <canonical chapter id>
local_owner: <worker name>
files_owned:
  - <relative runtime module directory or file>
files_read_only:
  - <canon or reference path>
dependencies:
  - <module or preceding chapter>
entry:
  area: <area id>
  event: <entry event id>
  state: [<required state and facts>]
exit:
  events: [<event ids>]
  state: [<guaranteed state and facts>]
  destinations: [<logical area ids>]
```

## Scene design

Describe what the player sees, what ordinary commands can do, and which
choices produce which visible consequences. Include map geometry, event
triggers, exits, doors, searches, and media as logical IDs. Use the current
definition contracts and examples from `goldbox schema`; do not copy a second
field reference into this file.

```text
Arrival: <player-facing text/media and initial state>
Choice A: <ordinary action> -> <state change> -> <visible consequence>
Choice B: <ordinary action> -> <state change> -> <visible consequence>
Revisit: <what persists and what is intentionally unchanged>
Unsupported gap: <capability and owner, if any>
```

## Merge contract

Before handoff, run validation and the smallest scripted play that exercises
entry, each branch, revisit, and exit. Report the command, seed, transcript,
changed files, assumptions, unresolved questions, and next command. A worker
may add local definitions; shared IDs, variables, module requirements, and
cross-chapter interfaces return to the canon owner.

When merging a chapter, the coordinator checks the current canon and the
preceding and following contracts. If two workers changed the same file,
preserve both edits and resolve the shared interface deliberately. A stale
handoff is a contradiction report, not permission to overwrite current files.

## Repair path

For a failed module check, fix the named module, file, JSON path, and rule,
then rerun the same command and save both receipts. For a failed play, retain
the first transcript, identify whether the cause is an invalid ID, missing
dependency, unsupported mechanic, or narrative contract mismatch, and assign
the repair to the owning file. Do not hide a missing runtime feature behind a
new local scheduler, renderer, or state authority.
