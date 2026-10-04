# Endings and consequence accounting

The return scene is a deliberate resolution phase, not a generic victory text.
It reads the seven child records, the seven double records, the route used,
and the relationships the party changed. It must account for every child and
double by ID, including an explicit `unknown` disposition where the party has
no reliable evidence. Runtime authors read and write the scalar variables
listed in [canon/README.md](README.md), not a keyed collection.
The final choice sets exactly one primary `ending_id`, then writes the
independent scalar booleans `epilogue_doubles_contained` and
`epilogue_masks_escaped` plus any optional disclosure/evidence booleans. This
keeps rescue success, court bargains, and failure comparable while still
showing every child and double consequence.

## Selection contract

The six primary ending IDs are `ending.lost_in_brugh`,
`ending.court_bound`, `ending.elf_bargain`, `ending.full_return`,
`ending.partial_return`, and `ending.truth_without_return`. The final choice
uses this precedence, after the party's records have been displayed:

1. If ordinary defeat or an unrecoverable state ended play before return, set
   `ending.lost_in_brugh`.
2. If the player explicitly accepts permanent court service or a court stay,
   set `ending.court_bound`.
3. If the player accepts a bargain whose terms release the named children, set
   `ending.elf_bargain`.
4. Otherwise, seven `returned` children set `ending.full_return`.
5. Otherwise, one to six `returned` children set `ending.partial_return`.
6. Otherwise, a living party with zero `returned` children sets
   `ending.truth_without_return`. Credible evidence adds
   `epilogue_truth_verified`; a retreat with no reliable evidence adds
   `epilogue_no_proof` and still receives a concrete next step.

`ending.doubles_contained` and `ending.masks_escaped` remain stable authoring
IDs, but they are additive epilogue labels, not values written to `ending_id`.
The corresponding scalar booleans are `epilogue_doubles_contained` and
`epilogue_masks_escaped`, set after all seven double variables have been
accounted for. `epilogue_doubles_contained` is true only when all seven doubles
are `contained`; an `escaped`, `exposed`, `masked`, `removed`, or `unknown`
record prevents it. `epilogue_masks_escaped` is true when any double is
`escaped`, `exposed`, `masked`, or `unknown`, and its text names the
uncertainty. A `removed` double gets its own status paragraph and does not
satisfy either epilogue boolean. Other optional scalar booleans are named
`epilogue_public_truth`, `epilogue_protected_account`,
`epilogue_truth_verified`, `epilogue_no_proof`, and `epilogue_court_terms`.
The renderer must print all seven child variables and all seven double
variables before the primary ending text and any selected epilogue paragraphs.

## Required return accounting

Before an ending can be selected:

1. All seven `child_<source_name>` variables and all seven matching
   `double_<source_name>` variables listed in [README.md](README.md) are
   present in the return account. Each gets exactly one status, even if that
   status is `unknown`.
2. Each `child_<source_name>` variable is `returned`, `freed` but not yet
   returned, `captive`, or `unknown`. A child who is only seen in the Brugh is
   still `captive`.
3. Each `double_<source_name>` variable is `masked`, `exposed`, `contained`,
   `escaped`, `removed`, or `unknown`. `masked` means the identity is not
   resolved; `exposed` means the identity is known but no custodian has been
   chosen; `contained` names a safe custodian; `escaped` left the party's
   custody; `removed` is no longer active, including a combat death; and
   `unknown` means the party has no reliable current account. Exposing a double
   is evidence first; a later custody choice may move it to another status.
4. The party chooses a response to each known double: contain with the priory
   or manor, release toward the forest, expose publicly, or keep the identity
   secret. The choice is shown with the consequence before confirmation.
5. Parents are told what the party knows. The party may be honest, careful, or
   evasive. A careful account can protect a frightened child without erasing
   the truth from the state.
6. The party receives recovery at a safe location before the final text. The
   source's one-day mortal-world compression is retained as a narrative fact;
   it is not a reason to skip ordinary injury, resource, or save handling.

## Ending IDs

The dotted IDs below map to underscore event IDs in the campaign module, for
example `ending.full_return` to `ending_full_return`.

### `ending.full_return` — all the children home

**Eligibility:** all seven children are `returned`; the seven double records
have been accounted for (including any `unknown` statuses); and the party did
not accept permanent court service. No double must be contained or even
identified for this primary ending.

**Choice:** the party chooses either a public truth or a protected family
account. Public truth raises long-term village and priory tension but prevents
the families from being isolated. A protected account lets parents control the
first announcement and gives the party more time to contain the doubles.

**Outcome:** each family receives its child, the Figworts pay for Arthur if he
is returned, and Goodall's reward is granted only when Amelia or Bernard is
actually home. The village is grateful but must live with evidence that its old
fairy story was incomplete. Doubles may remain unresolved; all seven statuses
are named and their uncertainty determines the additive epilogue without
changing this full-rescue primary ending.

### `ending.partial_return` — a hard-won partial rescue

**Eligibility:** at least one child is `returned`, fewer than seven are
`returned`, and the party has exited alive.

**Choice:** prioritize a family, the most endangered child, or a future route
back to the Brugh. The choice changes which household receives immediate aid
and whether the village sees the party as rescuers or dangerous witnesses.

**Outcome:** returned children are named and reunited. Freed-but-not-returned,
captive, or unknown children remain explicit in the epilogue, with a concrete lead or cost if a
future continuation is authored. The current campaign closes after this
account; any later rescue is an optional postscript, not an implied unresolved
runtime loop. A partial ending is not treated as failure; the consequences are
bittersweet because the party chose whom it could save.

### `ending.truth_without_return` — evidence carried home

**Eligibility:** the party exits alive with zero `returned` children, or
chooses to retreat before rescue. Credible evidence (`evidence.brugh_layout`,
a mask, a freed witness, or a child's object) is recorded when present, but is
not required for this fallback ending.

**Choice:** publish the evidence immediately, ask the priory to prepare a
rescue, or keep the route secret while recovering.

**Outcome:** the ending names every child as freed, captive, or unknown when it
has not been returned, and records
whether each double remains masked, exposed, contained, escaped, removed, or
unknown. With evidence, the village receives a truthful warning without a
rescue claim and gets `epilogue_truth_verified`; without evidence, it receives
an uncertain missing-child account and gets `epilogue_no_proof`. Both are
valid outcomes of a careful retreat and keep a future rescue possible without
pretending the current campaign solved it.

### `ending.elf_bargain` — a bargain with the court

**Eligibility:** the Elf Lord is `bargaining` or `curious`, the party has a
surviving negotiator, and the chosen terms explicitly release the named
children. The bargain cannot require the party to leave a child in permanent
servitude as an invisible cost.

**Possible terms:** the court keeps its privacy; the party returns a court
object; the village stops digging into the Faehill; or the party promises a
future audience. Terms are shown in full before confirmation.

**Outcome:** the agreed children return, the court survives, and the village
must live with an uncomfortable truce. `elf_lord_reaction` remains
`bargaining`; a future visit is possible. If a term is broken, the next story
starts with an explicit consequence rather than a hidden betrayal.

### `ending.doubles_contained` — additive epilogue: the mask problem is solved

This is an epilogue ID, never a value of `ending_id`.

**Eligibility:** all seven double records are `contained` by the priory, manor,
or another named custodian, and every returned child has a safe family
handoff. It may follow a full or partial rescue. A released, escaped, exposed,
removed, masked, or unknown double prevents this epilogue label.

**Choice:** the party's earlier per-double choices determine this label; the
epilogue does not silently convert release into containment. Public containment
gives villagers clarity but can produce fear; private containment lowers
immediate panic but puts responsibility on a small group.

**Outcome:** the chosen custodial relationship is recorded. The ending is
available as a distinct epilogue when the party treats the doubles as living
actors rather than loot or a single boss.

### `ending.masks_escaped` — additive epilogue: the village survives with loose threads

This is an epilogue ID, never a value of `ending_id`.

**Eligibility:** one or more doubles are `escaped`, `exposed`, `masked`, or
`unknown` at the end, and the party has returned at least one child or has a
living retreat account. An `unknown` record is explicitly named as uncertainty,
not treated as contained.

**Outcome:** the village gains a warning, but daily life remains uncertain. A
double may be hunted, reconciled with, or lost to the forest in a later
campaign. The epilogue names every escaped, exposed, masked, and unknown
identity; a `removed` identity receives its own consequence paragraph.

### `ending.court_bound` — an informed surrender

**Eligibility:** the player explicitly chooses service or a court stay after a
clear explanation, all characters affected by the choice are identified, and
the party has a viable exit choice before confirmation.

**Outcome:** the campaign closes on a strange courtly bargain. This is a
fictional consequence of player choice, never a punishment triggered by a
failed perception check. Children not released remain marked as captive; the
ending cannot convert them to “rescued” because the party stayed.

### `ending.lost_in_brugh` — catastrophic failure

**Eligibility:** the engine's ordinary party-defeat or unrecoverable state
ends play before the party can return. This is the failure record, not a secret
story requirement.

**Outcome:** no child or double status is rewritten to make the result look
better. The save remains inspectable, and a new run can choose different routes.

## Double-resolution choices

Each double gets the same four readable choices when identified:

| Choice ID | Immediate action | Cost / later effect |
| --- | --- | --- |
| `choice.double_contain` | Priory, manor, or another named custodian holds the double safely | Sets `contained`; supports the additive `ending.doubles_contained` epilogue only when all seven double records are in custody. |
| `choice.double_expose` | Tell the village and show evidence | Sets `exposed` and raises panic; it prevents a masked double from quietly repeating the swap. |
| `choice.double_release` | Return the double to the forest with a cu-sidhe route | Sets `escaped`; preserves life and avoids a cell, and can set additive `ending.masks_escaped`. |
| `choice.double_hide` | Keep the identity secret while arranging a private handoff | Leaves the record `masked` until a later resolution; protects a family from immediate shame and creates a later reveal risk. |

Killing a double is a possible combat outcome but not a listed moral choice.
The state records `contained` only when a surviving or defeated double is in a
named safe custody; a corpse is recorded as `removed`, with its prior evidence
and cause retained in the consequence record. Removal does not prove rescue of
the child.

## Recovery after a poor ending

The return scene always supplies one concrete next step: a priory rescue plan,
Wylda's mirror knowledge, Flynn's route, a child object, or a court bargain.
This keeps partial rescue and evidence-only endings meaningful without erasing
their cost. The campaign does not auto-retry the same room, resurrect a dead
character, or silently restore a missing child.
