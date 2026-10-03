using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Definitions;

/// <summary>
/// The fixed vocabulary of operations: the only ways rules data changes
/// combat state. Actions and conditions choose and combine them; modules
/// can't define new ones.
/// </summary>
public static class OperationTypes
{
    /// <summary>Roots for operation expressions; the containing field adds or removes some.</summary>
    public const Roots ActionRoots = Roots.Self | Roots.Target | Roots.Use | Roots.Combat;

    private static readonly Field To = new("to", new EnumKind(["target", "self"]), false, "Who it happens to: the action's target (the default) or the creature acting.");

    private static readonly Field OnTrack = new("track", new ReferenceKind("track"), false, "The track it acts on; without it, the combat definition's track.");

    private static readonly Field ResourceTrack = new("track", new ReferenceKind("track"), true, "The resource track to spend or alter.");

    public static DefinitionType Damage { get; } = new(
        "damage",
        "Lowers a track (hit points unless it names another), never below the track's minimum.",
        [
            new("amount", new ExpressionKind(ExprType.Number, ActionRoots), true, "How much it goes down; below 0 counts as 0."),
            OnTrack,
            To,
        ],
        """{ "op": "damage", "amount": "use.damage + self.str_damage" }""");

    public static DefinitionType Heal { get; } = new(
        "heal",
        "Raises a track (hit points unless it names another), never above its restore cap (normally its maximum).",
        [
            new("amount", new ExpressionKind(ExprType.Number, ActionRoots), true, "How much it goes up; below 0 counts as 0."),
            OnTrack,
            To,
        ],
        """{ "op": "heal", "amount": "1d8" }""");

    public static DefinitionType ApplyCondition { get; } = new(
        "apply_condition",
        "Gives a creature a condition, optionally for a number of rounds. Applying one it already has restarts its duration.",
        [
            new("condition", new ReferenceKind("condition"), true, "The condition."),
            new("values", new MapKind(new TextKind(), new ExpressionKind(ExprType.Number, ActionRoots)), false, "Values for the condition's declared values, worked out now, for example { \"amount\": \"1d6\" }; the rest keep their defaults."),
            new("rounds", new ExpressionKind(ExprType.Number, ActionRoots), false, "Rounds it lasts, counted at the end of each of the holder's turns, so 1 covers its next turn; without it, it lasts until removed or the combat ends."),
            To,
        ],
        """{ "op": "apply_condition", "condition": "blessed", "rounds": "6" }""");

    public static DefinitionType RemoveCondition { get; } = new(
        "remove_condition",
        "Ends a condition.",
        [
            new("condition", new ReferenceKind("condition"), true, "The condition."),
            To,
        ],
        """{ "op": "remove_condition", "condition": "asleep" }""");

    public static DefinitionType Check { get; } = new(
        "check",
        "Makes another check, such as a target's active defence, and runs the operations for its outcome. Its operations may read check for this check.",
        [
            new("check", new ReferenceKind("check"), true, "The check to make."),
            new("by", new EnumKind(["target", "self"]), false, "Who rolls it: the action's target (the default) or the creature acting; the other one is the check's target."),
            new("bonus", new ExpressionKind(ExprType.Number, ActionRoots), false, "Added to the roll with the modifiers, for example \"2\" for a charge."),
            new("outcomes", new MapKind(new TextKind(), new ListKind(new OperationKind(ActionRoots | Roots.Check))), true, "Operations for each outcome tier: success, failure or one of the check's tiers. They read this check as check, and when this check is made in another check's outcomes, that one as outer."),
        ],
        """{ "op": "check", "check": "parry", "outcomes": { "failure": [ { "op": "damage", "amount": "use.damage" } ] } }""");

    public static DefinitionType If { get; } = new(
        "if",
        "Runs operations when a boolean holds, and others when it doesn't.",
        [
            new("when", new ExpressionKind(ExprType.Boolean, ActionRoots), true, "The test, for example \"target.condition.prone\"."),
            new("then", new ListKind(new OperationKind(ActionRoots)), true, "Operations when it holds."),
            new("else", new ListKind(new OperationKind(ActionRoots)), false, "Operations when it doesn't."),
        ],
        """{ "op": "if", "when": "target.condition.prone", "then": [ { "op": "damage", "amount": "use.damage * 2" } ], "else": [ { "op": "damage", "amount": "use.damage" } ] }""");

    public static DefinitionType Move { get; } = new(
        "move",
        "Moves the creature acting on the combat field, cell by cell, toward its target the cheapest way round obstacles (stopping once within reach and in sight) or away from it. Creatures still fighting and impassable terrain block cells. In a fight without a field it does nothing.",
        [
            new("distance", new ExpressionKind(ExprType.Number, ActionRoots), true, "Most movement to spend: 1 a cell, or the terrain's cost to enter. For example \"floor(self.speed / 5)\"."),
            new("toward", new EnumKind(["target", "away"]), false, "Toward the action's target (the default) or away from it."),
            new("within", new ExpressionKind(ExprType.Number, ActionRoots), false, "Moving toward: stop once the target is this close and in sight, for example \"use.range\" to close only to shooting range; without it, 1 (adjacent)."),
            new("escape", new BooleanKind(), false, "Moving away: a creature with nowhere further to go at the field's edge (or in a fight without a field, at once) flees the fight. It is out of it but not felled, so its side gets no experience for it. Without it, false."),
            new("provokes", new BooleanKind(), false, "Whether stepping out of an enemy's reach sets off its leaves_reach reactions (a free blow at a fleeing foe); false for a careful withdrawal such as a fighting retreat. Without it, true."),
            new("beyond", new ExpressionKind(ExprType.Number, ActionRoots), false, "Moving away: stop once at least this far, for example \"3\" to keep out of a charge; without it, use the whole distance."),
        ],
        """{ "op": "move", "distance": "floor(self.speed / 5)" }""");

    public static DefinitionType Flee { get; } = new(
        "flee",
        "Takes the acting creature out of the fight without felling it. When its side has no one left, the campaign gets the combat event's on_flee chain.",
        [],
        """{ "op": "flee" }""");

    public static DefinitionType GrantBudget { get; } = new(
        "grant_budget",
        "Adds to a creature's named budget for its current turn after spending a resource track. This is the data hook for effects such as Action Surge or Quickened.",
        [
            new("budget", new TextKind(), true, "The combat budget ID to add to."),
            new("amount", new ExpressionKind(ExprType.Number, ActionRoots), true, "How much budget to add, rounded down."),
            ResourceTrack,
            new("spend", new ExpressionKind(ExprType.Number, ActionRoots), true, "How much of the resource track to spend."),
            To,
        ],
        """{ "op": "grant_budget", "budget": "action", "amount": "1", "track": "action_surge", "spend": "1", "to": "self" }""");

    public static DefinitionType ReduceDamage { get; } = new(
        "reduce_damage",
        "Reduces the pending damage currently being applied to a creature by an amount, or keeps a fraction of it. A shield track may also take the remaining damage. It is valid only from a reaction triggered by hit.",
        [
            new("amount", new ExpressionKind(ExprType.Number, ActionRoots), false, "A flat amount to subtract from pending damage."),
            new("fraction", new ExpressionKind(ExprType.Number, ActionRoots), false, "The fraction of pending damage that remains, from 0 to 1; for example 0.5 halves it."),
            new("shield_track", new ReferenceKind("track"), false, "A track holding shield hit points; after the reduction, the remaining pending damage is removed from this track too."),
            To,
        ],
        """{ "op": "reduce_damage", "amount": "self.shield_hardness", "shield_track": "shield_points", "to": "self" }""");

    public static IReadOnlyList<DefinitionType> All { get; } = [Damage, Heal, ApplyCondition, RemoveCondition, Check, If, Move, Flee, GrantBudget, ReduceDamage];

    public static DefinitionType? Find(string name) => All.FirstOrDefault(operation => operation.Name == name);
}
