#!/usr/bin/env python3
"""Headless opening and first-route verifier for the Blackapple campaign.

The verifier treats the goldbox process as the source of truth. It records the
command, exact stdout/stderr bytes, exit status, parsed transcript, and save
for every route, then checks the resulting data without editing campaign data
or altering Core behavior.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable


REPO_ROOT = Path(__file__).resolve().parents[3]
CURRENT_CAMPAIGN = REPO_ROOT / "campaigns" / "blackapple-brugh" / "modules" / "blackapple-brugh"
CURRENT_MODULES = REPO_ROOT / "modules"
CURRENT_PARTY_DIR = REPO_ROOT / "campaigns" / "blackapple-brugh" / ".goldbox" / "party"
CURRENT_SCRIPT_ROOT = REPO_ROOT / "campaigns" / "blackapple-brugh" / "scripts" / "routes"
DEFAULT_ROUTES = (
    "opening-lead",
    "opening-revisit",
    "forest-kindness",
    "forest-negotiation",
    "forest-retreat",
    "forest-combat",
    "shops-rest",
    "wylda-recruit",
)
ROUTE_CLASSIFICATIONS = {
    "opening-lead": "opening-proof",
    "opening-revisit": "opening-revisit-proof",
    "forest-kindness": "forest-noncombat-proof",
    "forest-negotiation": "forest-noncombat-proof",
    "forest-retreat": "forest-retreat-proof",
    "forest-combat": "automatic-combat-balance-only",
    "shops-rest": "shop-and-rest-proof",
    "wylda-recruit": "party-size-proof",
}


@dataclass(frozen=True)
class Config:
    cli: Path
    campaign: Path
    modules: tuple[Path, ...]
    party: tuple[Path, ...]
    snapshot: Path | None
    script_root: Path
    output_root: Path
    seed: str
    routes: tuple[str, ...]
    custom_script: Path | None
    append_commands: tuple[str, ...]
    fail_on_refusal: bool
    explicit_load: Path | None
    repo_root: Path


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Run bounded Blackapple opening routes against a published goldbox "
            "CLI and assert observed transcript/save state."
        )
    )
    parser.add_argument("--cli", type=Path, help="goldbox executable (GOLDBOX_CLI)")
    parser.add_argument(
        "--campaign", type=Path, help="campaign module directory (GOLDBOX_CAMPAIGN)"
    )
    parser.add_argument(
        "--modules",
        type=Path,
        action="append",
        help="installed module directory; repeatable (GOLDBOX_MODULES)",
    )
    parser.add_argument(
        "--party",
        action="append",
        help="party JSON path or comma-separated paths; repeatable (GOLDBOX_PARTY)",
    )
    parser.add_argument(
        "--party-dir",
        type=Path,
        help="directory containing party JSON files (GOLDBOX_PARTY_DIR)",
    )
    parser.add_argument(
        "--snapshot",
        type=Path,
        help="optional historical snapshot; defaults stay on the current repository",
    )
    parser.add_argument(
        "--script-root",
        type=Path,
        help="directory containing route scripts (GOLDBOX_SCRIPT_ROOT)",
    )
    parser.add_argument(
        "--output",
        type=Path,
        help="temporary verifier output directory (GOLDBOX_VERIFY_OUT)",
    )
    parser.add_argument("--seed", help="deterministic play seed (GOLDBOX_SEED; default: 1)")
    parser.add_argument(
        "--route",
        dest="routes",
        action="append",
        choices=("all",) + DEFAULT_ROUTES,
        help="route to run; repeatable (default: all)",
    )
    parser.add_argument("--script", type=Path, help="override the script for one selected route")
    parser.add_argument(
        "--append-command",
        dest="append_commands",
        action="append",
        default=[],
        help="append an ordinary script command to the copied input",
    )
    parser.add_argument("--load", type=Path, help="explicit save to load for opening-revisit")
    refusal = parser.add_mutually_exclusive_group()
    refusal.add_argument(
        "--fail-on-refusal",
        dest="fail_on_refusal",
        action="store_true",
        default=True,
        help="pass --fail-on-refusal to goldbox (default)",
    )
    refusal.add_argument(
        "--no-fail-on-refusal",
        dest="fail_on_refusal",
        action="store_false",
        help="diagnostic mode; expected shop status becomes zero",
    )
    parser.add_argument("--json", action="store_true", help="print the complete verifier report as JSON")
    return parser.parse_args(argv)


def env_path(argument: Path | None, variable: str, default: Path | None) -> Path | None:
    if argument is not None:
        return argument.expanduser().resolve()
    value = os.environ.get(variable)
    if value:
        return Path(value).expanduser().resolve()
    return default.expanduser().resolve() if default is not None else None


def split_paths(values: Iterable[str]) -> tuple[Path, ...]:
    result: list[Path] = []
    for value in values:
        for part in value.split(","):
            if part.strip():
                result.append(Path(part.strip()).expanduser().resolve())
    return tuple(result)


def resolve_config(args: argparse.Namespace) -> Config:
    snapshot_argument = args.snapshot or (
        Path(os.environ["GOLDBOX_SNAPSHOT"])
        if os.environ.get("GOLDBOX_SNAPSHOT")
        else None
    )
    snapshot = snapshot_argument.expanduser().resolve() if snapshot_argument else None
    cli_value = args.cli or os.environ.get("GOLDBOX_CLI")
    if cli_value:
        cli_candidate = Path(cli_value).expanduser()
        cli_found = shutil.which(str(cli_candidate))
        cli = Path(cli_found).resolve() if cli_found else cli_candidate.resolve()
    else:
        cli_found = shutil.which("goldbox")
        if not cli_found:
            raise ValueError(
                "goldbox was not found on PATH; pass --cli or set GOLDBOX_CLI"
            )
        cli = Path(cli_found).resolve()
    historical_root = snapshot / "source" / "modules" if snapshot else None
    if args.campaign is not None:
        campaign = args.campaign.expanduser().resolve()
    elif historical_root:
        campaign = (historical_root / "blackapple-brugh").resolve()
    else:
        campaign = env_path(None, "GOLDBOX_CAMPAIGN", CURRENT_CAMPAIGN)
    if args.modules:
        modules = tuple(path.expanduser().resolve() for path in args.modules)
    elif historical_root:
        modules = (historical_root.resolve(),)
    elif os.environ.get("GOLDBOX_MODULES"):
        modules = split_paths([os.environ["GOLDBOX_MODULES"]])
    else:
        modules = (CURRENT_MODULES, CURRENT_CAMPAIGN.parent)
    output_argument = args.output or os.environ.get("GOLDBOX_VERIFY_OUT")
    output = (
        Path(output_argument).expanduser().resolve()
        if output_argument
        else Path(tempfile.mkdtemp(prefix="goldbox-opening-verify-"))
    )
    if campaign is None:
        raise ValueError("could not resolve campaign path")

    raw_party = args.party
    if raw_party is None and not snapshot:
        party_env = os.environ.get("GOLDBOX_PARTY")
        raw_party = [party_env] if party_env else []
    if raw_party is None:
        raw_party = []
    party = split_paths(raw_party)
    if not party:
        if args.party_dir is not None:
            party_dir = args.party_dir.expanduser().resolve()
        elif snapshot:
            party_dir = (snapshot / "party").resolve()
        else:
            party_dir = env_path(None, "GOLDBOX_PARTY_DIR", CURRENT_PARTY_DIR)
        if party_dir is None:
            raise ValueError("could not resolve party directory")
        party = tuple(sorted(party_dir.glob("*.json")))

    if args.script_root is not None:
        script_root = args.script_root.expanduser().resolve()
    elif snapshot:
        script_root = (snapshot / "scripts").resolve()
    else:
        script_root = env_path(None, "GOLDBOX_SCRIPT_ROOT", CURRENT_SCRIPT_ROOT)
    if script_root is None:
        raise ValueError("could not resolve script root")

    raw_routes = tuple(args.routes or ("all",))
    routes = DEFAULT_ROUTES if "all" in raw_routes else raw_routes
    if args.script is not None and len(routes) != 1:
        raise ValueError("--script requires exactly one --route")
    if args.append_commands and len(routes) != 1:
        raise ValueError("--append-command requires exactly one --route")
    explicit_load = args.load.expanduser().resolve() if args.load else None
    return Config(
        cli=cli,
        campaign=campaign,
        modules=modules,
        party=party,
        snapshot=snapshot,
        script_root=script_root,
        output_root=output,
        seed=args.seed or os.environ.get("GOLDBOX_SEED", "1"),
        routes=routes,
        custom_script=args.script.expanduser().resolve() if args.script else None,
        append_commands=tuple(args.append_commands),
        fail_on_refusal=args.fail_on_refusal,
        explicit_load=explicit_load,
        repo_root=REPO_ROOT,
    )


def require_path(path: Path, label: str) -> None:
    if not path.exists():
        raise FileNotFoundError(f"{label} does not exist: {path}")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_json(path: Path, value: Any) -> None:
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def create_run_dir(output_root: Path) -> tuple[str, Path]:
    output_root.mkdir(parents=True, exist_ok=True)
    run_dir = Path(tempfile.mkdtemp(prefix="run-", dir=output_root))
    run_id = run_dir.name
    return run_id, run_dir


def campaign_variables(save: dict[str, Any]) -> dict[str, Any]:
    variables = save.get("variables")
    if not isinstance(variables, dict):
        return {}
    campaign = variables.get("campaign")
    return campaign if isinstance(campaign, dict) else {}


def transcript_facts(stdout: dict[str, Any] | None) -> list[dict[str, Any]]:
    if not isinstance(stdout, dict):
        return []
    facts: list[dict[str, Any]] = []
    transcript = stdout.get("transcript")
    if not isinstance(transcript, list):
        return facts
    for step in transcript:
        if not isinstance(step, dict):
            continue
        values = step.get("facts")
        if isinstance(values, list):
            facts.extend(item for item in values if isinstance(item, dict))
    return facts


def fact_texts(facts: Iterable[dict[str, Any]], kind: str | None = None) -> list[str]:
    return [
        str(fact.get("text", ""))
        for fact in facts
        if kind is None or fact.get("kind") == kind
    ]


def party_names(party: Iterable[dict[str, Any]]) -> list[str]:
    return [str(character.get("name")) for character in party if isinstance(character, dict)]


def party_from_files(paths: Iterable[Path]) -> list[dict[str, Any]]:
    characters: list[dict[str, Any]] = []
    for path in paths:
        try:
            value = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise ValueError(f"cannot read party JSON {path}: {error}") from error
        if not isinstance(value, dict):
            raise ValueError(f"party JSON is not an object: {path}")
        characters.append(value)
    return characters


def save_shape(save: dict[str, Any]) -> dict[str, Any]:
    party = save.get("party")
    party_fields: set[str] = set()
    if isinstance(party, list):
        for character in party:
            if isinstance(character, dict):
                party_fields.update(character.keys())
    return {
        "top_level_fields": sorted(save.keys()),
        "party_fields": sorted(party_fields),
        "campaign_variable_fields": sorted(campaign_variables(save).keys()),
    }


def assertion(assertions: list[dict[str, Any]], name: str, ok: bool, detail: str) -> None:
    assertions.append({"name": name, "ok": bool(ok), "detail": detail})


def find_script(config: Config, route: str) -> Path:
    if config.custom_script is not None:
        return config.custom_script
    candidate = config.script_root / f"{route}.script"
    if candidate.exists():
        return candidate
    raise FileNotFoundError(
        f"no script for route {route}: looked for {candidate}; pass --script-root or --script"
    )


def make_input_script(
    source: Path, route_dir: Path, append_commands: tuple[str, ...]
) -> Path:
    source_text = source.read_text(encoding="utf-8")
    if append_commands:
        if source_text and not source_text.endswith("\n"):
            source_text += "\n"
        source_text += "\n".join(append_commands) + "\n"
    target = route_dir / "input.script"
    target.write_text(source_text, encoding="utf-8")
    return target


def expected_status(route: str, fail_on_refusal: bool) -> int:
    return 1 if route == "shops-rest" and fail_on_refusal else 0


def run_subprocess(
    config: Config,
    route: str,
    route_dir: Path,
    input_script: Path,
    load_path: Path | None,
) -> dict[str, Any]:
    save_path = route_dir / "state.save.json"
    command: list[str] = [str(config.cli), "play", "--campaign", str(config.campaign)]
    if load_path is None:
        for module_directory in config.modules:
            command.extend(["--modules", str(module_directory)])
        command.extend(["--party", ",".join(str(path) for path in config.party)])
    else:
        # A save contains state, but module resolution still belongs to the
        # current campaign invocation. Keep reloads on the same roots as the
        # fresh opening run instead of relying on the CLI process cwd.
        for module_directory in config.modules:
            command.extend(["--modules", str(module_directory)])
        command.extend(["--load", str(load_path)])
    if load_path is None:
        command.extend(["--seed", config.seed])
    command.extend(["--script", str(input_script), "--save", str(save_path)])
    if config.fail_on_refusal:
        command.append("--fail-on-refusal")
    command.append("--json")

    try:
        completed = subprocess.run(
            command,
            cwd=config.repo_root,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        stdout_bytes = completed.stdout
        stderr_bytes = completed.stderr
        status: int | None = completed.returncode
    except OSError as error:
        stdout_bytes = b""
        stderr_bytes = f"{type(error).__name__}: {error}\n".encode("utf-8")
        status = None

    stdout_raw = route_dir / "stdout.raw"
    stderr_raw = route_dir / "stderr.raw"
    status_path = route_dir / "status"
    stdout_raw.write_bytes(stdout_bytes)
    stderr_raw.write_bytes(stderr_bytes)
    status_path.write_text("" if status is None else f"{status}\n", encoding="utf-8")

    stdout_json: dict[str, Any] | None = None
    stdout_error: str | None = None
    if stdout_bytes:
        try:
            parsed = json.loads(stdout_bytes.decode("utf-8"))
            if isinstance(parsed, dict):
                stdout_json = parsed
            else:
                stdout_error = "stdout JSON is not an object"
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            stdout_error = f"stdout is not JSON: {error}"
    else:
        stdout_error = "stdout is empty"
    if stdout_json is not None:
        write_json(route_dir / "stdout.json", stdout_json)

    receipt = {
        "argv": command,
        "cwd": str(config.repo_root),
        "route": route,
        "script": str(input_script),
        "script_sha256": sha256(input_script),
        "load": str(load_path) if load_path else None,
        "save": str(save_path),
        "status": status,
        "expected_status": expected_status(route, config.fail_on_refusal),
        "stdout_raw": str(stdout_raw),
        "stderr_raw": str(stderr_raw),
        "status_file": str(status_path),
    }
    write_json(route_dir / "command.json", receipt)
    return {
        "route": route,
        "route_dir": route_dir,
        "save_path": save_path,
        "command": command,
        "status": status,
        "stdout_json": stdout_json,
        "stdout_error": stdout_error,
        "save": None,
        "facts": transcript_facts(stdout_json),
    }


def load_save(result: dict[str, Any]) -> dict[str, Any] | None:
    save_path = result["save_path"]
    if not save_path.exists():
        return None
    try:
        value = json.loads(save_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None
    if isinstance(value, dict):
        result["save"] = value
        return value
    return None


def check_common(
    config: Config,
    route: str,
    result: dict[str, Any],
    assertions: list[dict[str, Any]],
) -> dict[str, Any] | None:
    stdout = result["stdout_json"]
    status = result["status"]
    expected = expected_status(route, config.fail_on_refusal)
    assertion(
        assertions,
        "exit status",
        status == expected,
        f"observed {status!r}, expected {expected}",
    )
    assertion(
        assertions,
        "JSON transcript",
        stdout is not None,
        result["stdout_error"] or "parsed goldbox JSON",
    )
    save = load_save(result)
    assertion(
        assertions,
        "save written",
        save is not None,
        f"observed {result['save_path']}",
    )
    if save is None:
        return None
    assertion(
        assertions,
        "save fields discovered",
        bool(save.keys()),
        ", ".join(sorted(save.keys())),
    )
    return save


def check_party_once(
    assertions: list[dict[str, Any]],
    save: dict[str, Any],
    expected_names: list[str],
    label: str,
) -> list[dict[str, Any]]:
    party = save.get("party")
    actual = party if isinstance(party, list) else []
    names = party_names(actual)
    counts = {name: names.count(name) for name in set(names)}
    assertion(
        assertions,
        f"{label} party names are unique",
        len(names) == len(set(names)),
        f"names={names!r}",
    )
    assertion(
        assertions,
        f"{label} source members appear once",
        all(counts.get(name, 0) == 1 for name in expected_names),
        f"expected={expected_names!r}, counts={counts!r}",
    )
    return actual


def check_opening_lead(
    result: dict[str, Any],
    assertions: list[dict[str, Any]],
    expected_names: list[str],
) -> None:
    save = result.get("save")
    if not isinstance(save, dict):
        return
    party = check_party_once(assertions, save, expected_names, "opening lead")
    campaign = campaign_variables(save)
    assertion(
        assertions,
        "Hen's Teeth route prepared",
        campaign.get("mirror_route") == "hen_teeth" and campaign.get("entry_ready") is True,
        f"mirror_route={campaign.get('mirror_route')!r}, entry_ready={campaign.get('entry_ready')!r}",
    )
    assertion(
        assertions,
        "opening save has no pending menu",
        save.get("pending_menu") is None,
        f"pending_menu={save.get('pending_menu')!r}",
    )
    experiences = [character.get("experience") for character in party]
    assertion(
        assertions,
        "each opening member has 300 XP",
        bool(party) and all(value == 300 for value in experiences),
        f"experiences={experiences!r}",
    )
    variable_events = [
        text
        for text in fact_texts(result["facts"], "variable")
        if "opening_xp_awarded is now true" in text
    ]
    experience_events = [fact for fact in result["facts"] if fact.get("kind") == "experience"]
    assertion(
        assertions,
        "opening XP guard fired once",
        campaign.get("opening_xp_awarded") is True and len(variable_events) == 1,
        f"save={campaign.get('opening_xp_awarded')!r}, events={len(variable_events)}",
    )
    assertion(
        assertions,
        "opening transcript reports one XP application",
        len(experience_events) == 1,
        f"experience facts={len(experience_events)}",
    )
    refusals = fact_texts(result["facts"], "refused")
    assertion(assertions, "opening route has no refusals", not refusals, repr(refusals))


def check_opening_revisit(
    result: dict[str, Any],
    assertions: list[dict[str, Any]],
    loaded_save: dict[str, Any] | None,
) -> None:
    save = result.get("save")
    if not isinstance(save, dict) or loaded_save is None:
        return
    loaded_party = loaded_save.get("party")
    expected_names = party_names(loaded_party if isinstance(loaded_party, list) else [])
    party = check_party_once(assertions, save, expected_names, "opening revisit")
    before = [character.get("experience") for character in loaded_party or []]
    after = [character.get("experience") for character in party]
    assertion(
        assertions,
        "revisit keeps 300 XP",
        bool(after) and after == before and all(value == 300 for value in after),
        f"before={before!r}, after={after!r}",
    )
    campaign = campaign_variables(save)
    variable_events = [
        text
        for text in fact_texts(result["facts"], "variable")
        if "opening_xp_awarded is now true" in text
    ]
    experience_events = [fact for fact in result["facts"] if fact.get("kind") == "experience"]
    assertion(
        assertions,
        "revisit retains XP guard",
        campaign.get("opening_xp_awarded") is True and len(variable_events) == 0,
        f"save={campaign.get('opening_xp_awarded')!r}, new guard events={len(variable_events)}",
    )
    assertion(
        assertions,
        "revisit has no duplicate XP application",
        len(experience_events) == 0,
        f"experience facts={len(experience_events)}",
    )


def check_forest(
    route: str,
    result: dict[str, Any],
    assertions: list[dict[str, Any]],
    expected_names: list[str],
) -> None:
    save = result.get("save")
    if not isinstance(save, dict):
        return
    check_party_once(assertions, save, expected_names, route)
    campaign = campaign_variables(save)
    facts = result["facts"]
    combats = [fact for fact in facts if fact.get("kind") == "combat"]
    inventory = save.get("inventory") if isinstance(save.get("inventory"), list) else []
    food_count = inventory.count("blackapple-brugh:forest_reward_food")
    pelts_count = inventory.count("blackapple-brugh:forest_reward_pelts")
    resolved_events = [
        text for text in fact_texts(facts, "variable")
        if "environs_wild_dog_resolved is now true" in text
    ]
    reward_events = [
        text for text in fact_texts(facts, "variable")
        if "environs_wild_dog_reward_given is now true" in text
    ]
    if route in ("forest-kindness", "forest-negotiation"):
        assertion(assertions, "noncombat forest resolution", not combats, f"combat facts={len(combats)}")
        assertion(
            assertions,
            "forest reward guard is set",
            len(reward_events) == 1,
            f"save_reward={campaign.get('environs_wild_dog_reward_given')!r}, transcript_events={len(reward_events)}",
        )
        assertion(
            assertions,
            "forest reward items are delivered once",
            len(resolved_events) == 1
            and food_count == 1
            and pelts_count == 1,
            f"save_resolved={campaign.get('environs_wild_dog_resolved')!r}, transcript_events={len(resolved_events)}, food={food_count}, pelts={pelts_count}",
        )
        assertion(
            assertions,
            "forest recovery is recorded",
            campaign.get("recovery_forest") is True and campaign.get("forest_stance") == "respect",
            f"recovery={campaign.get('recovery_forest')!r}, stance={campaign.get('forest_stance')!r}",
        )
    elif route == "forest-retreat":
        refusal_texts = fact_texts(facts, "refused")
        assertion(assertions, "retreat has no combat", not combats, f"combat facts={len(combats)}")
        assertion(
            assertions,
            "retreat leaves reward guard unset",
            not reward_events
            and not resolved_events
            and food_count == 0
            and pelts_count == 0,
            f"save_resolved={campaign.get('environs_wild_dog_resolved')!r}, save_reward={campaign.get('environs_wild_dog_reward_given')!r}, resolved_events={len(resolved_events)}, reward_events={len(reward_events)}, food={food_count}, pelts={pelts_count}",
        )
        assertion(
            assertions,
            "retreat recovery status is correct",
            campaign.get("recovery_forest") is True and campaign.get("forest_stance") == "respect",
            f"recovery={campaign.get('recovery_forest')!r}, stance={campaign.get('forest_stance')!r}",
        )
        assertion(
            assertions,
            "retreat transcript records no reward",
            any("no reward" in text.lower() for text in fact_texts(facts)),
            "searched transcript text",
        )
        assertion(assertions, "retreat route has no refusals", not refusal_texts, repr(refusal_texts))
    elif route == "forest-combat":
        assertion(
            assertions,
            "automatic combat balance route has combat fact",
            bool(combats),
            f"combat facts={len(combats)}; classification remains balance-only",
        )
        assertion(
            assertions,
            "automatic combat balance route resolves reward",
            len(reward_events) == 1 and len(resolved_events) == 1,
            f"save_reward={campaign.get('environs_wild_dog_reward_given')!r}, reward_events={len(reward_events)}, resolved_events={len(resolved_events)}",
        )


def shop_facts(result: dict[str, Any]) -> list[tuple[str | None, dict[str, Any]]]:
    values: list[tuple[str | None, dict[str, Any]]] = []
    stdout = result.get("stdout_json") or {}
    for step in stdout.get("transcript", []):
        if not isinstance(step, dict):
            continue
        command = step.get("command")
        for fact in step.get("facts", []):
            if isinstance(fact, dict) and isinstance(fact.get("shop"), dict):
                values.append((command, fact))
    return values


def check_shops(
    result: dict[str, Any],
    assertions: list[dict[str, Any]],
    expected_names: list[str],
    fail_on_refusal: bool,
) -> None:
    save = result.get("save")
    if not isinstance(save, dict):
        return
    check_party_once(assertions, save, expected_names, "shop route")
    facts = result["facts"]
    refusals = [fact for fact in facts if fact.get("kind") == "refused"]
    assertion(assertions, "poor purchase has exactly one refusal", len(refusals) == 1, f"refusals={len(refusals)}")
    refusal_text = str(refusals[0].get("text", "")) if refusals else ""
    assertion(
        assertions,
        "refusal is the intentionally poor dagger purchase",
        "dagger" in refusal_text.lower() and "costs" in refusal_text.lower(),
        refusal_text,
    )
    assertion(
        assertions,
        "refusal command is buy 1",
        any(
            step.get("command") == "buy 1"
            and any(fact.get("kind") == "refused" for fact in step.get("facts", []))
            for step in (result.get("stdout_json") or {}).get("transcript", [])
            if isinstance(step, dict)
        ),
        "searched transcript commands",
    )
    ordered_commands = [
        step.get("command")
        for step in (result.get("stdout_json") or {}).get("transcript", [])
        if isinstance(step, dict) and step.get("command") is not None
    ]
    try:
        first_buy = ordered_commands.index("buy 1")
        sell = ordered_commands.index("sell 2", first_buy + 1)
        second_buy = ordered_commands.index("buy 1", sell + 1)
        sequence_ok = first_buy < sell < second_buy
    except ValueError:
        sequence_ok = False
    assertion(assertions, "real sell then buy follows refusal", sequence_ok, f"commands={ordered_commands!r}")
    shop_states = shop_facts(result)
    sell_states = [shop for command, shop in shop_states if command == "sell 2"]
    buy_states = [shop for command, shop in shop_states if command == "buy 1"]
    sold_carried = sell_states[0].get("shop", {}).get("carried", []) if sell_states else []
    sold_items = [item.get("item") for item in sold_carried if isinstance(item, dict)]
    assertion(
        assertions,
        "sell changes carried equipment",
        bool(sell_states) and "fifth-srd:chain_mail" not in sold_items,
        f"sell carried items={sold_items!r}",
    )
    inventory = save.get("inventory") if isinstance(save.get("inventory"), list) else []
    assertion(
        assertions,
        "bought dagger is in saved inventory once",
        inventory.count("fifth-srd:dagger") == 1,
        f"inventory={inventory!r}",
    )
    assertion(
        assertions,
        "shop is closed after leave",
        save.get("pending_shop") is None,
        f"pending_shop={save.get('pending_shop')!r}",
    )
    assertion(
        assertions,
        "shop route records one day of rest",
        save.get("elapsed_days") == 1,
        f"elapsed_days={save.get('elapsed_days')!r}",
    )
    if buy_states:
        balances = buy_states[-1].get("shop", {}).get("balances", {})
        assertion(
            assertions,
            "successful buy reports post-sale balance",
            any(isinstance(value, (int, float)) and value > 0 for value in balances.values()),
            f"buy balances={balances!r}",
        )
    if not fail_on_refusal:
        assertion(
            assertions,
            "diagnostic mode records refusal without failing process",
            result.get("status") == 0,
            f"status={result.get('status')!r}",
        )


def check_wylda(
    result: dict[str, Any],
    assertions: list[dict[str, Any]],
    expected_names: list[str],
) -> None:
    save = result.get("save")
    if not isinstance(save, dict):
        return
    party = save.get("party") if isinstance(save.get("party"), list) else []
    names = party_names(party)
    check_party_once(assertions, save, expected_names, "Wylda route")
    wylda_count = names.count("Wylda Figwort")
    assertion(
        assertions,
        "Wylda joins exactly once",
        wylda_count == 1,
        f"Wylda Figwort count={wylda_count}, party size={len(party)}",
    )
    joined = [
        fact
        for fact in result["facts"]
        if fact.get("kind") == "joined" and isinstance(fact.get("party"), dict)
    ]
    joined_members = joined[-1].get("party", {}).get("members") if joined else None
    assertion(
        assertions,
        "join fact agrees with saved party size",
        bool(joined)
        and joined[-1].get("party", {}).get("joined") is True
        and joined_members == len(party),
        f"joined_members={joined_members!r}, saved_party_size={len(party)}",
    )
    assertion(
        assertions,
        "Wylda route grows the input party by one",
        len(party) == len(expected_names) + 1,
        f"input={len(expected_names)}, output={len(party)}",
    )


def route_assertions(
    config: Config,
    route: str,
    result: dict[str, Any],
    loaded_save: dict[str, Any] | None,
) -> list[dict[str, Any]]:
    assertions: list[dict[str, Any]] = []
    save = check_common(config, route, result, assertions)
    if save is None:
        return assertions
    if route == "opening-revisit" and loaded_save is not None:
        raw_party = loaded_save.get("party", [])
    else:
        raw_party = party_from_files(config.party)
    expected_names = party_names(raw_party if isinstance(raw_party, list) else [])
    if route == "opening-lead":
        check_opening_lead(result, assertions, expected_names)
    elif route == "opening-revisit":
        check_opening_revisit(result, assertions, loaded_save)
    elif route.startswith("forest-"):
        check_forest(route, result, assertions, expected_names)
    elif route == "shops-rest":
        check_shops(result, assertions, expected_names, config.fail_on_refusal)
    elif route == "wylda-recruit":
        check_wylda(result, assertions, expected_names)
    return assertions


def write_handoff(config: Config, run_id: str, run_dir: Path) -> Path:
    handoff = run_dir / "HANDOFF.md"
    handoff.write_text(
        "\n".join(
            [
                "# Temporary opening verifier handoff",
                "",
                "This directory is generated by verify-opening.py. It is temporary evidence, not a campaign status document.",
                "",
                f"- Run: {run_id}",
                f"- CLI: {config.cli}",
                f"- Campaign: {config.campaign}",
                f"- Modules: {', '.join(str(path) for path in config.modules)}",
                f"- Historical snapshot: {config.snapshot or 'not used; current repository defaults'}",
                f"- Exact route receipts: {run_dir}",
                "",
                "## Reuse",
                "",
                "Run against the current repository source with a generated or existing party:",
                "",
                "    PARTY_DIR=/path/to/party GOLDBOX_CLI=goldbox campaigns/blackapple-brugh/scripts/create-party.sh",
                "    python3 campaigns/blackapple-brugh/scripts/verify-opening.py --party-dir /path/to/party --json",
                "",
                "The goldbox executable is found on PATH; override it with --cli or GOLDBOX_CLI. Override campaign, modules, party, party directory, script root, seed, or output with their command-line options or GOLDBOX_* environment variables.",
                "",
                "For a historical replay, pass --snapshot explicitly and provide its CLI, party directory, and script root explicitly. The runner does not discover or copy session receipts into the output.",
                "If a current module presents a new menu, update the durable route input deliberately or pass --append-command for a one-route diagnostic; this runner never injects an unread legal choice heuristically.",
                "A current-module run needs every required dependency directory and a validating current source. This bounded verifier records source-resolution or validation failures; it does not repair unrelated campaign definitions as part of opening proof.",
                "",
                "## Scope and limits",
                "",
                "Opening proof checks Hen's Teeth preparation, null pending menu, one guarded XP application to each input member, and a revisit with unchanged XP.",
                "Kindness and negotiation check the actual noncombat forest reward and its once-only guard; retreat checks recovery without resolution or reward.",
                "The automatic combat route is labelled balance-only. It is not manual combat proof or full-adventure acceptance.",
                "Shop failure is intentional: shops-rest must produce one poor-purchase play.refusal and exit 1 with --fail-on-refusal, while its saved state proves the later sell, buy, leave, and rest actions.",
                "Wylda checks the saved party count and the join fact against the actual input party, without assuming a fixed roster size.",
                "This bounded runner does not accept the full adventure, finale, visual art, or cross-area callback integration.",
                "",
                "The report records save fields observed from raw route receipts under observed-save-fields.json; assertions use those runtime fields rather than coaching Core source behavior.",
            ]
        )
        + "\n",
        encoding="utf-8",
    )
    return handoff


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv or sys.argv[1:])
    try:
        config = resolve_config(args)
        require_path(config.cli, "goldbox CLI")
        require_path(config.campaign, "campaign")
        for module_directory in config.modules:
            require_path(module_directory, "modules directory")
        require_path(config.script_root, "script root")
        for party_path in config.party:
            require_path(party_path, "party file")
        if not config.party:
            raise ValueError(
                "no party files resolved; pass --party or --party-dir, or run "
                f"{REPO_ROOT / 'campaigns' / 'blackapple-brugh' / 'scripts' / 'create-party.sh'} "
                "with PARTY_DIR set"
            )
        if config.explicit_load is not None:
            require_path(config.explicit_load, "explicit load save")
    except (FileNotFoundError, ValueError) as error:
        print(f"verify-opening configuration error: {error}", file=sys.stderr)
        return 2

    run_id, run_dir = create_run_dir(config.output_root)
    handoff = write_handoff(config, run_id, run_dir)
    results: list[dict[str, Any]] = []
    route_results: dict[str, dict[str, Any]] = {}
    observed_shapes: dict[str, Any] = {}

    execution_routes = list(config.routes)
    implicit_lead = False
    if (
        "opening-revisit" in execution_routes
        and "opening-lead" not in execution_routes
        and config.explicit_load is None
    ):
        execution_routes.insert(0, "opening-lead")
        implicit_lead = True

    for route in execution_routes:
        route_dir = run_dir / route
        route_dir.mkdir(parents=True, exist_ok=False)
        assertions: list[dict[str, Any]] = []
        loaded_save: dict[str, Any] | None = None
        try:
            source_script = find_script(config, route)
            input_script = make_input_script(source_script, route_dir, config.append_commands)
            load_path: Path | None = None
            if route == "opening-revisit":
                load_path = config.explicit_load
                if load_path is None:
                    lead = route_results.get("opening-lead")
                    load_path = lead.get("save_path") if lead else None
                if load_path is None or not load_path.exists():
                    raise FileNotFoundError(
                        "opening-revisit requires opening-lead's saved state or --load"
                    )
                loaded_save = json.loads(load_path.read_text(encoding="utf-8"))
            result = run_subprocess(config, route, route_dir, input_script, load_path)
            assertions = route_assertions(config, route, result, loaded_save)
            save = result.get("save")
            if isinstance(save, dict):
                observed_shapes[route] = save_shape(save)
            report = {
                "route": route,
                "classification": ROUTE_CLASSIFICATIONS.get(route, "bounded-route"),
                "implicit_prerequisite": route == "opening-lead" and implicit_lead,
                "route_dir": str(route_dir),
                "save": str(result["save_path"]),
                "status": result.get("status"),
                "expected_status": expected_status(route, config.fail_on_refusal),
                "assertions": assertions,
                "passed": all(item["ok"] for item in assertions),
                "refusal_count": sum(1 for fact in result["facts"] if fact.get("kind") == "refused"),
                "stdout_error": result.get("stdout_error"),
            }
            route_results[route] = result
        except (FileNotFoundError, OSError, ValueError, json.JSONDecodeError) as error:
            assertions.append({"name": "route execution", "ok": False, "detail": str(error)})
            report = {
                "route": route,
                "classification": ROUTE_CLASSIFICATIONS.get(route, "bounded-route"),
                "implicit_prerequisite": route == "opening-lead" and implicit_lead,
                "route_dir": str(route_dir),
                "status": None,
                "expected_status": expected_status(route, config.fail_on_refusal),
                "assertions": assertions,
                "passed": False,
                "refusal_count": 0,
                "error": str(error),
            }
        results.append(report)

    write_json(run_dir / "observed-save-fields.json", observed_shapes)
    report = {
        "run_id": run_id,
        "passed": all(item.get("passed") for item in results) and len(results) == len(execution_routes),
        "routes_requested": list(config.routes),
        "routes_executed": execution_routes,
        "implicit_opening_prerequisite": implicit_lead,
        "fail_on_refusal": config.fail_on_refusal,
        "cli": str(config.cli),
        "campaign": str(config.campaign),
        "modules": [str(path) for path in config.modules],
        "party": [str(path) for path in config.party],
        "snapshot": str(config.snapshot),
        "script_root": str(config.script_root),
        "output_root": str(config.output_root),
        "run_dir": str(run_dir),
        "handoff": str(handoff),
        "observed_save_fields": str(run_dir / "observed-save-fields.json"),
        "routes": results,
    }
    write_json(run_dir / "report.json", report)

    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True))
    else:
        print(f"verifier run: {run_dir}")
        for item in results:
            mark = "PASS" if item.get("passed") else "FAIL"
            print(
                f"{mark} {item['route']} [{item['classification']}] "
                f"status={item.get('status')!r} expected={item.get('expected_status')!r} "
                f"refusals={item.get('refusal_count', 0)}"
            )
        print(f"handoff: {handoff}")
        print(f"report: {run_dir / 'report.json'}")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
