using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>An action a combatant can take, with the parameters it is used with.</summary>
/// <param name="Spell">When the use casts a spell, the spell, whose cost it also spends.</param>
/// <param name="FromItem">The authored item kind that supplied missing parameters, when this use came from equipment.</param>
public sealed record UseOption(
    Definition Action,
    string Name,
    IReadOnlyDictionary<string, CompiledExpression> Parameters,
    Definition? Spell = null,
    string? FromItem = null);

/// <summary>A creature in a fight: its side, state for this combat, and the actions it can take.</summary>
public sealed class Combatant(string name, Creature creature, IReadOnlyList<UseOption> uses, string? id = null)
{
    public string Name { get; } = name;

    /// <summary>
    /// Stable combat identity. It is intentionally separate from <see cref="Name"/>:
    /// encounters can contain repeated display names and a save can restore the
    /// same member after a campaign roster changes.
    /// </summary>
    public string Id { get; internal set; } = id ?? string.Empty;

    public Creature Creature { get; } = creature;

    /// <summary>The persistent character behind this combatant, when it is a party member.</summary>
    public Character? Character { get; init; }

    /// <summary>Whether this member's next turn is supplied by the live caller or the automatic policy.</summary>
    public CombatControlMode Controller { get; set; } = CombatControlMode.Automatic;

    /// <summary>What it can take, in order of preference; the fight adds its combat definition's actions every creature has.</summary>
    public List<UseOption> Uses { get; } = uses.ToList();

    /// <summary>The reactions the creature has, each with the use it reacts with.</summary>
    public List<(Definition Reaction, UseOption Use)> Reactions { get; } = [];

    public int Side { get; set; }

    /// <summary>Out of the fight: felled (by the combat's defeated rule) or escaped.</summary>
    public bool Defeated { get; set; }

    /// <summary>Fled the field: out of the fight, though not felled, and back in it never.</summary>
    public bool Escaped { get; set; }

    /// <summary>Rounds left for timed conditions; a condition without an entry lasts until removed.</summary>
    public Dictionary<Definition, decimal> ConditionRounds { get; } = [];

    /// <summary>Timed conditions applied during this creature's own current turn; they start counting at its next turn's end.</summary>
    public HashSet<Definition> AppliedThisTurn { get; } = [];

    public Dictionary<string, int> Budget { get; } = [];

    public decimal SurprisedRounds { get; set; }

    /// <summary>The spells it casts only from prepared copies.</summary>
    public HashSet<Definition> Preparing { get; } = [];

    /// <summary>The prepared copies it has left; casting a spell in <see cref="Preparing"/> uses one.</summary>
    public List<Definition> Prepared { get; } = [];

    /// <summary>Casts left of spells it has a number of times a day; those cost nothing else.</summary>
    public Dictionary<Definition, int> CastsLeft { get; } = [];

    /// <summary>Whether it has what casting the spell needs besides its cost: a cast left, or a prepared copy if the spell is one it prepares.</summary>
    public bool CanCast(Definition spell)
    {
        return CastsLeft.TryGetValue(spell, out int left) ? left > 0 : !Preparing.Contains(spell) || Prepared.Contains(spell);
    }

    /// <summary>Uses up a cast left or a prepared copy of the spell, where it has them.</summary>
    public void Cast(Definition spell)
    {
        if (CastsLeft.TryGetValue(spell, out int left))
        {
            CastsLeft[spell] = left - 1;
        }
        else if (Preparing.Contains(spell))
        {
            Prepared.Remove(spell);
        }
    }

    /// <summary>The same combatant under another name, before a fight starts.</summary>
    public Combatant Renamed(string newName)
    {
        if (newName == Name)
        {
            return this;
        }

        Creature renamed = new(newName)
        {
            Class = Creature.Class,
            Race = Creature.Race,
            Monster = Creature.Monster,
            Level = Creature.Level,
        };
        foreach ((Definition characterClass, int level) in Creature.ClassLevels)
        {
            renamed.ClassLevels[characterClass] = level;
        }

        renamed.LevelsTaken.AddRange(Creature.LevelsTaken);
        renamed.Features.AddRange(Creature.Features);

        foreach ((string id, TrackValue value) in Creature.Tracks)
        {
            renamed.Tracks[id] = new TrackValue { Current = value.Current, Max = value.Max };
        }

        foreach ((string id, decimal value) in Creature.Values)
        {
            renamed.Values[id] = value;
        }

        foreach ((string id, decimal bonus) in Creature.AdvancementBonuses)
        {
            renamed.AdvancementBonuses[id] = bonus;
        }

        renamed.Conditions.AddRange(Creature.Conditions);
        renamed.Equipment.AddRange(Creature.Equipment);
        Combatant copy = new(newName, renamed, Uses, Id) { Character = Character, Controller = Controller };
        copy.Reactions.AddRange(Reactions);
        copy.Preparing.UnionWith(Preparing);
        copy.Prepared.AddRange(Prepared);
        foreach ((Definition spell, int left) in CastsLeft)
        {
            copy.CastsLeft[spell] = left;
        }
        return copy;
    }

    /// <summary>
    /// A character as a combatant: the spells it knows, then its classes'
    /// actions in the order the classes were taken, then its features' actions, with item parameters
    /// from its equipment. A use already given (the same action and name) is
    /// listed once.
    /// </summary>
    public static Combatant FromCharacter(RuleSet rules, Character character)
    {
        Creature creature = character.ToCreature(character.Name);
        // Known spells come first, so a caster casts while it can pay.
        List<UseOption> spells = character.Spells
            .Where(spell => spell.Json.TryGetProperty("effect", out _))
            .SelectMany(spell => ReadUse(rules, spell, spell.Json.GetProperty("effect"), "$.effect", creature.Equipment)
                .Select(use => use with { Name = spell.Name, Spell = spell }))
            .ToList();
        List<UseOption> uses = spells;
        uses.AddRange(creature.ClassLevels.Keys
            .Concat(creature.Features.Distinct())
            .SelectMany(source => ReadUses(rules, source, "$.actions", creature.Equipment)));
        uses = uses.DistinctBy(use => (use.Action, use.Name)).ToList();
        Combatant combatant = new(character.Name, creature, uses) { Character = character };
        if (character.Npc?.Json.TryGetProperty("control", out JsonElement control) == true
            && control.ValueKind == JsonValueKind.String)
        {
            combatant.Controller = control.GetString() switch
            {
                "automatic" => CombatControlMode.Automatic,
                "manual" => CombatControlMode.Manual,
                _ => combatant.Controller,
            };
        }
        combatant.AddReactions(rules, creature.ClassLevels.Keys.Concat(creature.Features.Distinct()));
        combatant.Preparing.UnionWith(character.Spells.Where(spell => CharacterRules.NeedsPreparing(rules, character, spell)));
        combatant.Prepared.AddRange(CharacterRules.PreparedLeft(rules, character));
        return combatant;
    }

    /// <summary>A monster as a combatant: its track maxima rolled, every track at its start, and its spells before its actions.</summary>
    public static Combatant FromMonster(RuleSet rules, Definition monster, string name, Evaluator evaluator)
    {
        Creature creature = new(name);
        creature.Become(rules, monster);
        if (monster.Json.TryGetProperty("tracks", out JsonElement tracks))
        {
            foreach (JsonProperty entry in tracks.EnumerateObject())
            {
                Definition track = rules.Reference(monster, $"$.tracks.{entry.Name}");
                creature.Track(track.Id).Max = evaluator.Evaluate(rules.Expression(monster, $"$.tracks.{entry.Name}"), creature, null).Number;
            }
        }

        foreach (Definition track in rules.Tracks.Values)
        {
            evaluator.StartTrack(creature, track);
        }
        List<UseOption> uses = [];
        Dictionary<Definition, int> casts = [];
        if (monster.Json.TryGetProperty("spells", out JsonElement spells))
        {
            for (int index = 0; index < spells.GetArrayLength(); index++)
            {
                Definition spell = rules.Reference(monster, $"$.spells[{index}].spell");
                uses.AddRange(ReadUse(rules, spell, spell.Json.GetProperty("effect"), "$.effect", []).Select(use => use with { Name = spell.Name, Spell = spell }));
                if (spells[index].TryGetProperty("per_day", out JsonElement perDay))
                {
                    casts[spell] = perDay.GetInt32();
                }
            }
        }

        uses.AddRange(ReadUses(rules, monster, "$.actions", []));
        Combatant combatant = new(name, creature, uses);
        combatant.AddReactions(rules, [monster]);
        foreach ((Definition spell, int count) in casts)
        {
            combatant.CastsLeft[spell] = count;
        }
        return combatant;
    }

    /// <summary>
    /// Rebuilds a monster's definition-backed actions without starting any
    /// tracks or evaluating a random expression. A continuation restore calls
    /// this source builder, then overwrites every mutable combat value from
    /// its saved <see cref="CombatantState"/>.
    /// </summary>
    internal static Combatant FromMonsterState(RuleSet rules, Definition monster, string name)
    {
        Creature creature = new(name);
        creature.Become(rules, monster);
        List<UseOption> uses = [];
        Dictionary<Definition, int> casts = [];
        if (monster.Json.TryGetProperty("spells", out JsonElement spells))
        {
            for (int index = 0; index < spells.GetArrayLength(); index++)
            {
                Definition spell = rules.Reference(monster, $"$.spells[{index}].spell");
                uses.AddRange(ReadUse(rules, spell, spell.Json.GetProperty("effect"), "$.effect", []).Select(use => use with { Name = spell.Name, Spell = spell }));
                if (spells[index].TryGetProperty("per_day", out JsonElement perDay))
                {
                    casts[spell] = perDay.GetInt32();
                }
            }
        }

        uses.AddRange(ReadUses(rules, monster, "$.actions", []));
        Combatant combatant = new(name, creature, uses);
        combatant.AddReactions(rules, [monster]);
        foreach ((Definition spell, int count) in casts)
        {
            combatant.CastsLeft[spell] = count;
        }

        return combatant;
    }

    /// <summary>
    /// Adds the reactions <paramref name="sources"/> list, once each, with the
    /// use each reacts with (the first its equipment allows, for from_item).
    /// </summary>
    private void AddReactions(RuleSet rules, IEnumerable<Definition> sources)
    {
        foreach (Definition source in sources)
        {
            if (!source.Json.TryGetProperty("reactions", out JsonElement reactions))
            {
                continue;
            }

            for (int index = 0; index < reactions.GetArrayLength(); index++)
            {
                Definition reaction = rules.Reference(source, $"$.reactions[{index}]");
                if (Reactions.Any(entry => entry.Reaction == reaction))
                {
                    continue;
                }

                if (ReadUse(rules, reaction, reaction.Json.GetProperty("use"), "$.use", Creature.Equipment).FirstOrDefault() is UseOption use)
                {
                    Reactions.Add((reaction, use));
                }
            }
        }
    }

    internal static List<UseOption> ReadUses(RuleSet rules, Definition owner, string path, IReadOnlyList<Definition> equipment)
    {
        List<UseOption> options = [];
        if (!owner.Json.TryGetProperty(path[2..], out JsonElement uses))
        {
            return options;
        }

        int index = 0;
        foreach (JsonElement use in uses.EnumerateArray())
        {
            options.AddRange(ReadUse(rules, owner, use, $"{path}[{index}]", equipment));
            index++;
        }

        return options;
    }

    /// <summary>The options one use gives: itself, or with from_item one per equipped item of the kind that fills its parameters.</summary>
    private static List<UseOption> ReadUse(RuleSet rules, Definition owner, JsonElement use, string at, IReadOnlyList<Definition> equipment)
    {
        List<UseOption> options = [];
        Definition action = rules.Reference(owner, $"{at}.action");
        List<string> parameters = action.Json.TryGetProperty("parameters", out JsonElement declared)
            ? declared.EnumerateArray().Select(parameter => parameter.GetString()!).ToList()
            : [];
        Dictionary<string, CompiledExpression> given = [];
        foreach (string parameter in parameters)
        {
            if (rules.TryExpression(owner, $"{at}.{parameter}", out CompiledExpression? expression))
            {
                given[parameter] = expression!;
            }
        }

        string name = use.TryGetProperty("name", out JsonElement named) ? named.GetString()! : action.Name;
        if (!use.TryGetProperty("from_item", out JsonElement kind))
        {
            options.Add(new UseOption(action, name, given));
            return options;
        }

        string itemKind = kind.GetString()!;

        // One option per equipped item of the kind that has every missing parameter.
        foreach (Definition item in equipment.Where(item => item.Json.GetProperty("kind").GetString() == itemKind))
        {
            Dictionary<string, CompiledExpression> filled = new(given);
            foreach (string parameter in parameters.Where(parameter => !given.ContainsKey(parameter)))
            {
                if (rules.TryExpression(item, $"$.parameters.{parameter}", out CompiledExpression? fromItem))
                {
                    filled[parameter] = fromItem!;
                }
            }

            if (filled.Count == parameters.Count)
            {
                string itemName = use.TryGetProperty("name", out _) ? name : $"{action.Name} ({item.Name})";
                options.Add(new UseOption(action, itemName, filled, FromItem: itemKind));
            }
        }

        return options;
    }
}
