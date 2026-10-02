using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Characters;

/// <summary>A module the character was made under, recorded so it isn't loaded under a different set.</summary>
public sealed record ModuleStamp(string Id, ModuleVersion Version);

/// <summary>
/// One character level: the class it was taken in, what the level track
/// gained then from the level's hp (its hp_bonus, if any, is added as the
/// character is now), and the features and boosts chosen at it.
/// </summary>
public sealed record LevelTaken(Definition Class, decimal Gain, IReadOnlyList<Definition> Features)
{
    /// <summary>The attributes boosted at this level, in the order chosen (already in the character's scores).</summary>
    public IReadOnlyList<string> Boosts { get; init; } = [];
}

/// <summary>A player character: its choices, scores and progress under a rule set.</summary>
public sealed class Character
{
    public required string Name { get; set; }

    public required IReadOnlyList<ModuleStamp> Modules { get; set; }

    public required Definition Race { get; set; }

    /// <summary>The character-creation definition the character was made with; its grants are the first level's choices.</summary>
    public required Definition Creation { get; set; }

    /// <summary>Every level the character has, first to last; a new character has one.</summary>
    public List<LevelTaken> Levels { get; } = [];

    /// <summary>The class of the character's first level.</summary>
    public Definition Class => Levels[0].Class;

    /// <summary>The character's total level over all its classes.</summary>
    public int Level => Levels.Count;

    /// <summary>With experience split between classes: each class's own experience.</summary>
    public Dictionary<Definition, decimal> ClassExperience { get; } = [];

    /// <summary>Classes the character left by changing class (dual-classing); they no longer advance.</summary>
    public List<Definition> LeftClasses { get; } = [];

    /// <summary>The classes the character still advances in, in the order taken.</summary>
    public List<Definition> AdvancingClasses() => ClassLevels().Keys.Where(characterClass => !LeftClasses.Contains(characterClass)).ToList();

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

    /// <summary>Every feature the character has chosen, in the order chosen; a repeatable one may appear more than once.</summary>
    public IEnumerable<Definition> Features => Levels.SelectMany(level => level.Features);

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

    /// <summary>The spells the character knows or has prepared, cast in combat while it can pay for them.</summary>
    public List<Definition> Spells { get; } = [];

    public List<Definition> Conditions { get; } = [];

    /// <summary>The portrait asset the character is shown with, when one was chosen.</summary>
    public Definition? Portrait { get; set; }

    /// <summary>The character as a creature expressions can read.</summary>
    public Creature ToCreature(string label = "self")
    {
        Creature creature = new(label) { Class = Class, Race = Race, Level = Level };
        Dictionary<Definition, int> reached = [];
        foreach (LevelTaken taken in Levels)
        {
            reached[taken.Class] = reached.GetValueOrDefault(taken.Class) + 1;
            creature.LevelsTaken.Add((taken.Class, reached[taken.Class]));
        }

        // A class left by changing class waits until the new classes pass its level.
        int advancing = reached.Where(entry => !LeftClasses.Contains(entry.Key)).Select(entry => entry.Value).DefaultIfEmpty(0).Max();
        foreach ((Definition characterClass, int level) in reached)
        {
            if (!LeftClasses.Contains(characterClass) || level < advancing)
            {
                creature.ClassLevels[characterClass] = level;
            }
        }

        creature.Class = creature.ClassLevels.Keys.FirstOrDefault() ?? Class;
        creature.AdvancingClasses = reached.Keys.Count(characterClass => !LeftClasses.Contains(characterClass));
        creature.FormerLevel = LeftClasses.Select(characterClass => reached.GetValueOrDefault(characterClass)).DefaultIfEmpty(0).Max();

        foreach ((string id, TrackValue value) in Tracks)
        {
            creature.Tracks[id] = new TrackValue { Current = value.Current, Max = value.Max };
        }

        foreach ((string id, decimal score) in Attributes)
        {
            creature.Values[id] = score;
        }

        creature.Features.AddRange(Features);
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
        if (rules.ExperienceSplit)
        {
            return null;
        }

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

    /// <summary>With experience split between classes: each advancing class's experience and what its next level needs (null at its last).</summary>
    public IReadOnlyList<(Definition Class, decimal Experience, decimal? Next)> ClassProgress()
    {
        Dictionary<Definition, int> levels = ClassLevels();
        return AdvancingClasses().Select(characterClass =>
        {
            System.Text.Json.JsonElement table = characterClass.Json.GetProperty("levels");
            int level = levels[characterClass];
            decimal? next = level < table.GetArrayLength() && table[level].TryGetProperty("xp", out System.Text.Json.JsonElement xp) ? xp.GetDecimal() : null;
            return (characterClass, ClassExperience.GetValueOrDefault(characterClass), next);
        }).ToList();
    }

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
