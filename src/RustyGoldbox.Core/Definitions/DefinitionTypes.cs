using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Definitions;

/// <summary>Every definition type modules can declare. <c>goldbox schema</c> prints these.</summary>
public static class DefinitionTypes
{
    private static readonly ExpressionKind SelfNumber = new(ExprType.Number, Roots.Self);
    private static readonly ExpressionKind CombatNumber = new(ExprType.Number, Roots.Self | Roots.Target);
    private static readonly ExpressionKind PlainNumber = new(ExprType.Number, Roots.None);
    private static readonly ModifierKind Modifier = new();

    public static DefinitionType Attribute { get; } = new(
        "attribute",
        "A rolled or assigned score every character has, such as strength. Expressions read it as self.<id>.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("min", new IntegerKind(), true, "Lowest possible score."),
            new("max", new IntegerKind(), true, "Highest possible score."),
            new("default", new IntegerKind(), false, "Score used for creatures that have none, such as monsters. Without it, reading the attribute of such a creature is an error."),
        ],
        """
        { "type": "attribute", "id": "str", "name": "Strength", "min": 3, "max": 18, "default": 10 }
        """);

    public static DefinitionType Derived { get; } = new(
        "derived",
        "A value computed from other stats, such as armour class or a to-hit bonus. Expressions read it as self.<id>; its type is inferred from the expression.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("value", new ExpressionKind(null, Roots.Self), true, "How the value is computed. May read self only; derived values must not depend on each other in a loop."),
        ],
        """
        { "type": "derived", "id": "thac0", "name": "To hit AC 0", "value": "table(thac0, self.class, self.level)" }
        """);

    public static DefinitionType Table { get; } = new(
        "table",
        "A lookup table, read in expressions with table(<id>, key, ...). Number keys may be ranges.",
        [
            new("name", new TextKind(), false, "Display name."),
            new("keys", new ListKind(new ObjectKind(
            [
                new("name", new TextKind(), true, "What the key is, for example \"level\"."),
                new("type", new EnumKind(["number", "text"]), true, "Key type."),
            ])), true, "The table's keys, in the order table() takes them."),
            new("value", new EnumKind(["number", "text", "boolean"]), true, "Type of the values."),
            new("rows", new TableRowsKind(), true, "Rows matching keys to values. Number keys in one column must not overlap for the same other keys."),
        ],
        """
        {
          "type": "table",
          "id": "str_to_hit",
          "keys": [ { "name": "strength", "type": "number" } ],
          "value": "number",
          "rows": [ [3, -3], ["4-5", -2], ["6-7", -1], ["8-16", 0], ["17+", 1] ]
        }
        """);

    public static DefinitionType Race { get; } = new(
        "race",
        "A playable race: ability adjustments and limits, the classes it may take, and ongoing modifiers.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("ability_adjustments", new MapKind(new StatKind(true), new IntegerKind()), false, "Added to rolled attributes at character creation."),
            new("ability_limits", new MapKind(new StatKind(true), new ListKind(new IntegerKind(), 2)), false, "[min, max] each attribute must fall within after adjustment."),
            new("classes", new ListKind(new ReferenceKind("class")), true, "Classes characters of this race may take."),
            new("modifiers", new ListKind(Modifier), false, "Modifiers every member of the race has."),
        ],
        """
        {
          "type": "race",
          "id": "dwarf",
          "name": "Dwarf",
          "ability_adjustments": { "con": 1, "cha": -1 },
          "ability_limits": { "str": [8, 18], "con": [12, 19] },
          "classes": ["fighter", "thief"],
          "modifiers": [ { "check": "save_spell", "value": "floor(self.con / 3.5)" } ]
        }
        """);

    public static DefinitionType Class { get; } = new(
        "class",
        "A character class: requirements, hit points and experience per level, and spell slots.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("requirements", new MapKind(new StatKind(true), new IntegerKind()), false, "Minimum attribute scores."),
            new("prime_requisites", new ListKind(new StatKind(true)), false, "Attributes that matter most for the class."),
            new("levels", new ListKind(new ObjectKind(
            [
                new("xp", new IntegerKind(), true, "Experience needed for this level; the first level needs 0."),
                new("hp", SelfNumber, true, "Hit points gained on reaching this level, for example \"1d10\" or \"3\"."),
            ])), true, "One entry per level, starting at level 1."),
            new("spell_slots", new ListKind(new ListKind(new IntegerKind())), false, "Per level (same length as levels): spells per day for spell level 1, 2, ...; [] for none."),
        ],
        """
        {
          "type": "class",
          "id": "fighter",
          "name": "Fighter",
          "requirements": { "str": 9, "con": 7 },
          "prime_requisites": ["str"],
          "levels": [ { "xp": 0, "hp": "1d10" }, { "xp": 1900, "hp": "1d10" } ]
        }
        """);

    public static DefinitionType Check { get; } = new(
        "check",
        "A roll against a target number, such as an attack or a saving throw. Modifiers for the check add to the roll.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("roll", CombatNumber, true, "The roll, for example \"1d20\" or \"1d20 + self.str_to_hit\"."),
            new("target", CombatNumber, true, "The number the roll is compared with."),
            new("succeeds", new EnumKind(["at-least", "at-most"]), true, "Whether the roll must be at least or at most the target."),
        ],
        """
        {
          "type": "check",
          "id": "save_spell",
          "name": "Saving throw against spells",
          "roll": "1d20",
          "target": "table(saving_throws, self.class, self.level, 'spell')",
          "succeeds": "at-least"
        }
        """);

    public static DefinitionType Condition { get; } = new(
        "condition",
        "A state a creature can be in, such as blessed, with the modifiers it brings.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("description", new TextKind(), false, "What the condition means in play."),
            new("modifiers", new ListKind(Modifier), true, "Stat and check modifiers while the condition lasts."),
        ],
        """
        {
          "type": "condition",
          "id": "blessed",
          "name": "Blessed",
          "modifiers": [ { "check": "attack", "value": "1" } ]
        }
        """);

    public static DefinitionType Item { get; } = new(
        "item",
        "An item type: weapons, armour and gear. Modifiers apply while the item is equipped.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("kind", new EnumKind(["weapon", "armour", "shield", "ammunition", "gear"]), true, "What sort of item it is."),
            new("cost", new NumberKind(), true, "Price in gold pieces."),
            new("weight", new NumberKind(), true, "Encumbrance in pounds."),
            new("damage", CombatNumber, false, "Damage on a hit. May read target, for example to deal more against large creatures."),
            new("modifiers", new ListKind(Modifier), false, "Modifiers while equipped, for example armour lowering \"ac\"."),
        ],
        """
        { "type": "item", "id": "long_sword", "name": "Long sword", "kind": "weapon", "cost": 15, "weight": 7, "damage": "if target.size == 'large' then 1d12 else 1d8" }
        """);

    public static DefinitionType Spell { get; } = new(
        "spell",
        "A spell: which class lists it is on and at what level, and how it is cast.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("lists", new MapKind(new ReferenceKind("class"), new IntegerKind()), true, "Spell level on each class's list."),
            new("range", new TextKind(), true, "Range, as text."),
            new("duration", new TextKind(), true, "Duration, as text."),
            new("area", new TextKind(), true, "Area of effect, as text."),
            new("casting_time", new TextKind(), true, "Casting time, as text."),
            new("save", new ReferenceKind("check"), false, "The saving throw targets may make, if any."),
            new("description", new TextKind(), true, "What the spell does."),
        ],
        """
        {
          "type": "spell",
          "id": "magic_missile",
          "name": "Magic missile",
          "lists": { "magic_user": 1 },
          "range": "60 ft + 10 ft per level",
          "duration": "Instantaneous",
          "area": "One or more creatures in a 10 ft square",
          "casting_time": "1 segment",
          "description": "Unerring missiles of force, 1d4+1 damage each; one more missile for every two levels past first."
        }
        """);

    public static DefinitionType Monster { get; } = new(
        "monster",
        "A monster. It attacks and saves as a class at a level, and its stats replace derived values.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("class", new ReferenceKind("class"), true, "Class whose tables the monster uses; expressions see it as self.class."),
            new("level", new IntegerKind(), true, "Level the monster attacks and saves at; expressions see it as self.level."),
            new("hit_points", SelfNumber, true, "Hit points, for example \"2d8\"."),
            new("stats", new MapKind(new StatKind(false), new ExpressionKind(null, Roots.Self)), false, "Stat values that replace the derived ones, each of the stat's type, for example { \"ac\": \"6\", \"size\": \"'large'\" }."),
            new("attacks", new ListKind(new ObjectKind(
            [
                new("name", new TextKind(), true, "Name of the attack, for example \"bite\"."),
                new("damage", CombatNumber, true, "Damage on a hit."),
            ])), true, "Attacks the monster makes each round."),
            new("xp", new IntegerKind(), true, "Experience for defeating it."),
        ],
        """
        {
          "type": "monster",
          "id": "skeleton",
          "name": "Skeleton",
          "class": "fighter",
          "level": 2,
          "hit_points": "1d8",
          "stats": { "ac": "7" },
          "attacks": [ { "name": "claw", "damage": "1d6" } ],
          "xp": 14
        }
        """);

    public static DefinitionType Combat { get; } = new(
        "combat",
        "The parameters of the fixed combat procedure: surprise, initiative, round length, the attack check and the per-round action budget.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("surprise", PlainNumber, true, "Rolled once per side at the start of combat: segments the side is surprised for (0 for none)."),
            new("initiative", PlainNumber, true, "Initiative roll; highest acts first."),
            new("initiative_by", new EnumKind(["side", "creature"]), true, "Whether each side or each creature rolls initiative."),
            new("round_seconds", new IntegerKind(), true, "Length of a round in seconds."),
            new("segments", new IntegerKind(), true, "Segments in a round."),
            new("attack", new ReferenceKind("check"), true, "The check for a melee attack."),
            new("missile_attack", new ReferenceKind("check"), false, "The check for a missile attack; attack is used when absent."),
            new("actions", new ListKind(new ObjectKind(
            [
                new("id", new TextKind(), true, "Action name, for example \"attack\" or \"move\"."),
                new("per_round", new IntegerKind(), true, "How many a creature may take each round."),
            ])), true, "The per-round action budget."),
        ],
        """
        {
          "type": "combat",
          "id": "standard",
          "name": "Standard combat",
          "surprise": "table(surprise_segments, 1d6)",
          "initiative": "1d6",
          "initiative_by": "side",
          "round_seconds": 60,
          "segments": 10,
          "attack": "attack",
          "actions": [ { "id": "action", "per_round": 1 } ]
        }
        """);

    public static DefinitionType CharacterCreation { get; } = new(
        "character-creation",
        "How new characters are made: attribute rolls, whether they may be rearranged, and starting gold.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("attribute_roll", PlainNumber, true, "Roll for each attribute, for example \"3d6\"."),
            new("assignment", new EnumKind(["in-order", "arrange"]), true, "Whether rolls are taken in attribute order or arranged by the player."),
            new("starting_gold", new MapKind(new ReferenceKind("class"), SelfNumber), true, "Starting gold pieces for each class."),
        ],
        """
        {
          "type": "character-creation",
          "id": "standard",
          "name": "Standard",
          "attribute_roll": "3d6",
          "assignment": "in-order",
          "starting_gold": { "fighter": "(3d6 + 2) * 10" }
        }
        """);

    public static IReadOnlyList<DefinitionType> All { get; } =
    [
        Attribute, Derived, Table, Race, Class, Check, Condition, Item, Spell, Monster, Combat, CharacterCreation,
    ];

    public static DefinitionType? Find(string name) => All.FirstOrDefault(type => type.Name == name);
}
