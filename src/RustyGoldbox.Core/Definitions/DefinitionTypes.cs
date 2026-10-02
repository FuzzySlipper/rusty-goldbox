using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Definitions;

/// <summary>Every definition type modules can declare. <c>goldbox schema</c> prints these.</summary>
public static class DefinitionTypes
{
    private static readonly ExpressionKind SelfNumber = new(ExprType.Number, Roots.Self);
    private static readonly ExpressionKind CombatNumber = new(ExprType.Number, Roots.Self | Roots.Target);
    private static readonly ExpressionKind PlainNumber = new(ExprType.Number, Roots.None);
    private static readonly ModifierKind Modifier = new();
    private static readonly UseKind Use = new();

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
            new("actions", new ListKind(Use), false, "Actions characters of the class can take in combat, in order of preference."),
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
        "A roll compared with a target number, such as an attack, a saving throw or a skill roll, giving an outcome tier. Roll high or roll under; tiers express criticals, degrees of success and specials.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("roll", CombatNumber, true, "The dice, for example \"1d20\", \"1d100\", \"3d6\" or \"roll_count(self.pool, 10, 8)\". Tiers read it as check.roll."),
            new("bonus", CombatNumber, false, "Added to the roll, for example \"self.str_to_hit\". Modifiers for the check add too."),
            new("target", CombatNumber, true, "The number the total is compared with."),
            new("succeeds", new EnumKind(["at-least", "at-most"]), true, "Whether the total must be at least the target (roll high) or at most it (roll under)."),
            new("tiers", new ListKind(new ObjectKind(
            [
                new("name", new TextKind(), true, "Tier name that actions branch on, for example \"critical\"."),
                new("when", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Target | Roots.Check), true, "When this tier applies; may read check.roll, check.total, check.target and check.margin (how far the total beat the target)."),
            ])), false, "Outcome tiers in order; the first that applies wins, otherwise the outcome is \"success\" or \"failure\"."),
        ],
        """
        {
          "type": "check",
          "id": "save_spell",
          "name": "Saving throw against spells",
          "roll": "1d20",
          "target": "table(saving_throws, self.class, self.level, 'spell')",
          "succeeds": "at-least",
          "tiers": [ { "name": "critical", "when": "check.roll == 20" } ]
        }
        """);

    public static DefinitionType Condition { get; } = new(
        "condition",
        "A state a creature can be in, such as blessed, with the modifiers it brings.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("description", new TextKind(), false, "What the condition means in play."),
            new("modifiers", new ListKind(Modifier), true, "Stat and check modifiers while the condition lasts."),
            new("prevents_actions", new BooleanKind(), false, "If true, a creature with the condition takes no actions."),
            new("each_turn", new ListKind(new OperationKind(Roots.Self)), false, "Operations at the start of each of the creature's turns, for example ongoing damage (\"to\" must be self)."),
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
            new("kind", new TextKind(), true, "What sort of item it is, in the ruleset's own words, for example \"weapon\" or \"armour\". Uses with from_item match it."),
            new("cost", new NumberKind(), true, "Price, in the ruleset's money."),
            new("weight", new NumberKind(), true, "Weight, in the ruleset's unit."),
            new("parameters", new MapKind(new TextKind(), CombatNumber), false, "Values the item gives actions used with it (uses with from_item), for example { \"damage\": \"1d8\" }. May read target, for example to deal more against large creatures."),
            new("modifiers", new ListKind(Modifier), false, "Modifiers while equipped, for example armour lowering \"ac\"."),
        ],
        """
        { "type": "item", "id": "long_sword", "name": "Long sword", "kind": "weapon", "cost": 15, "weight": 7, "parameters": { "damage": "if target.size == 'large' then 1d12 else 1d8" } }
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
            new("effect", Use, false, "The action casting the spell uses, with its parameters."),
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
            new("class", new ReferenceKind("class"), false, "Class whose tables the monster uses, if the ruleset works that way; expressions see it as self.class."),
            new("level", new IntegerKind(), false, "Level the monster acts at, if the ruleset uses levels; expressions see it as self.level."),
            new("hit_points", SelfNumber, true, "Hit points, for example \"2d8\"."),
            new("stats", new MapKind(new StatKind(false), new ExpressionKind(null, Roots.Self)), false, "Stat values that replace the derived ones, each of the stat's type, for example { \"ac\": \"6\", \"size\": \"'large'\" }."),
            new("actions", new ListKind(Use), true, "Actions the monster takes in combat, in order of preference, for example { \"action\": \"melee_attack\", \"name\": \"bite\", \"damage\": \"1d3\" }."),
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
          "actions": [ { "action": "melee_attack", "name": "claw", "damage": "1d6" } ],
          "xp": 14
        }
        """);

    public static DefinitionType Combat { get; } = new(
        "combat",
        "Parameters of the fixed combat loop: surprise, initiative, round length, each turn's action budget, and when a creature is out of the fight.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("surprise", PlainNumber, false, "Rolled for each side at the start: whole rounds the side loses (0 for none)."),
            new("initiative", SelfNumber, true, "Initiative roll each round. With initiative_by \"side\" it is rolled once per side and self is the side's first creature still fighting."),
            new("initiative_by", new EnumKind(["side", "creature"]), true, "Whether each side or each creature rolls initiative."),
            new("initiative_order", new EnumKind(["highest-first", "lowest-first"]), true, "Which result acts first; ties keep side and listing order."),
            new("initiative_each", new EnumKind(["round", "combat"]), true, "Whether initiative is rolled again every round or once for the whole combat."),
            new("round_seconds", new IntegerKind(), true, "Length of a round in seconds."),
            new("budget", new ListKind(new ObjectKind(
            [
                new("id", new TextKind(), true, "Budget name that action costs use, for example \"action\", \"standard\" or \"actions\"."),
                new("per_turn", new IntegerKind(), true, "How many a creature has at the start of each turn."),
            ])), true, "The action budget each turn, for example one action, standard + move + swift, or three actions."),
            new("defeated", new ExpressionKind(ExprType.Boolean, Roots.Self), true, "When a creature is out of the fight, for example \"self.hit_points <= 0\". Checked after every operation; a creature it no longer holds for (say, after healing) is back in the fight."),
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
          "defeated": "self.hit_points <= 0"
        }
        """);

    public static DefinitionType Action { get; } = new(
        "action",
        "Something a creature does in combat: an attack, a spell's casting, a heal. It costs budget, picks a target, may make a check, and runs operations for the outcome. Uses supply its parameters.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("cost", new MapKind(new TextKind(), new IntegerKind()), true, "Budget spent, by budget ID from the combat definition, for example { \"action\": 1 }."),
            new("target", new EnumKind(["enemy", "ally", "hurt_ally", "self", "all_enemies", "all_allies"]), true, "Who it targets: one enemy (the one with fewest hit points), one ally (the first, which may be itself), the ally missing the most hit points, itself, or everyone on a side."),
            new("parameters", new ListKind(new TextKind()), false, "Names uses must supply (or get from an item), read as use.<name>, for example [\"damage\"]."),
            new("available", new ExpressionKind(ExprType.Boolean, Roots.Self), false, "Whether the creature may take it now; without it, always."),
            new("check", new ReferenceKind("check"), false, "The check that decides the outcome, made by the actor against the target."),
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
                new("count", PlainNumber, true, "How many, for example \"2\" or \"1d4 + 1\"."),
            ])), true, "The monsters and how many of each."),
        ],
        """
        { "type": "encounter", "id": "crypt_guard", "name": "Crypt guard", "monsters": [ { "monster": "skeleton", "count": "1d4 + 1" } ] }
        """);

    public static DefinitionType CharacterCreation { get; } = new(
        "character-creation",
        "How new characters are made: attribute rolls, whether they may be rearranged, and starting gold.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("attributes", new ListKind(new StatKind(true)), true, "Every attribute once, in the order rolls are taken and sheets show them."),
            new("attribute_roll", PlainNumber, true, "Roll for each attribute, for example \"3d6\" or \"roll_keep(4, 6, 3)\"."),
            new("assignment", new EnumKind(["in-order", "arrange"]), true, "Whether rolls are taken in attribute order or arranged by the player."),
            new("starting_gold", new MapKind(new ReferenceKind("class"), SelfNumber), true, "Starting gold pieces for each class."),
        ],
        """
        {
          "type": "character-creation",
          "id": "standard",
          "name": "Standard",
          "attributes": ["str", "dex", "con", "int", "wis", "cha"],
          "attribute_roll": "3d6",
          "assignment": "in-order",
          "starting_gold": { "fighter": "(3d6 + 2) * 10" }
        }
        """);

    public static IReadOnlyList<DefinitionType> All { get; } =
    [
        Attribute, Derived, Table, Race, Class, Check, Condition, Item, Spell, Monster, Action, Encounter, Combat, CharacterCreation,
    ];

    public static DefinitionType? Find(string name) => All.FirstOrDefault(type => type.Name == name);
}
