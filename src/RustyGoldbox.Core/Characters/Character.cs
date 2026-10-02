using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Characters;

/// <summary>A module the character was made under, recorded so it isn't loaded under a different set.</summary>
public sealed record ModuleStamp(string Id, ModuleVersion Version);

/// <summary>A player character: its choices, scores and progress under a rule set.</summary>
public sealed class Character
{
    public required string Name { get; set; }

    public required IReadOnlyList<ModuleStamp> Modules { get; set; }

    public required Definition Race { get; set; }

    public required Definition Class { get; set; }

    public int Level { get; set; } = 1;

    public decimal Experience { get; set; }

    /// <summary>Attribute scores in the ruleset's attribute order, after racial adjustments.</summary>
    public Dictionary<string, decimal> Attributes { get; } = [];

    /// <summary>
    /// Track values by track ID: every track's current value, and the maximum
    /// for the track built from level gains.
    /// </summary>
    public Dictionary<string, TrackValue> Tracks { get; } = [];

    /// <summary>What the level track gained at each level, starting with level 1.</summary>
    public List<decimal> LevelGains { get; } = [];

    public decimal Gold { get; set; }

    public List<Definition> Equipment { get; } = [];

    public List<Definition> Conditions { get; } = [];

    /// <summary>The portrait asset the character is shown with, when one was chosen.</summary>
    public Definition? Portrait { get; set; }

    /// <summary>The character as a creature expressions can read.</summary>
    public Creature ToCreature(string label = "self")
    {
        Creature creature = new(label) { Class = Class, Race = Race, Level = Level };
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

    /// <summary>The XP needed for the next level, or null at the class's last level.</summary>
    public decimal? NextLevelExperience()
    {
        System.Text.Json.JsonElement levels = Class.Json.GetProperty("levels");
        return Level < levels.GetArrayLength() ? levels[Level].GetProperty("xp").GetDecimal() : null;
    }

    /// <summary>Spells per day at the current level by spell level, or empty for classes without spells.</summary>
    public IReadOnlyList<int> SpellSlots()
    {
        if (!Class.Json.TryGetProperty("spell_slots", out System.Text.Json.JsonElement slots))
        {
            return [];
        }

        return slots[Level - 1].EnumerateArray().Select(slot => slot.GetInt32()).ToList();
    }

    public static IReadOnlyList<ModuleStamp> StampsOf(ModuleSet set)
    {
        return set.LoadOrder.Select(loaded => new ModuleStamp(loaded.Manifest.Id, loaded.Manifest.Version)).ToList();
    }
}
