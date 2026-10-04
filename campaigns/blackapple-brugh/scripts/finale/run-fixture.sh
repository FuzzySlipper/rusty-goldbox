#!/usr/bin/env bash
set -euo pipefail

scenario=${1:-}
case "$scenario" in
  full-contained) expected_ending=ending.full_return ;;
  partial-mixed) expected_ending=ending.partial_return ;;
  truth-no-proof) expected_ending=ending.truth_without_return ;;
  elf-bargain) expected_ending=ending.elf_bargain ;;
  court-bound) expected_ending=ending.court_bound ;;
  *)
    printf 'usage: %s {full-contained|partial-mixed|truth-no-proof|elf-bargain|court-bound}\n' "$0" >&2
    exit 2
    ;;
esac
scenario_id=${scenario//-/_}

script_dir=$(cd "$(dirname "$0")" && pwd)
campaign_root=$(cd "$script_dir/../.." && pwd)
repo_root=$(cd "$campaign_root/../.." && pwd)
goldbox_cli=${GOLDBOX_CLI:-goldbox}
run_root=${FINALE_RUN_ROOT:-"/tmp/goldbox-9304-finale-author/$scenario"}

rm -rf "$run_root"
mkdir -p "$run_root/module/events" "$run_root/module/areas" "$run_root/deps"
cp -a "$campaign_root/modules/blackapple-brugh/." "$run_root/module/"
cp -a "$campaign_root/modules/blackapple-art" "$run_root/deps/"
cp -a "$campaign_root/modules/blackapple-fae" "$run_root/deps/"

# Keep the run isolated from unfinished middle-act definitions. The fixture
# still uses the real Core, Engine CLI and fifth-srd ruleset, while its
# temporary campaign contains only the finale event graph and declared vars.
find "$run_root/module/events" -type f ! -name 'evt_fin_*.json' -delete
rm -rf "$run_root/module/areas" "$run_root/module/items" "$run_root/module/npcs"
mkdir -p "$run_root/module/areas"
# Fixture-only setup chains live with the scripts, outside the shipped runtime
# event directory. Copy them into the temporary campaign used by this harness.
cp -a "$script_dir/fixtures/." "$run_root/module/events/"
jq -n '{type:"area", id:"blackapple", name:"Finale fixture", map:["+--+--+", "|     |", "+--+--+"], entries:{north_road:{at:[0,0], facing:"east"}}}' \
  > "$run_root/module/areas/blackapple.json"
jq '.requires = [{id:"fifth-srd", version:"^0.1.0"}, {id:"blackapple-art", version:"^0.1.0"}, {id:"blackapple-fae", version:"^0.1.0"}]' \
  "$run_root/module/module.json" > "$run_root/module/module.tmp.json"
mv "$run_root/module/module.tmp.json" "$run_root/module/module.json"
jq --arg intro "evt_fin_setup_$scenario_id" '.intro = $intro' \
  "$run_root/module/campaign.json" > "$run_root/module/campaign.tmp.json"
mv "$run_root/module/campaign.tmp.json" "$run_root/module/campaign.json"
cat > "$run_root/module/events/evt_return_postscript.json" <<'EOF'
{
  "type": "event",
  "id": "evt_return_postscript",
  "kind": "text",
  "text": "Temporary fixture postscript: the completed finale account is recorded once."
}
EOF

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

"$goldbox_cli" play \
  --campaign "$run_root/module" \
  --modules "$run_root/deps" \
  --modules "$repo_root/modules" \
  --party "$party" --seed 9304 \
  --script "$script_dir/$scenario.script" \
  --save "$save" --fail-on-refusal --json > "$transcript"

"$goldbox_cli" play \
  --campaign "$run_root/module" \
  --modules "$run_root/deps" \
  --modules "$repo_root/modules" \
  --load "$save" --script "$script_dir/revisit-guard.script" \
  --save "$revisit_save" --fail-on-refusal --json > "$revisit_transcript"

jq -e '.ok == true' "$transcript" >/dev/null
jq -e '.ok == true' "$revisit_transcript" >/dev/null
jq -e --arg ending "$expected_ending" \
  '[.transcript[]?.facts[]? | select(.kind == "variable" and (.text | contains("ending_id is now " + $ending)))] | length > 0' \
  "$transcript" >/dev/null
jq -e '[.transcript[]?.facts[]? | select(.kind == "treasure")] | length == 0' \
  "$revisit_transcript" >/dev/null
for status_id in \
  child_arthur_figwort child_amelia_goodall child_bernard_goodall child_giles_weadley \
  child_philip_anvil child_ursula_cooke child_stevie_leeford \
  double_arthur_figwort double_amelia_goodall double_bernard_goodall double_giles_weadley \
  double_philip_anvil double_ursula_cooke double_stevie_leeford; do
  jq -e --arg id "$status_id" \
    '[.transcript[]?.facts[]?.text // "" | select(contains($id))] | length > 0' \
    "$transcript" >/dev/null
done
printf 'fixture=%s\nfirst=%s\nrevisit=%s\n' "$scenario" "$transcript" "$revisit_transcript"
