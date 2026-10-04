# Acts, quests, and consequences

The beat IDs below are stable authoring IDs. A runtime event may split a beat
into several definitions, but it should keep the beat ID in its source comment
or provenance field. `reads` and `writes` describe the intended Core state;
they are not a competing state store.

## Act I — A village that knows something is wrong

The act ends when the party has a credible mirror lead and a reason to return
to Blackapple after the expedition.

| Beat | Scene and play | Reads | Writes / consequence |
| --- | --- | --- | --- |
| `beat.arrival` | **North road arrival.** Pay or question the guard, see horseshoes over every doorway, and receive the Figwort dinner invitation. The opening gives the party a safe place to ask questions. | none | `village_trust` starts neutral; `arrival_seen=true`. |
| `beat.first_help` | **One ordinary problem.** The party may return an escaped patient alive, save the poisoned poet, handle wild dogs, or help at the priory. At least one route is noncombat. | `village_trust` | `village_trust` up or down; one service or rumor unlocked. |
| `beat.inn_base` | **Jolly Fox base.** Eat, rest, buy basic supplies, meet Alistair and Sir Ruprecht, and hear two rumors. Resting is safe but consumes campaign time. | `village_trust` | `services.jolly_fox_seen`; optional `quest.work_board`. |
| `beat.child_misbehavior` | **The pattern.** Witness a wicked child stealing, laughing at a fire, or damaging a home. A careful party can notice that the child avoids a familiar song or repeats a phrase. | `arrival_seen` | `evidence.double_behavior`; `village_trust` depends on whether the party protects the child from a mob. |
| `beat.parent_interviews` | **Parents of the wicked children.** Visit Goodall's shop or the northeast families. A sibling can describe the mirror rhyme when questioned privately. | `evidence.double_behavior` | `evidence.parent_suspicion`; `mirror_rhyme_known`; child names become visible. |
| `beat.figwort_dinner` | **The Figwort invitation.** Lord Figwort discusses fashion and household control; Lady Figwort asks for a discreet rescue from the sanitarium; Wylda tests whether the party treats danger as an invitation. | `arrival_seen` | `figwort_stance` becomes `watch`, `rescue`, or `protect`; optional recruit `npc.wylda-figwort`. |
| `beat.sanitarium` | **The wrong Arthur.** The party can investigate Dr. Livinius, prevent brain surgery, return the ibix to a safe holding place, or leave after learning the real Arthur is elsewhere. The adaptation treats the source's surgery proposal as a harm to avert, not a puzzle reward. | `figwort_stance`, `evidence.parent_suspicion` | `evidence.arthur_double`; `double.arthur-figwort` exposed or still masked; `recovery_flags.sanitarium`. |
| `beat.mirror_plan` | **Choose entry.** Figwort Manor is a social route; Hen's Teeth is a forest route with faun witnesses and Flynn's guarded trust. If the party knows the rhyme and waits for midnight, either mirror can open. | `mirror_rhyme_known`, `figwort_stance`, `village_trust` | `mirror_route` set to `figwort` or `hen-teeth`; `entry_ready=true`. |

### Act I optional quests

* `quest.wild-dog-lair` (`A1`) can earn food, coin, and Pigman Jack's trust. A
  party that notices the cu-sidhe connection may negotiate with the dogs rather
  than kill them.
* `quest.poisoned-poet` is a recovery and relationship scene tied to Wylda and
  the Figwort household. It can be solved with a healer, a potion, or careful
  transport.
* `quest.faehill-warning` (`B14`, `B15`, and `B17`) teaches that digging is a
  trap and that the mound is guarded. Smithson's disease is described without
  making contact mechanically dangerous by default; players receive a clear
  distance and care prompt.
* `quest.tenpenny-wood` (`A5`) is an ecology choice. Protecting the glowwood
  and avoiding needless tree cutting wins forest goodwill; harvesting under
  pressure earns quick money but complicates the final village reaction.
* `quest.confession-shrine` (`A4`) offers a cure and a truth-themed item after
  a consent-forward confession scene. The party may leave rather than disclose
  personal material; a fictional travel confession is always available.

## Act II — Roads, mirrors, and the forest's terms

Act II gives the party preparation, evidence, and a choice of cost before the
Brugh. It should be possible to proceed after one failed or refused optional
quest. The act is not a checklist.

| Beat | Scene and play | Reads | Writes / consequence |
| --- | --- | --- | --- |
| `beat.hen_teeth` | **Forest tavern.** Flynn provides slow service, safe lodging, rumors, and a mirror route only after trust is earned. Periwinkle Black appears at midnight and demonstrates that the court can cross worlds. | `mirror_rhyme_known`, `village_trust` | `evidence.fairy_mirrors`; `flynn_trust`; `entry_ready`. |
| `beat.faehill_watch` | **Faehill and ruins.** The cu-sidhe warns against digging. Billy Blurtweed may mock, observe, or guide the party. The ruins are evidence of the old court, not a hidden dungeon entrance. | `evidence.parent_suspicion` | `evidence.faehill_history`; `forest_stance` becomes `respect` or `exploit`. |
| `beat.forest_cost` | **A forest encounter.** Use an encounter or clue that tests restraint: goblin toll, dogs, stirges at Tenpenny Wood, or a dangerous creature. Retreat counts as a successful choice if the party keeps its people safe. | `forest_stance`, party condition | `recovery_flags.forest`; `village_trust` or `forest_stance` changes. |
| `beat.portal_opening` | **Midnight mirror.** After the route is prepared, the party recites or supplies the rhyme at midnight. The external mirror is an open doorway for a ten-minute ingress window; it leads only to C1, and no normal teleport or excavation bypasses it. | `entry_ready`, `mirror_route` | `mirror_route.active`; `mirror_route.portal_window`; resolve `state.glamour_truth` once per member; `brugh_entered=true`. |

## Act III — The Brugh and its two faces

The Brugh is a connected expedition, but a party may retreat, recover, and
re-enter if it can reopen a route. The source keyed rooms remain useful even if
the final map layout changes. Each room can offer a clue, a social consequence,
an optional reward, or a safe retreat; it does not need to be a mandatory fight.

| Beat | Scene and play | Reads | Writes / consequence |
| --- | --- | --- | --- |
| `beat.coatroom` | **C1 arrival.** Meet the attendant and choose what to surrender to the cloakroom. The entering mirror is not an exit. | `mirror_route.active`, `glamour_truth` | `brugh_c1_seen`; `entry_portal_covered=true` after departure. |
| `beat.palace_test` | **C2/C4/C5.** The reception, games, and fake library teach the party that glamour and truth coexist. The Weadley volume can reveal a secret route to level 3. | `glamour_truth`, `evidence.faehill_history` | `evidence.brugh_layout`; `route.secret_library` if found; `elf_lord_reaction` may shift. |
| `beat.fungal_choice` | **C6/C7/C8–C11.** The bluehouse, White Lady, frog deception, guest rooms, and mushroom ambassador offer a choice between curiosity, courtesy, and intrusion. Staying on paths avoids a fight. | `glamour_truth`, `elf_lord_reaction` | `npc.giles-weadley` clue; `court_favor` or `court_alarm`; optional `quest.mushroom-accord`. |
| `beat.court_dinner` | **C12/C12a/C13/C13a.** The ballroom, musicians, and dinner introduce the Elf Lord's caprice. The party may accept food, ask questions through Moth-in-Water, perform, refuse, or leave. Refusal uses a focused pit-rescue vignette; immediate attack throws the whole party into C18; favorable reactions open C8/C9 with Moth; a very favorable reaction adds an optional ibix pit spectacle. | `court_favor`, `glamour_truth`, the seven `child_<source_name>` variables | `elf_lord_reaction`; reveal `child_amelia_goodall`, `child_bernard_goodall`, `child_ursula_cooke`; possible pit setback or guest-room route. |
| `beat.servants` | **C14–C17.** Search the servant area, use the dumbwaiter, inspect the smoking room, or risk the Pool of Love and Drowning. These rooms supply objects and routes for a stealth rescue. | the seven `child_<source_name>` variables, `route.secret_library` | `evidence.gold_threads`; `route.dumbwaiter`; possible `recovery_flags.pool`. |
| `beat.pit_recovery` | **C18/C19.** If thrown into the pit or caught by a trap, survive, retreat through the small secret door, or negotiate with the tentacle worm. A setback is recoverable and does not erase rescued children. | party conditions, `recovery_flags` | `recovery_flags.pit`; `evidence.brugh_danger`; party can continue or return. |
| `beat.lower_level` | **C20–C23.** Choose whether to break the ice-devil bargain, use the kitchen and dumbwaiter, search the pantry, and approach Arthur's wine-cellar post. The goal is to reach children, not to clear every room. | `glamour_truth`, `elf_lord_reaction` | Set `child_stevie_leeford` or `child_arthur_figwort` to `freed` when those rescues succeed; `npc.master_ned` route; optional resources. |
| `beat.rescue_threads` | **Seven child scenes.** Each child has a small, readable rescue action: identify, reach, remove the gold thread, protect the child, and choose a route. A child can be rescued without killing the room's inhabitants when the party has earned a distraction or social opening. | the seven `child_<source_name>` variables, evidence | Set the matching `child_<source_name>` variable to `freed`; the matching `double_<source_name>` variable remains separate. `rescued_count` is derived from the seven scalar values. |
| `beat.lord_chamber` | **C24/C25/C26.** Moth-in-Water's room and the Elf Lord's chamber are optional risk. Master Ned can explain the court's cruelty and join the escape. The Elf Lord can be confronted, bargained with, deceived, or avoided. | `elf_lord_reaction`, rescued count, `court_favor` | `ending.elf_bargain` eligibility; `npc.master-ned` joined; `lord_confronted` if applicable. |
| `beat.treasure_exit` | **C27.** The party opens one of the return mirrors, handles the adversary mirror if chosen, and exits to Figwort Manor or Hen's Teeth. C27 is the only exit and does not require Arthur or any other single child to be rescued; partial rescue and evidence-only escape remain valid. Mirror #4 is sealed as an adaptation-safe unknown rather than a promise of arbitrary worlds. | the seven `child_<source_name>` variables, party health, `mirror_route` | `mirror_return`; `brugh_exited=true`; close or reopen entry according to route. |

## Act IV — Return and consequence

| Beat | Scene and play | Reads | Writes / consequence |
| --- | --- | --- | --- |
| `beat.return_triage` | **Account for people.** The party returns to a safe room, treats injuries and fear, and records every rescued, missing, and unaccounted-for child. The village has experienced only a short day. | all `child_<source_name>` and `double_<source_name>` variables, `recovery_flags` | `recovery_flags.returned`; parents and services become available. |
| `beat.double_resolution` | **What happened to the masks?** For each surviving or escaped double, the party can expose it publicly, contain it with the priory and manor, release it to the forest, or hide the truth. The choice changes trust and the epilogue booleans. | the seven `double_<source_name>` variables, `village_trust`, evidence | Set each double variable to `contained`, `escaped`, `exposed`, `masked`, or `removed`; an unresolved account remains `unknown`; `village_trust` shifts. Set `epilogue_doubles_contained` or `epilogue_masks_escaped` from those seven values. |
| `beat.family_return` | **Families respond.** Parents receive children who are actually returned. The Figworts, Goodall family, and poorer households respond differently; a partial rescue is still acknowledged. | the seven `child_<source_name>` variables, `figwort_stance` | `family_reactions`; rewards and services reflect actual children returned. |
| `beat.final_choice` | **Resolution.** The party chooses whether to publish the truth, protect the village with a partial story, accept a court bargain, or leave with unresolved survivors. | eligibility variables in [endings.md](endings.md) | Set exactly one scalar `ending_id`, then write independent scalar epilogue booleans; campaign ends or returns to a postscript. |

## Recovery and progression rules

* A failed check changes evidence, trust, time, or position; it does not erase
  a child from the story. A later clue, service, ally, or alternate route can
  restore a blocked path.
* A combat loss follows the fifth-srd ruleset's ordinary campaign behavior. A
  scripted rescue does not silently revive a dead character. The return scene
  supplies a safe rest and makes the remaining child statuses visible.
* The party earns its first advancement for establishing the mirror lead and
  protecting at least one person in Act I or II. It earns the second for a
  meaningful Brugh rescue or negotiated escape. Reaching level 3 is expected by
  the final resolution, but the campaign remains completable when a party has
  fewer levels after setbacks.
* Rest, temple care, shops, and training are useful services in Blackapple and
  at Hen's Teeth. Their prices and exact operations belong to the fifth-srd
  data conversion. The narrative never requires a specific gold total.
