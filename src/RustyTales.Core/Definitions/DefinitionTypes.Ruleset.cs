using RustyTales.Core.Expressions;

namespace RustyTales.Core.Definitions;

public static partial class DefinitionTypes
{
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
            new("max", Shared.SelfNumber, false, "The maximum, for example \"self.ht\". Without it, each creature brings its own: characters from their class levels (with from_levels) and monsters from their tracks."),
            new("min", Shared.SelfNumber, false, "The lowest it can go; damage stops there. Without it, there is no floor."),
            new("restore_cap", Shared.SelfNumber, false, "The highest healing can take it; without it, the maximum."),
            new("start", Shared.SelfNumber, false, "The value a creature starts with; without it, the maximum."),
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
            new("show_on_sheet", new BooleanKind(), false, "Show this named derived value on the ordinary member sheet. Useful for equipment effects such as armour class; omitted values stay internal to the rules."),
        ],
        """
        { "type": "derived", "id": "thac0", "name": "To hit AC 0", "value": "table(thac0, self.class, self.level)", "show_on_sheet": true }
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
            new("modifiers", new ListKind(Shared.Modifier), false, "Modifiers every member of the race has."),
            Shared.Boosts,
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
                new("hp", Shared.ClassNumber, false, "Gained on reaching this level by the track with from_levels (usually hit points), for example \"1d10\" or \"3\". Rolled once and kept. Required when a track has from_levels; left out when none does."),
                new("hp_bonus", Shared.ClassNumber, false, "Added to this level's gain as the character is now, not as it was: recomputed whenever the stats it reads change, for example \"self.con_mod\" so a higher constitution raises every level's hit points. Without it, the whole gain is hp."),
                new("grants", new ListKind(new ObjectKind([Shared.GrantKind, Shared.GrantKinds, Shared.GrantCount])), false, "Features the character chooses on reaching this level of the class, for example a bonus feat."),
            ])), true, "One entry per level of the class, starting at level 1."),
            new("spell_slots", new ListKind(new ListKind(new IntegerKind())), false, "Per level (same length as levels): spells per day for spell level 1, 2, ...; [] for none."),
            new("prepares_spells", new BooleanKind(), false, "If true, its spells are memorised each day: the character casts only the copies it prepared (as many as its tracks pay for), each copy once, until a rest that prepares spells; a spell that costs nothing (a cantrip) is always ready. Without it, the character casts any spell it knows while it can pay."),
            new("actions", new ListKind(Shared.Use), false, "Actions characters of the class can take in combat, in order of preference."),
            new("reactions", new ListKind(new ReferenceKind("reaction")), false, "Reactions it gives in combat."),
            new("starting", new MapKind(new ReferenceKind("currency"), Shared.SelfNumber), false, "Starting balances for a new character of this class, by currency. A class an extension adds brings its own this way when the creation does not name it."),
            new("equipment", new ExpressionKind(ExprType.Boolean, Roots.Self | Roots.Item), false, "Which items members of the class may equip, reading item.id, item.kind, item.weight and item.cost, for example \"item.kind != 'armour' or item.id == 'leather_armour'\". Without it, any item."),
            new("modifiers", new ListKind(Shared.ClassModifier), false, "Modifiers a creature with levels in the class has. They may read class.level, its level in this class, so per-class progressions add up across classes: { \"stat\": \"base_attack\", \"value\": \"floor(class.level * 3 / 4)\" }."),
            Shared.Boosts,
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
        "A predefined character, using character data without format or modules (the loader supplies those). Export with tales character npc; join and dismiss events use its qualified identity.",
        [
            new("character", new ObjectKind(Characters.CharacterFile.DataFields), true, "Complete character data, checked by the character reader. References must name this module or its requires. No rolling or creation happens when the NPC joins."),
            new("behavior", new ReferenceKind("combat-behavior"), false, "The authored combat behavior used when this NPC is under autonomous control; an explicit controller override wins."),
            new("control", new EnumKind(["manual", "automatic"]), false, "The NPC's default combat controller when it joins a party; a player's explicit controller choice wins and is saved with a live combat.")
        ],
        """{ "type": "npc", "id": "guide", "behavior": "protective", "control": "automatic", "character": { "name": "Guide", "race": "rules:folk", "creation": "rules:standard", "levels": [{ "class": "rules:scout", "gain": 4 }], "experience": 0, "attributes": { "agility": 10 }, "tracks": { "health": { "current": 4, "max": 4 } }, "balances": { "gold": 0 }, "equipment": [], "conditions": [] } }""");

    public static DefinitionType Resting { get; } = new(
        "resting",
        "Rules for one period of rest: its duration and track recovery. Campaign rest events choose this policy and how many periods.",
        [
            new("unit", new EnumKind(["rounds", "hours", "days"]), true, "The duration of one period."),
            new("combat", new ReferenceKind("combat"), false, "With rounds, the combat definition that owns round_seconds; required. Otherwise omit it."),
            new("restore", new ListKind(new ObjectKind(
            [
                new("track", new ReferenceKind("track"), true, "The track to heal."),
                new("amount", Shared.SelfNumber, true, "Recovery each uninterrupted period, up to the track's restore cap; self is the resting character."),
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
                new("cost", Shared.SelfNumber, true, "Amount for one level in the named currency, evaluated before levelling; self.level is the current total level. Must be nonnegative and must not roll dice."),
                new("currency", new ReferenceKind("currency"), true, "Currency paid for one level."),
                new("days", Shared.SelfNumber, true, "Days spent training, evaluated before levelling; may roll dice and must be nonnegative."),
            ]), false, "When present, earned experience waits for paid training at a training event. Each payment gains one level."),
            new("experience_to", new EnumKind(["survivors", "party", "each"]), false, "Who gets the experience a fight or an experience event awards in play: an even share (rounding down) to the characters still standing (\"survivors\", the default, also without an advancement definition) or to the whole party, or the whole amount to each member (\"each\")."),
            new("grants", new ListKind(new ObjectKind(
            [
                Shared.GrantKind,
                Shared.GrantKinds,
                Shared.GrantCount,
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
            new("modifiers", new ListKind(Shared.Modifier), false, "Modifiers a character with the feature has."),
            new("actions", new ListKind(Shared.Use), false, "Actions the feature lets a character take in combat, after its classes' actions."),
            new("reactions", new ListKind(new ReferenceKind("reaction")), false, "Reactions it gives in combat."),
            Shared.Boosts,
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
            new("roll", Shared.CombatNumber, true, "The dice, for example \"1d20\", \"1d100\", \"3d6\" or \"roll_count(self.pool, 10, 8)\". Tiers read it as check.roll."),
            new("bonus", Shared.CombatNumber, false, "Added to the roll, for example \"self.str_to_hit\". Modifiers for the check add too."),
            new("skill", new StatKind(false), false, "The skill this check uses. A successful use marks it for a ruleset's improvement procedure."),
            new("target", Shared.CombatNumber, true, "The number the total is compared with."),
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
        "An item type: weapons, armour and gear. Modifiers apply while equipped; an optional use describes a carried consumable.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("kind", new TextKind(), true, "What sort of item it is, in the ruleset's own words, for example \"weapon\" or \"armour\". Uses with from_item match it."),
            new("cost", new NumberKind(), true, "Price, at least 0, in the currency named by \"currency\". Shops charge this and buy back at the economy's sell_fraction of it."),
            new("currency", new ReferenceKind("currency"), true, "The currency in which this item's cost is paid."),
            new("weight", new NumberKind(), true, "Weight, in the ruleset's unit."),
            new("parameters", new MapKind(new TextKind(), Shared.CombatNumber), false, "Values the item gives actions used with it (uses with from_item), for example { \"damage\": \"1d8\" }. May read target, for example to deal more against large creatures."),
            new("modifiers", new ListKind(Shared.Modifier), false, "Modifiers while equipped, for example armour lowering \"ac\"."),
            new("use", new ObjectKind(
            [
                new("operations", new ListKind(new OperationKind(Roots.Self | Roots.Campaign | Roots.Area, ["damage", "heal", "apply_condition", "remove_condition"])), true,
                    "Existing track and condition operations performed on the selected party member; damage and heal need an explicit track."),
                new("duration_days", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Campaign | Roots.Area), false,
                    "Optional nonnegative fictional campaign-day expression for conditions applied by this item; evaluated once on use, with time advancing only through authored rest or training."),
            ]), false,
                "A carried consumable use. One inventory copy is consumed for one selected member; campaign use does not support combat rounds or transient condition values."),
        ],
        """
        { "type": "item", "id": "healing_draught", "name": "Healing draught", "kind": "consumable", "cost": 5, "currency": "gold", "weight": 1, "use": { "operations": [ { "op": "heal", "track": "hit_points", "amount": "1d8 + 1" } ] } }
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
            new("effect", Shared.Use, false, "The action casting the spell uses, with its parameters. Without it, the spell can't be cast in combat."),
            new("cost", new MapKind(new ReferenceKind("track"), Shared.SelfNumber), false, "What casting spends from the caster's tracks, for example { \"spells_1\": \"1\" } for a spell slot or { \"power_points\": \"3\" }. A spell is offered only while every track can pay; tracks are restored by rest events."),
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

    public static DefinitionType CharacterCreation { get; } = new(
        "character-creation",
        "How new characters are made: how attribute scores are made (rolled, an arranged array, point buy or boosts), staged profession and personal skill points, the features every character chooses, and starting balances. A ruleset may offer several; one marked default is used when none is named.",
        [
            new("name", new TextKind(), true, "Display name."),
            new("attributes", new ListKind(new StatKind(true)), true, "Every attribute once, in the order rolls are taken and sheets show them."),
            new("method", new EnumKind(["roll", "array", "point-buy", "boosts"]), false, "How attribute scores are made: rolled (the default), a fixed array the player arranges, points spent from a budget, or boosts from a base. Given scores (--attributes) skip roll, array and boosts; point buy checks them."),
            new("default", new BooleanKind(), false, "With more than one character-creation definition, true marks the one used when none is named."),
            new("attribute_roll", Shared.PlainNumber, false, "Method roll: the roll for each attribute, for example \"3d6\" or \"roll_keep(4, 6, 3)\"."),
            new("attribute_rolls", new MapKind(new StatKind(true), Shared.PlainNumber), false, "Method roll: a different roll for some attributes, for example { \"siz\": \"2d6 + 6\" }; the rest use attribute_roll. Rolls are made in attribute order; with assignment arrange, an attribute's own roll stays its own."),
            new("assignment", new EnumKind(["in-order", "arrange"]), false, "Method roll: whether rolls are taken in attribute order (the default) or arranged by the player."),
            new("array", new ListKind(new IntegerKind()), false, "Method array: one score per attribute, which the player arranges by priority, for example [15, 14, 13, 12, 10, 8]."),
            new("base", new IntegerKind(), false, "Methods point-buy and boosts: the score every attribute starts at."),
            new("budget", new IntegerKind(), false, "Method point-buy: the points to spend."),
            new("costs", new ReferenceKind("table"), false, "Method point-buy: a table from a score to its total cost from the base, for example rows [8, 0], [9, 1], ..., [18, 16]."),
            new("boost", new IntegerKind(), false, "Method boosts: how much each boost raises an attribute."),
            new("boosts", Shared.Boosts.Kind, false, "Method boosts: boosts every new character has besides those of its race, creation features and class; usually free ones ({})."),
            new("starting", new MapKind(new ReferenceKind("class"), new MapKind(new ReferenceKind("currency"), Shared.SelfNumber)), false, "Starting balances for each class it names, by currency; a class it doesn't name uses its own \"starting\" map. Without either, characters start with none."),
            new("features", new ListKind(new ObjectKind([Shared.GrantKind, Shared.GrantKinds, Shared.GrantCount])), false, "Features every new character chooses, for example a background and a heritage."),
            new("skill_points", Shared.SkillPoints, false, "Optional second creation step: after attributes and feature choices, spend the profession and personal budgets on the listed derived skills. Profession points are limited by the selected profession's `skills`; personal points may use any listed skill."),
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
}
