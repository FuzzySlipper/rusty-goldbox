using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Combat;
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
public sealed record LevelTaken(Definition? Class, decimal Gain, IReadOnlyList<Definition> Features)
{
    /// <summary>The attributes boosted at this level, in the order chosen (already in the character's scores).</summary>
    public IReadOnlyList<string> Boosts { get; init; } = [];
}

/// <summary>A recorded 2D6 or existing-check result during a career term.</summary>
public sealed record LifepathRoll(string Kind, decimal Roll, decimal Modifier, decimal Total, decimal Target, bool Success);

/// <summary>
/// One immutable entry in a character's prior-history ledger. The choices and
/// results are kept so a saved character can explain how its current skills,
/// characteristics, gear and money were gained.
/// </summary>
public sealed record LifepathTerm(
    string Career,
    int Number,
    int AgeBefore,
    int AgeAfter,
    int RankBefore,
    int RankAfter,
    LifepathRoll? Qualification,
    LifepathRoll? Survival,
    LifepathRoll? Commission,
    LifepathRoll? Advancement,
    LifepathRoll? Aging,
    bool Ended,
    bool BenefitsLost,
    IReadOnlyList<string> Choices,
    IReadOnlyList<string> Results);

/// <summary>
/// The module-authored perception mode a character resolved for an
/// expedition.  Both fields are opaque campaign data: Core stores them so a
/// member keeps the same view across rooms and saves, but it does not name or
/// interpret any particular mode.
/// </summary>
public sealed record PerceptionState(string Scope, string Mode);

/// <summary>A player character: its choices, scores and progress under a rule set.</summary>
public sealed class Character
{
    public required string Name { get; set; }

    public required IReadOnlyList<ModuleStamp> Modules { get; set; }

    public required Definition? Race { get; set; }

    /// <summary>The character-creation definition the character was made with; its grants are the first level's choices.</summary>
    public required Definition Creation { get; set; }

    /// <summary>The optional ruleset-owned term-by-term career procedure used to make this character.</summary>
    public Definition? Lifepath { get; set; }

    /// <summary>Age in years after the recorded career terms; zero means the character has no lifepath.</summary>
    public int Age { get; set; }

    /// <summary>Career terms in order, including a failed term that ended prior history.</summary>
    public List<LifepathTerm> CareerTerms { get; } = [];

    /// <summary>Whether prior history has ended and no further career terms may be taken.</summary>
    public bool LifepathEnded { get; set; }

    /// <summary>Every level the character has, first to last; a new character has one.</summary>
    public List<LevelTaken> Levels { get; } = [];

    /// <summary>The class of the character's first level.</summary>
    public Definition? Class => Levels[0].Class;

    /// <summary>The character's total level over all its classes.</summary>
    public int Level => Levels.Count;

    /// <summary>With experience split between classes: each class's own experience.</summary>
    public Dictionary<Definition, decimal> ClassExperience { get; } = [];

    /// <summary>Classes the character left by changing class (dual-classing); they no longer advance.</summary>
    public List<Definition> LeftClasses { get; } = [];

    /// <summary>The classes the character still advances in, in the order taken.</summary>
    public List<Definition> AdvancingClasses() => ClassLevels().Keys.Where(characterClass => !LeftClasses.Contains(characterClass)).ToList();

    /// <summary>
    /// Whether a class left by changing class still waits for the new classes
    /// to pass its level (its abilities work only if the character calls on
    /// them, <see cref="UsesFormerClasses"/>).
    /// </summary>
    public bool HasDormantClasses()
    {
        Dictionary<Definition, int> levels = ClassLevels();
        int advancing = levels.Where(entry => !LeftClasses.Contains(entry.Key)).Select(entry => entry.Value).DefaultIfEmpty(0).Max();
        return levels.Any(entry => LeftClasses.Contains(entry.Key) && entry.Value >= advancing);
    }

    /// <summary>Whether the character calls on its dormant classes' abilities anyway, at the cost of its experience (see <see cref="ForfeitsExperience"/>).</summary>
    public bool UsesFormerClasses { get; set; }

    /// <summary>Whether the character has called on a dormant class this adventure, so it earns no experience until the adventure ends.</summary>
    public bool ForfeitsExperience { get; set; }

    /// <summary>
    /// The player's preferred controller for this character's next combat.
    /// Null keeps the campaign or host default; the active combatant's
    /// controller remains the authority for the current fight.
    /// </summary>
    public CombatControlMode? CombatControlPreference { get; set; }

    /// <summary>The character's level in each of its classes, in the order it took them.</summary>
    public Dictionary<Definition, int> ClassLevels()
    {
        Dictionary<Definition, int> levels = [];
        foreach (LevelTaken taken in Levels)
        {
            if (taken.Class is Definition characterClass)
            {
                levels[characterClass] = levels.GetValueOrDefault(characterClass) + 1;
            }
        }

        return levels;
    }

    /// <summary>Every feature the character has chosen, in the order chosen; a repeatable one may appear more than once.</summary>
    public IEnumerable<Definition> Features => Levels.SelectMany(level => level.Features).Concat(MilestoneFeatures);

    /// <summary>The class of the latest level: where the next level goes unless the player picks another.</summary>
    public Definition? LatestClass => Levels[^1].Class;

    /// <summary>The classes and levels as people write them: "Fighter 3 / Thief 2".</summary>
    public string ClassText => string.Join(" / ", ClassLevels().Select(entry => $"{entry.Key.Name} {entry.Value}"));

    public decimal Experience { get; set; }

    /// <summary>Attribute scores in the ruleset's attribute order, after racial adjustments.</summary>
    public Dictionary<string, decimal> Attributes { get; } = [];

    /// <summary>Persistent bonuses added by staged creation, milestones or improvement checks.</summary>
    public Dictionary<string, decimal> StatBonuses { get; } = [];

    /// <summary>Profession and personal points already committed during staged creation.</summary>
    public Dictionary<string, SkillAllocation> SkillAllocations { get; } = [];

    /// <summary>The budgets, base values and profession eligibility rolled before staged skill choices.</summary>
    public SkillPointOptions? SkillPointData { get; set; }

    /// <summary>Whether the staged skill choices have been committed.</summary>
    public bool SkillPointsCommitted { get; set; }

    /// <summary>Whether this character still needs to commit its staged skill choices.</summary>
    public bool HasPendingSkillPoints => SkillPointData is not null && !SkillPointsCommitted;

    /// <summary>Successful uses waiting for an improvement check, by skill ID.</summary>
    public Dictionary<string, int> SkillMarks { get; } = [];

    /// <summary>Features granted outside a class level, such as a milestone stunt.</summary>
    public List<Definition> MilestoneFeatures { get; } = [];

    /// <summary>
    /// Track values by track ID: every track's current value, and the maximum
    /// for the track built from level gains.
    /// </summary>
    public Dictionary<string, TrackValue> Tracks { get; } = [];

    /// <summary>Money carried by currency ID. A ruleset with no currencies leaves this empty.</summary>
    public Dictionary<string, decimal> Balances { get; } = [];

    public List<Definition> Equipment { get; } = [];

    /// <summary>The spells the character knows, cast in combat while it can pay for them (and, for a class that prepares spells, has a copy prepared).</summary>
    public List<Definition> Spells { get; } = [];

    /// <summary>
    /// For classes that prepare spells, the copies the character memorises
    /// each day, one entry a casting; empty means its known spells in order,
    /// as many as its tracks pay for (<see cref="CharacterRules.MemorisedPlan"/>).
    /// </summary>
    public List<Definition> Memorised { get; } = [];

    /// <summary>The prepared copies not yet cast since the last rest that prepared spells; null when all of them are left.</summary>
    public List<Definition>? Prepared { get; set; }

    public List<Definition> Conditions { get; } = [];

    public Definition? Npc { get; set; }

    /// <summary>The current expedition perception result, when one was resolved.</summary>
    public PerceptionState? Perception { get; set; }

    internal Character Copy()
    {
        Character copy = new()
        {
            Name = Name, Modules = Modules, Race = Race, Creation = Creation,
            Lifepath = Lifepath, Age = Age, LifepathEnded = LifepathEnded,
            Experience = Experience, Portrait = Portrait, Npc = Npc,
            Perception = Perception,
            UsesFormerClasses = UsesFormerClasses, ForfeitsExperience = ForfeitsExperience,
            CombatControlPreference = CombatControlPreference,
            SkillPointsCommitted = SkillPointsCommitted,
            SkillPointData = SkillPointData is SkillPointOptions options
                ? new SkillPointOptions(options.Profession, options.Personal, options.Skills.ToList())
                : null,
            Prepared = Prepared?.ToList(),
        };
        copy.Levels.AddRange(Levels);
        copy.CareerTerms.AddRange(CareerTerms);
        copy.LeftClasses.AddRange(LeftClasses);
        copy.Equipment.AddRange(Equipment);
        copy.Spells.AddRange(Spells);
        copy.Memorised.AddRange(Memorised);
        copy.Conditions.AddRange(Conditions);
        copy.MilestoneFeatures.AddRange(MilestoneFeatures);
        foreach (var entry in ClassExperience)
        {
            copy.ClassExperience.Add(entry.Key, entry.Value);
        }

        foreach (var entry in Attributes)
        {
            copy.Attributes.Add(entry.Key, entry.Value);
        }

        foreach (var entry in Balances)
        {
            copy.Balances.Add(entry.Key, entry.Value);
        }

        foreach (var entry in StatBonuses)
        {
            copy.StatBonuses.Add(entry.Key, entry.Value);
        }

        foreach (var entry in SkillAllocations)
        {
            copy.SkillAllocations.Add(entry.Key, entry.Value);
        }

        foreach (var entry in SkillMarks)
        {
            copy.SkillMarks.Add(entry.Key, entry.Value);
        }

        foreach (var entry in Tracks)
        {
            copy.Tracks.Add(entry.Key, new TrackValue { Current = entry.Value.Current, Max = entry.Value.Max });
        }

        return copy;
    }

    /// <summary>The portrait asset the character is shown with, when one was chosen.</summary>
    public Definition? Portrait { get; set; }

    /// <summary>The character as a creature expressions can read.</summary>
    public Creature ToCreature(string label = "self")
    {
        Creature creature = new(label) { Class = Class, Race = Race, Level = Level };
        Dictionary<Definition, int> reached = [];
        foreach (LevelTaken taken in Levels.Where(taken => taken.Class is not null))
        {
            reached[taken.Class!] = reached.GetValueOrDefault(taken.Class!) + 1;
            creature.LevelsTaken.Add((taken.Class!, reached[taken.Class!]));
        }

        // A class left by changing class waits until the new classes pass its level.
        int advancing = reached.Where(entry => !LeftClasses.Contains(entry.Key)).Select(entry => entry.Value).DefaultIfEmpty(0).Max();
        foreach ((Definition characterClass, int level) in reached)
        {
            if (!LeftClasses.Contains(characterClass) || level < advancing || UsesFormerClasses)
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

        foreach ((string id, decimal bonus) in StatBonuses)
        {
            creature.AdvancementBonuses[id] = bonus;
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
        if (rules.AdvancementKind != "experience")
        {
            return null;
        }

        if (rules.ExperienceSplit)
        {
            return null;
        }

        // A classless character (in a ruleset without classes) doesn't level by experience.
        if (!rules.ExperienceByCharacter && Class is null)
        {
            return null;
        }

        System.Text.Json.JsonElement levels = rules.ExperienceByCharacter
            ? rules.Advancement!.Json.GetProperty("levels")
            : Class!.Json.GetProperty("levels");
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
