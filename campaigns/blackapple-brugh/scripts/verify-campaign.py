#!/usr/bin/env python3
"""Verify complete ordinary Blackapple routes against a published goldbox CLI.

The runner starts each route from the authored village opening, records the
exact command and process receipts, checks the resulting save, then reloads
that save with the same campaign/module roots.  It never edits campaign data or
constructs an in-memory shortcut to a later event.
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
CURRENT_CAMPAIGN = (
    REPO_ROOT
    / "campaigns"
    / "blackapple-brugh"
    / "modules"
    / "blackapple-brugh"
)
CURRENT_CAMPAIGN_MODULES = CURRENT_CAMPAIGN.parent
CURRENT_MODULES = REPO_ROOT / "modules"
CURRENT_SCRIPT_ROOT = (
    REPO_ROOT / "campaigns" / "blackapple-brugh" / "scripts" / "routes"
)

CHILD_KEYS = (
    "child_amelia_goodall",
    "child_arthur_figwort",
    "child_bernard_goodall",
    "child_giles_weadley",
    "child_philip_anvil",
    "child_stevie_leeford",
    "child_ursula_cooke",
)
DOUBLE_KEYS = (
    "double_amelia_goodall",
    "double_arthur_figwort",
    "double_bernard_goodall",
    "double_giles_weadley",
    "double_philip_anvil",
    "double_stevie_leeford",
    "double_ursula_cooke",
)


@dataclass(frozen=True)
class RouteSpec:
    name: str
    script_name: str
    seed: str
    classification: str
    expected_ending: str
    expected_children: dict[str, str]
    expected_xp: int
    expected_xp_applications: int
    expected_middle_xp: bool
    expected_recovery: str
    expected_court_service: bool
    expected_treasure: bool
    expected_combat: bool


ROUTES: dict[str, RouteSpec] = {
    "success": RouteSpec(
        name="success",
        script_name="full-success.script",
        seed="9360",
        classification="full-return-automatic-combat-free",
        expected_ending="ending.court_bound",
        expected_children={key: "returned" for key in CHILD_KEYS},
        expected_xp=900,
        expected_xp_applications=2,
        expected_middle_xp=True,
        expected_recovery="pool",
        expected_court_service=True,
        expected_treasure=True,
        expected_combat=False,
    ),
    "partial": RouteSpec(
        name="partial",
        script_name="full-partial.script",
        seed="9302",
        classification="partial-return-automatic-combat-free",
        expected_ending="ending.partial_return",
        expected_children={
            "child_amelia_goodall": "captive",
            "child_arthur_figwort": "returned",
            "child_bernard_goodall": "captive",
            "child_giles_weadley": "returned",
            "child_philip_anvil": "returned",
            "child_stevie_leeford": "returned",
            "child_ursula_cooke": "returned",
        },
        expected_xp=900,
        expected_xp_applications=2,
        expected_middle_xp=True,
        expected_recovery="pit",
        expected_court_service=False,
        expected_treasure=True,
        expected_combat=False,
    ),
    "success-six": RouteSpec(
        name="success-six",
        script_name="full-success-six.script",
        seed="9360",
        classification="full-return-six-party-stair-fallback-automatic-combat-free",
        expected_ending="ending.court_bound",
        expected_children={key: "returned" for key in CHILD_KEYS},
        expected_xp=900,
        expected_xp_applications=2,
        expected_middle_xp=True,
        expected_recovery="pool",
        expected_court_service=True,
        expected_treasure=True,
        expected_combat=False,
    ),
    "success-level": RouteSpec(
        name="success-level",
        script_name="full-success-level.script",
        seed="9431",
        classification="full-return-levels-1-to-3-automatic-combat-free",
        expected_ending="ending.court_bound",
        expected_children={key: "returned" for key in CHILD_KEYS},
        expected_xp=900,
        expected_xp_applications=2,
        expected_middle_xp=True,
        expected_recovery="pool",
        expected_court_service=True,
        expected_treasure=True,
        expected_combat=False,
    ),
    "zero": RouteSpec(
        name="zero",
        script_name="full-zero.script",
        seed="9304",
        classification="zero-return-automatic-combat-free",
        expected_ending="ending.truth_without_return",
        expected_children={key: "captive" for key in CHILD_KEYS},
        expected_xp=300,
        expected_xp_applications=1,
        expected_middle_xp=False,
        expected_recovery="pool",
        expected_court_service=False,
        expected_treasure=False,
        expected_combat=False,
    ),
    "adverse": RouteSpec(
        name="adverse",
        script_name="full-adverse.script",
        seed="9300",
        classification="adverse-c13-automatic-combat-balance-only",
        expected_ending="ending.truth_without_return",
        expected_children={key: "captive" for key in CHILD_KEYS},
        expected_xp=350,
        expected_xp_applications=2,
        expected_middle_xp=False,
        expected_recovery="pit",
        expected_court_service=False,
        expected_treasure=False,
        expected_combat=True,
    ),
}

# The default all-route batch uses the four outcome routes with the ordinary
# four-member party. The six-member capacity fallback and the advancement
# route are explicit because each input script intentionally takes a different
# branch or records player level choices.
DEFAULT_ROUTES = ("success", "partial", "zero", "adverse")


@dataclass(frozen=True)
class Config:
    cli: Path
    campaign: Path
    modules: tuple[Path, ...]
    party: tuple[Path, ...]
    snapshot: Path | None
    script_root: Path
    output_root: Path
    routes: tuple[str, ...]
    seed_override: str | None
    combat_control: str
    repo_root: Path


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Run ordinary Blackapple full-campaign routes, assert raw save "
            "fields, and verify reload continuity."
        )
    )
    parser.add_argument("--cli", type=Path, help="goldbox executable (GOLDBOX_CLI or PATH)")
    parser.add_argument("--campaign", type=Path, help="campaign module directory")
    parser.add_argument(
        "--modules",
        type=Path,
        action="append",
        help="module search directory; repeatable",
    )
    parser.add_argument(
        "--party",
        action="append",
        help="party JSON path or comma-separated paths; repeatable",
    )
    parser.add_argument("--party-dir", type=Path, help="directory containing party JSON files")
    parser.add_argument(
        "--snapshot",
        type=Path,
        help=(
            "explicit historical source snapshot; defaults remain the current "
            "repository and PATH goldbox"
        ),
    )
    parser.add_argument("--script-root", type=Path, help="directory containing full-*.script inputs")
    parser.add_argument("--output", type=Path, help="parent directory for unique temporary receipts")
    parser.add_argument(
        "--route",
        action="append",
        choices=(
            "all",
            "success",
            "success-six",
            "success-level",
            "partial",
            "zero",
            "adverse",
        ),
        help="route to run; repeatable (default: all)",
    )
    parser.add_argument("--seed", help="override the route seed for every selected route")
    parser.add_argument(
        "--combat-control",
        choices=("auto", "manual"),
        default="auto",
        help="goldbox combat control; auto is balance/headless evidence only",
    )
    parser.add_argument("--json", action="store_true", help="print the full report as JSON")
    return parser.parse_args(argv)


def split_paths(values: Iterable[str]) -> tuple[Path, ...]:
    result: list[Path] = []
    for value in values:
        for part in value.split(","):
            if part.strip():
                result.append(Path(part.strip()).expanduser().resolve())
    return tuple(result)


def cli_path(argument: Path | None) -> Path:
    value = argument or (Path(os.environ["GOLDBOX_CLI"]) if os.environ.get("GOLDBOX_CLI") else None)
    if value is not None:
        found = shutil.which(str(value))
        return Path(found).resolve() if found else value.expanduser().resolve()
    found = shutil.which("goldbox")
    if not found:
        raise ValueError("goldbox was not found on PATH; pass --cli or set GOLDBOX_CLI")
    return Path(found).resolve()


def snapshot_path(snapshot: Path, *parts: str) -> Path:
    direct = snapshot.joinpath(*parts)
    if direct.exists():
        return direct.resolve()
    source = snapshot / "source" / Path(*parts)
    return source.resolve()


def resolve_config(args: argparse.Namespace) -> Config:
    snapshot = args.snapshot.expanduser().resolve() if args.snapshot else None
    cli = cli_path(args.cli)

    if args.campaign:
        campaign = args.campaign.expanduser().resolve()
    elif snapshot:
        campaign = snapshot_path(snapshot, "campaigns", "blackapple-brugh", "modules", "blackapple-brugh")
    else:
        campaign = Path(
            os.environ.get("GOLDBOX_CAMPAIGN", str(CURRENT_CAMPAIGN))
        ).expanduser().resolve()

    if args.modules:
        modules = tuple(path.expanduser().resolve() for path in args.modules)
    elif snapshot:
        modules = (
            snapshot_path(snapshot, "modules"),
            snapshot_path(snapshot, "campaigns", "blackapple-brugh", "modules"),
        )
    elif os.environ.get("GOLDBOX_MODULES"):
        modules = split_paths([os.environ["GOLDBOX_MODULES"]])
    else:
        modules = (CURRENT_MODULES, CURRENT_CAMPAIGN_MODULES)

    raw_party = args.party
    if raw_party is None and os.environ.get("GOLDBOX_PARTY"):
        raw_party = [os.environ["GOLDBOX_PARTY"]]
    party = split_paths(raw_party or [])
    if not party:
        if args.party_dir:
            party_dir = args.party_dir.expanduser().resolve()
        elif os.environ.get("GOLDBOX_PARTY_DIR"):
            party_dir = Path(os.environ["GOLDBOX_PARTY_DIR"]).expanduser().resolve()
        elif snapshot:
            party_dir = snapshot / "party"
        else:
            raise ValueError(
                "no party was supplied; pass --party or --party-dir, or set GOLDBOX_PARTY_DIR"
            )
        party = tuple(sorted(party_dir.glob("*.json")))

    if args.script_root:
        script_root = args.script_root.expanduser().resolve()
    elif os.environ.get("GOLDBOX_SCRIPT_ROOT"):
        script_root = Path(os.environ["GOLDBOX_SCRIPT_ROOT"]).expanduser().resolve()
    elif snapshot:
        candidate = snapshot / "scripts" / "routes"
        script_root = (candidate if candidate.exists() else snapshot / "scripts").resolve()
    else:
        script_root = CURRENT_SCRIPT_ROOT

    output_value = args.output or os.environ.get("GOLDBOX_VERIFY_OUT")
    output_root = (
        Path(output_value).expanduser().resolve()
        if output_value
        else Path(tempfile.mkdtemp(prefix="goldbox-campaign-verify-"))
    )
    requested = tuple(args.route or ("all",))
    routes = DEFAULT_ROUTES if "all" in requested else requested
    return Config(
        cli=cli,
        campaign=campaign,
        modules=modules,
        party=party,
        snapshot=snapshot,
        script_root=script_root,
        output_root=output_root,
        routes=routes,
        seed_override=args.seed,
        combat_control=args.combat_control,
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
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def party_from_files(paths: Iterable[Path]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for path in paths:
        try:
            value = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise ValueError(f"cannot read party JSON {path}: {error}") from error
        if not isinstance(value, dict):
            raise ValueError(f"party JSON is not an object: {path}")
        result.append(value)
    return result


def campaign_variables(save: dict[str, Any]) -> dict[str, Any]:
    variables = save.get("variables")
    if not isinstance(variables, dict):
        return {}
    campaign = variables.get("campaign")
    return campaign if isinstance(campaign, dict) else {}


def transcript_facts(stdout: dict[str, Any] | None) -> list[dict[str, Any]]:
    if not isinstance(stdout, dict):
        return []
    result: list[dict[str, Any]] = []
    transcript = stdout.get("transcript")
    if not isinstance(transcript, list):
        return result
    for step in transcript:
        if not isinstance(step, dict):
            continue
        facts = step.get("facts")
        if isinstance(facts, list):
            result.extend(fact for fact in facts if isinstance(fact, dict))
    return result


def transcript_text(facts: Iterable[dict[str, Any]]) -> str:
    return "\n".join(str(fact.get("text", "")) for fact in facts).lower()


def save_signature(save: dict[str, Any]) -> dict[str, Any]:
    campaign = campaign_variables(save)
    party = save.get("party") if isinstance(save.get("party"), list) else []
    return {
        "ended": save.get("ended"),
        "pending_menu": save.get("pending_menu"),
        "pending_shop": save.get("pending_shop"),
        "area": save.get("area"),
        "x": save.get("x"),
        "y": save.get("y"),
        "facing": save.get("facing"),
        "elapsed_days": save.get("elapsed_days"),
        "inventory": save.get("inventory"),
        "campaign": {
            key: campaign.get(key)
            for key in sorted(
                set(CHILD_KEYS + DOUBLE_KEYS)
                | {
                    "ending_id",
                    "middle_xp_awarded",
                    "opening_xp_awarded",
                    "return_account_complete",
                    "court_service_accepted",
                    "recovery_pool",
                    "recovery_pit",
                    "recovery_return_triage",
                    "route_dumbwaiter",
                }
            )
        },
        "party_experience": [
            member.get("experience")
            for member in party
            if isinstance(member, dict)
        ],
        "party_levels": [
            member.get("levels")
            for member in party
            if isinstance(member, dict)
        ],
        "party_gold": [
            (
                member.get("balances", {}).get("gold")
                if isinstance(member, dict) and isinstance(member.get("balances"), dict)
                else None
            )
            for member in party
            if isinstance(member, dict)
        ],
    }


def copy_input(source: Path, route_dir: Path) -> Path:
    target = route_dir / "input.script"
    target.write_bytes(source.read_bytes())
    return target


def command_for(
    config: Config,
    input_script: Path,
    save_path: Path,
    spec: RouteSpec,
    load_path: Path | None,
) -> list[str]:
    command = [str(config.cli), "play", "--campaign", str(config.campaign)]
    for modules in config.modules:
        command.extend(["--modules", str(modules)])
    if load_path is None:
        command.extend(["--party", ",".join(str(path) for path in config.party)])
        command.extend(["--seed", config.seed_override or spec.seed])
    else:
        command.extend(["--load", str(load_path)])
    command.extend(
        [
            "--script",
            str(input_script),
            "--save",
            str(save_path),
            "--combat-control",
            config.combat_control,
        ]
    )
    if load_path is None:
        command.append("--fail-on-refusal")
    command.append("--json")
    return command


def run_process(
    config: Config,
    route_dir: Path,
    label: str,
    command: list[str],
) -> dict[str, Any]:
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

    stdout_raw = route_dir / f"{label}.stdout.raw"
    stderr_raw = route_dir / f"{label}.stderr.raw"
    status_path = route_dir / f"{label}.status"
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
        write_json(route_dir / f"{label}.stdout.json", stdout_json)

    receipt = {
        "argv": command,
        "cwd": str(config.repo_root),
        "status": status,
        "stdout_raw": str(stdout_raw),
        "stderr_raw": str(stderr_raw),
        "status_file": str(status_path),
        "stdout_json": str(route_dir / f"{label}.stdout.json") if stdout_json is not None else None,
    }
    write_json(route_dir / f"{label}.command.json", receipt)
    return {
        "label": label,
        "command": command,
        "status": status,
        "stdout_json": stdout_json,
        "stdout_error": stdout_error,
        "facts": transcript_facts(stdout_json),
        "stdout_raw": stdout_raw,
        "stderr_raw": stderr_raw,
        "status_path": status_path,
    }


def assertion(assertions: list[dict[str, Any]], name: str, ok: bool, detail: str) -> None:
    assertions.append({"name": name, "ok": bool(ok), "detail": detail})


def load_save(path: Path) -> dict[str, Any] | None:
    if not path.exists():
        return None
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None
    return value if isinstance(value, dict) else None


def total_gold(party: Any) -> int | float:
    if not isinstance(party, list):
        return 0
    total: int | float = 0
    for member in party:
        if not isinstance(member, dict):
            continue
        balances = member.get("balances")
        if isinstance(balances, dict) and isinstance(balances.get("gold"), (int, float)):
            total += balances["gold"]
    return total


def route_assertions(
    config: Config,
    spec: RouteSpec,
    fresh: dict[str, Any],
    fresh_save: dict[str, Any] | None,
    reload_result: dict[str, Any],
    reload_save: dict[str, Any] | None,
    input_party: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    assertions: list[dict[str, Any]] = []
    fresh_stdout = fresh.get("stdout_json")
    reload_stdout = reload_result.get("stdout_json")
    assertion(assertions, "fresh process exits successfully", fresh.get("status") == 0, f"status={fresh.get('status')!r}")
    assertion(assertions, "fresh transcript is JSON", isinstance(fresh_stdout, dict), fresh.get("stdout_error") or "parsed")
    assertion(assertions, "fresh save is present", fresh_save is not None, str(fresh.get("save_path", "")))
    assertion(assertions, "reload process exits successfully", reload_result.get("status") == 0, f"status={reload_result.get('status')!r}")
    assertion(assertions, "reload transcript is JSON", isinstance(reload_stdout, dict), reload_result.get("stdout_error") or "parsed")
    assertion(assertions, "reload save is present", reload_save is not None, str(reload_result.get("save_path", "")))

    if fresh_save is None:
        return assertions
    fresh_facts = fresh.get("facts", [])
    fresh_text = transcript_text(fresh_facts)
    campaign = campaign_variables(fresh_save)
    input_count = len(input_party)
    party = fresh_save.get("party") if isinstance(fresh_save.get("party"), list) else []
    input_gold = total_gold(input_party)
    saved_gold = total_gold(party)

    assertion(assertions, "adventure ended", fresh_save.get("ended") is True, f"ended={fresh_save.get('ended')!r}")
    assertion(assertions, "pending menu is clear", fresh_save.get("pending_menu") is None, f"pending_menu={fresh_save.get('pending_menu')!r}")
    assertion(
        assertions,
        "save position is the Hen's Teeth return position",
        (fresh_save.get("area"), fresh_save.get("x"), fresh_save.get("y"), fresh_save.get("facing"))
        == ("blackapple-brugh:environs", 4, 1, "east"),
        f"position={(fresh_save.get('area'), fresh_save.get('x'), fresh_save.get('y'), fresh_save.get('facing'))!r}",
    )
    runtime_position = fresh_stdout.get("position") if isinstance(fresh_stdout, dict) else None
    assertion(
        assertions,
        "transcript reports the same return position",
        runtime_position == {"area": "blackapple-brugh:environs", "x": 4, "y": 1, "facing": "east"},
        f"runtime_position={runtime_position!r}",
    )
    assertion(assertions, "Brugh entry and return are recorded", campaign.get("brugh_entered") is True and campaign.get("brugh_exited") is True, f"entered={campaign.get('brugh_entered')!r}, exited={campaign.get('brugh_exited')!r}")
    assertion(assertions, "return triage and account are complete", campaign.get("recovery_return_triage") is True and campaign.get("return_account_complete") is True, f"triage={campaign.get('recovery_return_triage')!r}, account={campaign.get('return_account_complete')!r}")
    assertion(assertions, "mirror route is explicit", campaign.get("mirror_route") == "hen_teeth", f"mirror_route={campaign.get('mirror_route')!r}")
    assertion(assertions, "party size remains the supplied size", len(party) == input_count, f"input={input_count}, saved={len(party)}")
    if spec.name in {"success", "success-six", "success-level", "partial"}:
        assertion(
            assertions,
            "dumbwaiter text uses the live party count",
            f"live party count is {input_count}" in fresh_text,
            f"searched for live party count {input_count}",
        )
    if spec.name == "success-six":
        assertion(
            assertions,
            "six-member capacity takes the connected Level 3 stair fallback",
            "six active members exceed the authored 200-pound dumbwaiter adaptation" in fresh_text
            and "leave the lift and stay on level 3" in fresh_text,
            "searched for the six-member capacity refusal and stair fallback",
        )

    child_values = {key: campaign.get(key) for key in CHILD_KEYS}
    double_values = {key: campaign.get(key) for key in DOUBLE_KEYS}
    assertion(assertions, "all seven child fields are present", set(child_values) == set(CHILD_KEYS) and all(value in {"captive", "freed", "returned"} for value in child_values.values()), repr(child_values))
    assertion(assertions, "all seven double fields are present", set(double_values) == set(DOUBLE_KEYS) and all(value in {"masked", "unknown", "exposed", "released", "removed", "contained"} for value in double_values.values()), repr(double_values))
    assertion(assertions, "child outcome matches route", child_values == spec.expected_children, f"observed={child_values!r}, expected={spec.expected_children!r}")
    assertion(assertions, "ending matches route", campaign.get("ending_id") == spec.expected_ending, f"ending={campaign.get('ending_id')!r}")
    assertion(assertions, "court service consent is explicit", campaign.get("court_service_accepted") is spec.expected_court_service, f"court_service_accepted={campaign.get('court_service_accepted')!r}")

    experience_values = [member.get("experience") for member in party if isinstance(member, dict)]
    assertion(assertions, "every active member has the route XP", bool(experience_values) and all(value == spec.expected_xp for value in experience_values), f"experience={experience_values!r}, expected={spec.expected_xp}")
    xp_facts = [fact for fact in fresh_facts if fact.get("kind") == "experience"]
    assertion(assertions, "XP applications have the expected count", len(xp_facts) == spec.expected_xp_applications, f"experience_facts={len(xp_facts)}")
    opening_events = [fact for fact in fresh_facts if fact.get("kind") == "variable" and "opening_xp_awarded is now true" in str(fact.get("text", ""))]
    middle_events = [fact for fact in fresh_facts if fact.get("kind") == "variable" and "middle_xp_awarded is now true" in str(fact.get("text", ""))]
    assertion(assertions, "opening XP guard fires once", campaign.get("opening_xp_awarded") is True and len(opening_events) == 1, f"guard={campaign.get('opening_xp_awarded')!r}, events={len(opening_events)}")
    assertion(assertions, "middle XP guard matches child progress", campaign.get("middle_xp_awarded") is spec.expected_middle_xp and len(middle_events) == (1 if spec.expected_middle_xp else 0), f"guard={campaign.get('middle_xp_awarded')!r}, events={len(middle_events)}")

    combat_facts = [fact for fact in fresh_facts if fact.get("kind") == "combat"]
    assertion(assertions, "combat classification matches route", bool(combat_facts) is spec.expected_combat, f"combat_facts={len(combat_facts)}; automatic combat is balance-only")
    if spec.name == "adverse":
        assertion(assertions, "C13 immediate attack reaches C18", "whole-party attack" in fresh_text and campaign.get("court_alarm") == 2, f"court_alarm={campaign.get('court_alarm')!r}")
    else:
        assertion(assertions, "non-adverse routes contain no combat", not combat_facts, f"combat_facts={len(combat_facts)}")

    recovery_value = campaign.get(f"recovery_{spec.expected_recovery}")
    assertion(assertions, "route recovery flag is set", recovery_value is True, f"recovery_{spec.expected_recovery}={recovery_value!r}")
    other_recovery = "pit" if spec.expected_recovery == "pool" else "pool"
    assertion(assertions, "alternate recovery flag remains clear", campaign.get(f"recovery_{other_recovery}") is False, f"recovery_{other_recovery}={campaign.get(f'recovery_{other_recovery}')!r}")

    inventory = fresh_save.get("inventory") if isinstance(fresh_save.get("inventory"), list) else []
    treasure_events = [fact for fact in fresh_facts if fact.get("kind") == "variable" and "brugh_l3_chest_one_taken is now true" in str(fact.get("text", ""))]
    if spec.expected_treasure:
        assertion(assertions, "treasure is carried exactly once", inventory.count("blackapple-brugh:obsidian_bracers") == 1 and len(treasure_events) == 1, f"obsidian_bracers={inventory.count('blackapple-brugh:obsidian_bracers')}, chest_events={len(treasure_events)}")
        assertion(assertions, "treasure changes party gold by the authored amount", saved_gold - input_gold >= 471, f"input_gold={input_gold}, saved_gold={saved_gold}")
    else:
        assertion(assertions, "zero/adverse routes do not take the chest", "blackapple-brugh:obsidian_bracers" not in inventory and not treasure_events, f"inventory={inventory!r}, chest_events={len(treasure_events)}")
        assertion(assertions, "zero/adverse routes keep chest gold out", saved_gold == input_gold, f"input_gold={input_gold}, saved_gold={saved_gold}")

    if spec.name in {"success", "success-six", "success-level"}:
        assertion(assertions, "C16 sling and doll evidence are carried", inventory.count("blackapple-brugh:bernard_sling") == 1 and inventory.count("blackapple-brugh:ursula_cloth_doll") == 1, f"inventory={inventory!r}")
        assertion(assertions, "C17 readable water avoidance is recorded", campaign.get("recovery_pool") is True and "mark the waterline" in fresh_text, f"recovery_pool={campaign.get('recovery_pool')!r}")
    if spec.name == "success-level":
        level_counts = [
            len(member.get("levels", []))
            for member in party
            if isinstance(member, dict) and isinstance(member.get("levels"), list)
        ]
        level_facts = [fact for fact in fresh_facts if fact.get("kind") == "level"]
        reached_level_two = [fact for fact in level_facts if " reaches level 2 " in str(fact.get("text", ""))]
        reached_level_three = [fact for fact in level_facts if " reaches level 3 " in str(fact.get("text", ""))]
        subclass_ids = {
            "fifth-srd:champion",
            "fifth-srd:thief",
            "fifth-srd:life_domain",
            "fifth-srd:evoker",
        }
        observed_subclasses = {
            feature
            for member in party
            if isinstance(member, dict) and isinstance(member.get("levels"), list)
            for level in member["levels"]
            if isinstance(level, dict) and isinstance(level.get("features"), list)
            for feature in level["features"]
            if feature in subclass_ids
        }
        assertion(assertions, "advancement route takes level 2 after the opening award", level_counts and all(count >= 2 for count in level_counts) and len(reached_level_two) == input_count, f"levels={level_counts!r}, level_2_facts={len(reached_level_two)}")
        assertion(assertions, "advancement route takes level 3 after the rescue award", level_counts and all(count == 3 for count in level_counts) and len(reached_level_three) == input_count, f"levels={level_counts!r}, level_3_facts={len(reached_level_three)}")
        assertion(assertions, "advancement route records each authored subclass choice", observed_subclasses == subclass_ids, f"subclasses={sorted(observed_subclasses)!r}")
    if spec.name == "partial":
        assertion(assertions, "partial account protects families", campaign.get("epilogue_protected_account") is True, f"epilogue_protected_account={campaign.get('epilogue_protected_account')!r}")
    if spec.name == "zero":
        assertion(assertions, "zero account publishes truth without return", campaign.get("epilogue_public_truth") is True and campaign.get("epilogue_truth_verified") is True, f"public={campaign.get('epilogue_public_truth')!r}, verified={campaign.get('epilogue_truth_verified')!r}")

    refusal_count = sum(1 for fact in fresh_facts if fact.get("kind") == "refused")
    assertion(assertions, "fresh route has no command refusals", refusal_count == 0, f"refusals={refusal_count}")
    reload_refusals = [fact for fact in reload_result.get("facts", []) if fact.get("kind") == "refused"]
    assertion(assertions, "ended save reload reports only the expected end refusal", len(reload_refusals) == 1 and "adventure has ended" in str(reload_refusals[0].get("text", "")).lower(), f"reload_refusals={len(reload_refusals)}")
    if reload_save is not None:
        assertion(assertions, "reload preserves observed save fields", save_signature(fresh_save) == save_signature(reload_save), "fresh and reload signatures compared")
    return assertions


def write_handoff(config: Config, run_dir: Path, input_party: list[dict[str, Any]]) -> Path:
    path = run_dir / "HANDOFF.md"
    lines = [
        "# Temporary full-campaign verifier handoff",
        "",
        "This is receipt evidence generated by verify-campaign.py. It is not campaign status and is not a substitute for manual combat or visual acceptance.",
        "",
        f"- CLI: `{config.cli}`",
        f"- Campaign: `{config.campaign}`",
        f"- Module roots: {', '.join(f'`{path}`' for path in config.modules)}",
        f"- Party files: {len(input_party)} active members",
        f"- Snapshot: `{config.snapshot}`" if config.snapshot else "- Snapshot: current repository defaults",
        f"- Receipts: `{run_dir}`",
        "",
        "## Portable current-source command",
        "",
        "```bash",
        f"GOLDBOX_CLI=goldbox python3 {REPO_ROOT / 'campaigns/blackapple-brugh/scripts/verify-campaign.py'} --party-dir /path/to/party --route all",
        "```",
        "",
        "The default CLI comes from PATH or GOLDBOX_CLI. The default campaign and module roots come from this checkout. The default receipt directory is a new tempfile directory; no historical session receipt is discovered or copied.",
        "",
        "## Scope and limits",
        "",
        "The success route walks the authored village opening, enters through Hen's Teeth, visits connected Level 1/2/3 rooms, rescues all seven named children, takes C16 evidence and C27 treasure, returns through the sole Hen's Teeth mirror, accounts for all children and doubles, and explicitly confirms permanent court service.",
        "The partial, zero, and adverse routes assert separate ending states. The adverse route uses --combat-control auto and therefore proves a repeatable headless balance path only; it is not manual combat acceptance.",
        "The explicit success-six route supplies six active members, records the live capacity refusal, and verifies the connected Level 3 stair fallback. The explicit success-level route takes real level-2 commands after the opening 300 XP and level-3 subclass commands after the rescue 600 XP; its level fields and level facts are checked separately from XP.",
        "Every route reloads its ended save with the same campaign and module roots and compares raw save fields. A fresh route must have no refusal facts; the ended-save status reload has its one expected end refusal.",
        "",
        "If a route fails, the copied input script and exact fresh/reload stdout, stderr, status, command, and save files remain in that route receipt directory for diagnosis.",
    ]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return path


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv or sys.argv[1:])
    try:
        config = resolve_config(args)
        require_path(config.cli, "goldbox CLI")
        require_path(config.campaign, "campaign")
        for module_directory in config.modules:
            require_path(module_directory, "module directory")
        require_path(config.script_root, "script root")
        input_party = party_from_files(config.party)
        if not input_party:
            raise ValueError("party directory did not contain any JSON characters")
        for path in config.party:
            require_path(path, "party file")
        for route in config.routes:
            require_path(config.script_root / ROUTES[route].script_name, f"route script for {route}")
    except (FileNotFoundError, ValueError, KeyError) as error:
        print(f"verify-campaign configuration error: {error}", file=sys.stderr)
        return 2

    config.output_root.mkdir(parents=True, exist_ok=True)
    run_dir = Path(tempfile.mkdtemp(prefix="run-", dir=config.output_root))
    input_party = party_from_files(config.party)
    handoff = write_handoff(config, run_dir, input_party)
    write_json(
        run_dir / "source-receipt.json",
        {
            "cli": str(config.cli),
            "campaign": str(config.campaign),
            "modules": [str(path) for path in config.modules],
            "party": [
                {"path": str(path), "sha256": sha256(path)} for path in config.party
            ],
            "snapshot": str(config.snapshot) if config.snapshot else None,
            "script_root": str(config.script_root),
            "combat_control": config.combat_control,
        },
    )

    reports: list[dict[str, Any]] = []
    for route in config.routes:
        spec = ROUTES[route]
        route_dir = run_dir / route
        route_dir.mkdir()
        source_script = config.script_root / spec.script_name
        input_script = copy_input(source_script, route_dir)
        save_path = route_dir / "state.save.json"
        fresh_command = command_for(config, input_script, save_path, spec, None)
        fresh = run_process(config, route_dir, "fresh", fresh_command)
        fresh["save_path"] = save_path
        fresh["script"] = input_script
        fresh_save = load_save(save_path)

        reload_script = route_dir / "reload-status.script"
        reload_script.write_text("status\n", encoding="utf-8")
        reload_save_path = route_dir / "reload.save.json"
        if fresh_save is not None:
            reload_command = command_for(config, reload_script, reload_save_path, spec, save_path)
            reload_result = run_process(config, route_dir, "reload", reload_command)
            reload_result["save_path"] = reload_save_path
            reload_result["script"] = reload_script
            reload_save = load_save(reload_save_path)
        else:
            reload_result = {
                "status": None,
                "stdout_json": None,
                "stdout_error": "fresh save unavailable; reload skipped",
                "facts": [],
            }
            reload_save = None

        assertions = route_assertions(config, spec, fresh, fresh_save, reload_result, reload_save, input_party)
        report = {
            "route": route,
            "classification": spec.classification,
            "seed": config.seed_override or spec.seed,
            "route_dir": str(route_dir),
            "fresh_status": fresh.get("status"),
            "reload_status": reload_result.get("status"),
            "fresh_save": str(save_path),
            "reload_save": str(reload_save_path),
            "fresh_refusal_count": sum(1 for fact in fresh.get("facts", []) if fact.get("kind") == "refused"),
            "reload_refusal_count": sum(1 for fact in reload_result.get("facts", []) if fact.get("kind") == "refused"),
            "assertions": assertions,
            "passed": all(item["ok"] for item in assertions),
        }
        write_json(route_dir / "report.json", report)
        reports.append(report)

    report = {
        "passed": bool(reports) and all(item["passed"] for item in reports),
        "routes": list(config.routes),
        "cli": str(config.cli),
        "campaign": str(config.campaign),
        "modules": [str(path) for path in config.modules],
        "party": [str(path) for path in config.party],
        "snapshot": str(config.snapshot) if config.snapshot else None,
        "script_root": str(config.script_root),
        "run_dir": str(run_dir),
        "handoff": str(handoff),
        "source_receipt": str(run_dir / "source-receipt.json"),
        "route_reports": reports,
    }
    write_json(run_dir / "report.json", report)
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True))
    else:
        print(f"verifier run: {run_dir}")
        for item in reports:
            mark = "PASS" if item["passed"] else "FAIL"
            print(f"{mark} {item['route']} [{item['classification']}] fresh={item['fresh_status']!r} reload={item['reload_status']!r}")
        print(f"handoff: {handoff}")
        print(f"report: {run_dir / 'report.json'}")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
