using RustyTales.Core.Expressions;

namespace RustyTales.Core.Definitions;

/// <summary>Every definition type modules can declare. <c>tales schema</c> prints these.</summary>
public static partial class DefinitionTypes
{
    static DefinitionTypes()
    {
        IReadOnlyList<DefinitionType> targetTypes =
        [
            Attribute, Track, Currency, Derived, Table, Race, Class, Resting, Advancement, Npc, CombatBehavior,
            Feature, Reaction, Check, Condition, Item, Economy, Spell, Monster, Action, Encounter, Combat,
            CharacterCreation, Variable, Asset, Area, Event, Campaign, Figure, Skin, Lifepath,
        ];
        Patch = new DefinitionType(
            "patch",
            "Replaces or removes top-level fields of an existing definition in a required module. Allowed in extension and campaign modules; patches apply in module load order and validation reports each one.",
            [
                new("target_type", new EnumKind(targetTypes.Select(type => type.Name).ToList()), true, "The definition type of the target, for example \"economy\", \"combat\" or \"monster\"."),
                new("target", new TextKind(), true, "The target definition in a required module, written as \"module:id\"."),
                new("set", new PatchSetKind(), true, "Top-level fields of target_type to replace (or set to null to remove an optional field). Cannot set \"type\", \"id\" or \"extends\"."),
            ],
            """
            {
              "type": "patch",
              "id": "generous_shops",
              "target_type": "economy",
              "target": "classic:standard",
              "set": { "sell_fraction": 0.75 }
            }
            """);
        All = [.. targetTypes, Patch];
    }

    public static DefinitionType Patch { get; }

    public static IReadOnlyList<DefinitionType> All { get; }

    public static DefinitionType? Find(string name) => All.FirstOrDefault(type => type.Name == name);

    /// <summary>
    /// Shared field kinds referenced across the partial files. Keeping them in a
    /// nested type guarantees they initialize on first access regardless of the
    /// order partial files initialize in.
    /// </summary>
    private static class Shared
    {
        public static readonly ExpressionKind SelfNumber = new(ExprType.Number, Roots.Self);
        public static readonly ExpressionKind CombatNumber = new(ExprType.Number, Roots.Self | Roots.Target | Roots.Combat);
        public static readonly ExpressionKind PlainNumber = new(ExprType.Number, Roots.None);
        public static readonly ExpressionKind FightNumber = new(ExprType.Number, Roots.Self | Roots.Combat);
        public static readonly ExpressionKind ClassNumber = new(ExprType.Number, Roots.Self | Roots.Class);
        public static readonly ModifierKind Modifier = new();
        public static readonly ModifierKind ClassModifier = new(Roots.Self | Roots.Class);
        public static readonly UseKind Use = new();

        public static readonly Field Boosts = new("boosts", new ListKind(new ObjectKind(
            [
                new("from", new ListKind(new StatKind(true)), false, "The attributes this boost may raise; one is fixed, several are the player's choice, and without it any attribute."),
            ])), false, "Under character creation with method \"boosts\": each entry raises one attribute by the creation's boost, and one source never boosts the same attribute twice.");

        public static readonly Field GrantKind = new("kind", new TextKind(), false, "The kind of feature chosen, as features name it, for example \"feat\" or \"background\". Give kind or kinds.");
        public static readonly Field GrantKinds = new("kinds", new ListKind(new TextKind()), false, "Kinds any of which may be chosen, for example [\"feat\", \"combat feat\"] for a general slot that also takes combat feats.");
        public static readonly Field GrantCount = new("count", new IntegerKind(), false, "How many to choose; without it, 1.");

        public static readonly ObjectKind SkillPoints = new(
        [
            new("profession", SelfNumber, true, "Points available for skills allowed by the chosen profession, evaluated after attributes and features are chosen."),
            new("personal", SelfNumber, true, "Points available for any listed skill, evaluated after attributes and features are chosen."),
            new("skills", new MapKind(new StatKind(false), SelfNumber), true, "Base chance for each skill that may receive points, evaluated after attributes and features are chosen."),
        ]);
    }
}
