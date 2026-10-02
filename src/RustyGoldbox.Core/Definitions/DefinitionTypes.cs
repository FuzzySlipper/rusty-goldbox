using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Definitions;

/// <summary>Every definition type modules can declare. <c>goldbox schema</c> prints these.</summary>
public static class DefinitionTypes
{
    private static readonly ExpressionKind SelfNumber = new(ExprType.Number, Roots.Self);
    private static readonly ExpressionKind CombatNumber = new(ExprType.Number, Roots.Self | Roots.Target);
    private static readonly ExpressionKind PlainNumber = new(ExprType.Number, Roots.None);
    private static readonly ExpressionKind ClassNumber = new(ExprType.Number, Roots.Self | Roots.Class);
    private static readonly ModifierKind Modifier = new();
    private static readonly ModifierKind ClassModifier = new(Roots.Self | Roots.Class);
    private static readonly UseKind Use = new();

    private static readonly Field Boosts = new("boosts", new ListKind(new ObjectKind(
        [
            new("from", new ListKind(new StatKind(true)), false, "The attributes this boost may raise; one is fixed, several are the player's choice, and without it any attribute."),
        ])), false, "Under character creation with method \"boosts\": each entry raises one attribute by the creation's boost, and one source never boosts the same attribute twice.");

    private static readonly Field GrantKind = new("kind", new TextKind(), false, "The kind of feature chosen, as features name it, for example \"feat\" or \"background\". Give kind or kinds.");
    private static readonly Field GrantKinds = new("kinds", new ListKind(new TextKind()), false, "Kinds any of which may be chosen, for example [\"feat\", \"combat feat\"] for a general slot that also takes combat feats.");
    private static readonly Field GrantCount = new("count", new IntegerKind(), false, "How many to choose; without it, 1.");

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

    public static DefinitionType Track { get; } = new(
        "track",
        "A pool that goes down and up in play: hit points, fatigue, magic points, a dying value. Expressions read it as self.<id> (current) and self.max_<id> (maximum).",
        [
            new("name", new TextKind(), true, "Display name, for example \"Hit points\"."),
            new("max", SelfNumber, false, "The maximum, for example \"self.ht\". Without it, each creature brings its own: characters from their class levels (with from_levels) and monsters from their tracks."),
            new("min", SelfNumber, false, "The lowest it can go; damage stops there. Without it, there is no floor."),
            new("restore_cap", SelfNumber, false, "The highest healing can take it; without it, the maximum."),
            new("start", SelfNumber, false, "The value a creature starts with; without it, the maximum."),
            new("from_levels", new BooleanKind(), false, "If true, characters' class level \"hp\" gains make up this track's maximum. At most one track may have it."),
        ],
        """
        { "type": "track", "id": "hit_points", "name": "Hit points", "from_levels": true }
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
            new("classes", new ListKind(new ReferenceKind("class")), false, "Classes characters of this race may take; without it, any class."),
            new("multiclasses", new ListKind(new ListKind(new ReferenceKind("class"))), false, "With experience \"split\": the combinations of classes a character of the race may start with together, for example [[\"fighter\", \"thief\"]]."),
            new("modifiers", new ListKind(Modifier), false, "Modifiers every member of the race has."),
            Boosts,
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
        "A character class: requirements, hit points and experience per level, spell slots, actions, and modifiers that grow with the class's level. A character may hold levels in several classes when the advancement definition allows it.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("requirements", new MapKind(new StatKind(true), new IntegerKind()), false, "Minimum attribute scores, checked when a character first takes the class."),
            new("prime_requisites", new ListKind(new StatKind(true)), false, "Attributes that matter most for the class."),
            new("levels", new ListKind(new ObjectKind(
            [
                new("xp", new IntegerKind(), false, "Experience needed for this level; the first level needs 0. Required when each class has its own experience (no advancement definition, or one with experience \"class\"); left out when the advancement definition sets experience by character level."),
                new("hp", ClassNumber, false, "Gained on reaching this level by the track with from_levels (usually hit points), for example \"1d10\" or \"3\". Rolled once and kept. Required when a track has from_levels; left out when none does."),
                new("hp_bonus", ClassNumber, false, "Added to this level's gain as the character is now, not as it was: recomputed whenever the stats it reads change, for example \"self.con_mod\" so a higher constitution raises every level's hit points. Without it, the whole gain is hp."),
                new("grants", new ListKind(new ObjectKind([GrantKind, GrantKinds, GrantCount])), false, "Features the character chooses on reaching this level of the class, for example a bonus feat."),
            ])), true, "One entry per level of the class, starting at level 1."),
            new("spell_slots", new ListKind(new ListKind(new IntegerKind())), false, "Per level (same length as levels): spells per day for spell level 1, 2, ...; [] for none."),
            new("actions", new ListKind(Use), false, "Actions characters of the class can take in combat, in order of preference."),
            new("modifiers", new ListKind(ClassModifier), false, "Modifiers a creature with levels in the class has. They may read class.level, its level in this class, so per-class progressions add up across classes: { \"stat\": \"base_attack\", \"value\": \"floor(class.level * 3 / 4)\" }."),
            Boosts,
        ],
        """
        {
          "type": "class",
          "id": "fighter",
          "name": "Fighter",
          "requirements": { "str": 9, "con": 7 },
          "prime_requisites": ["str"],
          "levels": [ { "xp": 0, "hp": "1d10" }, { "xp": 1900, "hp": "1d10" } ],
          "modifiers": [ { "stat": "base_attack", "value": "class.level" } ]
        }
        """);

    public static DefinitionType Advancement { get; } = new(
        "advancement",
        "How characters gain levels. Without one, each class has its own experience table (levels[].xp) and a character stays in one class. With experience \"character\", one table gives the experience for each total character level, and each new level is taken in a class of the player's choice, so a character can hold levels in several classes. A module set has at most one.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("experience", new EnumKind(["class", "character", "split"]), true, "\"class\": each class's levels[].xp, one class per character. \"character\": the levels below, by total level, with a class chosen for each level. \"split\": a character may start with several classes (as its race's multiclasses allow); experience is divided evenly between the classes it advances in, each on its own levels[].xp, and class_change may let it leave its class for a new one."),
            new("class_change", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Class), false, "With experience \"split\": whether a character may leave its classes for a new one (dual-classing), read with class.id as the new class, for example \"self.race == 'human' and self.classes == 1\". The classes left stop advancing and their modifiers and actions wait until the new class's level is higher; self.former_level is the highest of them. Without it, no class change."),
            new("levels", new ListKind(new IntegerKind()), false, "With experience \"character\": the experience needed for each character level, starting with 0 for level 1."),
            new("grants", new ListKind(new ObjectKind(
            [
                GrantKind,
                GrantKinds,
                GrantCount,
                new("when", new ExpressionKind(ExprType.Boolean, Roots.Self), true, "At which character levels, read as the character is with the new level, for example \"self.level == 1 or self.level % 3 == 0\"."),
            ])), false, "Features characters choose as their total level rises, whatever the class: feats, ability increases."),
            new("level_boosts", new ListKind(new ObjectKind(
            [
                new("count", new IntegerKind(), true, "How many boosts, each to a different attribute."),
                new("when", new ExpressionKind(ExprType.Boolean, Roots.Self), true, "At which character levels, for example \"self.level % 5 == 0\"."),
                new("amounts", new ReferenceKind("table"), true, "A table from an attribute's score to how much a boost raises it, for example [\"1-17\", 2], [\"18+\", 1]."),
            ])), false, "Boosts characters choose as their total level rises (PF2e-style), raising attribute scores for good."),
        ],
        """
        {
          "type": "advancement",
          "id": "standard",
          "name": "Character levels",
          "experience": "character",
          "levels": [0, 1000, 3000, 6000, 10000],
          "grants": [ { "kind": "feat", "when": "self.level == 1 or self.level % 3 == 0" } ]
        }
        """);

    public static DefinitionType Feature { get; } = new(
        "feature",
        "Something a character chooses: a background, heritage, feat, class feature or ability increase. Character creation, the advancement and class levels grant choices of a kind; a feature brings modifiers and actions.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("kind", new TextKind(), true, "What sort of feature it is, in the ruleset's own words; grants choose by kind, for example \"feat\"."),
            new("description", new TextKind(), false, "What the feature means in play."),
            new("requirements", new ExpressionKind(ExprType.Boolean, Roots.Self), false, "What the character must be to choose it, read with the level that grants it, for example \"self.might >= 13\" or \"self.race == 'dwarf'\"."),
            new("repeatable", new BooleanKind(), false, "If true, a character may choose it more than once, and its modifiers add each time (ability increases)."),
            new("modifiers", new ListKind(Modifier), false, "Modifiers a character with the feature has."),
            new("actions", new ListKind(Use), false, "Actions the feature lets a character take in combat, after its classes' actions."),
            Boosts,
        ],
        """
        {
          "type": "feature",
          "id": "iron_will",
          "name": "Iron will",
          "kind": "feat",
          "requirements": "self.level >= 1",
          "modifiers": [ { "check": "save_will", "value": "2" } ]
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
            new("values", new MapKind(new TextKind(), new NumberKind()), false, "Numbers the condition is applied with, and their defaults, read in its own fields as condition.<name>: { \"amount\": 5 } for \"ongoing 5\". apply_condition may give others."),
            new("modifiers", new ListKind(new ModifierKind(Roots.Self | Roots.Condition)), true, "Stat and check modifiers while the condition lasts."),
            new("prevents_actions", new BooleanKind(), false, "If true, a creature with the condition takes no actions."),
            new("each_turn", new ListKind(new OperationKind(Roots.Self | Roots.Condition)), false, "Operations at the start of each of the creature's turns, for example ongoing damage (\"to\" must be self)."),
            new("end_of_turn", new ListKind(new OperationKind(Roots.Self | Roots.Condition)), false, "Operations at the end of each of the creature's turns, for example a save that ends the condition."),
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
            new("tracks", new MapKind(new ReferenceKind("track"), SelfNumber), false, "Maximum for each track the ruleset doesn't compute itself, for example { \"hit_points\": \"2d8\" }; rolled when the monster appears."),
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
            new("surprise", CombatNumber, false, "Rolled at the start: whole rounds lost (0 for none). By side, self is the side's lead and target the other side's; by creature, self is the creature and target each enemy in turn, for example \"if 1d20 + target.stealth > self.perception then 1 else 0\"."),
            new("surprise_by", new EnumKind(["side", "creature"]), false, "Whether a whole side is surprised together (the default) or each creature on its own, so only some may be."),
            new("surprise_lead", SelfNumber, false, "By side: which member the side's surprise reads as self (and the other side as target), the highest, for example \"self.perception\" for the most alert or a scout."),
            new("initiative", SelfNumber, true, "Initiative roll each round. With initiative_by \"side\" it is rolled once per side and self is the side's first creature still fighting."),
            new("initiative_by", new EnumKind(["side", "creature"]), true, "Whether each side or each creature rolls initiative."),
            new("initiative_order", new EnumKind(["highest-first", "lowest-first"]), true, "Which result acts first; ties keep side and listing order."),
            new("initiative_each", new EnumKind(["round", "combat"]), true, "Whether initiative is rolled again every round or once for the whole combat."),
            new("round_seconds", new IntegerKind(), true, "Length of a round in seconds."),
            new("round_limit", new IntegerKind(), false, $"Rounds before a fight nobody can finish is called undecided (at least 1); without it, {RustyGoldbox.Core.Combat.CombatRunner.DefaultRoundLimit}."),
            new("budget", new ListKind(new ObjectKind(
            [
                new("id", new TextKind(), true, "Budget name that action costs use, for example \"action\", \"standard\" or \"actions\"."),
                new("per_turn", SelfNumber, true, "How many a creature has at the start of each turn, for example 3 or \"self.actions\" so conditions can change it; rounded down, never below 0."),
            ])), true, "The action budget each turn, for example one action, standard + move + swift, or three actions."),
            new("track", new ReferenceKind("track"), true, "The track damage and heal act on when they don't name one, and that targeting looks at (fewest left, most missing)."),
            new("defeated", new ExpressionKind(ExprType.Boolean, Roots.Self), true, "When a creature is out of the fight, for example \"self.hit_points <= 0\". Checked after every operation; a creature it no longer holds for (say, after healing) is back in the fight."),
            new("downed_conditions", new BooleanKind(), false, "If true, a creature out of the fight still runs its conditions' start- and end-of-turn operations and counts their durations down at the end of each round it had no turn, though it takes no actions (bleeding out, a save to stabilise). Without it, a defeated creature's conditions wait."),
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
            new("valid_target", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Target | Roots.Use), false, "Which candidates it may target, for example \"not target.condition.shaken\"; with none left, the action isn't taken."),
            new("prefer", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Target | Roots.Use), false, "For a single target, how much the creature wants each candidate; the highest is chosen, the first on a tie. Without it, the target kind's default."),
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

    public static DefinitionType Figure { get; } = new(
        "figure",
        "How a monster or a class looks: the sprite drawn for it in combat and in the 3D view. Rulesets carry no art, so "
        + "figures live in a campaign or extension that requires both the ruleset and an assets module. Give exactly one of "
        + "monster and class; a module set has at most one figure for each.",
        [
            new("monster", new ReferenceKind("monster"), false, "The monster it draws."),
            new("class", new ReferenceKind("class"), false, "The class it draws (for every character of the class)."),
            new("sprite", new ReferenceKind("asset", "sprite"), true, "The sprite asset."),
            new("icon", new ReferenceKind("asset", "icon"), false, "A small picture for lists, such as the combat roster."),
        ],
        """
        { "type": "figure", "id": "skeleton", "monster": "classic:skeleton", "sprite": "placeholder-art:skeleton", "icon": "placeholder-art:skull" }
        """);

    public static DefinitionType Variable { get; } = new(
        "variable",
        "A campaign variable: declared with a type and an initial value, read as campaign.var.<id>, changed by set events. Undeclared variables are errors.",
        [
            new("value_type", new EnumKind(["number", "boolean", "text"]), true, "Its type."),
            new("initial", new ExpressionKind(null, Roots.None), true, "Its value when the campaign starts, of its type, for example \"false\" or \"0\"."),
            new("description", new TextKind(), false, "What it records."),
        ],
        """
        { "type": "variable", "id": "gate_open", "value_type": "boolean", "initial": "false" }
        """);

    /// <summary>The asset kinds the presentation draws, each an RGBA PNG.</summary>
    public static IReadOnlyList<string> AssetKinds { get; } = ["wall_set", "backdrop", "portrait", "icon", "sprite"];

    /// <summary>The frames a wall set has; the first two are required.</summary>
    public static IReadOnlyList<string> WallSetFrames { get; } = ["wall", "door", "floor", "ceiling"];

    /// <summary>The fields only a sprite has.</summary>
    public static IReadOnlyList<string> SpriteFields { get; } = ["frame_size", "frame_count", "faces", "anchor", "height", "animations"];

    public static DefinitionType Asset { get; } = new(
        "asset",
        "A logical asset ID mapped to an image in the module. Other modules refer to it as module:id, never by path. "
        + "The file is an 8-bit RGBA PNG (the format the Engine renderer admits). Kinds: wall_set (one image holding "
        + "the frames the first-person view draws an area with), backdrop (a cell's background picture), portrait "
        + "(a character's picture), icon (a small picture) and sprite (a figure or prop standing in the 3D view or in "
        + "combat: a sheet of equal frames, drawn facing one way and flipped for the other, optionally animated).",
        [
            new("kind", new EnumKind(AssetKinds), true, "What the asset is for; references say which kind they need."),
            new("file", new TextKind(), true, "Path of the PNG inside this module, with forward slashes."),
            new("frames", new MapKind(new TextKind(), new ListKind(new IntegerKind(), 4)), false,
                "wall_set only: named pixel rectangles [x, y, width, height] inside the image. wall and door are required; floor and ceiling are optional."),
            new("frame_size", new ListKind(new IntegerKind(), 2), false,
                "sprite only (required): [width, height] of one frame in pixels. Frames are read left to right, then top to bottom, and the image must be a whole number of frames across and down."),
            new("frame_count", new IntegerKind(), false, "sprite only: how many frames the sheet holds, when the last row isn't full. Defaults to every cell."),
            new("faces", new EnumKind(["left", "right"]), false, "sprite only (required): which way the art faces; the renderer flips it to face the other way."),
            new("anchor", new ListKind(new IntegerKind(), 2), false, "sprite only: the pixel [x, y] in a frame that stands on the floor. Defaults to the bottom centre."),
            new("height", new NumberKind(), false, "sprite only (required): how tall a frame stands, in cells (a cell is 1 x 1 x 1); the width follows the frame's shape."),
            new("animations", new MapKind(new TextKind(), new ObjectKind(
            [
                new("frames", new ListKind(new IntegerKind()), true, "Frame numbers in play order, counting from 0."),
                new("fps", new NumberKind(), true, "Frames per second."),
                new("loop", new BooleanKind(), false, "Repeat (true, the default) or play once and hold the last frame."),
            ])), false, "sprite only: named animations such as idle, walk, attack, hit or die. Without one, the sprite shows frame 0."),
        ],
        """
        {
          "type": "asset",
          "id": "skeleton",
          "kind": "sprite",
          "file": "sprites/skeleton.png",
          "frame_size": [32, 48],
          "faces": "right",
          "height": 0.8,
          "animations": { "idle": { "frames": [0, 1, 2, 1], "fps": 4 }, "attack": { "frames": [3, 4, 5], "fps": 8, "loop": false } }
        }
        """);

    public static DefinitionType Area { get; } = new(
        "area",
        "A map: a grid of cells with walls, doors and openings on cell edges, plus cell features and named entry points.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("map", new ListKind(new TextKind()), true, "The grid as " + Campaigns.AreaMap.FormatDescription),
            new("wall_set", new ReferenceKind("asset", "wall_set"), false, "The wall_set asset the first-person view draws the area with."),
            new("cells", new ListKind(new ObjectKind(
            [
                new("at", new ListKind(new IntegerKind(), 2), true, "[x, y] of the cell; x west to east and y north to south, from 0."),
                new("zone", new TextKind(), false, "A zone tag for the cell."),
                new("backdrop", new ReferenceKind("asset", "backdrop"), false, "The backdrop asset shown in the cell."),
                new("prop", new ObjectKind(
                [
                    new("sprite", new ReferenceKind("asset", "sprite"), true, "The sprite standing in the cell (a chest, a pillar, bones, a guard)."),
                    new("hidden", new ExpressionKind(Expressions.ExprType.Boolean, Roots.Campaign), false,
                        "While this is true the prop isn't there, for example \"campaign.var.chest_opened\"; it may read campaign.var."),
                ]), false, "Something standing in the middle of the cell in the first-person view. It doesn't block movement."),
                new("event", new ReferenceKind("event"), false, "The event that runs when the party enters the cell."),
                new("facing", new EnumKind(["north", "east", "south", "west"]), false, "Run the event only when the party enters facing this way."),
                new("once", new BooleanKind(), false, "If true, the event runs only the first time."),
            ])), false, "Cells with features."),
            new("entries", new MapKind(new TextKind(), new ObjectKind(
            [
                new("at", new ListKind(new IntegerKind(), 2), true, "[x, y] of the cell."),
                new("facing", new EnumKind(["north", "east", "south", "west"]), true, "Which way the party faces on arrival."),
            ])), true, "Named places the party can arrive at (by starting or by teleport); arriving doesn't run the cell's event."),
        ],
        """
        {
          "type": "area",
          "id": "hall",
          "name": "Hall",
          "map": [
            "+--+--+",
            "|     |",
            "+  +DD+",
            "|  |  |",
            "+--+--+"
          ],
          "cells": [ { "at": [1, 1], "event": "gate", "once": true } ],
          "entries": { "start": { "at": [0, 0], "facing": "east" } }
        }
        """);

    public static DefinitionType Campaign { get; } = new(
        "campaign",
        "Where a campaign starts and who may play it. A campaign module has exactly one.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("start", new ObjectKind(
            [
                new("area", new ReferenceKind("area"), true, "The starting area."),
                new("entry", new TextKind(), true, "The entry point there."),
            ]), true, "Where the party begins."),
            new("party", new ObjectKind(
            [
                new("min", new IntegerKind(), true, "Fewest characters."),
                new("max", new IntegerKind(), true, "Most characters."),
            ]), true, "Party size."),
            new("intro", new ReferenceKind("event"), false, "An event that runs before the first command."),
        ],
        """
        { "type": "campaign", "id": "crypt", "name": "The Crypt", "start": { "area": "hall", "entry": "start" }, "party": { "min": 1, "max": 6 } }
        """);

    public static DefinitionType Event { get; } = new(
        "event",
        "A step in an event chain. Its kind (" + string.Join(", ", EventTypes.All.Select(kind => kind.Name)) + ") decides its other fields; see `goldbox schema events`.",
        [new("kind", new EnumKind(EventTypes.All.Select(kind => kind.Name).ToList()), true, "What the event does.")],
        EventTypes.Text.Example);

    public static DefinitionType CharacterCreation { get; } = new(
        "character-creation",
        "How new characters are made: how attribute scores are made (rolled, an arranged array, point buy or boosts), the features every character chooses, and starting gold. A ruleset may offer several; one marked default is used when none is named.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("attributes", new ListKind(new StatKind(true)), true, "Every attribute once, in the order rolls are taken and sheets show them."),
            new("method", new EnumKind(["roll", "array", "point-buy", "boosts"]), false, "How attribute scores are made: rolled (the default), a fixed array the player arranges, points spent from a budget, or boosts from a base. Given scores (--attributes) skip roll, array and boosts; point buy checks them."),
            new("default", new BooleanKind(), false, "With more than one character-creation definition, true marks the one used when none is named."),
            new("attribute_roll", PlainNumber, false, "Method roll: the roll for each attribute, for example \"3d6\" or \"roll_keep(4, 6, 3)\"."),
            new("assignment", new EnumKind(["in-order", "arrange"]), false, "Method roll: whether rolls are taken in attribute order (the default) or arranged by the player."),
            new("array", new ListKind(new IntegerKind()), false, "Method array: one score per attribute, which the player arranges by priority, for example [15, 14, 13, 12, 10, 8]."),
            new("base", new IntegerKind(), false, "Methods point-buy and boosts: the score every attribute starts at."),
            new("budget", new IntegerKind(), false, "Method point-buy: the points to spend."),
            new("costs", new ReferenceKind("table"), false, "Method point-buy: a table from a score to its total cost from the base, for example rows [8, 0], [9, 1], ..., [18, 16]."),
            new("boost", new IntegerKind(), false, "Method boosts: how much each boost raises an attribute."),
            new("boosts", Boosts.Kind, false, "Method boosts: boosts every new character has besides those of its race, creation features and class; usually free ones ({})."),
            new("starting_gold", new MapKind(new ReferenceKind("class"), SelfNumber), true, "Starting gold pieces for each class."),
            new("features", new ListKind(new ObjectKind([GrantKind, GrantKinds, GrantCount])), false, "Features every new character chooses, for example a background and a heritage."),
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
        Attribute, Track, Derived, Table, Race, Class, Advancement, Feature, Check, Condition, Item, Spell, Monster, Action, Encounter, Combat, CharacterCreation,
        Variable, Asset, Area, Event, Campaign, Figure,
    ];

    public static DefinitionType? Find(string name) => All.FirstOrDefault(type => type.Name == name);
}
