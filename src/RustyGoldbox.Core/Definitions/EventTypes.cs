using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Definitions;

/// <summary>
/// The fixed vocabulary of campaign event kinds. An event definition's
/// <c>kind</c> picks one; its other fields are that kind's. Events chain by
/// naming the next event; any choice can be guarded by an expression.
/// </summary>
public static class EventTypes
{
    private static readonly ExpressionKind Guard = new(ExprType.Boolean, Roots.Campaign);
    private static readonly Field Next = new("next", new ReferenceKind("event"), false, "The event that follows; without it, the chain ends and the party can move again.");
    private static readonly Field Picture = new("picture", new ReferenceKind("asset", "picture"), false, "A picture shown with the event, any visual media; it stays until the party moves or another event shows one.");
    private static readonly Field Sound = new("sound", new ReferenceKind("asset", "sound"), false, "Audio played once as the event begins.");
    private static readonly Field Music = new("music", new ReferenceKind("asset", "music"), false, "Audio that loops from this event on, until another event's music replaces it.");

    public static DefinitionType Text { get; } = new(
        "text",
        "Shows text.",
        [new("text", new TextKind(), true, "What the party sees or hears."), Next, Picture, Sound, Music],
        """{ "type": "event", "id": "gate", "kind": "text", "text": "A rusted gate bars the way.", "picture": "crypt-art:gate", "sound": "crypt-art:creak", "next": "gate_choice" }""");

    public static DefinitionType Menu { get; } = new(
        "menu",
        "Asks the party to choose; play waits for a choose command.",
        [
            new("text", new TextKind(), true, "The question."),
            new("options", new ListKind(new ObjectKind(
            [
                new("label", new TextKind(), true, "What the option says."),
                new("when", Guard, false, "Offered only when this is true."),
                new("next", new ReferenceKind("event"), false, "The event if chosen; without it, the chain ends."),
            ])), true, "The options, in order; choose takes the number shown."),
            Picture,
            Sound,
            Music,
        ],
        """{ "type": "event", "id": "gate_choice", "kind": "menu", "text": "Force the gate?", "options": [ { "label": "Force it", "next": "forced" }, { "label": "Leave it" } ] }""");

    public static DefinitionType Combat { get; } = new(
        "combat",
        "Fights an encounter with the ruleset's combat loop. The party keeps the damage it takes.",
        [
            new("encounter", new ReferenceKind("encounter"), true, "The monsters."),
            new("combat", new ReferenceKind("combat"), false, "The combat definition; needed only when the module set has more than one."),
            new("party_start", new ListKind(new IntegerKind(), 2), false, "On a combat field, the party's starting cell [x, y]; members deploy outward from it. Without it, the combat definition chooses the party's edge."),
            new("monsters_start", new ListKind(new IntegerKind(), 2), false, "On a combat field, the monsters' starting cell [x, y]; members deploy outward from it. Without it, the combat definition chooses the monsters' edge."),
            new("surprise", new EnumKind(["party", "monsters"]), false, "The side that starts surprised, instead of rolling the combat definition's surprise; one round unless surprise_rounds says otherwise."),
            new("surprise_rounds", new IntegerKind(), false, "How many rounds the side named by surprise loses; at least 1."),
            new("on_win", new ReferenceKind("event"), false, "The event after a win."),
            new("on_lose", new ReferenceKind("event"), false, "The event after a loss; without it, the adventure ends."),
            new("on_draw", new ReferenceKind("event"), false, "The event when neither side wins within the combat's round limit; without it, the chain ends and play goes on."),
            new("on_flee", new ReferenceKind("event"), false, "The event after either side flees, whether by a flee action, a flee rule or (when flee_on_draw is true) the round limit."),
            new("flee_on_draw", new BooleanKind(), false, "Treat an undecided fight at its round limit as a flee outcome and use on_flee."),
            Picture,
            Sound,
            Music,
        ],
        """{ "type": "event", "id": "guards", "kind": "combat", "encounter": "classic:crypt_guard", "music": "crypt-art:battle", "on_win": "loot" }""");

    public static DefinitionType Set { get; } = new(
        "set",
        "Sets a campaign variable.",
        [
            new("variable", new ReferenceKind("variable"), true, "The variable."),
            new("value", new ExpressionKind(null, Roots.Campaign), true, "Its new value, of the variable's type; may read campaign.var."),
            Next,
        ],
        """{ "type": "event", "id": "forced", "kind": "set", "variable": "gate_open", "value": "true", "next": "through" }""");

    public static DefinitionType Branch { get; } = new(
        "branch",
        "Goes to the first event whose condition holds.",
        [
            new("branches", new ListKind(new ObjectKind(
            [
                new("when", Guard, true, "The condition."),
                new("next", new ReferenceKind("event"), true, "The event when it holds."),
            ])), true, "Conditions in order."),
            new("otherwise", new ReferenceKind("event"), false, "The event when none holds; without it, the chain ends."),
        ],
        """{ "type": "event", "id": "check_gate", "kind": "branch", "branches": [ { "when": "campaign.var.gate_open", "next": "through" } ], "otherwise": "gate" }""");

    public static DefinitionType Teleport { get; } = new(
        "teleport",
        "Moves the party to an entry point, in this area or another. Arriving doesn't run the destination cell's event; only moving into a cell does.",
        [
            new("area", new ReferenceKind("area"), true, "The area."),
            new("entry", new TextKind(), true, "The entry point's name in that area."),
            Next,
        ],
        """{ "type": "event", "id": "stairs", "kind": "teleport", "area": "depths", "entry": "stairs_down" }""");

    public static DefinitionType Treasure { get; } = new(
        "treasure",
        "Gives the party an explicitly named currency and items.",
        [
            new("currency", new ReferenceKind("currency"), false, "The currency found. Required with amount; omit both for item-only treasure."),
            new("amount", new ExpressionKind(ExprType.Number, Roots.Campaign), false, "Currency found, for example \"3d6 * 10\"; shared evenly among the characters, the remainder to the first."),
            new("items", new ListKind(new ReferenceKind("item")), false, "Items found."),
            Next,
        ],
        """{ "type": "event", "id": "loot", "kind": "treasure", "currency": "classic:gold", "amount": "2d6 * 10", "items": ["classic:dagger"] }""");

    public static DefinitionType Experience { get; } = new(
        "experience",
        "Awards experience to the party, as for a quest done or a puzzle solved. Characters gain the levels it reaches that need no choice; the others wait for a level command.",
        [
            new("amount", new ExpressionKind(ExprType.Number, Roots.Campaign), true, "Experience awarded, for example \"500\"; split as fights' awards are (see the advancement's experience_to) unless each is true."),
            new("each", new BooleanKind(), false, "If true, every character gets the whole amount. Without it, the amount is shared."),
            new("text", new TextKind(), false, "What the party sees first."),
            Next,
        ],
        """{ "type": "event", "id": "quest_done", "kind": "experience", "amount": "500", "text": "The abbot thanks you.", "next": "abbey" }""");

    public static DefinitionType Give { get; } = new(
        "give",
        "Gives copies of an item to the party's carried inventory.",
        [new("item", new ReferenceKind("item"), true, "The item."), new("count", new IntegerKind(), false, "How many copies, a positive whole number; defaults to 1."), Next],
        """{ "type": "event", "id": "supplies", "kind": "give", "item": "classic:dagger", "count": 2, "next": "gate" }""");

    public static DefinitionType Take { get; } = new(
        "take",
        "Takes copies of an item, from carried inventory first, then party members' equipment. If there aren't enough, takes nothing and follows on_refused.",
        [
            new("item", new ReferenceKind("item"), true, "The item."),
            new("count", new IntegerKind(), false, "How many copies, a positive whole number; defaults to 1."),
            Next,
            new("on_refused", new ReferenceKind("event"), false, "The event when the party lacks the full count; without it, the chain ends."),
        ],
        """{ "type": "event", "id": "pay_toll", "kind": "take", "item": "classic:dagger", "next": "through", "on_refused": "gate" }""");

    public static DefinitionType Milestone { get; } = new(
        "milestone",
        "Applies a ruleset milestone to one party member or every member. Choices are data, so Fate swaps, raises and new stunts use the same campaign hook.",
        [
            new("text", new TextKind(), true, "What the party sees first."),
            new("member", new IntegerKind(), false, "Party member number; omit to apply the milestone to every member."),
            new("raises", new MapKind(new StatKind(false), new IntegerKind()), false, "Skills to raise; the configured milestone amount is used."),
            new("swaps", new ListKind(new ObjectKind([
                new("from", new StatKind(false), true, "Skill to give up."),
                new("to", new StatKind(false), true, "Skill to receive the rating."),
            ])), false, "Pairs of skills whose ratings are exchanged."),
            new("features", new ListKind(new ReferenceKind("feature")), false, "New features, such as Fate stunts."),
            Next,
            Picture,
            Sound,
            Music,
        ],
        """{ "type": "event", "id": "milestone", "kind": "milestone", "text": "The journey changes you.", "member": 1, "raises": { "craft": 1 }, "features": ["rules:stunt"], "next": "road" }""");

    public static DefinitionType Improve { get; } = new(
        "improve",
        "Runs the ruleset's marked-skill improvement checks for one party member or every member.",
        [
            new("text", new TextKind(), true, "What the party sees first."),
            new("member", new IntegerKind(), false, "Party member number; omit to improve every member."),
            Next,
            Picture,
            Sound,
            Music,
        ],
        """{ "type": "event", "id": "study", "kind": "improve", "text": "You reflect on what you learned.", "next": "road" }""");

    public static DefinitionType Shop { get; } = new(
        "shop",
        "Offers guarded stock at item cost and buys carried items at the ruleset economy's sell_fraction. Waits for buy <n>, sell <n> or leave; stock is unlimited.",
        [
            new("text", new TextKind(), true, "The shopkeeper's greeting."),
            new("items", new ListKind(new ObjectKind(
            [
                new("item", new ReferenceKind("item"), true, "An item for sale, at its cost."),
                new("when", Guard, false, "Offered only when this campaign condition holds."),
            ])), true, "Stock in order; buy takes the number shown. An empty list is a shop that only buys."),
            Next,
            Picture,
            Sound,
            Music,
        ],
        """{ "type": "event", "id": "outfitter", "kind": "shop", "text": "Supplies for the road.", "items": [ { "item": "classic:dagger" }, { "item": "classic:long_sword", "when": "campaign.var.gate_open" } ], "next": "farewell" }""");

    public static DefinitionType Temple { get; } = new(
        "temple",
        "Offers priced services on a chosen party member. Waits for serve <service> <member> or leave.",
        [
            new("text", new TextKind(), true, "The greeting."),
            new("services", new ListKind(new ObjectKind(
            [
                new("label", new TextKind(), true, "The service name."),
                new("cost", new ExpressionKind(ExprType.Number, Roots.Self | Roots.Campaign), true, "Amount charged in the named currency; may read the chosen character as self and campaign variables. Must evaluate to a nonnegative number."),
                new("currency", new ReferenceKind("currency"), true, "Currency charged for this service."),
                new("operations", new ListKind(new OperationKind(Roots.Self | Roots.Campaign, ["heal", "remove_condition"])), true, "Operations on the chosen character; heal requires a track and to, if given, must be self."),
            ])), true, "Services in order, numbered from 1."),
            Next, Picture, Sound, Music,
        ],
        """{ "type": "event", "id": "temple", "kind": "temple", "text": "Welcome.", "services": [{ "label": "Healing", "cost": "10", "currency": "classic:gold", "operations": [{ "op": "heal", "track": "classic:hit_points", "amount": "1d8" }] }] }""");

    public static DefinitionType Training { get; } = new(
        "training",
        "Offers the ruleset advancement's training: a declared currency amount and days for one waiting level. Waits for train <member> with the level command's choices, or leave.",
        [new("text", new TextKind(), true, "The trainer's greeting."), Next, Picture, Sound, Music],
        """{ "type": "event", "id": "trainer", "kind": "training", "text": "Train here.", "next": "farewell" }""");

    public static DefinitionType Join { get; } = new(
        "join",
        "Adds a predefined NPC to the party, within the campaign maximum. An absent NPC rejoins with its existing state; one already present is refused.",
        [new("npc", new ReferenceKind("npc"), true, "The NPC."), Next, new("on_refused", new ReferenceKind("event"), false, "Fallback when the party is full or the NPC already present; without it the chain ends.")],
        """{ "type": "event", "id": "recruit", "kind": "join", "npc": "guide", "next": "gate", "on_refused": "gate" }""");

    public static DefinitionType Dismiss { get; } = new(
        "dismiss",
        "Moves an NPC out of the party, preserving it for later rejoining and saves. The party cannot fall below its campaign minimum.",
        [new("npc", new ReferenceKind("npc"), true, "The NPC to remove; player characters are unaffected."), Next, new("on_refused", new ReferenceKind("event"), false, "Fallback when absent or at the minimum; without it the chain ends.")],
        """{ "type": "event", "id": "dismiss_guide", "kind": "dismiss", "npc": "guide", "next": "gate" }""");

    public static DefinitionType End { get; } = new(
        "end",
        "Ends the adventure.",
        [new("text", new TextKind(), true, "The closing text.")],
        """{ "type": "event", "id": "victory", "kind": "end", "text": "The crypt is quiet at last." }""");

    public static DefinitionType Rest { get; } = new(
        "rest",
        "The party rests: immediate restoration, or timed periods under a resting policy. Named tracks and prepared spells restore only after all periods complete.",
        [
            new("text", new TextKind(), true, "What the party sees."),
            new("tracks", new ListKind(new ReferenceKind("track")), true, "The tracks restored."),
            new("resting", new ReferenceKind("resting"), false, "A ruleset policy for timed recovery. Without it, named tracks restore immediately as before."),
            new("periods", new IntegerKind(), false, "With resting, how many whole periods to attempt; required and positive."),
            new("wandering", new ObjectKind(
            [
                new("when", Guard, true, "Checked once per attempted period; may roll dice. If true, that period grants no recovery, rest stops and the event runs."),
                new("event", new ReferenceKind("event"), true, "The combat event for the wandering encounter, including its outcome chains."),
            ]), false, "A wandering encounter check during timed rest. The attempted period still advances time."),
            new("prepare", new BooleanKind(), false, "If true, characters also prepare their memorised spells again (for classes with prepares_spells). Without it, prepared spells already cast stay spent."),
            Next,
        ],
        """{ "type": "event", "id": "camp", "kind": "rest", "text": "You rest and pray.", "tracks": ["classic:spells_1"], "resting": "classic:natural", "periods": 1 }""");

    public static IReadOnlyList<DefinitionType> All { get; } = [Text, Menu, Combat, Set, Branch, Teleport, Treasure, Rest, Experience, Give, Take, Milestone, Improve, Shop, Temple, Training, Join, Dismiss, End];

    public static DefinitionType? Find(string name) => All.FirstOrDefault(kind => kind.Name == name);
}
