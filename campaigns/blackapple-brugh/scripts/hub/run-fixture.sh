#!/usr/bin/env bash
set -euo pipefail

scenario=${1:-}
case "$scenario" in
  guild-respect) expected_key=forest_stance; expected_value=respect; expected_text='forest_stance is now respect.' ;;
  guild-exploit) expected_key=forest_stance; expected_value=exploit; expected_text='forest_stance is now exploit.' ;;
  jack-dog) expected_key=; expected_value=; expected_text='same wild-dog trail' ;;
  jack-care) expected_key=blackapple_optional_jack_secret; expected_value=consented_care; expected_text='blackapple_optional_jack_secret is now consented_care.' ;;
  jack-containment) expected_key=blackapple_optional_jack_secret; expected_value=consented_containment; expected_text='blackapple_optional_jack_secret is now consented_containment.' ;;
  tobler-cemetery) expected_key=blackapple_optional_tobler_evidence; expected_value=private_evidence; expected_text='blackapple_optional_tobler_evidence is now private_evidence.' ;;
  cemetery-agatha) expected_key=blackapple_optional_tobler_evidence; expected_value=agatha_truth; expected_text='lichguard keeps a grave from being troubled' ;;
  cemetery-expose) expected_key=blackapple_optional_tobler_evidence; expected_value=grave_abuse_exposed; expected_text='blackapple_optional_tobler_evidence is now grave_abuse_exposed.' ;;
  smithson-care) expected_key=blackapple_optional_smithson_care; expected_value=care_referral; expected_text='real care' ;;
  services-hazard) expected_key=; expected_value=; expected_text='Hazard' ;;
  services-agatha-gap) expected_key=; expected_value=; expected_text='market lane is open again' ;;
  *)
    printf 'usage: %s {guild-respect|guild-exploit|jack-dog|jack-care|jack-containment|tobler-cemetery|cemetery-agatha|cemetery-expose|smithson-care|services-hazard|services-agatha-gap}\n' "$0" >&2
    exit 2
    ;;
esac

script_dir=$(cd "$(dirname "$0")" && pwd)
campaign_root=$(cd "$script_dir/../.." && pwd)
repo_root=$(cd "$campaign_root/../.." && pwd)
goldbox_cli=${GOLDBOX_CLI:-goldbox}
evidence_root=${HUB_EVIDENCE_ROOT:-"${TMPDIR:-/tmp}/goldbox-9303-optional-hub-author"}
initial_validate=${HUB_INITIAL_VALIDATE:-}
run_root=${HUB_RUN_ROOT:-"$evidence_root/hub/$scenario"}

mkdir -p "$evidence_root"
if [[ -n "$initial_validate" && -f "$initial_validate" ]]; then
  cp "$initial_validate" "$evidence_root/initial-module-validate.json"
fi
rm -rf "$run_root"
mkdir -p "$run_root/module" "$run_root/deps"
if [[ -n "$initial_validate" && -f "$initial_validate" ]]; then
  cp "$initial_validate" "$run_root/initial-module-validate.json"
fi
cp -a "$campaign_root/modules/blackapple-brugh/." "$run_root/module/"
cp -a "$campaign_root/modules/blackapple-fae" "$run_root/deps/"
cp -a "$campaign_root/modules/blackapple-art" "$run_root/deps/"

# The optional hub is tested through text and state in a headless local-intro
# fixture. The temporary copy retains the normal module's maps and art refs;
# only its intro is redirected to this focused hub route.
jq '.intro = "evt_vil_optional_hub"' \
  "$run_root/module/campaign.json" > "$run_root/module/campaign.tmp.json"
mv "$run_root/module/campaign.tmp.json" "$run_root/module/campaign.json"

party_dir="$run_root/party" \
  PARTY_DIR="$run_root/party" \
  RULESET_PATH="$repo_root/modules/fifth-srd" \
  GOLDBOX_CLI="$goldbox_cli" \
  "$campaign_root/scripts/create-party.sh" > "$run_root/party-path.txt"
party=$(tail -n 1 "$run_root/party-path.txt")
save="$run_root/$scenario.save.json"
transcript="$run_root/$scenario.json"
revisit_save="$run_root/$scenario.revisit.save.json"
revisit_transcript="$run_root/$scenario.revisit.json"

validate_status=0
"$goldbox_cli" module validate "$run_root/module" \
  --modules "$run_root/deps" --modules "$repo_root/modules" --json \
  > "$run_root/validate.json" || validate_status=$?
if (( validate_status != 0 )); then
  cat "$run_root/validate.json" >&2
  exit "$validate_status"
fi
jq -e '.ok == true' "$run_root/validate.json" >/dev/null

"$goldbox_cli" play \
  --campaign "$run_root/module" \
  --modules "$run_root/deps" \
  --modules "$repo_root/modules" \
  --party "$party" --seed 9303 \
  --script "$script_dir/$scenario.script" \
  --save "$save" --fail-on-refusal --json > "$transcript"

"$goldbox_cli" play \
  --campaign "$run_root/module" \
  --modules "$run_root/deps" \
  --modules "$repo_root/modules" \
  --load "$save" --script "$script_dir/revisit.script" \
  --save "$revisit_save" --fail-on-refusal --json > "$revisit_transcript"

jq -e '.ok == true' "$transcript" >/dev/null
jq -e '.ok == true' "$revisit_transcript" >/dev/null
all_text=$(mktemp)
trap 'rm -f "$all_text"' EXIT
jq -r '.transcript[]?.facts[]?.text // empty' "$transcript" > "$all_text"
grep -F "$expected_text" "$all_text" >/dev/null

# Hub routes may read existing campaign facts, but they must not mutate the
# finale's child, double, or ending records.
jq -e '[.transcript[]?.facts[]? | select(.kind == "variable" and ((.text // "") | test("child_|double_|ending_id")))] | length == 0' \
  "$transcript" >/dev/null

if [[ -n "$expected_key" ]]; then
  jq -e --arg key "$expected_key" --arg value "$expected_value" \
    '[.variables | .. | objects | select(has($key) and .[$key] == $value)] | length > 0' \
    "$save" >/dev/null
  jq -e --arg key "$expected_key" --arg value "$expected_value" \
    '[.variables | .. | objects | select(has($key) and .[$key] == $value)] | length > 0' \
    "$revisit_save" >/dev/null
fi
jq -e '[.transcript[]?.facts[]? | select(.kind == "treasure")] | length == 0' \
  "$revisit_transcript" >/dev/null

if [[ "$scenario" == "services-agatha-gap" ]]; then
  # This route uses a real shop transaction: sell carried chain mail, buy one
  # finite Bed Time Tea dose, leave the shop, use that carried dose through the
  # ordinary command surface, and remain in the free world.
  jq -e '[.transcript[]?.facts[]? | select(.kind == "bought" and ((.text // "") | contains("Bed Time Tea")))] | length == 1' \
    "$transcript" >/dev/null
  jq -e '[.transcript[]?.facts[]? | select(.kind == "used" and ((.text // "") | contains("Bed Time Tea")))] | length == 1' \
    "$transcript" >/dev/null
  jq -e '[.transcript[]?.facts[]? | select(.kind == "condition_applied" and ((.text // "") | contains("Deep sleep")))] | length == 1' \
    "$transcript" >/dev/null
  jq -e '[.transcript[]?.facts[]? | select(.kind == "refused")] | length == 0' \
    "$transcript" >/dev/null
  jq -e '.inventory | map(select(. == "blackapple-brugh:agatha_bed_time_tea")) | length == 0' \
    "$save" >/dev/null
  jq -e '.inventory | map(select(. == "blackapple-brugh:agatha_bed_time_tea")) | length == 0' \
    "$revisit_save" >/dev/null
  jq -e '.party[0].conditions | index("blackapple-fae:agatha_deep_sleep") != null' "$save" >/dev/null
  jq -e '.party[0].conditions | index("blackapple-fae:agatha_deep_sleep") != null' "$revisit_save" >/dev/null
  jq -e '.variables.campaign.agatha_bed_time_tea_stock == 5' "$save" >/dev/null
  jq -e '.variables.campaign.agatha_bed_time_tea_stock == 5' "$revisit_save" >/dev/null
  jq -e '.pending_menu == null and .area == "blackapple-brugh:blackapple"' "$save" >/dev/null
  jq -e '.pending_menu == null and .area == "blackapple-brugh:blackapple"' "$revisit_save" >/dev/null
fi

printf 'scenario=%s\nvalidate=%s\nfirst=%s\nrevisit=%s\n' \
  "$scenario" "$run_root/validate.json" "$transcript" "$revisit_transcript"
