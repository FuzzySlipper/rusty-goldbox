using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Characters;

/// <summary>What a new character should be.</summary>
/// <param name="Attributes">Scores to use instead of rolling; every attribute, before racial adjustments.</param>
/// <param name="Priority">For rulesets that let players arrange rolls: attributes from most to least important; the highest roll goes first.</param>
/// <param name="Creation">The character-creation definition to use when the set has more than one.</param>
public sealed record CreationRequest(
    string Name,
    string Class,
    string Race,
    IReadOnlyDictionary<string, decimal>? Attributes = null,
    IReadOnlyList<string>? Priority = null,
    string? Creation = null);

/// <summary>A level gained: the level reached and the hit points it added.</summary>
public sealed record LevelGain(int Level, decimal Amount);

/// <summary>
/// Creates and advances characters from the rule set's character-creation,
/// race and class definitions. Problems are rule diagnostics, not exceptions.
/// </summary>
public static class CharacterRules
{
    public static Character? Create(RuleSet rules, IReadOnlyList<ModuleStamp> modules, CreationRequest request, DiceRoller dice, List<ModuleDiagnostic> problems)
    {
        Definition? creation = FindCreation(rules, request.Creation, problems);
        Definition? characterClass = Find(rules, DefinitionTypes.Class, request.Class, "class", problems);
        Definition? race = Find(rules, DefinitionTypes.Race, request.Race, "race", problems);
        if (creation is null || characterClass is null || race is null)
        {
            return null;
        }

        List<Definition> attributes = creation.Json.GetProperty("attributes").EnumerateArray()
            .Select(entry => rules.Stats[entry.GetString()!].Definition)
            .ToList();
        Evaluator evaluator = new(rules, dice);
        Dictionary<string, decimal>? scores;
        try
        {
            scores = request.Attributes is null
                ? RollAttributes(rules, creation, attributes, request.Priority, evaluator, problems)
                : GivenAttributes(attributes, request.Attributes, request.Priority, problems);
        }
        catch (RuleFailure failure)
        {
            problems.Add(failure.Diagnostic);
            return null;
        }

        if (scores is null)
        {
            return null;
        }

        Adjust(race, scores);
        CheckRace(rules, race, characterClass, scores, problems);
        CheckClass(characterClass, scores, problems);
        if (problems.Count > 0)
        {
            return null;
        }

        Character character = new() { Name = request.Name, Modules = modules, Race = race, Class = characterClass };
        foreach ((string id, decimal score) in scores)
        {
            character.Attributes[id] = score;
        }

        try
        {
            if (rules.LevelTrack is Definition levelTrack)
            {
                decimal gain = HitPointsForLevel(rules, character, evaluator, 1);
                character.LevelGains.Add(gain);
                character.Tracks[levelTrack.Id] = new TrackValue { Max = gain };
            }

            StartTracks(rules, character, evaluator);
            character.Gold = StartingGold(rules, creation, character, evaluator, problems);
        }
        catch (RuleFailure failure)
        {
            problems.Add(failure.Diagnostic);
        }

        return problems.Count > 0 ? null : character;
    }

    /// <summary>
    /// Adds experience and gains every level it reaches, rolling hit points
    /// for each. On a problem it returns null and the character may be partly
    /// advanced; don't save it.
    /// </summary>
    public static List<LevelGain>? AddExperience(RuleSet rules, Character character, decimal experience, DiceRoller dice, List<ModuleDiagnostic> problems)
    {
        try
        {
            character.Experience = checked(character.Experience + experience);
            Evaluator evaluator = new(rules, dice);
            List<LevelGain> gains = [];
            while (character.NextLevelExperience() is decimal needed && character.Experience >= needed)
            {
                character.Level++;
                decimal gain = HitPointsForLevel(rules, character, evaluator, character.Level);
                character.LevelGains.Add(gain);
                if (rules.LevelTrack is Definition levelTrack)
                {
                    TrackValue value = character.Tracks[levelTrack.Id];
                    value.Max = checked((value.Max ?? 0) + gain);
                    value.Current = checked((value.Current ?? 0) + gain);
                }

                gains.Add(new LevelGain(character.Level, gain));
            }

            return gains;
        }
        catch (Exception exception) when (exception is RuleFailure or OverflowException)
        {
            problems.Add(exception is RuleFailure failure
                ? failure.Diagnostic
                : new ModuleDiagnostic("character.number", "Experience or a track would become too large to be a number."));
            return null;
        }
    }

    /// <summary>Gives every track the character lacks its starting value (normally its maximum).</summary>
    private static void StartTracks(RuleSet rules, Character character, Evaluator evaluator)
    {
        Creature creature = character.ToCreature();
        foreach (Definition track in rules.Tracks.Values)
        {
            string path = track.Json.TryGetProperty("start", out _) ? "$.start" : "$.max";
            Located(track, path, () =>
            {
                evaluator.StartTrack(creature, track);
                return true;
            });
            TrackValue value = character.Tracks.TryGetValue(track.Id, out TrackValue? existing) ? existing : new TrackValue();
            value.Current ??= creature.Track(track.Id).Current;
            character.Tracks[track.Id] = value;
        }
    }

    private static T Located<T>(Definition definition, string path, Func<T> work)
    {
        try
        {
            return work();
        }
        catch (ExpressionException exception)
        {
            throw new RuleFailure(new ModuleDiagnostic("character.evaluate", exception.Message, definition.Module, definition.File, path));
        }
    }

    private static decimal HitPointsForLevel(RuleSet rules, Character character, Evaluator evaluator, int level)
    {
        return Evaluate(rules, evaluator, character.Class, $"$.levels[{level - 1}].hp", character.ToCreature());
    }

    /// <summary>Evaluates a definition's expression; a failure names the definition, file and path.</summary>
    private static decimal Evaluate(RuleSet rules, Evaluator evaluator, Definition definition, string path, Creature? self)
    {
        try
        {
            return evaluator.Evaluate(rules.Expression(definition, path), self, null).Number;
        }
        catch (ExpressionException exception)
        {
            throw new RuleFailure(new ModuleDiagnostic(
                "character.evaluate",
                $"In '{rules.Expression(definition, path).Text}': {exception.Message}",
                definition.Module,
                definition.File,
                path));
        }
    }

    /// <summary>A rule expression that failed while making or advancing a character.</summary>
    private sealed class RuleFailure(ModuleDiagnostic diagnostic) : Exception(diagnostic.Message)
    {
        public ModuleDiagnostic Diagnostic { get; } = diagnostic;
    }

    private static decimal StartingGold(RuleSet rules, Definition creation, Character character, Evaluator evaluator, List<ModuleDiagnostic> problems)
    {
        foreach (JsonProperty entry in creation.Json.GetProperty("starting_gold").EnumerateObject())
        {
            string path = $"$.starting_gold.{entry.Name}";
            if (rules.Reference(creation, path) == character.Class)
            {
                return Evaluate(rules, evaluator, creation, path, character.ToCreature());
            }
        }

        problems.Add(new ModuleDiagnostic(
            "character.gold",
            $"{creation.QualifiedId} has no starting_gold for {character.Class.QualifiedId}. Add \"{character.Class.Id}\" to its starting_gold.",
            creation.Module,
            creation.File,
            "$.starting_gold"));
        return 0;
    }

    private static Dictionary<string, decimal>? RollAttributes(
        RuleSet rules,
        Definition creation,
        List<Definition> attributes,
        IReadOnlyList<string>? priority,
        Evaluator evaluator,
        List<ModuleDiagnostic> problems)
    {
        bool arrange = creation.Json.GetProperty("assignment").GetString() == "arrange";
        if (!arrange && priority is not null)
        {
            problems.Add(new ModuleDiagnostic("character.priority", $"{creation.QualifiedId} assigns rolls in attribute order, so a priority doesn't apply. Leave it out, or give the attribute scores."));
            return null;
        }

        if (priority is not null && !IsPermutation(priority, attributes))
        {
            problems.Add(new ModuleDiagnostic("character.priority", $"The priority must list every attribute once: {string.Join(", ", attributes.Select(attribute => attribute.Id))}."));
            return null;
        }

        List<decimal> rolls = attributes.Select(_ => Evaluate(rules, evaluator, creation, "$.attribute_roll", null)).ToList();
        Dictionary<string, decimal> scores = [];
        if (priority is null)
        {
            for (int i = 0; i < attributes.Count; i++)
            {
                scores[attributes[i].Id] = rolls[i];
            }

            return scores;
        }

        List<decimal> best = rolls.OrderDescending().ToList();
        Dictionary<string, decimal> byPriority = [];
        for (int i = 0; i < priority.Count; i++)
        {
            byPriority[priority[i]] = best[i];
        }

        foreach (Definition attribute in attributes)
        {
            scores[attribute.Id] = byPriority[attribute.Id];
        }

        return scores;
    }

    private static Dictionary<string, decimal>? GivenAttributes(
        List<Definition> attributes,
        IReadOnlyDictionary<string, decimal> given,
        IReadOnlyList<string>? priority,
        List<ModuleDiagnostic> problems)
    {
        if (priority is not null)
        {
            problems.Add(new ModuleDiagnostic("character.priority", "A priority arranges rolled scores; with given attribute scores there is nothing to arrange. Use one or the other."));
            return null;
        }

        Dictionary<string, decimal> scores = [];
        foreach (Definition attribute in attributes)
        {
            if (!given.TryGetValue(attribute.Id, out decimal score))
            {
                problems.Add(new ModuleDiagnostic("character.attributes", $"The attribute scores have no {attribute.Id}. Give every attribute: {string.Join(", ", attributes.Select(entry => entry.Id))}."));
                continue;
            }

            decimal min = attribute.Json.GetProperty("min").GetDecimal();
            decimal max = attribute.Json.GetProperty("max").GetDecimal();
            if (score < min || score > max)
            {
                problems.Add(new ModuleDiagnostic("character.attributes", $"{attribute.Id} {score} is outside {attribute.QualifiedId}'s range {min} to {max}."));
            }

            scores[attribute.Id] = score;
        }

        foreach (string id in given.Keys.Where(id => attributes.All(attribute => attribute.Id != id)))
        {
            problems.Add(new ModuleDiagnostic("character.attributes", $"'{id}' is not an attribute. Attributes: {string.Join(", ", attributes.Select(attribute => attribute.Id))}."));
        }

        return problems.Count > 0 ? null : scores;
    }

    private static void Adjust(Definition race, Dictionary<string, decimal> scores)
    {
        if (!race.Json.TryGetProperty("ability_adjustments", out JsonElement adjustments))
        {
            return;
        }

        foreach (JsonProperty adjustment in adjustments.EnumerateObject())
        {
            scores[adjustment.Name] += adjustment.Value.GetDecimal();
        }
    }

    private static void CheckRace(RuleSet rules, Definition race, Definition characterClass, Dictionary<string, decimal> scores, List<ModuleDiagnostic> problems)
    {
        int index = 0;
        bool allowed = false;
        foreach (JsonElement _ in race.Json.GetProperty("classes").EnumerateArray())
        {
            allowed |= rules.Reference(race, $"$.classes[{index}]") == characterClass;
            index++;
        }

        if (!allowed)
        {
            string classes = string.Join(", ", race.Json.GetProperty("classes").EnumerateArray().Select(entry => entry.GetString()));
            problems.Add(new ModuleDiagnostic("character.race", $"{race.QualifiedId} can't take the class {characterClass.QualifiedId}. Its classes: {classes}.", race.Module, race.File, "$.classes"));
        }

        if (race.Json.TryGetProperty("ability_limits", out JsonElement limits))
        {
            foreach (JsonProperty limit in limits.EnumerateObject())
            {
                decimal min = limit.Value[0].GetDecimal();
                decimal max = limit.Value[1].GetDecimal();
                decimal score = scores[limit.Name];
                if (score < min || score > max)
                {
                    problems.Add(new ModuleDiagnostic(
                        "character.race",
                        $"{race.QualifiedId} needs {limit.Name} from {min} to {max} after adjustment, but it is {score}.",
                        race.Module,
                        race.File,
                        $"$.ability_limits.{limit.Name}"));
                }
            }
        }
    }

    private static void CheckClass(Definition characterClass, Dictionary<string, decimal> scores, List<ModuleDiagnostic> problems)
    {
        if (!characterClass.Json.TryGetProperty("requirements", out JsonElement requirements))
        {
            return;
        }

        foreach (JsonProperty requirement in requirements.EnumerateObject())
        {
            decimal minimum = requirement.Value.GetDecimal();
            if (scores[requirement.Name] < minimum)
            {
                problems.Add(new ModuleDiagnostic(
                    "character.class",
                    $"{characterClass.QualifiedId} needs {requirement.Name} {minimum} or more, but it is {scores[requirement.Name]}.",
                    characterClass.Module,
                    characterClass.File,
                    $"$.requirements.{requirement.Name}"));
            }
        }
    }

    private static Definition? FindCreation(RuleSet rules, string? id, List<ModuleDiagnostic> problems)
    {
        if (id is not null)
        {
            return Find(rules, DefinitionTypes.CharacterCreation, id, "creation", problems);
        }

        List<Definition> all = rules.OfType(DefinitionTypes.CharacterCreation).ToList();
        if (all.Count == 1)
        {
            return all[0];
        }

        string message = all.Count == 0
            ? "The module set has no character-creation definition. Add one (see `goldbox schema character-creation`)."
            : $"The module set has more than one character-creation definition ({string.Join(", ", all.Select(definition => definition.QualifiedId))}); choose one by its ID.";
        problems.Add(new ModuleDiagnostic("character.creation", message));
        return null;
    }

    private static Definition? Find(RuleSet rules, DefinitionType type, string reference, string field, List<ModuleDiagnostic> problems)
    {
        Definition? found = rules.Find(type, reference, out string? problem);
        if (found is null)
        {
            problems.Add(new ModuleDiagnostic("character.reference", $"{field}: {problem}"));
        }

        return found;
    }

    private static bool IsPermutation(IReadOnlyList<string> priority, List<Definition> attributes)
    {
        return priority.Count == attributes.Count && priority.Order(StringComparer.Ordinal).SequenceEqual(attributes.Select(attribute => attribute.Id).Order(StringComparer.Ordinal));
    }
}
