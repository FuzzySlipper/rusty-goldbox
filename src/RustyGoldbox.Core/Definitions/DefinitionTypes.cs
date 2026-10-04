using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Definitions;

/// <summary>Every definition type modules can declare. <c>goldbox schema</c> prints these.</summary>
public static class DefinitionTypes
{
    private static readonly ExpressionKind SelfNumber = new(ExprType.Number, Roots.Self);
    private static readonly ExpressionKind CombatNumber = new(ExprType.Number, Roots.Self | Roots.Target | Roots.Combat);
    private static readonly ExpressionKind PlainNumber = new(ExprType.Number, Roots.None);
    private static readonly ExpressionKind FightNumber = new(ExprType.Number, Roots.Self | Roots.Combat);
    private static readonly ExpressionKind ClassNumber = new(ExprType.Number, Roots.Self | Roots.Class);
    private static readonly ModifierKind Modifier = new();
    private static readonly ModifierKind ClassModifier = new(Roots.Self | Roots.Class);
    private static readonly UseKind Use = new();
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
        new("action", Use, true,
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

    private static readonly Field Boosts = new("boosts", new ListKind(new ObjectKind(
        [
            new("from", new ListKind(new StatKind(true)), false, "The attributes this boost may raise; one is fixed, several are the player's choice, and without it any attribute."),
        ])), false, "Under character creation with method \"boosts\": each entry raises one attribute by the creation's boost, and one source never boosts the same attribute twice.");

    private static readonly Field GrantKind = new("kind", new TextKind(), false, "The kind of feature chosen, as features name it, for example \"feat\" or \"background\". Give kind or kinds.");
    private static readonly Field GrantKinds = new("kinds", new ListKind(new TextKind()), false, "Kinds any of which may be chosen, for example [\"feat\", \"combat feat\"] for a general slot that also takes combat feats.");
    private static readonly Field GrantCount = new("count", new IntegerKind(), false, "How many to choose; without it, 1.");
    private static readonly ObjectKind SkillPoints = new(
    [
        new("profession", SelfNumber, true, "Points available for skills allowed by the chosen profession, evaluated after attributes and features are chosen."),
        new("personal", SelfNumber, true, "Points available for any listed skill, evaluated after attributes and features are chosen."),
        new("skills", new MapKind(new StatKind(false), SelfNumber), true, "Base chance for each skill that may receive points, evaluated after attributes and features are chosen."),
    ]);

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
            new("start_on_combat", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Condition), false, "When true, a zero current value is started at this track's maximum when a combat begins; useful for equipment pools whose maximum was zero before the equipment was added."),
            new("from_levels", new BooleanKind(), false, "If true, characters' class level \"hp\" gains make up this track's maximum. At most one track may have it."),
        ],
        """
        { "type": "track", "id": "hit_points", "name": "Hit points", "from_levels": true }
        """);

    public static DefinitionType Currency { get; } = new(
        "currency",
        "A named kind of money a ruleset uses. Balances are kept separately for each currency; a ruleset may declare none.",
        [
            new("name", new TextKind(), true, "Display name, for example \"Gold pieces\" or \"Credits\"."),
        ],
        """
        { "type": "currency", "id": "gold", "name": "Gold" }
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
            new("multiclass_equipment", new EnumKind(["any", "all"]), false, "For a character with several classes: whether an item one class allows may be equipped (\"any\", the default) or every class must allow it (\"all\")."),
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
            new("prepares_spells", new BooleanKind(), false, "If true, its spells are memorised each day: the character casts only the copies it prepared (as many as its tracks pay for), each copy once, until a rest that prepares spells; a spell that costs nothing (a cantrip) is always ready. Without it, the character casts any spell it knows while it can pay."),
            new("actions", new ListKind(Use), false, "Actions characters of the class can take in combat, in order of preference."),
            new("reactions", new ListKind(new ReferenceKind("reaction")), false, "Reactions it gives in combat."),
            new("starting", new MapKind(new ReferenceKind("currency"), SelfNumber), false, "Starting balances for a new character of this class, by currency. A class an extension adds brings its own this way when the creation does not name it."),
            new("equipment", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Item), false, "Which items members of the class may equip, reading item.id, item.kind, item.weight and item.cost, for example \"item.kind != 'armour' or item.id == 'leather_armour'\". Without it, any item."),
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

    public static DefinitionType Npc { get; } = new(
        "npc",
        "A predefined character, using character data without format or modules (the loader supplies those). Export with goldbox character npc; join and dismiss events use its qualified identity.",
        [
            new("character", new ObjectKind(Characters.CharacterFile.DataFields), true, "Complete character data, checked by the character reader. References must name this module or its requires. No rolling or creation happens when the NPC joins."),
            new("behavior", new ReferenceKind("combat-behavior"), false, "The authored combat behavior used when this NPC is under autonomous control; an explicit controller override wins."),
            new("control", new EnumKind(["manual", "automatic"]), false, "The NPC's default combat controller when it joins a party; a player's explicit controller choice wins and is saved with a live combat.")
        ],
        """{ "type": "npc", "id": "guide", "behavior": "protective", "control": "automatic", "character": { "name": "Guide", "race": "rules:folk", "creation": "rules:standard", "levels": [{ "class": "rules:scout", "gain": 4 }], "experience": 0, "attributes": { "agility": 10 }, "tracks": { "health": { "current": 4, "max": 4 } }, "balances": { "gold": 0 }, "equipment": [], "conditions": [] } }""");

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

    public static DefinitionType Resting { get; } = new(
        "resting",
        "Rules for one period of rest: its duration and track recovery. Campaign rest events choose this policy and how many periods.",
        [
            new("unit", new EnumKind(["rounds", "hours", "days"]), true, "The duration of one period."),
            new("combat", new ReferenceKind("combat"), false, "With rounds, the combat definition that owns round_seconds; required. Otherwise omit it."),
            new("restore", new ListKind(new ObjectKind(
            [
                new("track", new ReferenceKind("track"), true, "The track to heal."),
                new("amount", SelfNumber, true, "Recovery each uninterrupted period, up to the track's restore cap; self is the resting character."),
            ])), true, "Recovery per period, in order. An empty list advances time without gradual recovery."),
        ],
        """{ "type": "resting", "id": "natural", "unit": "days", "restore": [{ "track": "hit_points", "amount": "1" }] }""");

    public static DefinitionType Advancement { get; } = new(
        "advancement",
        "How characters advance. The default kind is experience, which uses class or character levels; milestone and improvement kinds provide ruleset-owned advancement without experience levels. A module set has at most one.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("kind", new EnumKind(["experience", "milestone", "improvement"]), false, "The advancement procedure. Without it, an advancement with experience uses the experience procedure."),
            new("experience", new EnumKind(["class", "character", "split"]), false, "For kind experience: \"class\": each class's levels[].xp, one class per character; \"character\": the levels below, by total level, with a class chosen for each level; \"split\": experience is divided between starting classes."),
            new("class_change", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Class), false, "With experience \"split\": whether a character may leave its classes for a new one (dual-classing), read with class.id as the new class, for example \"self.race == 'human' and self.classes == 1\". The classes left stop advancing and their modifiers and actions wait until the new class's level is higher; self.former_level is the highest of them. Without it, no class change."),
            new("levels", new ListKind(new IntegerKind()), false, "With experience \"character\": the experience needed for each character level, starting with 0 for level 1."),
            new("training", new ObjectKind(
            [
                new("cost", SelfNumber, true, "Amount for one level in the named currency, evaluated before levelling; self.level is the current total level. Must be nonnegative and must not roll dice."),
                new("currency", new ReferenceKind("currency"), true, "Currency paid for one level."),
                new("days", SelfNumber, true, "Days spent training, evaluated before levelling; may roll dice and must be nonnegative."),
            ]), false, "When present, earned experience waits for paid training at a training event. Each payment gains one level."),
            new("experience_to", new EnumKind(["survivors", "party", "each"]), false, "Who gets the experience a fight or an experience event awards in play: an even share (rounding down) to the characters still standing (\"survivors\", the default, also without an advancement definition) or to the whole party, or the whole amount to each member (\"each\")."),
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
            new("milestones", new ObjectKind(
            [
                new("skill_raise", new ObjectKind([
                    new("count", new IntegerKind(), true, "How many skills a milestone may raise."),
                    new("amount", new IntegerKind(), true, "The amount each selected skill rises."),
                ]), false, "A milestone's selected skill raises."),
                new("skill_swap", new ObjectKind([
                    new("count", new IntegerKind(), true, "How many skill levels may move from one skill to another."),
                ]), false, "A milestone's selected skill swaps."),
                new("feature", new ObjectKind([
                    new("kind", new TextKind(), true, "The feature kind granted by the milestone."),
                    new("count", new IntegerKind(), true, "How many features of that kind may be chosen."),
                ]), false, "New stunt or other feature choices."),
            ]), false, "Rules for a milestone grant. Choices are supplied by a campaign event or the character CLI."),
            new("improvement", new ObjectKind([
                new("checks", new ListKind(new ObjectKind([
                    new("skill", new StatKind(false), true, "The marked skill this check improves."),
                    new("when", new ExpressionKind(ExprType.Boolean, Roots.Self), true, "The improvement check, evaluated after a successful use."),
                    new("amount", new ExpressionKind(ExprType.Number, Roots.Self), true, "The amount added when the check succeeds."),
                ])), true, "The improvement check for each marked skill."),
            ]), false, "Rules for improving skills marked by successful use."),
        ],
        """
        {
          "type": "advancement",
          "id": "standard",
          "name": "Character levels",
          "experience": "character",
          "levels": [0, 1000, 3000, 6000, 10000],
          "grants": [ { "kind": "feat", "when": "self.level == 1 or self.level % 3 == 0" } ],
          "training": { "cost": "self.level * 20", "currency": "gold", "days": "2" }
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
            new("skills", new ListKind(new StatKind(false)), false, "Skills to which a staged character-creation profession may spend its profession points. Omit when the feature is not a profession or imposes no limit."),
            new("modifiers", new ListKind(Modifier), false, "Modifiers a character with the feature has."),
            new("actions", new ListKind(Use), false, "Actions the feature lets a character take in combat, after its classes' actions."),
            new("reactions", new ListKind(new ReferenceKind("reaction")), false, "Reactions it gives in combat."),
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
            new("use", Use, true, "The action it takes against the creature that triggered it (or itself, for an action targeting self), with its parameters or from_item."),
        ],
        """
        { "type": "reaction", "id": "attack_of_opportunity", "name": "Attack of opportunity", "trigger": "leaves_reach", "cost": { "reaction": 1 }, "use": { "action": "melee_attack", "from_item": "weapon" } }
        """);

    public static DefinitionType Check { get; } = new(
        "check",
        "A roll compared with a target number, such as an attack, a saving throw or a skill roll, giving an outcome tier. Roll high or roll under; tiers express criticals, degrees of success and specials.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("roll", CombatNumber, true, "The dice, for example \"1d20\", \"1d100\", \"3d6\" or \"roll_count(self.pool, 10, 8)\". Tiers read it as check.roll."),
            new("bonus", CombatNumber, false, "Added to the roll, for example \"self.str_to_hit\". Modifiers for the check add too."),
            new("skill", new StatKind(false), false, "The skill this check uses. A successful use marks it for a ruleset's improvement procedure."),
            new("target", CombatNumber, true, "The number the total is compared with."),
            new("succeeds", new EnumKind(["at-least", "at-most"]), true, "Whether the total must be at least the target (roll high) or at most it (roll under)."),
            new("tiers", new ListKind(new ObjectKind(
            [
                new("name", new TextKind(), true, "Tier name that actions branch on, for example \"critical\"."),
                new("when", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Target | Roots.Check), true, "When this tier applies; may read check.roll, check.total, check.target and check.margin (how far the total beat the target)."),
            ])), false, "Outcome tiers in order; the first that applies wins, otherwise the outcome is \"success\" or \"failure\"."),
            new("post_roll", new ListKind(new ObjectKind(
            [
                new("name", new TextKind(), false, "Display name in the combat transcript; without one, the effect is named +2 or reroll."),
                new("track", new ReferenceKind("track"), true, "The resource track to spend after seeing this check's roll."),
                new("cost", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Target | Roots.Check | Roots.Combat), true, "How much of the resource it spends."),
                new("bonus", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Target | Roots.Check | Roots.Combat), false, "The bonus added to the roll after it was made."),
                new("reroll", new BooleanKind(), false, "If true, roll the check's dice again and use the new result."),
                new("score", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Target | Roots.Check | Roots.Combat), true, "The deterministic policy score after the roll; the highest positive affordable option is taken, otherwise none."),
            ])), false, "Affordable options a check may take after its roll. Each spends its track and either adds a bonus or rerolls; score is read with check.roll, check.total, check.target and check.margin."),
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
            new("each_turn", new ListKind(new OperationKind(Roots.Self | Roots.Condition | Roots.Combat)), false, "Operations at the start of each of the creature's turns, for example ongoing damage (\"to\" must be self)."),
            new("end_of_turn", new ListKind(new OperationKind(Roots.Self | Roots.Condition | Roots.Combat)), false, "Operations at the end of each of the creature's turns, for example a save that ends the condition."),
            new("on_apply", new ListKind(new OperationKind(Roots.Self | Roots.Condition | Roots.Combat)), false, "Operations when the condition is applied, on its holder (\"to\" must be self), reading the values it was applied with."),
            new("rounds_end", new EnumKind(["turn_end", "turn_start"]), false, "When its rounds count down: at the end of each of the holder's turns (\"turn_end\", the default; 1 round covers the holder's next turn) or at the start (\"turn_start\"; 1 round lasts until the holder's next turn begins, as a raised shield or a dodge does)."),
            new("instant", new BooleanKind(), false, "If true, applying it only runs its on_apply operations: the creature never has it, and no one sees it come and go. A shared procedure such as absorbing a hit, applied with values like { \"shifts\": \"check.margin\" }."),
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
            new("cost", new NumberKind(), true, "Price, at least 0, in the currency named by \"currency\". Shops charge this and buy back at the economy's sell_fraction of it."),
            new("currency", new ReferenceKind("currency"), true, "The currency in which this item's cost is paid."),
            new("weight", new NumberKind(), true, "Weight, in the ruleset's unit."),
            new("parameters", new MapKind(new TextKind(), CombatNumber), false, "Values the item gives actions used with it (uses with from_item), for example { \"damage\": \"1d8\" }. May read target, for example to deal more against large creatures."),
            new("modifiers", new ListKind(Modifier), false, "Modifiers while equipped, for example armour lowering \"ac\"."),
        ],
        """
        { "type": "item", "id": "long_sword", "name": "Long sword", "kind": "weapon", "cost": 15, "currency": "gold", "weight": 7, "parameters": { "damage": "if target.size == 'large' then 1d12 else 1d8" } }
        """);

    public static DefinitionType Economy { get; } = new(
        "economy",
        "The ruleset's shop resale policy. A module set has at most one; shop events need one. Purchases use each item's declared currency and pooled character balances; proceeds are shared evenly, with the remainder to the first character.",
        [new("sell_fraction", new NumberKind(), true, "The fraction of item cost paid for a carried item, from 0 to 1. Prices keep fractional currency without rounding.")],
        """{ "type": "economy", "id": "standard", "sell_fraction": 0.5 }""");

    public static DefinitionType Spell { get; } = new(
        "spell",
        "A spell: which class lists it is on and at what level, what casting it spends, and the action it uses. A character casts the spells it knows (its spells list) while it can pay their cost.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("lists", new MapKind(new ReferenceKind("class"), new IntegerKind()), true, "Spell level on each class's list."),
            new("range", new TextKind(), true, "Range, as text."),
            new("duration", new TextKind(), true, "Duration, as text."),
            new("area", new TextKind(), true, "Area of effect, as text."),
            new("casting_time", new TextKind(), true, "Casting time, as text."),
            new("save", new ReferenceKind("check"), false, "The saving throw targets may make, if any."),
            new("effect", Use, false, "The action casting the spell uses, with its parameters. Without it, the spell can't be cast in combat."),
            new("cost", new MapKind(new ReferenceKind("track"), SelfNumber), false, "What casting spends from the caster's tracks, for example { \"spells_1\": \"1\" } for a spell slot or { \"power_points\": \"3\" }. A spell is offered only while every track can pay; tracks are restored by rest events."),
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
            new("surprise", CombatNumber, false, "Rolled at the start: whole rounds lost (0 for none). By side, self is the side's lead and target the other side's; by creature, self is the creature and target each enemy in turn, for example \"if 1d20 + target.stealth > self.perception then 1 else 0\"."),
            new("surprise_by", new EnumKind(["side", "creature"]), false, "Whether a whole side is surprised together (the default) or each creature on its own, so only some may be."),
            new("surprise_lead", SelfNumber, false, "By side: which member the side's surprise reads as self (and the other side as target), the highest, for example \"self.perception\" for the most alert or a scout."),
            new("initiative", FightNumber, false, "Initiative roll each round. With initiative_by \"side\" it is rolled once per side and self is the side's first creature still fighting. Required for rolled mode; ignored when initiative_mode is \"elective\"."),
            new("initiative_by", new EnumKind(["side", "creature"]), false, "Whether each side or each creature rolls initiative; required for rolled mode."),
            new("initiative_order", new EnumKind(["highest-first", "lowest-first"]), false, "Which result acts first; ties keep side and listing order. Required for rolled mode."),
            new("initiative_each", new EnumKind(["round", "combat"]), false, "Whether initiative is rolled again every round or once for the whole combat. Required for rolled mode."),
            new("initiative_mode", new EnumKind(["rolled", "elective"]), false, "The default rolled order, or elective (popcorn) order where the last actor chooses the next unacted creature."),
            new("initiative_score", CombatNumber, false, "Elective mode's AI policy: after an actor, score each unacted creature as target and choose the highest. Ties keep listing order; without it, choose the first."),
            new("round_seconds", new IntegerKind(), true, "Length of a round in seconds."),
            new("round_limit", new IntegerKind(), false, $"Rounds before a fight nobody can finish is called undecided (at least 1); without it, {RustyGoldbox.Core.Combat.CombatRunner.DefaultRoundLimit}."),
            new("budget", new ListKind(new ObjectKind(
            [
                new("id", new TextKind(), true, "Budget name that action costs use, for example \"action\", \"standard\" or \"actions\"."),
                new("per_turn", FightNumber, true, "How many a creature has at the start of each turn, for example 3, \"self.actions\" so conditions can change it, or \"if combat.surprise_round then 1 else 2\"; rounded down, never below 0."),
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
            new("actions", new ListKind(Use), false, "Actions every creature in these fights can take, after its own, such as attacking with any skill where a system lets anyone try anything; uses as in classes and monsters."),
            new("downed_conditions", new BooleanKind(), false, "If true, a creature out of the fight still runs its conditions' start- and end-of-turn operations and counts their durations down each round, though it takes no actions (bleeding out, a save to stabilise). It does so at its place in the turn order; with initiative each round a defeated creature isn't rolled for, so it does so at the round's end, as does one out of the fight from the start. Without it, a defeated creature's conditions wait."),
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
            new("portions", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Use | Roots.Combat), false, "How many times the effect resolves, worked out when the action is committed. Automatic resolution chooses each portion with prefer or the target kind's default, so missiles can move on to the next foe when one falls: \"use.missiles\" or \"1 + floor((self.level - 1) / 2)\". A manual command may provide target IDs in portion order; repeated IDs are allowed only within this count and omitted portions keep the authored automatic choice."),
            new("max_targets", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Use | Roots.Combat), false, "The maximum number of legal targets, worked out once when the action is committed (for example \"2d4\"). A manual controller then chooses legal targets up to that committed cap; an automatic controller ranks them by prefer when present and takes the highest-ranked targets. With no max_targets, all_enemies and all_allies affect every legal target, while ordinary target kinds accept one."),
            new("range", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Use | Roots.Combat), false, "On a combat field, the most cells away a target may be, for example \"1\" for melee or \"use.range\"; a target in range must also be in sight. Without it, any distance, seen or not (for moving toward an enemy)."),
            new("valid_target", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Target | Roots.Use | Roots.Combat), false, "Which candidates it may target, for example \"not target.condition.shaken\"; with none left, the action isn't taken."),
            new("prefer", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Target | Roots.Use | Roots.Combat), false, "For a single target, how much the creature wants each candidate; the highest is chosen, the first on a tie. Without it, the target kind's default."),
            new("score", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Target | Roots.Use | Roots.Combat), false, "How much the creature wants to take it now, against the target it would pick (the first, for a whole side). When any of a creature's uses has a score, it takes the highest-scoring use it can (a use without one scores 0; the first in its list on a tie) instead of the first, for example \"if target.hit_points * 2 < target.max_hit_points then 10 else -1\" to heal only the badly hurt."),
            new("parameters", new ListKind(new TextKind()), false, "Names uses must supply (or get from an item), read as use.<name>, for example [\"damage\"]."),
            new("available", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Combat), false, "Whether the creature may take it now; without it, always."),
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
                new("count", PlainNumber, true, "How many, for example \"2\" or \"1d4 + 1\"."),
            ])), true, "The monsters and how many of each."),
            new("terrain", new ListKind(new TextKind()), false, "On a combat field, the ground it is fought on: one row of text per row of cells, \".\" for open ground and the field's terrain keys elsewhere, for example [\"..#..\", \".....\", \"..~..\"]. Its size must match every field it can be fought on. Without it, open ground."),
        ],
        """
        { "type": "encounter", "id": "crypt_guard", "name": "Crypt guard", "monsters": [ { "monster": "skeleton", "count": "1d4 + 1" } ] }
        """);

    public static DefinitionType Figure { get; } = new(
        "figure",
        "How a monster, a class or a kind of combat terrain looks: the sprite drawn for it in combat and in the 3D view. Rulesets carry no art, so "
        + "figures live in a campaign or extension that requires both the ruleset and an assets module. Give exactly one of "
        + "monster, class and terrain (with its combat); a module set has at most one figure for each.",
        [
            new("monster", new ReferenceKind("monster"), false, "The monster it draws."),
            new("class", new ReferenceKind("class"), false, "The class it draws (for every character of the class)."),
            new("combat", new ReferenceKind("combat"), false, "With terrain: the combat definition whose field declares it."),
            new("terrain", new TextKind(), false, "A terrain key the combat's field declares, for example \"#\": the sprite stands on every cell of it."),
            new("sprite", new ReferenceKind("asset", "figure"), true, "The sheet that draws it (a figure slot)."),
            new("icon", new ReferenceKind("asset", "picture"), false, "A small picture for lists, such as the combat roster (a picture slot)."),
        ],
        """
        { "type": "figure", "id": "skeleton", "monster": "classic:skeleton", "sprite": "placeholder-art:skeleton", "icon": "placeholder-art:skull" }
        """);

    public static DefinitionType Variable { get; } = new(
        "variable",
        "A campaign or area variable: declared with a type and an initial value, read as campaign.var.<id> or area.var.<id>. Area values are separate for every area and changed by set events in the current area. Undeclared variables are errors.",
        [
            new("value_type", new EnumKind(["number", "boolean", "text"]), true, "Its type."),
            new("initial", new ExpressionKind(null, Roots.None), true, "Its value when the campaign starts, of its type, for example \"false\" or \"0\"."),
            new("scope", new EnumKind(["campaign", "area"]), false, "Where its value lives: campaign (the default) is shared by the whole adventure; area gives each area its own value and is read as area.var.<id>."),
            new("description", new TextKind(), false, "What it records."),
        ],
        """
        { "type": "variable", "id": "gate_open", "value_type": "boolean", "initial": "false" }
        """);

    /// <summary>The asset kinds the presentation draws, each an RGBA PNG.</summary>
    public static DefinitionType Asset { get; } = new(
        "asset",
        "A logical asset ID mapped to a file in the module, and what media the file is. Other modules refer to it as module:id, never by path. "
        + "An asset says only what it is; each reference names the slot it fills (picture, figure, wall_set) and the slot decides which media fit, "
        + "so anything that shows a picture shows any visual media (see `goldbox schema media`). Images are 8-bit RGBA PNGs, the format the Engine renderer admits; "
        + "audio is Ogg (Vorbis or Opus), WAV or FLAC, the formats the Engine decodes.",
        [
            new("media", new EnumKind(Media.Types), true, "What the file is: \"image\" (one picture, optionally with named regions), \"sheet\" (equal frames, optionally animated) or \"audio\" (a sound or music)."),
            new("sampling", new EnumKind(Media.SamplingModes), false, "Visual scaling: \"nearest\" keeps pixel art sharp (the default), while \"linear\" smoothly scales illustrated art. Omit it for nearest. A linear sheet frame or named region cut from a larger image needs at least 2 pixels on every cropped axis; use nearest for a one-pixel crop."),
            new("file", new TextKind(), true, "Path of the file inside this module, with forward slashes: a PNG for an image or sheet; an Ogg, WAV or FLAC file for audio."),
            new("regions", new MapKind(new TextKind(), new ListKind(new IntegerKind(), 4)), false,
                "image only: named pixel rectangles [x, y, width, height] inside the image. A wall set needs wall and door, and may have floor and ceiling."),
            new("frame_size", new ListKind(new IntegerKind(), 2), false,
                "sheet only (required): [width, height] of one frame in pixels. Frames are read left to right, then top to bottom, and the image must be a whole number of frames across and down."),
            new("frame_count", new IntegerKind(), false, "sheet only: how many frames the sheet holds, when the last row isn't full. Defaults to every cell."),
            new("animations", new MapKind(new TextKind(), new ObjectKind(
            [
                new("frames", new ListKind(new IntegerKind()), true, "Frame numbers in play order, counting from 0."),
                new("fps", new NumberKind(), true, "Frames per second."),
                new("loop", new BooleanKind(), false, "Repeat (true, the default) or play once and hold the last frame."),
            ])), false, "sheet only: named animations such as idle, walk, attack, hit or die. A picture plays the first; a figure picks by name. Without any, it shows frame 0."),
            new("faces", new EnumKind(["left", "right"]), false, "sheet only: which way the art faces; the renderer flips it to face the other way. A figure needs it."),
            new("anchor", new ListKind(new IntegerKind(), 2), false, "sheet only: the pixel [x, y] in a frame that stands on the floor. Defaults to the bottom centre."),
            new("height", new NumberKind(), false, "sheet only: how tall a frame stands, in cells (a cell is 1 x 1 x 1); the width follows the frame's shape. A figure needs it."),
            new("tags", new ListKind(new TextKind()), false, "Words that say what the art is meant for, so pickers can offer it: \"portrait\" puts it in the portrait chooser. They don't limit where it can be used."),
        ],
        """
        {
          "type": "asset",
          "id": "skeleton",
          "media": "sheet",
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
            new("wall_set", new ReferenceKind("asset", "wall_set"), false, "The image the first-person view draws the area with (a wall_set slot: wall and door regions)."),
            new("cells", new ListKind(new ObjectKind(
            [
                new("at", new ListKind(new IntegerKind(), 2), true, "[x, y] of the cell; x west to east and y north to south, from 0."),
                new("zone", new TextKind(), false, "A zone tag for the cell."),
                new("backdrop", new ReferenceKind("asset", "picture"), false, "The picture shown over the view in the cell (a picture slot)."),
                new("prop", new ObjectKind(
                [
                    new("sprite", new ReferenceKind("asset", "figure"), true, "The sheet standing in the cell (a figure slot): a chest, a pillar, bones, a guard."),
                    new("hidden", new ExpressionKind(Expressions.ExprType.Boolean, Roots.Campaign | Roots.Area), false,
                        "While this is true the prop isn't there, for example \"campaign.var.chest_opened\" or \"area.var.chest_opened\"; it may read campaign.var or area.var."),
                ]), false, "Something standing in the middle of the cell in the first-person view. It doesn't block movement."),
                new("event", new ReferenceKind("event"), false, "The event that runs when the party enters the cell."),
                new("facing", new EnumKind(["north", "east", "south", "west"]), false, "Run the event only when the party enters facing this way."),
                new("once", new BooleanKind(), false, "If true, the event runs only the first time."),
            ])), false, "Cells with features."),
            new("search", new ReferenceKind("check"), false, "The ruleset check used by the search command to discover secret doors around the party; when omitted, a check named search is used if the ruleset provides one."),
            new("doors", new ListKind(new ObjectKind(
            [
                new("id", new TextKind(), true, "Door name used by open events and commands."),
                new("at", new ListKind(new IntegerKind(), 2), true, "[x, y] of the cell whose side is the door."),
                new("facing", new EnumKind(["north", "east", "south", "west"]), true, "The cell side containing the door."),
                new("locked", new BooleanKind(), false, "Whether the door starts locked; true is the default when this field is omitted."),
                new("key", new ReferenceKind("item"), false, "An item that opens the door without being consumed."),
                new("pick", new ReferenceKind("check"), false, "A check any party member may succeed at to pick the lock."),
                new("force", new ReferenceKind("check"), false, "A check any party member may succeed at to force the door."),
                new("event", new ReferenceKind("event"), false, "An event chain that may unlock the door; an open event names this door."),
            ])), false, "Locked door declarations. Each one names a DD map edge and at least one key, pick, force or event mechanism when locked."),
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
          "doors": [ { "id": "gate", "at": [1, 0], "facing": "south", "key": "classic:key" } ],
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
            new("skin", new ReferenceKind("skin"), false, "How the Game's panels look while this campaign plays, unless the player picks another skin."),
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
        "How new characters are made: how attribute scores are made (rolled, an arranged array, point buy or boosts), staged profession and personal skill points, the features every character chooses, and starting balances. A ruleset may offer several; one marked default is used when none is named.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("attributes", new ListKind(new StatKind(true)), true, "Every attribute once, in the order rolls are taken and sheets show them."),
            new("method", new EnumKind(["roll", "array", "point-buy", "boosts"]), false, "How attribute scores are made: rolled (the default), a fixed array the player arranges, points spent from a budget, or boosts from a base. Given scores (--attributes) skip roll, array and boosts; point buy checks them."),
            new("default", new BooleanKind(), false, "With more than one character-creation definition, true marks the one used when none is named."),
            new("attribute_roll", PlainNumber, false, "Method roll: the roll for each attribute, for example \"3d6\" or \"roll_keep(4, 6, 3)\"."),
            new("attribute_rolls", new MapKind(new StatKind(true), PlainNumber), false, "Method roll: a different roll for some attributes, for example { \"siz\": \"2d6 + 6\" }; the rest use attribute_roll. Rolls are made in attribute order; with assignment arrange, an attribute's own roll stays its own."),
            new("assignment", new EnumKind(["in-order", "arrange"]), false, "Method roll: whether rolls are taken in attribute order (the default) or arranged by the player."),
            new("array", new ListKind(new IntegerKind()), false, "Method array: one score per attribute, which the player arranges by priority, for example [15, 14, 13, 12, 10, 8]."),
            new("base", new IntegerKind(), false, "Methods point-buy and boosts: the score every attribute starts at."),
            new("budget", new IntegerKind(), false, "Method point-buy: the points to spend."),
            new("costs", new ReferenceKind("table"), false, "Method point-buy: a table from a score to its total cost from the base, for example rows [8, 0], [9, 1], ..., [18, 16]."),
            new("boost", new IntegerKind(), false, "Method boosts: how much each boost raises an attribute."),
            new("boosts", Boosts.Kind, false, "Method boosts: boosts every new character has besides those of its race, creation features and class; usually free ones ({})."),
            new("starting", new MapKind(new ReferenceKind("class"), new MapKind(new ReferenceKind("currency"), SelfNumber)), false, "Starting balances for each class it names, by currency; a class it doesn't name uses its own \"starting\" map. Without either, characters start with none."),
            new("features", new ListKind(new ObjectKind([GrantKind, GrantKinds, GrantCount])), false, "Features every new character chooses, for example a background and a heritage."),
            new("skill_points", SkillPoints, false, "Optional second creation step: after attributes and feature choices, spend the profession and personal budgets on the listed derived skills. Profession points are limited by the selected profession's `skills`; personal points may use any listed skill."),
            new("lifepath", new ReferenceKind("lifepath"), false, "The optional term-by-term career procedure available to this creation. The CLI or Game supplies the career, table and benefit choices.")
        ],
        """
        {
          "type": "character-creation",
          "id": "standard",
          "name": "Standard",
          "attributes": ["str", "dex", "con", "int", "wis", "cha"],
          "attribute_roll": "3d6",
          "assignment": "in-order",
          "starting": { "fighter": { "gold": "(3d6 + 2) * 10" } },
          "skill_points": {
            "profession": "250",
            "personal": "self.int * 10",
            "skills": { "sword": "15", "dodge": "self.dex * 2" }
          }
        }
        """);

    private static readonly ObjectKind LifepathDice = new(
    [
        new("count", new IntegerKind(), true, "Number of dice rolled."),
        new("sides", new IntegerKind(), true, "Number of faces on each die."),
    ]);

    private static readonly ObjectKind LifepathThrow = new(
    [
        new("stat", new StatKind(false), false, "The stat added to the configured career dice; use a derived stat when the ruleset wants a skill check."),
        new("check", new ReferenceKind("check"), false, "An existing check definition to resolve with the normal Evaluator."),
        new("target", new IntegerKind(), false, "The target for a stat throw; totals at least this number succeed."),
        new("modifier", new IntegerKind(), false, "A fixed modifier added to the throw."),
    ]);

    private static readonly ObjectKind LifepathTableEntry = new(
    [
        new("roll", new IntegerKind(), true, "The configured skill-die total that selects this entry."),
        new("kind", new EnumKind(["skill", "attribute"]), true, "Whether the result raises a skill or an attribute."),
        new("stat", new StatKind(false), true, "The skill or attribute to raise; the kind decides which is allowed."),
        new("amount", new IntegerKind(), false, "Levels or points to add; without it, 1."),
    ]);

    private static readonly ObjectKind LifepathSkillTable = new(
    [
        new("id", new TextKind(), true, "The choice ID supplied by the player, for example \"personal\"."),
        new("name", new TextKind(), true, "Display name."),
        new("entries", new ListKind(LifepathTableEntry), true, "One entry per possible configured skill-die total."),
    ]);

    private static readonly ObjectKind LifepathSkillRolls = new(
    [
        new("base", new IntegerKind(), true, "Skill rolls before commission or advancement successes."),
        new("no_commission", new IntegerKind(), true, "Skill rolls for a career without commission or advancement rules."),
        new("commission", new IntegerKind(), true, "Additional skill rolls when commission succeeds."),
        new("advancement", new IntegerKind(), true, "Additional skill rolls when advancement succeeds."),
    ]);

    private static readonly ObjectKind LifepathRank = new(
    [
        new("rank", new IntegerKind(), true, "The rank number reached."),
        new("name", new TextKind(), true, "Display name for the rank."),
        new("skill", new StatKind(false), false, "Optional skill granted when this rank is reached."),
        new("amount", new IntegerKind(), false, "Skill levels granted with skill; without it, 1."),
    ]);

    private static readonly ObjectKind LifepathCashBenefit = new(
    [
        new("roll", new IntegerKind(), true, "The configured benefit-die total that selects this benefit."),
        new("currency", new ReferenceKind("currency"), true, "Currency added to the character's balance."),
        new("amount", new IntegerKind(), true, "Amount added to the balance."),
    ]);

    private static readonly ObjectKind LifepathMaterialBenefit = new(
    [
        new("roll", new IntegerKind(), true, "The configured benefit-die total that selects this benefit."),
        new("kind", new EnumKind(["skill", "attribute", "item", "currency"]), true, "The material benefit's effect."),
        new("stat", new StatKind(false), false, "Skill or attribute raised by this benefit."),
        new("item", new ReferenceKind("item"), false, "Item granted by this benefit."),
        new("currency", new ReferenceKind("currency"), false, "Currency granted by this benefit."),
        new("amount", new IntegerKind(), false, "Levels, points or currency amount; without it, 1."),
    ]);

    private static readonly ObjectKind LifepathRankBenefit = new(
    [
        new("min_rank", new IntegerKind(), true, "Minimum final career rank for these extra benefit rolls."),
        new("count", new IntegerKind(), true, "Extra benefit rolls at or above the rank."),
    ]);

    private static readonly ObjectKind LifepathMaterialRollModifier = new(
    [
        new("min_rank", new IntegerKind(), true, "Minimum final career rank for this material-roll modifier."),
        new("amount", new IntegerKind(), true, "Added to the configured material benefit roll."),
    ]);

    private static readonly ObjectKind LifepathBenefitTables = new(
    [
        new("per_term", new IntegerKind(), true, "Benefit rolls granted for each successful career term."),
        new("rank_benefits", new ListKind(LifepathRankBenefit), true, "Additional benefit rolls selected by final rank."),
        new("material_roll_modifier", LifepathMaterialRollModifier, false, "Optional modifier to material benefit rolls at a rank."),
        new("cash", new ListKind(LifepathCashBenefit), false, "Cash benefits selected by the configured benefit die."),
        new("material", new ListKind(LifepathMaterialBenefit), false, "Material benefits selected by the configured benefit die and optional modifier."),
    ]);

    private static readonly ObjectKind LifepathCareer = new(
    [
        new("id", new TextKind(), true, "Stable career ID used by choices and the career ledger."),
        new("name", new TextKind(), true, "Display name."),
        new("qualification", LifepathThrow with { }, true, "The qualification or enlistment throw for entering the career."),
        new("survival", LifepathThrow with { }, true, "The survival throw for a term; a failed throw ends the career."),
        new("commission", LifepathThrow with { }, false, "Optional commission throw when the character is rank 0."),
        new("advancement", LifepathThrow with { }, false, "Optional advancement throw when the character already has a rank."),
        new("reenlistment", LifepathThrow with { }, true, "The throw required to continue after this term."),
        new("skill_rolls", LifepathSkillRolls, true, "Policy for base, commission and advancement skill rolls."),
        new("skills", new ListKind(LifepathSkillTable), true, "The skill and training tables available for term rolls."),
        new("ranks", new ListKind(LifepathRank), false, "Ranks and optional skill grants, indexed from 1."),
        new("benefits", LifepathBenefitTables, true, "Cash and material benefit tables used when the career ends."),
    ]);

    public static DefinitionType Lifepath { get; } = new(
        "lifepath",
        "A data-driven term-by-term career procedure: qualification, survival, optional commission and advancement, skill tables, ageing, reenlistment and mustering-out benefits. Rolls use Engine Random through the caller's DiceRoller; choices are supplied by the CLI or Game.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("start_age", new IntegerKind(), true, "Age at the start of the first term."),
            new("term_years", new IntegerKind(), true, "Years added by each term."),
            new("max_terms", new IntegerKind(), true, "Maximum terms before retirement."),
            new("qualification_previous_career_modifier", new IntegerKind(), true, "Modifier applied once per distinct career already completed when qualifying for a new career."),
            new("dice", new ObjectKind([
                new("career", LifepathDice, true, "Dice for raw qualification, survival, commission, advancement and reenlistment throws."),
                new("skill", LifepathDice, true, "Dice for each skill-table selection."),
                new("aging", LifepathDice, true, "Dice for ageing effects."),
                new("benefit", LifepathDice, true, "Dice for cash and material benefits."),
            ]), true, "Dice shapes used by the lifepath's raw throws and tables; existing check definitions own their own dice."),
            new("survival_natural_failure", new IntegerKind(), false, "Optional raw career-die total that automatically fails survival."),
            new("reenlistment_natural_success", new IntegerKind(), false, "Optional raw career-die total that automatically succeeds reenlistment."),
            new("aging", new ObjectKind([
                new("start_age", new IntegerKind(), true, "Age at which ageing begins."),
                new("start_term", new IntegerKind(), true, "First term that rolls on the ageing table."),
                new("term_modifier", new IntegerKind(), true, "Modifier multiplied by the term number and added to the ageing roll."),
                new("zero_ends", new BooleanKind(), true, "Whether ageing that reduces any attribute to zero ends prior history."),
                new("effects", new ListKind(new ObjectKind([
                    new("min", new IntegerKind(), true, "Lowest modified ageing result that selects this row."),
                    new("max", new IntegerKind(), true, "Highest modified ageing result that selects this row."),
                    new("changes", new ListKind(new ObjectKind([
                        new("stat", new StatKind(true), true, "Physical or mental attribute changed by ageing."),
                        new("amount", new IntegerKind(), true, "Amount subtracted from the attribute."),
                    ])), true, "Attribute changes; an empty list means no effect."),
                ])), true, "Ageing rows selected by the configured ageing-die result."),
            ]), true, "Ageing policy; its term modifier is applied to the configured ageing die."),
            new("careers", new ListKind(LifepathCareer), true, "Careers available to choose term by term."),
        ],
        """
        {
          "type": "lifepath",
          "id": "prior_history",
          "name": "Prior history",
          "start_age": 18,
          "term_years": 4,
          "max_terms": 7,
          "qualification_previous_career_modifier": -2,
          "dice": { "career": { "count": 2, "sides": 6 }, "skill": { "count": 1, "sides": 6 }, "aging": { "count": 2, "sides": 6 }, "benefit": { "count": 1, "sides": 6 } },
          "survival_natural_failure": 2,
          "reenlistment_natural_success": 12,
          "aging": { "start_age": 34, "start_term": 4, "term_modifier": -1, "zero_ends": true, "effects": [ { "min": 1, "max": 12, "changes": [] } ] },
          "careers": [
            {
              "id": "scout",
              "name": "Scout",
              "qualification": { "stat": "int", "target": 6 },
              "survival": { "stat": "end", "target": 7 },
              "reenlistment": { "target": 6 },
              "skill_rolls": { "base": 1, "no_commission": 2, "commission": 1, "advancement": 1 },
              "skills": [ { "id": "personal", "name": "Personal development", "entries": [ { "roll": 1, "kind": "attribute", "stat": "str" }, { "roll": 2, "kind": "attribute", "stat": "dex" }, { "roll": 3, "kind": "attribute", "stat": "end" }, { "roll": 4, "kind": "skill", "stat": "gun_combat" }, { "roll": 5, "kind": "skill", "stat": "athletics" }, { "roll": 6, "kind": "skill", "stat": "melee_combat" } ] } ],
              "benefits": { "per_term": 1, "rank_benefits": [], "cash": [ { "roll": 1, "currency": "credits", "amount": 1000 } ], "material": [ { "roll": 1, "kind": "attribute", "stat": "edu" } ] }
            }
          ]
        }
        """);

    /// <summary>The theme colours a skin may set.</summary>
    public static IReadOnlyList<string> SkinColors { get; } =
        ["background", "text", "muted", "accent", "border", "inset", "button", "button_text", "page", "page_text", "page_accent", "page_link"];

    private static readonly ObjectKind NineSlice = new(
    [
        new("picture", new ReferenceKind("asset", "border"), true, "The image, cut into nine: corners stay as drawn, edges and middle stretch."),
        new("slice", new IntegerKind(), true, "How many pixels in from each edge the cuts are; under half the image's width and height."),
    ]);

    public static DefinitionType Skin { get; } = new(
        "skin",
        "How the Game's panels look: theme colours and pictures, in an assets or campaign module. A campaign names its skin; "
        + "the player may pick any installed one instead. Skins restyle the panels and may set their proportions; they don't change what the panels do.",
        [
            new("name", new TextKind(), true, "Display name in the skin chooser."),
            new("colors", new MapKind(new TextKind(), new TextKind()), false,
                "Theme colours as #rgb, #rrggbb or #rrggbbaa, by name: " + string.Join(", ", SkinColors) + ". Any left out keep the Game's own."),
            new("panel", new ReferenceKind("asset", "picture"), false, "A picture tiled behind the panels."),
            new("frame", NineSlice, false, "A border drawn around the panels."),
            new("button", NineSlice, false, "The face of every button."),
            new("title", new ReferenceKind("asset", "picture"), false, "Art shown at the top of the panels in place of the product's name."),
            new("layout", new ObjectKind(SkinLayout.Parts.Select(part => new Field(
                part.Name, new NumberKind(), false, $"{part.Description}; {part.Minimum} to {part.Maximum}, {part.Default} when left out.")).ToList()), false,
                "The panels' proportions for this skin; any left out keep the Game's own. The player's own layout overrides them."),
        ],
        """
        {
          "type": "skin",
          "id": "stone",
          "name": "Crypt stone",
          "colors": { "background": "#141318e8", "text": "#e6e0d4", "accent": "#e8b04a" },
          "panel": "crypt-art:stone_tile",
          "frame": { "picture": "crypt-art:stone_frame", "slice": 8 },
          "button": { "picture": "crypt-art:stone_button", "slice": 4 },
          "layout": { "log_share": 0.42, "text_scale": 1.1 }
        }
        """);

    public static IReadOnlyList<DefinitionType> All { get; } =
    [
        Attribute, Track, Currency, Derived, Table, Race, Class, Resting, Advancement, Npc, CombatBehavior, Feature, Reaction, Check, Condition, Item, Economy, Spell, Monster, Action, Encounter, Combat, CharacterCreation,
        Variable, Asset, Area, Event, Campaign, Figure, Skin, Lifepath,
    ];

    public static DefinitionType? Find(string name) => All.FirstOrDefault(type => type.Name == name);
}
