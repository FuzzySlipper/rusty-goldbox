# Canon index and contract

## Authority

This is the retained campaign canon for `campaign.blackapple-brugh`. The
campaign is a deliberate adaptation of `source.blackapple-brugh-r21` and uses
`ruleset.fifth-srd`. `sample-crypt` remains the repository's small fixture and
is not folded into this campaign.

The source PDF is the supplied *The Blackapple Brugh*, 1st Edition Release 21,
by Kyle Hettinger. Its SHA-256 is
`87e22e0d0bbdb66c54010d4485df05e92d0237b0234c2dffa24298250d4f6de5`.
The source's text, maps, floorplans, diagrams, charts, and forms are CC BY-SA
4.0. Its other artwork is excluded. The legal and attribution boundary is
spelled out in [source-mapping.md](source-mapping.md) and
[PROVENANCE.md](../PROVENANCE.md).

## Stable IDs

IDs are lowercase, ASCII, and stable once implementation begins. `source.*`
IDs describe the supplied publication. `area.*`, `scene.*`, `npc.*`,
`child.*`, `double.*`, `quest.*`, `choice.*`, `ending.*`, and `state.*` are
adaptation IDs. A runtime module may use a qualified module prefix around these
IDs; it must not rename them casually.

The dotted IDs are the authoring namespace, not literal module-definition
strings. Rusty Goldbox module IDs use lowercase hyphens (`blackapple-brugh`,
`blackapple-art`, and optional data-only `blackapple-fae`), while definition
IDs use lowercase underscores. The initial runtime mapping is:

| Canon ID | Runtime definition ID |
| --- | --- |
| `campaign.blackapple-brugh` | `blackapple_brugh` in module `blackapple-brugh` |
| `area.blackapple` | `blackapple` |
| `area.environs` | `environs` |
| `area.brugh.l1` / `.l2` / `.l3` | `brugh_l1` / `brugh_l2` / `brugh_l3` |
| `scene.mirror_entry` / `.mirror_return` | `scene_mirror_entry` / `scene_mirror_return` |
| `npc.wylda-figwort` / `.master-ned` | `wylda_figwort` / `master_ned` |
| `check.fifthsave-int` | `fifthsave_int` in `blackapple-fae` |

Child and double IDs map the same way (`child.arthur-figwort` becomes
`arthur_figwort`; `double.arthur-figwort` becomes `double_arthur_figwort`).
Implementers should add the final mapping to source provenance when a
definition is created, rather than putting dots into JSON IDs that the loader
will reject.

| Kind | IDs | Meaning |
| --- | --- | --- |
| Campaign | `campaign.blackapple-brugh` | The complete adapted campaign. |
| Source | `source.blackapple-brugh-r21` | The supplied 1e Release 21 publication. |
| Ruleset | `ruleset.fifth-srd` | The repository's SRD 5.2.1 dependency. |
| Hub | `area.blackapple` | Revisitable village services, families, and leads. |
| Environs | `area.environs` | Roads, forest, Faehill, shrine, and optional expeditions. |
| Brugh | `area.brugh.l1`, `.l2`, `.l3` | The three source dungeon levels. |
| Entry/return | `scene.mirror_entry`, `scene.mirror_return` | Scripted mirror access and exit. |
| Investigation | `scene.arrival`, `scene.hub_investigation`, `scene.environs_hooks` | Arrival and evidence gathering. |
| Rescue | `scene.brugh_search`, `scene.children_rescue` | Search, release, and escape. |
| Resolution | `scene.return_resolution` | Village accounting and ending selection. |
| State | `child_*`, `double_*`, `ending_id`, `epilogue_*`, `state.glamour_truth` | Scalar campaign facts described below; member perception remains the #9362 seam. |

Child IDs are immutable and identify the real children, regardless of who is
wearing the matching mask:

| Child | Age in source | Brugh duty | Source double |
| --- | ---: | --- | --- |
| `child.arthur-figwort` | 11 | Wine-cellar guard, C23 | `double.arthur-figwort` |
| `child.amelia-goodall` | 12 | Dining-hall server, C13 | `double.amelia-goodall` |
| `child.bernard-goodall` | 10 | Dining-hall server, C13 | `double.bernard-goodall` |
| `child.giles-weadley` | 12 | Bluehouse gardener, C6 | `double.giles-weadley` |
| `child.philip-anvil` | 10 | Server-area attendant, C14 | `double.philip-anvil` |
| `child.ursula-cooke` | 5 | Timpani musician, C12a | `double.ursula-cooke` |
| `child.stevie-leeford` | 7 | Kitchen sous-chef, C21 | `double.stevie-leeford` |

## Scalar runtime variable contract

The dotted child and double IDs above are canon labels. Runtime campaign
definitions use the existing scalar `Variables` map; they do not create a
keyed child/double collection or a parallel dictionary. Each status below is a
string variable with only the listed vocabulary. The optional evidence fields
are strings written only when a scene has evidence or a removal cause to
retain.

| Source name | Child status variable | Double status variable | Optional double fields |
| --- | --- | --- | --- |
| `arthur_figwort` | `child_arthur_figwort` | `double_arthur_figwort` | `double_arthur_figwort_removal_cause`, `double_arthur_figwort_evidence` |
| `amelia_goodall` | `child_amelia_goodall` | `double_amelia_goodall` | `double_amelia_goodall_removal_cause`, `double_amelia_goodall_evidence` |
| `bernard_goodall` | `child_bernard_goodall` | `double_bernard_goodall` | `double_bernard_goodall_removal_cause`, `double_bernard_goodall_evidence` |
| `giles_weadley` | `child_giles_weadley` | `double_giles_weadley` | `double_giles_weadley_removal_cause`, `double_giles_weadley_evidence` |
| `philip_anvil` | `child_philip_anvil` | `double_philip_anvil` | `double_philip_anvil_removal_cause`, `double_philip_anvil_evidence` |
| `ursula_cooke` | `child_ursula_cooke` | `double_ursula_cooke` | `double_ursula_cooke_removal_cause`, `double_ursula_cooke_evidence` |
| `stevie_leeford` | `child_stevie_leeford` | `double_stevie_leeford` | `double_stevie_leeford_removal_cause`, `double_stevie_leeford_evidence` |

The return contract also uses these existing scalar variables:

| Variable | Type | Values / meaning |
| --- | --- | --- |
| `ending_id` | string | One primary ending ID from [endings.md](endings.md). |
| `epilogue_doubles_contained` | boolean | True only when all seven double status variables are `contained`. |
| `epilogue_masks_escaped` | boolean | True when any double status is `escaped`, `exposed`, `masked`, or `unknown`. |
| `epilogue_public_truth`, `epilogue_protected_account`, `epilogue_truth_verified`, `epilogue_no_proof`, `epilogue_court_terms` | boolean, optional | Additive disclosure, evidence, and bargain consequences; each is an independent scalar. |

## State contract

The canon names story facts; Core remains the sole owner of their runtime
values. Runtime definitions use the exact scalar variables in the contract
above, backed by the existing campaign `Variables` maps; they do not add a
keyed child/double collection or a parallel dictionary. They must preserve
these semantics:

* The child-status group is the canon grouping for the seven
  `child_<source_name>` scalar variables in the table above. Each has the value
  `captive`, `freed`, `returned`, or `unknown`; a child is not counted as
  returned merely because the party saw the child.
* The double-status group is the canon grouping for the seven
  `double_<source_name>` scalar variables in the table above. Each has the
  value `masked`, `exposed`, `contained`, `escaped`, `removed`, or `unknown`.
  `masked` means the identity is not resolved; `exposed` means the identity is
  known but the double has no safe custodian; `contained` means a named
  custodian holds it; `escaped` means it left the party's custody; `removed`
  means it is no longer active (including a combat death), with the cause kept
  in the consequence record. A double's status never changes the matching
  child's status.
* `state.glamour_truth` is per active party member. Each member receives one
  deterministic mode on Brugh entry, `glamour` or `truth`, and retains it until
  the party leaves. A member's mode affects descriptions and readable clues,
  not whether the underlying room, route, child, or consequence exists.
* `state.mirror_route` records whether entry was through `figwort` or
  `hen-teeth`, which return mirrors are active, and whether the party has
  already crossed. After the route is prepared, the external mirror is open
  for a ten-minute midnight ingress window (an adaptation timing decision);
  C1 is entry only and the C27 vault supplies the asymmetric return.
* `state.village_trust` is a small authored relationship value used for
  villagers' help and the return scene. `state.figwort_stance` records the
  Figwort family's current request (`watch`, `rescue`, `protect`, or
  `reconcile`). `state.elf_lord_reaction` records the current court stance
  (`curious`, `amused`, `suspicious`, `hostile`, or `bargaining`).
* `state.recovery_flags` is a canon grouping for individual scalar recovery
  booleans, such as a sickened member receiving care, a separated member
  rejoining, or a closed route being reopened. It is not a keyed collection or
  a second event log.
* `ending_id` is set exactly once: during return resolution for a living party,
  or at terminal defeat before return as `ending.lost_in_brugh`. It is one of
  the stable primary ending IDs in [endings.md](endings.md). The return scene
  writes the independent scalar booleans `epilogue_doubles_contained` and
  `epilogue_masks_escaped`, plus any optional disclosure/evidence booleans in
  the table above. These flags select closing paragraphs without replacing or
  combining the primary ending.

The authored party is 4–6 active members at levels 1–3. `npc.wylda-figwort`
is an optional recruit before entry. `npc.master-ned` is a possible Brugh
rescue companion and does not increase the opening party requirement. No
source child joins the fighting party.

## Source fact, adaptation decision, open runtime question

Every canon document uses these labels:

* **Source fact** is directly supported by the supplied Release 21 text or its
  source notice. It is not a claim that the current engine already implements
  it.
* **Adaptation decision** is an intentional change or conversion for a digital
  fifth-srd campaign. It must not be attributed to Hettinger.
* **Open runtime question** is a concrete implementation seam. It belongs in
  the feature matrix until the owning task answers it; it is not permission to
  invent a parallel state owner.

Read the files in this order: [premise.md](premise.md), [beats.md](beats.md),
[characters.md](characters.md), [locations.md](locations.md),
[endings.md](endings.md), [source-mapping.md](source-mapping.md), then
[scene-matrix.md](scene-matrix.md).
