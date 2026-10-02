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
/// <param name="Features">Features for the choices creation and the first level grant, matched to them by kind in order.</param>
public sealed record CreationRequest(
    string Name,
    string Class,
    string Race,
    IReadOnlyDictionary<string, decimal>? Attributes = null,
    IReadOnlyList<string>? Priority = null,
    string? Creation = null,
    IReadOnlyList<string>? Features = null);

/// <summary>A choice a character makes: <see cref="Count"/> features of a kind, and what grants it, for messages.</summary>
public sealed record Grant(string Kind, int Count, string From);

/// <summary>A level gained: the character level reached, the class it was taken in, and the hit points it added.</summary>
public sealed record LevelGain(int Level, Definition Class, decimal Amount);

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
        List<Definition>? choices = FindFeatures(rules, request.Features, problems);
        if (creation is null || characterClass is null || race is null || choices is null)
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
        CheckRaceClass(rules, race, characterClass, problems);
        CheckRaceLimits(race, scores, problems);
        CheckClass(characterClass, scores, problems);
        if (problems.Count > 0)
        {
            return null;
        }

        Character character = new() { Name = request.Name, Modules = modules, Race = race };
        foreach ((string id, decimal score) in scores)
        {
            character.Attributes[id] = score;
        }

        try
        {
            (decimal kept, _) = TakeLevel(rules, character, characterClass, evaluator);
            if (rules.LevelTrack is Definition levelTrack)
            {
                // The track's own maximum is what levels keep; StartTracks starts it at the full maximum.
                character.Tracks[levelTrack.Id] = new TrackValue { Max = kept };
            }

            List<Grant> grants = [.. CreationGrants(creation), .. LevelGrants(rules, character, evaluator)];
            if (!Choose(rules, character, grants, choices, evaluator, problems) || !NoneLeft(choices, problems))
            {
                return null;
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

    /// <summary>Gives a character a portrait asset; problems name the reference.</summary>
    public static bool SetPortrait(RuleSet rules, Character character, string reference, List<ModuleDiagnostic> problems)
    {
        if (FindPortrait(rules, reference, problems) is not Definition portrait)
        {
            return false;
        }

        character.Portrait = portrait;
        return true;
    }

    /// <summary>The portrait asset <paramref name="reference"/> names, or null with the problem.</summary>
    public static Definition? FindPortrait(RuleSet rules, string reference, List<ModuleDiagnostic> problems)
    {
        if (rules.Find(DefinitionTypes.Asset, reference, out string? missing) is not Definition asset)
        {
            problems.Add(new ModuleDiagnostic("character.portrait", missing!));
            return null;
        }

        if (PortraitProblem(asset) is string problem)
        {
            problems.Add(new ModuleDiagnostic("character.portrait", problem));
            return null;
        }

        return asset;
    }

    /// <summary>Why an asset can't be a portrait, or null when it can.</summary>
    public static string? PortraitProblem(Definition asset)
    {
        string kind = asset.Json.GetProperty("kind").GetString()!;
        return kind == "portrait" ? null : $"{asset.QualifiedId} is a {kind} asset, but a character's portrait must be a portrait.";
    }

    /// <summary>
    /// Adds experience and gains every level it reaches, rolling hit points
    /// for each. Under an advancement by character, each level is taken in
    /// <paramref name="nextClass"/> (a new class must accept the character),
    /// otherwise in the class of the latest level; with experience by class
    /// the character stays in its class. Levelling stops where that class has
    /// no more levels. The choices each level grants take <paramref name="features"/>
    /// by kind, in order. On a problem it returns null and the character may
    /// be partly advanced; don't save it.
    /// </summary>
    public static List<LevelGain>? AddExperience(
        RuleSet rules,
        Character character,
        decimal experience,
        DiceRoller dice,
        List<ModuleDiagnostic> problems,
        string? nextClass = null,
        IReadOnlyList<string>? features = null)
    {
        List<Definition>? choices = FindFeatures(rules, features, problems);
        if (choices is null)
        {
            return null;
        }

        Definition? chosen = null;
        if (nextClass is not null)
        {
            chosen = Find(rules, DefinitionTypes.Class, nextClass, "class", problems);
            if (chosen is null || !CanTake(rules, character, chosen, problems))
            {
                return null;
            }
        }

        try
        {
            character.Experience = checked(character.Experience + experience);
            Evaluator evaluator = new(rules, dice);
            List<LevelGain> gains = [];
            while (character.NextLevelExperience(rules) is decimal needed && character.Experience >= needed)
            {
                // A class with no levels left stops here; the level waits for another class (see LevelWaiting).
                Definition characterClass = chosen ?? character.LatestClass;
                if (character.ClassLevels().GetValueOrDefault(characterClass) >= characterClass.Json.GetProperty("levels").GetArrayLength())
                {
                    break;
                }

                (decimal kept, decimal bonus) = TakeLevel(rules, character, characterClass, evaluator);
                decimal gain = checked(kept + bonus);
                if (rules.LevelTrack is Definition levelTrack)
                {
                    TrackValue value = character.Tracks[levelTrack.Id];
                    value.Max = checked((value.Max ?? 0) + kept);
                    value.Current = checked((value.Current ?? 0) + gain);
                }

                gains.Add(new LevelGain(character.Level, characterClass, gain));
                if (!Choose(rules, character, LevelGrants(rules, character, evaluator), choices, evaluator, problems))
                {
                    return null;
                }
            }

            return NoneLeft(choices, problems) ? gains : null;
        }
        catch (Exception exception) when (exception is RuleFailure or OverflowException)
        {
            problems.Add(exception is RuleFailure failure
                ? failure.Diagnostic
                : new ModuleDiagnostic("character.number", "Experience or a track would become too large to be a number."));
            return null;
        }
    }

    /// <summary>
    /// Whether the character's next levels may go to <paramref name="characterClass"/>:
    /// its own classes always; a new class only under an advancement by
    /// character, and only if the race allows it and the character meets its
    /// requirements.
    /// </summary>
    private static bool CanTake(RuleSet rules, Character character, Definition characterClass, List<ModuleDiagnostic> problems)
    {
        if (character.ClassLevels().ContainsKey(characterClass))
        {
            return true;
        }

        if (!rules.ExperienceByCharacter)
        {
            string why = rules.Advancement is Definition advancement
                ? $"{advancement.QualifiedId} counts experience by class"
                : "the module set has no advancement definition with experience \"character\"";
            problems.Add(new ModuleDiagnostic("character.multiclass", $"{character.Name} can't take a level in {characterClass.QualifiedId}: {why}, so a character keeps its one class."));
            return false;
        }

        int before = problems.Count;
        CheckRaceClass(rules, character.Race, characterClass, problems);
        CheckClass(characterClass, character.Attributes, problems);
        return problems.Count == before;
    }

    /// <summary>
    /// Adds a level in <paramref name="characterClass"/>. Returns its hp,
    /// which is kept, and its hp_bonus as the character is now, which the
    /// level track's maximum recomputes.
    /// </summary>
    private static (decimal Kept, decimal Bonus) TakeLevel(RuleSet rules, Character character, Definition characterClass, Evaluator evaluator)
    {
        // The gain is evaluated as the character is once it has the level.
        character.Levels.Add(new LevelTaken(characterClass, 0, []));
        int classLevel = character.ClassLevels()[characterClass];
        Creature creature = character.ToCreature();
        decimal kept = Evaluate(rules, evaluator, characterClass, $"$.levels[{classLevel - 1}].hp", creature);
        string bonusPath = $"$.levels[{classLevel - 1}].hp_bonus";
        decimal bonus = rules.TryExpression(characterClass, bonusPath, out _) ? Evaluate(rules, evaluator, characterClass, bonusPath, creature) : 0;
        character.Levels[^1] = character.Levels[^1] with { Gain = kept };
        return (kept, bonus);
    }

    /// <summary>The choices every new character makes, from the character-creation definition.</summary>
    private static List<Grant> CreationGrants(Definition creation)
    {
        return Grants(creation.Json, "features", $"creation ({creation.QualifiedId})");
    }

    /// <summary>
    /// The choices the character's latest level grants: the advancement's
    /// grants whose "when" holds now, then the class level's.
    /// </summary>
    private static List<Grant> LevelGrants(RuleSet rules, Character character, Evaluator evaluator)
    {
        List<Grant> grants = [];
        if (rules.Advancement is Definition advancement && advancement.Json.TryGetProperty("grants", out JsonElement advancementGrants))
        {
            for (int index = 0; index < advancementGrants.GetArrayLength(); index++)
            {
                string path = $"$.grants[{index}]";
                if (Holds(rules, evaluator, advancement, $"{path}.when", character.ToCreature()))
                {
                    grants.Add(Grant(advancementGrants[index], $"level {character.Level} ({advancement.QualifiedId})"));
                }
            }
        }

        Definition characterClass = character.LatestClass;
        int classLevel = character.ClassLevels()[characterClass];
        grants.AddRange(Grants(characterClass.Json.GetProperty("levels")[classLevel - 1], "grants", $"{characterClass.Name} level {classLevel}"));
        return grants;
    }

    private static List<Grant> Grants(JsonElement owner, string field, string from)
    {
        return owner.TryGetProperty(field, out JsonElement grants) ? grants.EnumerateArray().Select(grant => Grant(grant, from)).ToList() : [];
    }

    private static Grant Grant(JsonElement grant, string from)
    {
        int count = grant.TryGetProperty("count", out JsonElement given) ? given.GetInt32() : 1;
        return new Grant(grant.GetProperty("kind").GetString()!, count, from);
    }

    /// <summary>
    /// Fills each grant from <paramref name="choices"/>: the first features
    /// left of its kind, each one's requirements met and not taken twice
    /// unless repeatable. The chosen features join the latest level.
    /// </summary>
    private static bool Choose(RuleSet rules, Character character, List<Grant> grants, List<Definition> choices, Evaluator evaluator, List<ModuleDiagnostic> problems)
    {
        foreach (Grant grant in grants)
        {
            for (int made = 0; made < grant.Count; made++)
            {
                Definition? feature = choices.FirstOrDefault(choice => choice.Json.GetProperty("kind").GetString() == grant.Kind);
                if (feature is null)
                {
                    List<string> offered = rules.OfType(DefinitionTypes.Feature)
                        .Where(candidate => candidate.Json.GetProperty("kind").GetString() == grant.Kind && Problem(rules, character, candidate, evaluator) is null)
                        .Select(candidate => candidate.QualifiedId)
                        .ToList();
                    problems.Add(new ModuleDiagnostic(
                        "character.feature",
                        $"{grant.From} grants {grant.Count} {grant.Kind} for {character.Name}, so choose {(grant.Count - made == 1 ? "one" : $"{grant.Count - made} more")} with --feature. {Offer(grant.Kind, offered)}"));
                    return false;
                }

                choices.Remove(feature);
                if (Problem(rules, character, feature, evaluator) is ModuleDiagnostic problem)
                {
                    problems.Add(problem);
                    return false;
                }

                LevelTaken latest = character.Levels[^1];
                character.Levels[^1] = latest with { Features = [.. latest.Features, feature] };
            }
        }

        return true;
    }

    /// <summary>Why the character can't choose <paramref name="feature"/> now: it has it and it isn't repeatable, or it misses the requirements.</summary>
    private static ModuleDiagnostic? Problem(RuleSet rules, Character character, Definition feature, Evaluator evaluator)
    {
        bool repeatable = feature.Json.TryGetProperty("repeatable", out JsonElement repeat) && repeat.GetBoolean();
        if (!repeatable && character.Features.Contains(feature))
        {
            return new ModuleDiagnostic("character.feature", $"{character.Name} already has {feature.QualifiedId}, and it isn't repeatable.", feature.Module, feature.File, "$");
        }

        if (rules.TryExpression(feature, "$.requirements", out CompiledExpression? requirements)
            && !Holds(rules, evaluator, feature, "$.requirements", character.ToCreature()))
        {
            return new ModuleDiagnostic(
                "character.feature",
                $"{character.Name} doesn't meet {feature.QualifiedId}'s requirements: {requirements!.Text}.",
                feature.Module,
                feature.File,
                "$.requirements");
        }

        return null;
    }

    private static string Offer(string kind, List<string> offered)
    {
        return offered.Count == 0 ? $"No {kind} is open to the character now." : $"Open to the character: {string.Join(", ", offered)}.";
    }

    /// <summary>Every feature given must have filled a grant.</summary>
    private static bool NoneLeft(List<Definition> choices, List<ModuleDiagnostic> problems)
    {
        foreach (Definition extra in choices)
        {
            problems.Add(new ModuleDiagnostic("character.feature", $"Nothing granted a {extra.Json.GetProperty("kind").GetString()} for {extra.QualifiedId}; leave it out, or give it at a level that grants one."));
        }

        return choices.Count == 0;
    }

    private static List<Definition>? FindFeatures(RuleSet rules, IReadOnlyList<string>? ids, List<ModuleDiagnostic> problems)
    {
        int before = problems.Count;
        List<Definition> found = [];
        foreach (string id in ids ?? [])
        {
            if (Find(rules, DefinitionTypes.Feature, id, "feature", problems) is Definition feature)
            {
                found.Add(feature);
            }
        }

        return problems.Count == before ? found : null;
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

    /// <summary>Evaluates a definition's expression; a failure names the definition, file and path.</summary>
    private static decimal Evaluate(RuleSet rules, Evaluator evaluator, Definition definition, string path, Creature? self)
    {
        return Value(rules, evaluator, definition, path, self).Number;
    }

    /// <summary>Evaluates a definition's boolean expression, such as a requirement.</summary>
    private static bool Holds(RuleSet rules, Evaluator evaluator, Definition definition, string path, Creature self)
    {
        return Value(rules, evaluator, definition, path, self).Boolean;
    }

    private static Value Value(RuleSet rules, Evaluator evaluator, Definition definition, string path, Creature? self)
    {
        try
        {
            return evaluator.Evaluate(rules.Expression(definition, path), self, null);
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

    private static void CheckRaceClass(RuleSet rules, Definition race, Definition characterClass, List<ModuleDiagnostic> problems)
    {
        if (!race.Json.TryGetProperty("classes", out _))
        {
            return;
        }

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
    }

    private static void CheckRaceLimits(Definition race, IReadOnlyDictionary<string, decimal> scores, List<ModuleDiagnostic> problems)
    {
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

    private static void CheckClass(Definition characterClass, IReadOnlyDictionary<string, decimal> scores, List<ModuleDiagnostic> problems)
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
