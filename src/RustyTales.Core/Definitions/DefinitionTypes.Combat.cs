using RustyTales.Core.Expressions;

namespace RustyTales.Core.Definitions;

public static partial class DefinitionTypes
{
    private const Roots BehaviorRoots = Roots.Self | Roots.Target | Roots.Combat | Roots.Behavior;
    private static readonly ExpressionKind BehaviorNumber = new(ExprType.Number, BehaviorRoots);
    private static readonly ExpressionKind BehaviorBoolean = new(ExprType.Boolean, BehaviorRoots);

    private static readonly ObjectKind BehaviorDestination = new(
    [
        new("kind", new EnumKind(["toward", "away", "within", "outside"]), true,
            "How the actor should move relative to the selected target. "
            + "toward and within stop at or inside the distance; away and outside stop at or beyond it."),
        new("distance", BehaviorNumber, true,
            "The preferred distance in cells, evaluated against the actor and selected target."),
    ]);

    private static readonly ObjectKind BehaviorStep = new(
    [
        new("action", Shared.Use, true,
            "The existing action use to propose after movement, with the same parameters and legality as a human choice."),
        new("target", new EnumKind(["self", "enemy", "ally", "hurt_ally", "fallen_ally"]), false,
            "The target group to choose for this step; without it the action's own target kind and preference decide."),
        new("target_score", BehaviorNumber, false,
            "A score for one candidate in the target group; the highest wins, with listing order breaking ties."),
        new("destination", BehaviorDestination, false,
            "A preferred movement destination before this action. Movement is proposed through the shared legal resolver."),
        new("spell", new ReferenceKind("spell"), false,
            "An optional spell whose effect is the action use; its normal spell preparation and resource rules still apply."),
    ]);

    private static readonly ObjectKind BehaviorRule = new(
    [
        new("when", BehaviorBoolean, false,
            "A guard evaluated before this rule is considered; without it the rule is always eligible."),
        new("priority", BehaviorNumber, false,
            "A deterministic priority. The highest eligible priority wins; omit it when using score."),
        new("score", BehaviorNumber, false,
            "A deterministic score for eligible alternatives. The highest score wins; omit it when using priority."),
        new("commit", new EnumKind(["step", "plan"]), false,
            "Whether to reassess after each step or keep this rule's remaining steps as a short commitment while they remain legal."),
        new("steps", new ListKind(BehaviorStep), true,
            "A fixed, short sequence of existing action uses. The sequence never loops; an invalid step uses the rule fallback."),
        new("fallback", new EnumKind(["next", "end-turn", "flee"]), false,
            "What to do when a step can no longer be proposed; without it the profile fallback applies."),
    ]);

    public static DefinitionType CombatBehavior { get; } = new(
        "combat-behavior",
        "A reusable, module-authored combat policy. It selects among legal action plans with guards and priorities or scores, can move toward a target before each action, and contains fixed short sequences without scripts or loops.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("parameters", new MapKind(new TextKind(), new ExpressionKind(ExprType.Number, BehaviorRoots)), false, "Named numeric defaults. Rules and action-use parameters read them as behavior.<name>; parameter expressions may read another named behavior parameter (cycles are rejected), and values are evaluated in the current actor and target context."),
            new("fallback", new EnumKind(["next", "end-turn", "flee"]), false, "What autonomous control does when no rule or step can be used; without it, end-turn."),
            new("rules", new ListKind(BehaviorRule), true, "Ordered alternatives. An eligible rule with the highest priority or score is chosen; a tie keeps definition order."),
        ],
        """
        {
          "type": "combat-behavior",
          "id": "skirmisher",
          "name": "Skirmisher",
          "parameters": { "safe_distance": 3 },
          "fallback": "end-turn",
          "rules": [
            {
              "when": "self.hit_points > 0",
              "priority": 10,
              "commit": "plan",
              "steps": [
                { "destination": { "kind": "outside", "distance": "behavior.safe_distance" }, "action": { "action": "shoot", "damage": "1d6" }, "target": "enemy" }
              ],
              "fallback": "next"
            }
          ]
        }
        """);

    public static DefinitionType Reaction { get; } = new(
        "reaction",
        "Something a creature does out of turn when a trigger happens: an attack of opportunity, a shield raised before a blow lands, a riposte after a wound. It spends a combat budget, usually one refilled each turn (\"reaction\": 1), and resolves its use against the creature that triggered it. Classes, monsters and features list the reactions they give. Reactions don't trigger further reactions, except counter-reactions, one level deep.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("trigger", new EnumKind(["leaves_reach", "targeted", "hit", "damaged", "ally_defeated"]), true, "\"leaves_reach\": an enemy steps from within its reach to outside it (before the step). \"targeted\": an enemy's action is about to resolve against it, before any check (an interrupt). \"hit\": an attack or other damage is known to have landed, before its pending damage is applied. \"damaged\": an enemy's operation lowered its track. \"ally_defeated\": an enemy's operation put one of its allies out of the fight; the enemy is the one it reacts against (avenge), or it acts on itself (rage, rally)."),
            new("cost", new MapKind(new TextKind(), new IntegerKind()), true, "Budget spent, by budget ID from the combat definition, for example { \"reaction\": 1 }."),
            new("reach", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Combat), false, "With leaves_reach, how many cells it watches; without it, 1."),
            new("when", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Target | Roots.Combat), false, "Whether it reacts, with target as the creature that triggered it."),
            new("physical", new BooleanKind(), false, "With trigger \"hit\", if true this reaction answers only a pending hit marked physical by its damage condition."),
            new("attack", new BooleanKind(), false, "With trigger \"hit\", if true this reaction answers only a pending hit marked as coming from a confirmed attack roll."),
            new("counter", new BooleanKind(), false, "If true, it may also be taken against an enemy's reaction (a shield raised against an attack of opportunity, a counterspell), though nothing reacts to it in turn. Without it, it only answers actions on a turn."),
            new("use", Shared.Use, true, "The action it takes against the creature that triggered it (or itself, for an action targeting self), with its parameters or from_item."),
        ],
        """
        { "type": "reaction", "id": "attack_of_opportunity", "name": "Attack of opportunity", "trigger": "leaves_reach", "cost": { "reaction": 1 }, "use": { "action": "melee_attack", "from_item": "weapon" } }
        """);

    public static DefinitionType Monster { get; } = new(
        "monster",
        "A monster. It attacks and saves as a class at a level, and its stats replace derived values.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("class", new ReferenceKind("class"), false, "Class whose tables the monster uses, if the ruleset works that way; expressions see it as self.class."),
            new("level", new IntegerKind(), false, "Level the monster acts at, if the ruleset uses levels; expressions see it as self.level."),
            new("tracks", new MapKind(new ReferenceKind("track"), Shared.SelfNumber), false, "Maximum for each track the ruleset doesn't compute itself, for example { \"hit_points\": \"2d8\" }; rolled when the monster appears."),
            new("stats", new MapKind(new StatKind(false), new ExpressionKind(null, Roots.Self)), false, "Stat values that replace the derived ones, each of the stat's type, for example { \"ac\": \"6\", \"size\": \"'large'\" }."),
            new("actions", new ListKind(Shared.Use), true, "Actions the monster takes in combat, in order of preference, for example { \"action\": \"melee_attack\", \"name\": \"bite\", \"damage\": \"1d3\" }."),
            new("behavior", new ReferenceKind("combat-behavior"), false, "The authored combat behavior used when this monster is under autonomous control; an explicit controller override wins."),
            new("spells", new ListKind(new ObjectKind(
            [
                new("spell", new ReferenceKind("spell"), true, "The spell; it needs an effect."),
                new("per_day", new IntegerKind(), false, "Casts a day (an innate power), instead of paying the spell's cost from the monster's tracks."),
            ])), false, "Spells it casts in combat before its actions, while it can: from its own tracks (give them in \"tracks\", for example { \"spells_1\": \"2\" }), or a number of times a day, for example [ { \"spell\": \"bless\" }, { \"spell\": \"sleep\", \"per_day\": 1 } ]."),
            new("reactions", new ListKind(new ReferenceKind("reaction")), false, "Reactions it gives in combat."),
            new("xp", new IntegerKind(), true, "Experience for defeating it."),
        ],
        """
        {
          "type": "monster",
          "id": "skeleton",
          "name": "Skeleton",
          "class": "fighter",
          "level": 2,
          "tracks": { "hit_points": "1d8" },
          "stats": { "ac": "7" },
          "actions": [ { "action": "melee_attack", "name": "claw", "damage": "1d6" } ],
          "xp": 14
        }
        """);

    public static DefinitionType Combat { get; } = new(
        "combat",
        "Parameters of the fixed combat loop: surprise, initiative, round length, each turn's action budget, and when a creature is out of the fight.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("surprise", Shared.CombatNumber, false, "Rolled at the start: whole rounds lost (0 for none). By side, self is the side's lead and target the other side's; by creature, self is the creature and target each enemy in turn, for example \"if 1d20 + target.stealth > self.perception then 1 else 0\"."),
            new("surprise_by", new EnumKind(["side", "creature"]), false, "Whether a whole side is surprised together (the default) or each creature on its own, so only some may be."),
            new("surprise_lead", Shared.SelfNumber, false, "By side: which member the side's surprise reads as self (and the other side as target), the highest, for example \"self.perception\" for the most alert or a scout."),
            new("initiative", Shared.FightNumber, false, "Initiative roll each round. With initiative_by \"side\" it is rolled once per side and self is the side's first creature still fighting. Required for rolled mode; ignored when initiative_mode is \"elective\"."),
            new("initiative_by", new EnumKind(["side", "creature"]), false, "Whether each side or each creature rolls initiative; required for rolled mode."),
            new("initiative_order", new EnumKind(["highest-first", "lowest-first"]), false, "Which result acts first; ties keep side and listing order. Required for rolled mode."),
            new("initiative_each", new EnumKind(["round", "combat"]), false, "Whether initiative is rolled again every round or once for the whole combat. Required for rolled mode."),
            new("initiative_mode", new EnumKind(["rolled", "elective"]), false, "The default rolled order, or elective (popcorn) order where the last actor chooses the next unacted creature."),
            new("initiative_score", Shared.CombatNumber, false, "Elective mode's AI policy: after an actor, score each unacted creature as target and choose the highest. Ties keep listing order; without it, choose the first."),
            new("round_seconds", new IntegerKind(), true, "Length of a round in seconds."),
            new("round_limit", new IntegerKind(), false, $"Rounds before a fight nobody can finish is called undecided (at least 1); without it, {RustyTales.Core.Combat.CombatRunner.DefaultRoundLimit}."),
            new("budget", new ListKind(new ObjectKind(
            [
                new("id", new TextKind(), true, "Budget name that action costs use, for example \"action\", \"standard\" or \"actions\"."),
                new("per_turn", Shared.FightNumber, true, "How many a creature has at the start of each turn, for example 3, \"self.actions\" so conditions can change it, or \"if combat.surprise_round then 1 else 2\"; rounded down, never below 0."),
            ])), true, "The action budget each turn, for example one action, standard + move + swift, or three actions."),
            new("track", new ReferenceKind("track"), true, "The track damage and heal act on when they don't name one, and that targeting looks at (fewest left, most missing)."),
            new("behavior", new ReferenceKind("combat-behavior"), false, "The default authored behavior for autonomous combatants that have no creature or controller override."),
            new("defeated", new ExpressionKind(ExprType.Boolean, Roots.Self), true, "When a creature is out of the fight, for example \"self.hit_points <= 0\". Checked after every operation; a creature it no longer holds for (say, after healing) is back in the fight."),
            new("flee", new ListKind(new ObjectKind(
            [
                new("side", new EnumKind(["party", "monsters"]), true, "The side whose creature checks this rule; side 0 is the party and side 1 is the encounter."),
                new("when", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Combat), true, "When true at the start of that creature's turn, it flees without taking an action."),
            ])), false, "Rules that make a creature flee at the start of its turn. A side leaves only when all its creatures have fled; its combat event then follows on_flee."),
            new("field", new ObjectKind(
            [
                new("width", new IntegerKind(), true, "Cells across; the first side starts on the left edge and the second on the right."),
                new("height", new IntegerKind(), true, "Cells down."),
                new("mode", new EnumKind(["grid", "zones"]), false, "A grid (the default), where one creature occupies a cell, or zones, where a cell is a shared zone and several creatures may stand together."),
                new("metric", new EnumKind(["chebyshev", "manhattan"]), false, "How distance counts: a diagonal step is 1 (chebyshev, the default) or there are only straight steps (manhattan)."),
                new("terrain", new MapKind(new TextKind(), new ObjectKind(
                [
                    new("name", new TextKind(), true, "Display name, for example \"Pillar\"."),
                    new("passable", new BooleanKind(), false, "Whether creatures can enter it; without it, true."),
                    new("cost", new IntegerKind(), false, "Movement it takes to enter, for example 2 for rubble; without it, 1."),
                    new("blocks_sight", new BooleanKind(), false, "Whether it blocks line of sight: an action with a range can't target a creature behind it. Without it, false."),
                ])), false, "Kinds of ground an encounter's terrain rows may use, each by a one-character key other than \".\" (open ground), for example { \"#\": { \"name\": \"Pillar\", \"passable\": false, \"blocks_sight\": true }, \"~\": { \"name\": \"Mud\", \"cost\": 2 } }."),
            ]), false, "A grid the fight is on: creatures have positions, actions have a range in cells within which they need line of sight to their target, and the move operation moves them, round obstacles. In zones mode, cells are shared zones and several creatures may stand together; without a field, fights have no positions and everyone is in reach (combat.distance is 1)."),
            new("rolled", new EnumKind(["turn", "round"]), false, "What self.rolled.<check> counts: checks made since the creature's own turn began (\"turn\", the default; a multiple attack penalty) or since the round began (\"round\"; a penalty for each defence after the first in a round)."),
            new("actions", new ListKind(Shared.Use), false, "Actions every creature in these fights can take, after its own, such as attacking with any skill where a system lets anyone try anything; uses as in classes and monsters."),
            new("downed_conditions", new BooleanKind(), false, "If true, a creature out of the fight still runs its conditions' start- and end-of-turn operations and counts their durations down each round, though it takes no actions (bleeding out, a save to stabilise). It does so at its place in the turn order; with initiative each round a defeated creature isn't rolled for, so it does so at the round's end, as does one out of the fight from the start. Without it, a defeated creature's conditions wait."),
            new("presentation", new EnumKind(["tactical", "first_person", "side_view"]), false, "How the Game frames fights using this combat definition: \"tactical\" (angled 3D floor grid, the default), \"first_person\" (Wizardry / Dragon Quest style: foes arrayed facing the camera over a backdrop), or \"side_view\" (JRPG style: party on the right and foes on the left in fixed formation rows)."),
        ],
        """
        {
          "type": "combat",
          "id": "standard",
          "name": "Standard combat",
          "surprise": "if 1d6 <= 2 then 1 else 0",
          "initiative": "1d6",
          "initiative_by": "side",
          "initiative_order": "highest-first",
          "initiative_each": "round",
          "round_seconds": 60,
          "budget": [ { "id": "action", "per_turn": 1 } ],
          "track": "hit_points",
          "defeated": "self.hit_points <= 0"
        }
        """);

    public static DefinitionType Action { get; } = new(
        "action",
        "Something a creature does in combat: an attack, a spell's casting, a heal. It costs budget, picks a target, may make a check, and runs operations for the outcome. Uses supply its parameters.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("cost", new MapKind(new TextKind(), new IntegerKind()), true, "Budget spent, by budget ID from the combat definition, for example { \"action\": 1 }."),
            new("target", new EnumKind(["enemy", "ally", "hurt_ally", "fallen_ally", "self", "all_enemies", "all_allies"]), true, "Who it targets: one enemy (by default the one with the least left on the combat's track), one ally (the first, which may be itself), the ally missing the most of it, an ally out of the fight (to bring back), itself, or everyone on a side."),
            new("portions", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Use | Roots.Combat), false, "How many times the effect resolves, worked out when the action is committed. This value must be deterministic while legal choices are inspected; random effects belong in max_targets, checks or operations after commitment. Automatic resolution chooses each portion with prefer or the target kind's default, so missiles can move on to the next foe when one falls: \"use.missiles\" or \"1 + floor((self.level - 1) / 2)\". A manual command may provide target IDs in portion order; repeated IDs are allowed only within this count and omitted portions keep the authored automatic choice."),
            new("max_targets", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Use | Roots.Combat), false, "The maximum number of legal targets, worked out once when the action is committed (for example \"2d4\"). A manual controller then chooses legal targets up to that committed cap; an automatic controller ranks them by prefer when present and takes the highest-ranked targets. With no max_targets, all_enemies and all_allies affect every legal target, while ordinary target kinds accept one."),
            new("range", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Use | Roots.Combat), false, "On a combat field, the most cells away a target may be, for example \"1\" for melee or \"use.range\"; this value must be deterministic while legal choices are inspected. A target in range must also be in sight. Without it, any distance, seen or not (for moving toward an enemy)."),
            new("valid_target", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Target | Roots.Use | Roots.Combat), false, "Which candidates it may target, for example \"not target.condition.shaken\"; this value must be deterministic while legal choices are inspected. With none left, the action isn't taken."),
            new("prefer", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Target | Roots.Use | Roots.Combat), false, "For a single target, how much the creature wants each candidate; the highest is chosen, the first on a tie. Without it, the target kind's default."),
            new("score", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Target | Roots.Use | Roots.Combat), false, "How much the creature wants to take it now, against the target it would pick (the first, for a whole side). When any of a creature's uses has a score, it takes the highest-scoring use it can (a use without one scores 0; the first in its list on a tie) instead of the first, for example \"if target.hit_points * 2 < target.max_hit_points then 10 else -1\" to heal only the badly hurt."),
            new("parameters", new ListKind(new TextKind()), false, "Names uses must supply (or get from an item), read as use.<name>, for example [\"damage\"]."),
            new("available", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Combat), false, "Whether the creature may take it now; this value must be deterministic while legal choices are inspected. Without it, always."),
            new("check", new ReferenceKind("check"), false, "The check that decides the outcome, made by the actor against the target."),
            new("check_bonus", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Target | Roots.Use | Roots.Combat), false, "Added to the check's roll with the modifiers, worked out against the target: a range penalty such as \"0 - 2 * floor((combat.distance - 1) / use.increment)\", or \"4\" for a blow at a fleeing foe."),
            new("outcomes", new MapKind(new TextKind(), new ListKind(new OperationKind(OperationTypes.ActionRoots | Roots.Check))), false, "Operations for each outcome tier of the check: success, failure or one of its tiers. Tiers without an entry do nothing."),
            new("always", new ListKind(new OperationKind(OperationTypes.ActionRoots)), false, "Operations that run whatever the outcome, or the whole effect of an action without a check."),
        ],
        """
        {
          "type": "action",
          "id": "melee_attack",
          "name": "Melee attack",
          "cost": { "action": 1 },
          "target": "enemy",
          "parameters": ["damage"],
          "check": "attack",
          "outcomes": { "success": [ { "op": "damage", "amount": "use.damage + self.str_damage" } ] }
        }
        """);

    public static DefinitionType Encounter { get; } = new(
        "encounter",
        "A group of monsters to fight.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("monsters", new ListKind(new ObjectKind(
            [
                new("monster", new ReferenceKind("monster"), true, "The monster."),
                new("count", Shared.PlainNumber, true, "How many, for example \"2\" or \"1d4 + 1\"."),
            ])), true, "The monsters and how many of each."),
            new("terrain", new ListKind(new TextKind()), false, "On a combat field, the ground it is fought on: one row of text per row of cells, \".\" for open ground and the field's terrain keys elsewhere, for example [\"..#..\", \".....\", \"..~..\"]. Its size must match every field it can be fought on. Without it, open ground."),
        ],
        """
        { "type": "encounter", "id": "crypt_guard", "name": "Crypt guard", "monsters": [ { "monster": "skeleton", "count": "1d4 + 1" } ] }
        """);
}
