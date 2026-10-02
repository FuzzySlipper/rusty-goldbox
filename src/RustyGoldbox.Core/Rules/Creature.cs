using System.Text.Json;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Core.Rules;

/// <summary>
/// A creature expressions can read: a character (class, race, level and
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

    public Definition? Class { get; set; }

    public Definition? Race { get; set; }

    public Definition? Monster { get; set; }

    public int? Level { get; set; }

    public decimal? HitPoints { get; set; }

    public decimal? MaxHitPoints { get; set; }

    /// <summary>Attribute scores and stat values given directly; they replace computed values.</summary>
    public Dictionary<string, decimal> Values { get; } = [];

    public List<Definition> Conditions { get; } = [];

    public List<Definition> Equipment { get; } = [];

    /// <summary>The race, conditions and equipment whose modifiers apply to this creature.</summary>
    public IEnumerable<Definition> ModifierSources()
    {
        if (Race is not null)
        {
            yield return Race;
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

    /// <summary>
    /// Reads a creature description:
    /// <c>{ "monster": "skeleton" }</c> or
    /// <c>{ "class": "fighter", "race": "dwarf", "level": 5, "str": 17, "conditions": [...], "equipment": [...] }</c>.
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
            creature.Monster = Find(rules, DefinitionTypes.Monster, monster, $"{label}.monster", errors);
            if (creature.Monster is not null)
            {
                creature.Class = creature.Monster.Json.TryGetProperty("class", out _) ? rules.Reference(creature.Monster, "$.class") : null;
                creature.Level = creature.Monster.Json.TryGetProperty("level", out JsonElement level) ? level.GetInt32() : null;
            }
        }

        foreach (JsonProperty property in json.EnumerateObject())
        {
            string at = $"{label}.{property.Name}";
            switch (property.Name)
            {
                case "monster":
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
                case "hit_points" or "max_hit_points":
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDecimal(out decimal points))
                    {
                        if (property.Name == "hit_points")
                        {
                            creature.HitPoints = points;
                        }
                        else
                        {
                            creature.MaxHitPoints = points;
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
                case "equipment":
                    ReadList(rules, DefinitionTypes.Item, property.Value, at, creature.Equipment, errors);
                    break;
                default:
                    ReadStat(rules, property, at, creature, errors);
                    break;
            }
        }

        return creature;
    }

    private static void ReadStat(RuleSet rules, JsonProperty property, string at, Creature creature, List<string> errors)
    {
        if (!rules.Stats.TryGetValue(property.Name, out Stat? stat))
        {
            string stats = string.Join(", ", rules.Stats.Keys.Order(StringComparer.Ordinal));
            errors.Add($"{at}: '{property.Name}' is not a stat or creature field. Fields: monster, class, race, level, hit_points, max_hit_points, conditions, equipment. Stats: {stats}.");
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
