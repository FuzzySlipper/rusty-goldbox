# Blackapple finale fixtures

These scripts exercise the authored finale through ordinary `goldbox play`
menus. The `fixtures/evt_fin_setup_*` chains are temporary authored fixtures:
they set the canon variables to a named scenario, set `brugh_exited` and
`recovery_return_triage`, and enter `evt_fin_return_start`. They live beside
the scripts and are copied only into the temporary campaign made by
`run-fixture.sh`; no setup mutation is shipped in the runtime event directory.
The integration must call `evt_fin_return_start` after its root event records
those two entry variables; the finale exits through the existing
`evt_return_postscript` event.

Run the fixtures from the campaign directory after creating the normal four
member party:

```bash
campaign_root="$PWD/campaigns/blackapple-brugh"
"$campaign_root/scripts/create-party.sh"
party="$campaign_root/.goldbox/party/mara.json,$campaign_root/.goldbox/party/orin.json,$campaign_root/.goldbox/party/sela.json,$campaign_root/.goldbox/party/tamsin.json"
goldbox="${GOLDBOX_CLI:-goldbox}"
mkdir -p /tmp/goldbox-9304-finale
"$goldbox" play --campaign "$campaign_root/modules/blackapple-brugh" \
  --modules "$PWD/modules" --modules "$campaign_root/modules" --party "$party" \
  --seed 9304 --script "$campaign_root/scripts/finale/full-contained.script" \
  --save /tmp/goldbox-9304-finale/full-contained.save.json \
  --fail-on-refusal --json > /tmp/goldbox-9304-finale/full-contained.json
```

Until the middle return sender exists, `run-fixture.sh` makes an isolated
temporary finale-only campaign copy, copies the selected setup fixture chain
into that copy, points its intro at it, and supplies a temporary
`evt_return_postscript`. It also runs
`revisit-guard.script` from the saved result. No repository area, manifest, or
coordinator event is changed by that harness:

```bash
GOLDBOX_CLI="$goldbox" "$campaign_root/scripts/finale/run-fixture.sh" full-contained
GOLDBOX_CLI="$goldbox" "$campaign_root/scripts/finale/run-fixture.sh" partial-mixed
GOLDBOX_CLI="$goldbox" "$campaign_root/scripts/finale/run-fixture.sh" truth-no-proof
GOLDBOX_CLI="$goldbox" "$campaign_root/scripts/finale/run-fixture.sh" elf-bargain
GOLDBOX_CLI="$goldbox" "$campaign_root/scripts/finale/run-fixture.sh" court-bound
```

The JSON transcript is the evidence for the scalar values. `set` events emit
`VariableFact` records with the machine-readable scalar IDs and values; player
text names each child and double in ordinary prose with its actual status,
custodian, or removal cause. The Core text event has no interpolation. The
five scripts cover these primary outcomes:

| Script | Primary outcome | Account/effect coverage |
| --- | --- | --- |
| `full-contained.script` | `ending.full_return` | all seven handoffs, every double contained, public trusted account |
| `partial-mixed.script` | `ending.partial_return` | two returned children, escaped/masked/exposed/removed/unknown doubles, protected account |
| `truth-no-proof.script` | `ending.truth_without_return` | zero returned children, no global evidence, loose and removed doubles, no-proof account |
| `elf-bargain.script` | `ending.elf_bargain` | one named freed child, explicit release confirmation, court-term consequence |
| `court-bound.script` | `ending.court_bound` | explicit court stay/service confirmation and all contained doubles |

Arthur's C27 printed pages 38–39 reward is authored as one 400-gp
`fifth-srd:gold` treasure event after an actual Arthur family handoff. An
actual Amelia or Bernard handoff awards `100 * party_size()` fifth-srd gold,
the digital conversion of one source garnet per active member; the source does
not provide a digital garnet item. The source also names one class-appropriate,
highest-level random spell scroll for each spellcaster; this module has no
scroll item or source spell-table definition, so that part remains an explicit
integration gap rather than an invented reward. The bargain route applies the
same named gold conversion only after its explicit release confirmation. The
`ending_id`/`return_account_complete` entry guards make a completed return
account a no-op on revisit, so the implemented reward events cannot be
replayed by a saved finale.

`--fail-on-refusal` is used for every accepted route. If a deliberately
negative route is added later, inspect its JSON diagnostic instead of treating a
refusal as an accepted finale fact.
