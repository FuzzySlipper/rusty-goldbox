#!/usr/bin/env bash
set -euo pipefail

# Use a published goldbox executable with GOLDBOX_CLI=/path/to/goldbox.
# Characters are generated output; the campaign's ordinary creation UI also works.
campaign_root=$(cd "$(dirname "$0")/.." && pwd)
goldbox_cli=${GOLDBOX_CLI:-goldbox}
party_dir=${PARTY_DIR:-"$campaign_root/.goldbox/party"}
ruleset_path=${RULESET_PATH:-"$campaign_root/../../modules/fifth-srd"}
mkdir -p "$party_dir"

"$goldbox_cli" character new --module "$ruleset_path" --class fighter --race human \
  --name 'Mara Venn' --priority str,con,dex,wis,int,cha \
  --feature soldier,savage_attacker,defense --equipment longsword,chain_mail,shield --seed 9301 --out "$party_dir/mara.json" --json
"$goldbox_cli" character new --module "$ruleset_path" --class rogue --race human \
  --name 'Orin Reed' --priority dex,con,int,cha,wis,str \
  --feature criminal,alert --equipment rapier,leather --seed 9302 --out "$party_dir/orin.json" --json
"$goldbox_cli" character new --module "$ruleset_path" --class cleric --race human \
  --name 'Sela Ash' --priority wis,con,str,dex,cha,int \
  --feature acolyte,tough --equipment mace,scale_mail,shield --spells cure_wounds,healing_word \
  --seed 9303 --out "$party_dir/sela.json" --json
"$goldbox_cli" character new --module "$ruleset_path" --class wizard --race human \
  --name 'Tamsin Vale' --priority int,con,dex,wis,cha,str \
  --feature sage,alert --equipment quarterstaff,dagger --spells magic_missile,fire_bolt \
  --seed 9304 --out "$party_dir/tamsin.json" --json

printf '%s\n' "$party_dir/mara.json,$party_dir/orin.json,$party_dir/sela.json,$party_dir/tamsin.json"
