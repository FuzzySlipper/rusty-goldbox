# Blackapple Brugh runtime contract

This coordinator-owned document turns the settled Blackapple canon into exact
module, area, entry, event, and scalar-variable interfaces for independent
content workers. The coordinator is the final owner of shared IDs, cross-area
links, and campaign variables. A worker may propose a shared change in its
handoff, but must not silently add an alias, collection, or cross-area state
name.

This is an authoring contract, not a second module schema. The CLI schema is
authoritative for every JSON field and example:

~~~
goldbox schema --json
goldbox schema module --json
goldbox schema campaign --json
goldbox schema variable --json
goldbox schema area --json
goldbox schema events --json
goldbox module validate <module> --json
~~~

This contract records stable interfaces and acceptance semantics independently
of implementation status. Current validation, authoring progress, and review
state belong in Den. A missing dependency is a diagnostic to repair or report;
it is never permission to add a fake module.

## Module and workspace boundary

The runtime set has exactly these module IDs and roles:

| Module ID | Kind | Stable contents | Required modules |
| --- | --- | --- | --- |
| blackapple-brugh | campaign | campaign start, five areas, events, NPCs, scalar variables, adapted prose and encounters | fifth-srd:^0.1.0, blackapple-fae:^0.1.0, blackapple-art:^0.1.0 |
| blackapple-fae | extension | concrete Blackapple data such as converted fae, ibix, NPC, encounter, condition, and fifthsave_int definitions | fifth-srd:^0.1.0 and no campaign |
| blackapple-art | assets | original logical pictures, figures and wall media | none unless its own asset dependency is approved |
| fifth-srd | ruleset | existing SRD 5.2.1 rules and economy | existing repository module |

blackapple-brugh never requires a campaign. blackapple-fae must not require
blackapple-brugh; the dependency direction is blackapple-brugh -> blackapple-fae
-> fifth-srd and blackapple-brugh -> blackapple-art, with no cycle. Rules,
checks and conditions stay in module data. A missing primitive is a Core task
and schema/test concern, never C# or embedded module code.

The source workspace is represented by the checked-in goldbox.json:

~~~json
{
  "modules": ["../../modules"],
  "authoring": {
    "modules": [
      "modules/blackapple-brugh",
      "modules/blackapple-fae",
      "modules/blackapple-art"
    ],
    "staging": ".goldbox/staged",
    "exports": "exports"
  }
}
~~~

modules is the dependency search directory for the existing fifth-srd.
authoring.modules names the three independent runtime module roots, including
the extension and asset roots so a fresh worker sees the intended split.
Generated .goldbox/staged and exports roots are output only and are ignored by the
repository. Canon, prompts, references, accepted/rejected art, scripts and
receipts remain outside runtime module roots. workspace build/export operate on
these explicit roots; no hand-created container or local identity hash is
allowed.

The campaign module root carries the complete
LICENSE-CC-BY-SA-4.0.txt and relevant PROVENANCE.md, copied from the retained
campaign source. The same attribution and rights boundary travels with any
campaign export. The source text, maps, floorplans, diagrams, charts and forms
are the licensed adaptation material; source illustrations and PDF page images
are excluded. blackapple-fae and blackapple-art workers must carry their own
module-root rights files before those modules can be accepted. fifth-srd keeps
its existing separate licence.

## Canonical IDs and local IDs

The campaign definition is campaign.blackapple-brugh, mapped to
blackapple_brugh in blackapple-brugh. The five logical areas map as follows:

| Canon area | Runtime definition | Worker |
| --- | --- | --- |
| area.blackapple | blackapple | village + environs |
| area.environs | environs | village + environs |
| area.brugh.l1 | brugh_l1 | Level 1 |
| area.brugh.l2 | brugh_l2 | Level 2 |
| area.brugh.l3 | brugh_l3 | Level 3 |

Source C1-C27 labels remain traceability labels. Runtime definition IDs use
lowercase underscores and must be added to module provenance when authored.

## Stable area entries

These names are public cross-area interfaces. Workers choose each map's at and
facing in its area definition. Adding or renaming a cross-area entry requires
coordinator approval; an area worker may add a private entry only with its area
prefix and no cross-area dependency.

| Area | Required entry | Used by | Contract |
| --- | --- | --- | --- |
| blackapple | north_road | campaign start | guardhouse opening and evt_root_intro |
| blackapple | from_environs | environs return | ordinary forest-loop return |
| blackapple | figwort_manor | route preparation and Figwort return | family/mirror location; not the Brugh exit |
| blackapple | jolly_fox | hub rest and return triage | safe village base |
| blackapple | priory | care and double custody | safe recovery/containment |
| environs | from_blackapple | village departure | forest-loop entry |
| environs | hen_teeth | alternate route and return | forest tavern mirror and safe rest |
| environs | sanitarium | Arthur investigation | sensitive scene entry |
| environs | shrine_confession | optional cure | consent-forward shrine |
| environs | faehill | forest warning | landmark; no physical Brugh entrance |
| environs | tenpenny_wood | ecology route | optional forest choice |
| brugh_l1 | c1_entry | shared ingress | C1 coatroom; entry-only external mirror |
| brugh_l1 | from_l2 | Level 2 return | whole-party connection |
| brugh_l1 | guest_rooms | favorable C13 reaction | C8/C9 guest-room arrival; whole-party position |
| brugh_l2 | from_l1 | Level 1 descent | whole-party arrival |
| brugh_l2 | from_l3 | Level 3 return | whole-party arrival |
| brugh_l2 | server_area | C14 server-area return | whole-party dumbwaiter arrival |
| brugh_l3 | from_l2 | Level 2 descent | whole-party arrival |
| brugh_l3 | library_passage | C5 secret route | whole-party Level 3 library arrival |
| brugh_l3 | pit | C13a whole-party fall | whole-party pit arrival |
| brugh_l3 | kitchen_lift | C14/C21a dumbwaiter | whole-party kitchen-lift arrival |
| brugh_l3 | c27_vault | Level 3 finale | C27 treasure vault and only ordinary Brugh return point |

The required movement guarantees are:

~~~
blackapple.from_environs -> village scenes
environs.from_blackapple -> forest scenes
prepared route -> brugh_l1.c1_entry
brugh_l1 -> brugh_l2.from_l1 -> brugh_l3.from_l2
brugh_l1.library secret -> brugh_l3.library_passage
brugh_l2.favorable dinner -> brugh_l1.guest_rooms
brugh_l2.C13a -> brugh_l3.pit
brugh_l2.server_area <-> brugh_l3.kitchen_lift
brugh_l3.c27_vault -> one selected mundane return entry
~~~

C1 is never a return link. C27 is the sole Brugh exit and may return to
Figwort or Hen's Teeth according to mirror_route. C27 does not test Arthur, any
named child, or a generic all-rescued flag. Partial rescue and evidence-only
escape are valid. Mirror #4 is unavailable or labelled as a future route.

The map workers may use any connected geometry that preserves these entries,
the keyed-room identity, and shared geometry for paired views. A perception
mode never selects another area, entry, route, enemy set, or consequence state.

## Event ownership and shared chains

Every event ID in blackapple-brugh starts with a reserved prefix. A worker owns
only its prefix and assigned area/event files. It may reference another prefix
through the interfaces below, but must not define or rewrite an event owned by
another prefix.

| Prefix | Owner | Scope |
| --- | --- | --- |
| evt_root_ | final coordinator | campaign intro and wiring |
| evt_entry_ | final coordinator | mirror preparation, ingress, perception reset and C1 handoff |
| evt_return_ | final coordinator | C27 return, triage, accounting boundary and postscript wiring |
| evt_defeat_ | final coordinator | ordinary unrecoverable defeat before return |
| evt_xp_ | final coordinator | fifth-srd experience rewards |
| evt_vil_ | village worker | Blackapple hub |
| evt_env_ | environs worker | forest, Faehill, Hen's Teeth and optional environs |
| evt_l1_ | Level 1 worker | C1-C11 and Level 1 transitions |
| evt_l2_ | Level 2 worker | C12-C17 and Level 2 transitions |
| evt_l3_ | Level 3 worker | C18-C27, rescue and return choice handoff |
| evt_fin_ | finale worker | return account, six-ending choice and epilogue |

The following shared IDs are reserved now so workers can reference them
without inventing aliases:

| Event ID | Kind/contract | Reads | Writes or next interface |
| --- | --- | --- | --- |
| evt_root_intro | text/wiring | none | starts at blackapple.north_road, then authored village arrival evt_vil_arrival |
| evt_vil_arrival | village arrival | arrival_seen | authored text at blackapple.north_road; continues village opening |
| evt_entry_prepare_figwort | route chain | arrival_seen, mirror_rhyme_known, figwort_stance, village_trust | writes mirror_route, entry_ready, mirror_active |
| evt_entry_prepare_hen_teeth | route chain | mirror_rhyme_known, flynn_trust, forest_stance | writes mirror_route, entry_ready, mirror_active |
| evt_entry_midnight | branch/menu | entry_ready, mirror_route | writes portal_window_open, then evt_entry_brugh_perception |
| evt_entry_brugh_perception | perception | active party, portal_window_open | blackapple-fae:fifthsave_int, scope brugh, reset true, success truth, failure glamour |
| evt_entry_c1 | teleport/wiring | mirror_route, portal_window_open | whole party to brugh_l1.c1_entry, sets brugh_entered, then explicit evt_l1_arrival_c1 |
| evt_l1_arrival_c1 | Level 1 arrival text/views | brugh_entered and per-member perception | C1 text/perception presentation; continues C1 scene |
| evt_l1_arrival_guests | Level 1 arrival text/views | court_favor and C13 reaction | C8/C9 guest-room presentation; continues local scene |
| evt_l1_arrival_from_l2 | Level 1 arrival text/views | Level 2 return state | starts the optional upward Level 1 return scene |
| evt_l2_arrival_from_l1 | Level 2 arrival text/views | Level 1 exit state | starts the Level 2 C12-C17 scene chain |
| evt_l2_arrival_from_l3 | Level 2 arrival text/views | Level 3 return state | starts the Level 2 return chain |
| evt_l2_arrival_server_area | Level 2 arrival text/views | route_dumbwaiter | starts the C14 server-area scene |
| evt_l3_arrival_from_l2 | Level 3 arrival text/views | Level 2 exit state | starts the C18-C27 scene chain |
| evt_l3_arrival_library | Level 3 arrival text/views | route_secret_library | starts the C5 secret-passage scene |
| evt_l3_arrival_pit | Level 3 arrival text/views | C13a reaction state | starts the whole-party C18 pit recovery scene |
| evt_l3_arrival_kitchen_lift | Level 3 arrival text/views | route_dumbwaiter | starts the C21a kitchen-lift scene |
| evt_return_from_c27_figwort | teleport/wiring | mirror_route, child statuses | whole party to blackapple.figwort_manor, then evt_return_begin |
| evt_return_from_c27_hen_teeth | teleport/wiring | mirror_route, child statuses | whole party to environs.hen_teeth, then evt_return_begin |
| evt_return_begin | triage boundary | all child/double statuses, recovery flags | sets brugh_exited and recovery_return_triage, then evt_fin_return_start |
| evt_fin_return_start | finale entry | brugh_exited, recovery_return_triage | exact external finale entry after evt_return_begin; starts return account |
| evt_return_postscript | final wiring | ending_id and epilogues | postscript or end |
| evt_defeat_lost_in_brugh | set/end chain | ordinary Engine party defeat | sets ending_id to ending.lost_in_brugh; leaves child/double state unchanged |
| evt_xp_investigation | guarded experience | Act I/II completion, opening_xp_awarded | sets opening_xp_awarded and awards ordinary fifth-srd XP once |
| evt_xp_brugh | guarded experience | meaningful rescue/negotiated escape, middle_xp_awarded | sets middle_xp_awarded and awards ordinary fifth-srd XP once |

The sender/arrival pairs below are part of the interface. The sender's
teleport event must set its named next event; it must not rely on the
destination cell's event firing:

| Sender event | Destination | Required next arrival event |
| --- | --- | --- |
| evt_entry_c1 | brugh_l1.c1_entry | evt_l1_arrival_c1 |
| evt_l1_to_l2 | brugh_l2.from_l1 | evt_l2_arrival_from_l1 |
| evt_l2_to_l1 | brugh_l1.from_l2 | evt_l1_arrival_from_l2 (if upward retreat is authored) |
| evt_l1_secret_library | brugh_l3.library_passage | evt_l3_arrival_library |
| evt_l2_to_l3 | brugh_l3.from_l2 | evt_l3_arrival_from_l2 |
| evt_l3_to_l2 | brugh_l2.from_l3 | evt_l2_arrival_from_l3 |
| evt_l2_to_guest_rooms | brugh_l1.guest_rooms | evt_l1_arrival_guests |
| evt_l2_to_pit | brugh_l3.pit | evt_l3_arrival_pit |
| evt_l2_to_server_area | brugh_l2.server_area | evt_l2_arrival_server_area |
| evt_l2_to_kitchen_lift | brugh_l3.kitchen_lift | evt_l3_arrival_kitchen_lift |
| evt_return_from_c27_figwort | blackapple.figwort_manor | evt_return_begin |
| evt_return_from_c27_hen_teeth | environs.hen_teeth | evt_return_begin |

evt_root_intro, evt_entry_*, evt_return_*, evt_defeat_*, and evt_xp_* are
coordinator-owned integration points. Workers call them by reference and report
a signature change in shared_change_proposals. A normal encounter loss is a
recovery or retreat branch where possible; it calls the defeat chain only when
the Engine's ordinary unrecoverable party-defeat state ends play. No worker
adds a second defeat loop, clock, scheduler, renderer, or party-position map.

An explicit teleport does not run the destination cell event. Every sender
chain therefore names its authored destination arrival event: C1 uses
evt_l1_arrival_c1; favorable C13 uses evt_l1_arrival_guests; Level 1/3
transitions use evt_l2_arrival_from_l1, evt_l2_arrival_from_l3,
evt_l3_arrival_from_l2, evt_l3_arrival_library, evt_l3_arrival_pit, and
evt_l3_arrival_kitchen_lift; the server-area lift uses
evt_l2_arrival_server_area. The Level 2 to Level 1 return uses a local
evt_l1_arrival_from_l2 handoff when that optional upward route is authored.
These arrival events own the first text/perception presentation after the
teleport; a destination cell event is never relied upon implicitly.

The source/adaptation describes a ten-minute midnight ingress window. The
current scalar contract represents it as one explicit portal_window_open gate
and closes it after C1 entry. Current CLI/Core has no minute-level clock or
scheduled close. Authors must not add a local timer or claim exact
elapsed-minute enforcement; if that precision is required for acceptance, the
coordinator routes a narrow Core capability while retaining this C1/C27
interface.

## Per-member perception and shared map state

The Brugh is one shared party position and one set of doors, searches,
encounters and consequences. At evt_entry_brugh_perception, Core resolves the
declared fifthsave_int check once per active member through Engine Random and
persists that member's opaque mode until the party leaves scope brugh.
blackapple-fae:fifthsave_int is data shaped like the fifth-srd check: 1d20 plus
the character's Intelligence save value against 10, truth on success and
glamour on failure.

Every paired text event supplies both views:

~~~json
"views": [
  { "mode": "truth", "text": "..." },
  { "mode": "glamour", "text": "..." }
]
~~~

The ordinary member view selects presentation. It does not move the party,
reroll a member, alter child/double status, or create a second map. The minimum
paired set is C1, C2, C6/C7, C11, C12, C17, C26 and C27. Other rooms may use a
single text with subtle cues. The campaign does not declare a glamour_truth map
variable; the per-member scope/mode belongs to Core's character/save seam and
is read through existing presentation controls.

## Party and progression invariants

campaign.blackapple-brugh declares party min 4 and max 6. The authored party
starts at level 1 and is expected to reach level 3; the module does not promise
the product-wide twelve-member platform capability. Wylda may join before
entry and Master Ned may join after rescue, subject to the same maximum.
Children are scene records and never combat-party members.

Advancement uses ordinary fifth-srd experience events. There is no paid-training
event or campaign-specific advancement owner. Rest, shops, temple care and XP
use existing ruleset definitions and Engine-backed state. A worker that wants a
new check, effect, time behavior or presentation primitive records the exact
unsupported capability and receiving owner rather than adding local C# or a
parallel state path.

## Scalar state inventory

All cross-area story state is a campaign-scoped scalar in
modules/blackapple-brugh/variables/. Events read and write the exact names
below. Status values are text literals, not definition IDs, and workers preserve
the listed vocabulary.

### Seven children and seven doubles

Each child begins captive and can be captive, freed, returned, or unknown. A
child is returned only after family handoff. Each matching double begins masked
and can be masked, exposed, contained, escaped, removed, or unknown. Double
status never changes child status.

| Source name | Child scalar | Double scalar | Optional consequence scalars |
| --- | --- | --- | --- |
| arthur_figwort | child_arthur_figwort | double_arthur_figwort | double_arthur_figwort_removal_cause, double_arthur_figwort_evidence |
| amelia_goodall | child_amelia_goodall | double_amelia_goodall | double_amelia_goodall_removal_cause, double_amelia_goodall_evidence |
| bernard_goodall | child_bernard_goodall | double_bernard_goodall | double_bernard_goodall_removal_cause, double_bernard_goodall_evidence |
| giles_weadley | child_giles_weadley | double_giles_weadley | double_giles_weadley_removal_cause, double_giles_weadley_evidence |
| philip_anvil | child_philip_anvil | double_philip_anvil | double_philip_anvil_removal_cause, double_philip_anvil_evidence |
| ursula_cooke | child_ursula_cooke | double_ursula_cooke | double_ursula_cooke_removal_cause, double_ursula_cooke_evidence |
| stevie_leeford | child_stevie_leeford | double_stevie_leeford | double_stevie_leeford_removal_cause, double_stevie_leeford_evidence |

Optional double fields are text scalars initialized to ''. Evidence is written
when identity or disposition is established; removal_cause is written for
removed. There is no keyed collection, mask_problem flag, generic rescued
counter, or parallel event log. Finale branches use the seven explicit
scalars and ordinary repeated branch events.

### Ending and additive epilogues

ending_id starts as '' and is set exactly once at final choice or terminal
defeat. Its only final values are:

~~~
ending.lost_in_brugh
ending.court_bound
ending.elf_bargain
ending.full_return
ending.partial_return
ending.truth_without_return
~~~

These booleans are independent additive epilogues and never replace ending_id:
epilogue_doubles_contained, epilogue_masks_escaped, epilogue_public_truth,
epilogue_protected_account, epilogue_truth_verified, epilogue_no_proof, and
epilogue_court_terms. All begin false. doubles_contained is true only when
all seven doubles are contained. masks_escaped is true when any double is
escaped, exposed, masked, or unknown. A removed double gets a separate status
paragraph and satisfies neither boolean.

The finale displays all fourteen status scalars before selecting the primary
ending. Its precedence is lost_in_brugh, explicit court_bound, explicit
qualifying elf_bargain, seven returned children (full_return), one to six
returned children (partial_return), then a living zero-return account
(truth_without_return). Defeat leaves child/double values unchanged.

The finale writes elf_bargain_accepted only after the player confirms a
qualifying bargain whose named children are released. It writes
court_service_accepted only after the player explicitly confirms permanent
court service or a court stay with the affected characters and exit choice
shown. These flags are explicit-choice guards for the ending branch; a future
finale may branch directly from the menu result only if it preserves these
same scalar meanings.

### Coordinator-approved additional campaign scalars

These are the only proposed cross-area additions beyond the fixed status,
ending and epilogue contract. They are declared by this scaffold so workers do
not invent competing names.

| Scalar | Type/initial | Meaning |
| --- | --- | --- |
| arrival_seen | boolean/false | North-road opening completed |
| first_help_done | boolean/false | At least one ordinary Act I help route resolved |
| services_jolly_fox_seen | boolean/false | Hub services introduced |
| opening_xp_awarded | boolean/false | Shared opening/investigation XP has been awarded once |
| middle_xp_awarded | boolean/false | Shared Brugh/middle XP has been awarded once |
| village_trust | number/0 | Authored relationship value, neutral at 0 |
| evidence_double_behavior | boolean/false | First reliable double clue |
| evidence_parent_suspicion | boolean/false | Parent interviews establish the pattern |
| evidence_arthur_double | boolean/false | Sanitarium evidence identifies Arthur's double |
| mirror_rhyme_known | boolean/false | Party can prepare a route |
| figwort_stance | text/none | none, watch, rescue, protect, reconcile |
| mirror_route | text/none | none, figwort, hen_teeth |
| entry_ready | boolean/false | An approved route can open |
| evidence_fairy_mirrors | boolean/false | Forest route confirms mirror lore |
| flynn_trust | number/0 | Hen's Teeth relationship value |
| evidence_faehill_history | boolean/false | Faehill/ruins history clue found |
| forest_stance | text/none | none, respect, exploit |
| portal_window_open | boolean/false | One explicit midnight ingress interaction is available |
| mirror_active | boolean/false | Selected external mirror is prepared |
| brugh_entered | boolean/false | C1 ingress completed |
| brugh_exited | boolean/false | C27 return completed |
| brugh_c1_seen | boolean/false | C1 visited |
| entry_portal_covered | boolean/false | C1 entry mirror covered after departure |
| evidence_brugh_layout | boolean/false | Brugh route/layout clue established |
| route_secret_library | boolean/false | C5 secret route discovered |
| court_favor | number/0 | Court relationship value |
| court_alarm | number/0 | Court alert value |
| evidence_gold_threads | boolean/false | Child rescue thread evidence found |
| recovery_sanitarium | boolean/false | Sanitarium harm/recovery route resolved |
| recovery_forest | boolean/false | Forest encounter recovery route resolved |
| recovery_pool | boolean/false | C17 water-risk recovery resolved |
| recovery_pit | boolean/false | C18 pit setback recovery resolved |
| recovery_return_triage | boolean/false | Safe return care completed |
| evidence_brugh_danger | boolean/false | Pit/hazard danger understood |
| route_dumbwaiter | boolean/false | C14/C21a route opened |
| lord_confronted | boolean/false | Optional Elf Lord confrontation occurred |
| elf_bargain_accepted | boolean/false | Player explicitly accepted a qualifying Elf Lord bargain |
| court_service_accepted | boolean/false | Player explicitly accepted permanent court service or a stay |
| elf_lord_reaction | text/curious | curious, amused, suspicious, hostile, bargaining |
| return_account_complete | boolean/false | All fourteen status records displayed and answered |

Workers may add area-scoped variables only for private map details, with an
area prefix (blackapple_, environs_, brugh_l1_, brugh_l2_, or brugh_l3_) and no
cross-area reads. A cross-area need must use this inventory or be proposed to
the coordinator before authoring. Quest IDs remain canon labels; cross-area
effects use the named evidence, stance, route, recovery, or status scalar.

## Worker contracts

Every worker receives current canon files and source mapping as read-only
inputs. The following contracts make reads/writes and ownership explicit.

### Village and environs

Owns areas/blackapple.json, areas/environs.json, and only evt_vil_/evt_env_
event files. Reads village_trust, arrival_seen, mirror_rhyme_known,
figwort_stance, and evidence needed by the sanitarium/forest routes. Writes
first_help_done, services_jolly_fox_seen, village_trust, evidence_double_behavior,
evidence_parent_suspicion, evidence_arthur_double, mirror_rhyme_known,
figwort_stance, evidence_fairy_mirrors, flynn_trust, evidence_faehill_history,
forest_stance, recovery_sanitarium, recovery_forest, and private service flags.
Enters at blackapple.north_road, blackapple.from_environs and
environs.from_blackapple. The authored north-road arrival is evt_vil_arrival.
Hands route preparation and XP to reserved shared events.

### Level 1

Owns areas/brugh_l1.json and only evt_l1_ event files. Enters at
brugh_l1.c1_entry from evt_entry_c1 through evt_l1_arrival_c1, may enter
brugh_l1.guest_rooms through evt_l1_arrival_guests, and returns from Level 2
at brugh_l1.from_l2 through evt_l1_arrival_from_l2 when that optional route is
authored. It hands the party to brugh_l2.from_l1 through
evt_l2_arrival_from_l1. Reads the perception views, evidence_faehill_history,
court values, and fixed statuses needed by C1-C11. Writes brugh_c1_seen,
entry_portal_covered, evidence_brugh_layout, route_secret_library,
child_giles_weadley (C6 sets it to `freed`), evidence_gold_threads, court_favor
and court_alarm. It never writes perception modes or child returned status.

### Level 2

Owns areas/brugh_l2.json and only evt_l2_ event files. Enters at
brugh_l2.from_l1 through evt_l2_arrival_from_l1, can return to
brugh_l2.from_l3 through evt_l2_arrival_from_l3, and uses
brugh_l2.server_area through evt_l2_arrival_server_area. It hands the party to
brugh_l3.from_l2 through evt_l3_arrival_from_l2, to brugh_l1.guest_rooms
through evt_l1_arrival_guests, and to brugh_l3.pit through
evt_l3_arrival_pit. Reads perception views, court values, route_secret_library,
route_dumbwaiter, elf_lord_reaction, and fixed statuses. Writes
child_amelia_goodall, child_bernard_goodall, child_philip_anvil, and
child_ursula_cooke only to their
allowed vocabulary (normally freed), evidence_gold_threads, route_dumbwaiter,
recovery_pool, court_favor, court_alarm and elf_lord_reaction. It never marks
a child returned or resolves final double custody.

### Level 3

Owns areas/brugh_l3.json and only evt_l3_ event files. Enters at
brugh_l3.from_l2 through evt_l3_arrival_from_l2, at
brugh_l3.library_passage through evt_l3_arrival_library, at brugh_l3.pit
through evt_l3_arrival_pit, and at brugh_l3.kitchen_lift through
evt_l3_arrival_kitchen_lift; it reaches brugh_l3.c27_vault. C27 branches only
to evt_return_from_c27_figwort or evt_return_from_c27_hen_teeth; it has no
Arthur prerequisite. Reads all fixed child statuses, mirror_route, court
values, route_dumbwaiter, and perception views. Writes child_stevie_leeford and
child_arthur_figwort only to their allowed vocabulary (normally freed),
evidence_gold_threads, evidence_brugh_danger, recovery_pit,
route_dumbwaiter, lord_confronted, court_favor and court_alarm. It does not
choose the final ending.

### Finale

Owns only evt_fin_ event files and no area map. Enters through the exact
external event evt_fin_return_start after evt_return_begin has set brugh_exited
and recovery_return_triage. Reads all child/double status
and optional fields, village_trust, figwort_stance, elf_lord_reaction, evidence,
recovery fields and mirror_route. Writes child returned values only after
family handoff; each chosen double disposition and evidence/cause; then
return_account_complete, elf_bargain_accepted, court_service_accepted, exactly
one ending_id, and independent epilogues. It references evt_return_postscript
after the final choice and never combines an epilogue into ending_id.

The final coordinator owns evt_root_, evt_entry_, evt_return_, evt_defeat_ and
evt_xp_ definitions after checking worker links. Shared chains read/write only
this inventory and ordinary Core state. Workers must not add another start,
return route, defeat owner, XP owner, timer, or party-position map.

## Handoff requirements

Each worker handoff is files-first and includes all four integration fields,
even when a field is empty:

~~~yaml
changed_files: [every changed path]
assumptions: [facts and deliberate choices used]
unresolved: [unsupported behavior, contradiction, or question]
next_command: one exact validation/play command, including --modules and seed when relevant
~~~

The worker packet also carries the installed kit's chapter fields:
entry_contract, exit_contract, allowed_changes, and shared_change_proposals.
Before handoff, run the smallest schema-guided validation and a seeded
headless route covering entry, every branch, revisit and exit. Report separate
source, build, runtime, visible and review claims. A stale or contradictory
handoff is resolved from current files and canon; no revision fence, admission
layer or orchestration service is added.

## Acceptance boundary

This contract establishes interfaces and declarations; it does not claim that
the campaign is playable, exported, visually accepted, balanced, or complete.
The opening checkpoint requires the village/environs slice plus a bounded first
expedition and local resolution path, including the required dependencies and
coordinator entry/return/defeat/XP chains. That checkpoint is narrower than
full-campaign acceptance. Full-campaign acceptance requires all five area/event
slices, the complete fae and art modules, independent module exports, seeded
narrative/balance/save checks, ordinary player-visible validation,
rights/provenance review, and the parent task's final review.
