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
    public const Roots ActionRoots = Roots.Self | Roots.Target | Roots.Use;

    private static readonly Field To = new("to", new EnumKind(["target", "self"]), false, "Who it happens to: the action's target (the default) or the creature acting.");

    private static readonly Field OnTrack = new("track", new ReferenceKind("track"), false, "The track it acts on; without it, the combat definition's track.");

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
            new("outcomes", new MapKind(new TextKind(), new ListKind(new OperationKind(ActionRoots | Roots.Check))), true, "Operations for each outcome tier: success, failure or one of the check's tiers. They read this check as check, and when this check is made in another check's outcomes, that one as outer."),
        ],
        """{ "op": "check", "check": "parry", "outcomes": { "failure": [ { "op": "damage", "amount": "use.damage" } ] } }""");

    public static IReadOnlyList<DefinitionType> All { get; } = [Damage, Heal, ApplyCondition, RemoveCondition, Check];

    public static DefinitionType? Find(string name) => All.FirstOrDefault(operation => operation.Name == name);
}
