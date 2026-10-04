# Blackapple finale fixtures

These scripts exercise the authored finale through ordinary `goldbox play`
commands and menus. The `fixtures/evt_fin_setup_*` chains are temporary authored
fixtures:
they set the canon variables to a named scenario, set `brugh_exited` and
`recovery_return_triage`, and enter `evt_fin_return_start`. They live beside
the scripts and are copied only into the temporary campaign made by
`run-fixture.sh`; no setup mutation is shipped in the runtime event directory.
The normal campaign intro remains `evt_root_intro`; launching the normal
campaign does not select one of these fixture intros. Its authored return path
enters `evt_fin_return_start`, while this harness points a temporary campaign
directly at the selected setup and exits through a temporary
`evt_return_postscript` event.

Run the fixtures from the campaign directory after creating the normal four
member party:

```bash
campaign_root="$PWD/campaigns/blackapple-brugh"
"$campaign_root/scripts/create-party.sh"
party="$campaign_root/.goldbox/party/mara.json,$campaign_root/.goldbox/party/orin.json,$campaign_root/.goldbox/party/sela.json,$campaign_root/.goldbox/party/tamsin.json"
goldbox="${GOLDBOX_CLI:-goldbox}"
mkdir -p /tmp/goldbox-9304-finale
# This is the normal campaign route; it does not load a finale fixture setup.
"$goldbox" play --campaign "$campaign_root/modules/blackapple-brugh" \
  --modules "$PWD/modules" --modules "$campaign_root/modules" --party "$party" \
  --seed 9304 --script "$campaign_root/scripts/routes/opening-lead.script" \
  --save /tmp/goldbox-9304-finale/normal-opening-lead.save.json \
  --fail-on-refusal --json > /tmp/goldbox-9304-finale/normal-opening-lead.json
```

`run-fixture.sh` makes an isolated temporary finale-only campaign copy, copies
the selected setup fixture chain into that copy, points its intro at it, and
supplies a temporary `evt_return_postscript`. It also runs
`revisit-guard.script` from the saved result for return cases; the terminal
lost case uses the command-free `lost-revisit.script`. No repository area,
manifest, or coordinator event is changed by that harness:

```bash
GOLDBOX_CLI="$goldbox" "$campaign_root/scripts/finale/run-fixture.sh" full-contained
GOLDBOX_CLI="$goldbox" "$campaign_root/scripts/finale/run-fixture.sh" partial-mixed
GOLDBOX_CLI="$goldbox" "$campaign_root/scripts/finale/run-fixture.sh" truth-no-proof
GOLDBOX_CLI="$goldbox" "$campaign_root/scripts/finale/run-fixture.sh" elf-bargain
GOLDBOX_CLI="$goldbox" "$campaign_root/scripts/finale/run-fixture.sh" court-bound
GOLDBOX_CLI="$goldbox" "$campaign_root/scripts/finale/run-fixture.sh" lost-defeat
```

The JSON transcript is the evidence for the scalar values. `set` events emit
`VariableFact` records with the machine-readable scalar IDs and values; player
text names each child and double in ordinary prose with its actual status,
custodian, or removal cause. The Core text event has no interpolation. The
five return scripts cover these primary outcomes:

| Script | Primary outcome | Account/effect coverage |
| --- | --- | --- |
| `full-contained.script` | `ending.full_return` | all seven handoffs, every double contained, public trusted account |
| `partial-mixed.script` | `ending.partial_return` | two returned children, escaped/masked/exposed/removed/unknown doubles, protected account |
| `truth-no-proof.script` | `ending.truth_without_return` | zero returned children, no global evidence, loose and removed doubles, no-proof account |
| `elf-bargain.script` | `ending.elf_bargain` | one named freed child, explicit release confirmation, court-term consequence |
| `court-bound.script` | `ending.court_bound` | explicit court stay/service confirmation and all contained doubles |

`lost-defeat.script` is a sixth, deliberately allowed authored scenario. It
records non-default child and double states, then enters the shipped
`evt_defeat_lost_in_brugh` → `evt_defeat_begin` → `evt_defeat_close` chain. It
does not change an ordinary combat's `on_lose` recovery route or claim that a
normal campaign path currently produces this defeat. `run-fixture.sh` copies
the three authored defeat events into this temporary scenario only and checks
that both the first and revisited saves contain `ending.lost_in_brugh` while
the recorded child and double states remain unchanged.

Arthur's C27 printed pages 38–39 reward is authored as one 400-gp
`fifth-srd:gold` treasure event after an actual Arthur family handoff. An
actual Amelia or Bernard handoff awards `100 * party_size()` fifth-srd gold,
the digital conversion of one source garnet per active member; the source does
not provide a digital garnet item. The same handoff runs the generic
`spell_reward` event: each eligible active spellcaster receives one unknown,
class-appropriate highest-level spell selected by the required ruleset data,
saved directly in the character's known, castable spell list as the digital
counterpart of the source's scroll-like lesson. The bargain route applies the
same named gold conversion only after its explicit release confirmation. The
`ending_id`/`return_account_complete` entry guards make a completed return
account a no-op on revisit, so the implemented reward events cannot be
replayed by a saved finale.

`--fail-on-refusal` is used for every accepted route. If a deliberately
negative route is added later, inspect its JSON diagnostic instead of treating a
refusal as an accepted finale fact.
