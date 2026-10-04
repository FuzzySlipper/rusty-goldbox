# Optional Blackapple hub routes

These scripts start new `evt_vil_optional_*` events directly so the routes can
be checked before the coordinator appends choices to `evt_vil_market`. They use
ordinary `goldbox play` commands, saved state, and a second load for revisit
checks. `run-fixture.sh` copies the normal campaign into a temporary module and
redirects only that copy's intro to the focused hub; it does not strip art or
change the shipped campaign, maps, or asset module. These local-intro receipts
prove the authored hub routes and save guards; they are not full ordinary
market-entry integration proof.

The source mapping is deliberately narrow:

| Source key | New entry | Data conversion | Existing owner used |
| --- | --- | --- | --- |
| B6 Merchants' Guild, p. 16 | `evt_vil_optional_guild` | A guarded glowwood contract writes the existing `forest_stance`: respectful protection or exploitative harvest. Refusing the contract records respect. | Existing forest stance and Tenpenny/dog route |
| B8 Pigman Jack, p. 17 | `evt_vil_optional_jack` | The dog lead hands to `evt_vil_exit_to_environs`; the wereboar secret requires consent and records only a local conversation state. | Existing environs dog quest and `evt_vil_priory` menu |
| B11 Tobler, p. 18 | `evt_vil_optional_tobler` | The night-watch dismissal and grave-robbery rumour become local evidence, without making an accusation or displacing the child mystery. | Existing Priory witness conversation |
| B12 Cemetery, p. 18 | `evt_vil_optional_cemetery` | Night choices record Agatha truth, lichguard evidence, grave-abuse exposure, or a private note. No child combat or required undead clear is added. | Existing Agatha referral, shrine temple, and Priory menu |
| B17 Smithson, p. 22 | `evt_vil_optional_smithson` | Distance is explicit. A care choice leaves food and hands the actual party-care decision to the existing Priory temple. | Existing `evt_vil_priory_care` |
| B2/B7 services, pp. 12–13 and 16–17 | `evt_vil_optional_services` | Hazard sells existing fifth-srd gear at declared item costs. Agatha runs a six-dose-per-preparation shop with the source prices; a purchased consumable can be used through the ordinary party command surface before the party leaves back into the open market lane. | Existing Shop and Temple event owners |

The source text describes Agatha's six dose prices as 2, 6, 80, 5, 10, and
3 gp for sleep, rage, charm, healing, possum, and lichguard preparations. The
campaign now represents those prices as six data-authored item definitions and
six finite stock variables. The focused route sells carried chain mail, buys
one Bed Time Tea dose, verifies the stock falls from six to five, leaves the
shop with no pending menu, uses the real carried item, and verifies the
condition and stock survive a save reload. Its duration uses campaign days;
the route does not add a fixture-only item or mutate a copied module to make
`use` reachable.

Hazard's value-only buying limit is also data-authored through the existing
shop owner. The hub route keeps the seller's declared maximum and ordinary
gear costs visible, while the Agatha route is the focused finite-stock and
consumable-use check.

Jack's current conversion is equally explicit: the existing Priory menu owns
party healing and safe witness/holding conversation. There is no NPC
lycanthropy cure operation, so the consented route does not claim to cure or
contain Jack silently. A future narrow owner may add an NPC condition service;
until then, the consent and referral text is the complete data-only behavior.

Run one focused route with a portable CLI and evidence directory:

```bash
GOLDBOX_CLI=/path/to/goldbox \
HUB_EVIDENCE_ROOT=/path/to/temporary/evidence \
  campaigns/blackapple-brugh/scripts/hub/run-fixture.sh guild-respect
```

Supported scenarios are `guild-respect`, `guild-exploit`, `jack-dog`,
`jack-care`, `jack-containment`, `tobler-cemetery`, `cemetery-agatha`,
`cemetery-expose`, `smithson-care`, `services-hazard`, and
`services-agatha-gap`. Each receipt is written below the configured evidence
directory at `hub/<scenario>/`.
