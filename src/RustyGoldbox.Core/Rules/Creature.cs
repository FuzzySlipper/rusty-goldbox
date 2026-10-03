using System.Text.Json;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Core.Rules;

/// <summary>A creature's value on one track; <see cref="Max"/> is its own maximum when the track doesn't compute one.</summary>
public sealed class TrackValue
{
    public decimal? Current { get; set; }

    public decimal? Max { get; set; }
}

/// <summary>
/// A creature expressions can read: a character (classes, race, level and
/// attributes) or a monster, plus any conditions and equipment it has.
/// </summary>
public sealed class Creature
{
    public Creature(string label)
    {
        Label = label;
    }

    /// <summary>How the creature is named in messages: "self" or "target".</summary>
    public string Label { get; }

    /// <summary>What expressions read as class: a character's first class, or a monster's.</summary>
    public Definition? Class { get; set; }

    public Definition? Race { get; set; }

    public Definition? Monster { get; set; }

    /// <summary>What expressions read as level: a character's total over its classes, or a monster's.</summary>
    public int? Level { get; set; }

    /// <summary>The creature's level in each of its classes, in the order taken; each class's modifiers apply at that level.</summary>
    public Dictionary<Definition, int> ClassLevels { get; } = [];

    /// <summary>Where the creature stands on the combat field, in a fight that has one.</summary>
    public Combat.Cell? Position { get; set; }

    /// <summary>How many classes a character advances in (self.classes); without a value, its number of classes.</summary>
    public int? AdvancingClasses { get; set; }

    /// <summary>The highest level of a class the character left by changing class (self.former_level), or 0.</summary>
    public int FormerLevel { get; set; }

    /// <summary>
    /// A character's levels as (class, level in that class): each one's
    /// hp_bonus adds to the level track's own maximum, as the creature is now.
    /// Monsters bring their maximum whole, so they have none.
    /// </summary>
    public List<(Definition Class, int ClassLevel)> LevelsTaken { get; } = [];

    /// <summary>Track values by track ID.</summary>
    public Dictionary<string, TrackValue> Tracks { get; } = [];

    public TrackValue Track(string id)
    {
        if (!Tracks.TryGetValue(id, out TrackValue? value))
        {
            value = new TrackValue();
            Tracks[id] = value;
        }

        return value;
    }

    /// <summary>Attribute scores and stat values given directly; they replace computed values.</summary>
    public Dictionary<string, decimal> Values { get; } = [];

    /// <summary>Persistent character advancement bonuses added after authored modifiers.</summary>
    public Dictionary<string, decimal> AdvancementBonuses { get; } = [];

    public List<Definition> Conditions { get; } = [];

    /// <summary>Values conditions were applied with, by condition; a condition without an entry has its defaults.</summary>
    public Dictionary<Definition, Dictionary<string, decimal>> ConditionValues { get; } = [];

    /// <summary>How many times the creature has rolled each check (by ID) since its latest turn began, read as self.rolled.&lt;check&gt;.</summary>
    public Dictionary<string, int> Rolled { get; } = [];

    /// <summary>A character's chosen features, repeats included.</summary>
    public List<Definition> Features { get; } = [];

    public List<Definition> Equipment { get; } = [];

    /// <summary>The race, classes, features, conditions and equipment whose modifiers apply to this creature.</summary>
    public IEnumerable<Definition> ModifierSources()
    {
        if (Race is not null)
        {
            yield return Race;
        }

        foreach (Definition characterClass in ClassLevels.Keys)
        {
            yield return characterClass;
        }

        foreach (Definition feature in Features)
        {
            yield return feature;
        }

        foreach (Definition condition in Conditions)
        {
            yield return condition;
        }

        foreach (Definition item in Equipment)
        {
            yield return item;
        }
    }

    /// <summary>Makes this creature the monster: its class and level, if it names them, count as one class level.</summary>
    public void Become(RuleSet rules, Definition monster)
    {
        Monster = monster;
        Class = monster.Json.TryGetProperty("class", out _) ? rules.Reference(monster, "$.class") : null;
        Level = monster.Json.TryGetProperty("level", out JsonElement level) ? level.GetInt32() : null;
        ClassLevels.Clear();
        if (Class is not null && Level is int classLevel)
        {
            ClassLevels[Class] = classLevel;
        }
    }

    /// <summary>
    /// Reads a creature description:
    /// <c>{ "monster": "skeleton" }</c>,
    /// <c>{ "class": "fighter", "race": "dwarf", "level": 5, "str": 17, "conditions": [...], "equipment": [...] }</c> or
    /// <c>{ "classes": { "fighter": 3, "thief": 2 }, ... }</c> for several classes.
    /// Any other key is a stat value. Problems are added to <paramref name="errors"/>.
    /// </summary>
    public static Creature Read(JsonElement json, string label, RuleSet rules, List<string> errors)
    {
        Creature creature = new(label);
        if (json.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{label} must be a JSON object like {{ \"class\": \"fighter\", \"level\": 5 }}.");
            return creature;
        }

        if (json.TryGetProperty("monster", out JsonElement monster))
        {
            if (Find(rules, DefinitionTypes.Monster, monster, $"{label}.monster", errors) is Definition found)
            {
                creature.Become(rules, found);
            }
        }

        if (json.TryGetProperty("classes", out _) && (json.TryGetProperty("class", out _) || json.TryGetProperty("level", out _)))
        {
            errors.Add($"{label} gives \"classes\" and \"class\" or \"level\"; use one. \"classes\" sets the class (the first) and the level (the total).");
            return creature;
        }

        foreach (JsonProperty property in json.EnumerateObject())
        {
            string at = $"{label}.{property.Name}";
            switch (property.Name)
            {
                case "monster":
                    break;
                case "classes":
                    ReadClasses(rules, property.Value, at, creature, errors);
                    break;
                case "class":
                    creature.Class = Find(rules, DefinitionTypes.Class, property.Value, at, errors) ?? creature.Class;
                    break;
                case "race":
                    creature.Race = Find(rules, DefinitionTypes.Race, property.Value, at, errors);
                    break;
                case "level":
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int level))
                    {
                        creature.Level = level;
                    }
                    else
                    {
                        errors.Add($"{at} must be a whole number.");
                    }

                    break;
                case var name when rules.TryTrack(name, out Definition? track, out bool maximum):
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDecimal(out decimal points))
                    {
                        if (maximum)
                        {
                            creature.Track(track!.Id).Max = points;
                        }
                        else
                        {
                            creature.Track(track!.Id).Current = points;
                        }
                    }
                    else
                    {
                        errors.Add($"{at} must be a number.");
                    }

                    break;
                case "conditions":
                    ReadList(rules, DefinitionTypes.Condition, property.Value, at, creature.Conditions, errors);
                    break;
                case "features":
                    ReadList(rules, DefinitionTypes.Feature, property.Value, at, creature.Features, errors);
                    break;
                case "equipment":
                    ReadList(rules, DefinitionTypes.Item, property.Value, at, creature.Equipment, errors);
                    break;
                default:
                    ReadStat(rules, property, at, creature, errors);
                    break;
            }
        }

        if (!json.TryGetProperty("classes", out _))
        {
            creature.ClassLevels.Clear();
            if (creature.Class is not null && creature.Level is int classLevel)
            {
                creature.ClassLevels[creature.Class] = classLevel;
            }
        }

        return creature;
    }

    private static void ReadClasses(RuleSet rules, JsonElement classes, string at, Creature creature, List<string> errors)
    {
        if (classes.ValueKind != JsonValueKind.Object || !classes.EnumerateObject().Any())
        {
            errors.Add($"{at} must be an object of class levels, like {{ \"fighter\": 3, \"thief\": 2 }}.");
            return;
        }

        creature.ClassLevels.Clear();
        foreach (JsonProperty entry in classes.EnumerateObject())
        {
            Definition? characterClass = rules.Find(DefinitionTypes.Class, entry.Name, out string? problem);
            if (characterClass is null)
            {
                errors.Add($"{at}.{entry.Name}: {problem}");
            }
            else if (entry.Value.ValueKind != JsonValueKind.Number || !entry.Value.TryGetInt32(out int level) || level < 1)
            {
                errors.Add($"{at}.{entry.Name} must be a level, a whole number 1 or more.");
            }
            else
            {
                creature.ClassLevels[characterClass] = level;
            }
        }

        creature.Class = creature.ClassLevels.Keys.FirstOrDefault();
        creature.Level = creature.ClassLevels.Count > 0 ? creature.ClassLevels.Values.Sum() : null;
    }

    private static void ReadStat(RuleSet rules, JsonProperty property, string at, Creature creature, List<string> errors)
    {
        if (!rules.Stats.TryGetValue(property.Name, out Stat? stat))
        {
            string stats = string.Join(", ", rules.Stats.Keys.Order(StringComparer.Ordinal));
            string tracks = string.Join(", ", rules.Tracks.Keys.SelectMany(id => new[] { id, $"max_{id}" }));
            errors.Add($"{at}: '{property.Name}' is not a stat, track or creature field. Fields: monster, class, classes, race, level, features, conditions, equipment. Tracks: {tracks}. Stats: {stats}.");
        }
        else if (stat.Type != Expressions.ExprType.Number)
        {
            errors.Add($"{at}: stat '{property.Name}' is {Expressions.ExprTypes.Name(stat.Type)}; only number stats can be given values.");
        }
        else if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDecimal(out decimal value))
        {
            errors.Add($"{at} must be a number no larger than {decimal.MaxValue}.");
        }
        else
        {
            creature.Values[property.Name] = value;
        }
    }

    private static void ReadList(RuleSet rules, DefinitionType type, JsonElement value, string at, List<Definition> into, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{at} must be an array of {type.Name} IDs.");
            return;
        }

        int index = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            Definition? found = Find(rules, type, item, $"{at}[{index}]", errors);
            if (found is not null)
            {
                into.Add(found);
            }

            index++;
        }
    }

    private static Definition? Find(RuleSet rules, DefinitionType type, JsonElement value, string at, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{at} must be a {type.Name} ID.");
            return null;
        }

        Definition? found = rules.Find(type, value.GetString()!, out string? problem);
        if (found is null)
        {
            errors.Add($"{at}: {problem}");
        }

        return found;
    }
}
