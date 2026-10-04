# Author brief template (draft 0.1)

Use this file before creating runtime module JSON. It is an author-visible
intake, not a hidden evaluator prompt. Replace every `<placeholder>` and keep
the completed brief beside the editable workspace.

## Inputs

```yaml
title: <campaign title>
campaign_id: <lowercase module id>
audience: <who will play or review it>
party_size: <campaign-defined range>
duration: <hours and sessions>
ruleset: <required ruleset module id and range>
tone: <tone, content boundaries, and reading level>
visual_style: <pixel, illustrated, ink, 3d, or other>
art_rights: <licence and source restrictions>
source_material: <source, edition, and provenance or original>
chapters: <number and names>
```

Also record the desired player-facing outcome in plain language:

```text
The player should understand <situation> after <entry action>, choose among
<visible choices>, and see <observable consequences>. The adventure closes
when <explicit ending condition>; a live checkpoint is expected to
<end / remain resumable> in headless play.
```

If a source is adapted, list what is licensed, what is original, what is left
out, and where attribution and the full licence text live. Never use excluded
source illustrations as image-edit inputs. Keep source facts separate from
adaptation choices.

## Exact acceptance facts

Put exact facts here when a route depends on them. Authors and reviewers must
receive this section when it is part of the assignment; do not compare an
author with a numeric value that was withheld from the author.

```yaml
seed: <seed for a reproducible trial>
routes:
  - id: <route id>
    command_script: <relative script path>
    expected_state:
      <variable>: <exact value>
    visible_consequence: <text or media the player can observe>
    ends_headless_run: <true or false>
```

## Outputs

- A brief with no unresolved placeholders or unlabelled assumptions.
- A canonical ID and local-ID map in `canon.md`.
- A list of chapter contracts, encounter targets, art references, validation
  commands, and unresolved questions.
- A handoff naming changed files, assumptions, dependencies, and the next
  command. The handoff must be resumable from files.

## Repair path

Run `goldbox schema --json` and the relevant schema topic before writing a new
field. Run `goldbox module validate <module> --json`; preserve the first
diagnostic and every subsequent repair receipt. A diagnostic naming
`module/file/json path/rule` is the repair target. If a play command needs a
dependency directory, include `--modules <dir>` in the command and record how
the sibling dependencies are staged. If an expression diagnostic says a
literal is not typed as text, quote the literal and retain the before/after
receipts. If a desired mechanic is unsupported, record the gap and its owner;
do not silently invent a new runtime mechanism.

## Current example

The #9297 Blackapple slice used one fixed brief, three route scripts, a
seeded party, and a visible kindness/expose/leave boundary. The compact trial
used a trust value of `2` while the post hoc oracle expected `1`; the semantic
outcome was still a visible trust increase. The kit therefore requires exact
expected values to be supplied to authors and labels any later oracle mismatch
as a protocol finding rather than an author failure.
