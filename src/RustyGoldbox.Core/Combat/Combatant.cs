using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Combat;

/// <summary>An action a combatant can take, with the parameters it is used with.</summary>
public sealed record UseOption(Definition Action, string Name, IReadOnlyDictionary<string, CompiledExpression> Parameters);

/// <summary>A creature in a fight: its side, state for this combat, and the actions it can take.</summary>
public sealed class Combatant(string name, Creature creature, IReadOnlyList<UseOption> uses)
{
    public string Name { get; } = name;

    public Creature Creature { get; } = creature;

    public IReadOnlyList<UseOption> Uses { get; } = uses;

    public int Side { get; set; }

    public bool Defeated { get; set; }

    /// <summary>Rounds left for timed conditions; a condition without an entry lasts until removed.</summary>
    public Dictionary<Definition, decimal> ConditionRounds { get; } = [];

    /// <summary>Timed conditions applied during this creature's own current turn; they start counting at its next turn's end.</summary>
    public HashSet<Definition> AppliedThisTurn { get; } = [];

    public Dictionary<string, int> Budget { get; } = [];

    public decimal SurprisedRounds { get; set; }

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

        foreach ((string id, TrackValue value) in Creature.Tracks)
        {
            renamed.Tracks[id] = new TrackValue { Current = value.Current, Max = value.Max };
        }

        foreach ((string id, decimal value) in Creature.Values)
        {
            renamed.Values[id] = value;
        }

        renamed.Conditions.AddRange(Creature.Conditions);
        renamed.Equipment.AddRange(Creature.Equipment);
        return new Combatant(newName, renamed, Uses);
    }

    /// <summary>
    /// A character as a combatant: its classes' actions in the order the
    /// classes were taken, with item parameters from its equipment. A use
    /// another class already gives (the same action and name) is listed once.
    /// </summary>
    public static Combatant FromCharacter(RuleSet rules, Character character)
    {
        Creature creature = character.ToCreature(character.Name);
        List<UseOption> uses = creature.ClassLevels.Keys
            .SelectMany(characterClass => ReadUses(rules, characterClass, "$.actions", creature.Equipment))
            .DistinctBy(use => (use.Action, use.Name))
            .ToList();
        return new Combatant(character.Name, creature, uses);
    }

    /// <summary>A monster as a combatant: its track maxima rolled, every track at its start.</summary>
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
        return new Combatant(name, creature, ReadUses(rules, monster, "$.actions", []));
    }

    private static List<UseOption> ReadUses(RuleSet rules, Definition owner, string path, IReadOnlyList<Definition> equipment)
    {
        List<UseOption> options = [];
        if (!owner.Json.TryGetProperty(path[2..], out JsonElement uses))
        {
            return options;
        }

        int index = 0;
        foreach (JsonElement use in uses.EnumerateArray())
        {
            string at = $"{path}[{index}]";
            index++;
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
                continue;
            }

            // One option per equipped item of the kind that has every missing parameter.
            foreach (Definition item in equipment.Where(item => item.Json.GetProperty("kind").GetString() == kind.GetString()))
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
                    options.Add(new UseOption(action, itemName, filled));
                }
            }
        }

        return options;
    }
}
