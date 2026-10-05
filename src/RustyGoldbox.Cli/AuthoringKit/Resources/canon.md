# Canon and shared-state contract (revision 1.0)

The coordinator owns this file. Workers may propose changes in their handoff;
the coordinator deliberately edits shared canon after checking the proposal
against current source and dependencies. A worker never silently renames a
shared ID or changes another module's definition.

## Canon record

```yaml
campaign:
  id: <campaign module id>
  title: <title>
  version: <version>
  ruleset: <required module id and range>
  extensions: [<optional extension ids>]
  assets: [<asset module ids>]
source:
  title: <source or original work>
  edition: <edition if applicable>
  licence: <licence id>
  excluded_material: [<art, setting names, or other excluded material>]
ids:
  canon.dotted.name: local_name
variables:
  - id: <local variable>
    type: <schema-supported type>
    owner: <coordinator or chapter owner>
    writers: [<disjoint worker ids>]
interfaces:
  - id: <scene or event interface>
    enters_with: [<state facts>]
    exits_with: [<state facts>]
    media: [<logical asset ids>]
```

Canonical IDs may be descriptive dotted names while runtime local IDs must fit
the rules documented by `goldbox schema`. Keep the map explicit in this file;
it is continuity evidence. Record whether a state is a scalar, an authored
table, or a runtime capability. Do not turn a prose list into a new collection
or add a Core primitive just to avoid making a deliberate adaptation choice.

## Ownership rules

- The coordinator owns module identity, required-module references, shared
  variables, cross-chapter interfaces, and the canonical-to-local ID map.
- A chapter owner writes only the chapter's directories and its declared local
  events and variables. Cross-chapter changes are proposals in the handoff.
- An encounter owner writes only encounter definitions and its declared
  balance notes. Rules belong in the selected ruleset data.
- An art owner writes source records, prompts, accepted/rejected decisions,
  and logical asset definitions in its assigned roots. It does not edit scene
  state or invent asset paths in another module.
- The coordinator merges shared changes deliberately after checking current
  files. Preserve unrelated edits and never replace a file because a copied
  handoff says it is unchanged.

## Source and rights

Keep provenance, attribution, and the complete applicable licence with the
adapted module. Keep original generated art's prompt and source metadata in the
editable art roots. A module references an asset by logical ID; use
`goldbox schema media` for the exact definition fields and sampling choices.

## Repair path

If validation reports a missing or conflicting dependency, inspect the named
`module.json` and its JSON path, then correct the owning source. If a worker
uses an alias not in this map, stop that merge, identify the affected scene and
state, and update the map only after a canon decision. If a worker claims a
runtime capability the current schema or CLI cannot express, record the gap,
preserve the narrative contract, and route the narrow request to its owning
Core or Engine task.
