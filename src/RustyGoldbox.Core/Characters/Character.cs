using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Characters;

/// <summary>A module the character was made under, recorded so it isn't loaded under a different set.</summary>
public sealed record ModuleStamp(string Id, ModuleVersion Version);

/// <summary>One character level: the class it was taken in, and what the level track gained.</summary>
public sealed record LevelTaken(Definition Class, decimal Gain);

/// <summary>A player character: its choices, scores and progress under a rule set.</summary>
public sealed class Character
{
    public required string Name { get; set; }

    public required IReadOnlyList<ModuleStamp> Modules { get; set; }

    public required Definition Race { get; set; }

    /// <summary>Every level the character has, first to last; a new character has one.</summary>
    public List<LevelTaken> Levels { get; } = [];

    /// <summary>The class of the character's first level.</summary>
    public Definition Class => Levels[0].Class;

    /// <summary>The character's total level over all its classes.</summary>
    public int Level => Levels.Count;

    /// <summary>The character's level in each of its classes, in the order it took them.</summary>
    public Dictionary<Definition, int> ClassLevels()
    {
        Dictionary<Definition, int> levels = [];
        foreach (LevelTaken taken in Levels)
        {
            levels[taken.Class] = levels.GetValueOrDefault(taken.Class) + 1;
        }

        return levels;
    }

    /// <summary>The class of the latest level: where the next level goes unless the player picks another.</summary>
    public Definition LatestClass => Levels[^1].Class;

    /// <summary>The classes and levels as people write them: "Fighter 3 / Thief 2".</summary>
    public string ClassText => string.Join(" / ", ClassLevels().Select(entry => $"{entry.Key.Name} {entry.Value}"));

    public decimal Experience { get; set; }

    /// <summary>Attribute scores in the ruleset's attribute order, after racial adjustments.</summary>
    public Dictionary<string, decimal> Attributes { get; } = [];

    /// <summary>
    /// Track values by track ID: every track's current value, and the maximum
    /// for the track built from level gains.
    /// </summary>
    public Dictionary<string, TrackValue> Tracks { get; } = [];

    public decimal Gold { get; set; }

    public List<Definition> Equipment { get; } = [];

    public List<Definition> Conditions { get; } = [];

    /// <summary>The portrait asset the character is shown with, when one was chosen.</summary>
    public Definition? Portrait { get; set; }

    /// <summary>The character as a creature expressions can read.</summary>
    public Creature ToCreature(string label = "self")
    {
        Creature creature = new(label) { Class = Class, Race = Race, Level = Level };
        foreach ((Definition characterClass, int level) in ClassLevels())
        {
            creature.ClassLevels[characterClass] = level;
        }

        foreach ((string id, TrackValue value) in Tracks)
        {
            creature.Tracks[id] = new TrackValue { Current = value.Current, Max = value.Max };
        }

        foreach ((string id, decimal score) in Attributes)
        {
            creature.Values[id] = score;
        }

        creature.Equipment.AddRange(Equipment);
        creature.Conditions.AddRange(Conditions);
        return creature;
    }

    /// <summary>
    /// The experience needed for the next level, or null when there is none:
    /// by character level under an advancement by character, otherwise from
    /// the class's own table.
    /// </summary>
    public decimal? NextLevelExperience(RuleSet rules)
    {
        System.Text.Json.JsonElement levels = rules.ExperienceByCharacter
            ? rules.Advancement!.Json.GetProperty("levels")
            : Class.Json.GetProperty("levels");
        if (Level >= levels.GetArrayLength())
        {
            return null;
        }

        return rules.ExperienceByCharacter ? levels[Level].GetDecimal() : levels[Level].GetProperty("xp").GetDecimal();
    }

    /// <summary>
    /// Whether the character has the experience for its next level but hasn't
    /// taken it, because the class it levels in has no more levels.
    /// </summary>
    public bool LevelWaiting(RuleSet rules) => NextLevelExperience(rules) is decimal needed && Experience >= needed;

    /// <summary>Spells per day by spell level for each class that has spells, at the character's level in it.</summary>
    public IReadOnlyList<(Definition Class, IReadOnlyList<int> Slots)> SpellSlots()
    {
        List<(Definition, IReadOnlyList<int>)> all = [];
        foreach ((Definition characterClass, int level) in ClassLevels())
        {
            if (characterClass.Json.TryGetProperty("spell_slots", out System.Text.Json.JsonElement slots))
            {
                all.Add((characterClass, slots[level - 1].EnumerateArray().Select(slot => slot.GetInt32()).ToList()));
            }
        }

        return all;
    }

    public static IReadOnlyList<ModuleStamp> StampsOf(ModuleSet set)
    {
        return set.LoadOrder.Select(loaded => new ModuleStamp(loaded.Manifest.Id, loaded.Manifest.Version)).ToList();
    }
}
