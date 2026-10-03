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
/// <param name="AlsoClasses">Under experience split between classes: further classes to start with, as the race's multiclasses allow.</param>
/// <param name="Boosts">Under creation by boosts: the attribute for each boost that offers a choice, in order (race, creation features, class, creation).</param>
public sealed record CreationRequest(
    string Name,
    string Class,
    string Race,
    IReadOnlyDictionary<string, decimal>? Attributes = null,
    IReadOnlyList<string>? Priority = null,
    string? Creation = null,
    IReadOnlyList<string>? Features = null,
    IReadOnlyList<string>? Boosts = null,
    IReadOnlyList<string>? AlsoClasses = null);

/// <summary>A choice a character makes: <see cref="Count"/> features of any of <see cref="Kinds"/>, and what grants it, for messages.</summary>
public sealed record Grant(IReadOnlyList<string> Kinds, int Count, string From)
{
    public string KindText => string.Join(" or ", Kinds);

    public bool Takes(Definition feature) => Kinds.Contains(feature.Json.GetProperty("kind").GetString()!);
}

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
        List<Definition> classes = characterClass is null ? [] : [characterClass];
        foreach (string also in request.AlsoClasses ?? [])
        {
            if (Find(rules, DefinitionTypes.Class, also, "class", problems) is Definition another)
            {
                classes.Add(another);
            }
        }

        Definition? race = Find(rules, DefinitionTypes.Race, request.Race, "race", problems);
        List<Definition>? choices = FindFeatures(rules, request.Features, problems);
        if (creation is null || characterClass is null || race is null || choices is null || problems.Count > 0)
        {
            return null;
        }

        List<Definition> attributes = creation.Json.GetProperty("attributes").EnumerateArray()
            .Select(entry => rules.Stats[entry.GetString()!].Definition)
            .ToList();
        Evaluator evaluator = new(rules, dice);
        string method = creation.Json.TryGetProperty("method", out JsonElement declared) ? declared.GetString()! : "roll";
        if (request.Boosts is not null && method != "boosts")
        {
            problems.Add(new ModuleDiagnostic("character.boosts", $"{creation.QualifiedId} makes scores by {method}, not boosts; leave the boosts out."));
            return null;
        }

        if (request.Priority is not null && method == "boosts")
        {
            problems.Add(new ModuleDiagnostic("character.priority", $"{creation.QualifiedId} makes scores by boosts, which name their attributes; a priority doesn't apply."));
            return null;
        }

        Dictionary<string, decimal>? scores;
        try
        {
            scores = (method, request.Attributes) switch
            {
                ("point-buy", null) => Missing(creation, "point buy spends points on scores, so give them (--attributes)", problems),
                ("point-buy", _) => PointBuy(rules, creation, attributes, request.Attributes, request.Priority, problems),
                (_, not null) => GivenAttributes(attributes, request.Attributes, request.Priority, problems),
                ("array", _) => ArrayAttributes(creation, attributes, request.Priority, problems),
                ("boosts", _) => BoostAttributes(rules, creation, attributes, race, characterClass, CreationFeatures(creation, choices), request.Boosts ?? [], problems),
                _ => RollAttributes(rules, creation, attributes, request.Priority, evaluator, problems),
            };
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
        if (classes.Count > 1)
        {
            CheckMulticlass(rules, race, classes, problems);
        }
        else
        {
            CheckRaceClass(rules, race, characterClass, problems);
        }

        CheckRaceLimits(race, scores, problems);
        foreach (Definition each in classes)
        {
            CheckClass(each, scores, problems);
        }

        if (problems.Count > 0)
        {
            return null;
        }

        Character character = new() { Name = request.Name, Modules = modules, Race = race, Creation = creation };
        foreach ((string id, decimal score) in scores)
        {
            character.Attributes[id] = score;
        }

        try
        {
            decimal kept = TakeLevels(rules, character, classes, evaluator).Sum(gain => gain.Kept);
            if (rules.ExperienceSplit)
            {
                foreach (Definition each in classes)
                {
                    character.ClassExperience[each] = 0;
                }
            }

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

    /// <summary>
    /// Checks a character read from a file against what it could have chosen:
    /// replaying its levels, each level's features must fill exactly what
    /// creation (at the first level), the advancement and the class level
    /// granted, meeting each feature's requirements and repeat rule, and its
    /// boosts must number what the advancement granted. Requirements read the
    /// character's current scores.
    /// </summary>
    public static void CheckHistory(RuleSet rules, Character character, List<ModuleDiagnostic> problems)
    {
        Character replay = new() { Name = character.Name, Modules = character.Modules, Race = character.Race, Creation = character.Creation };
        replay.LeftClasses.AddRange(character.LeftClasses);
        if (ScoresBeforeBoosts(rules, character, problems) is not Dictionary<string, decimal> scores)
        {
            return;
        }

        foreach ((string id, decimal score) in scores)
        {
            replay.Attributes[id] = score;
        }

        Evaluator evaluator = new(rules, null);
        for (int index = 0; index < character.Levels.Count; index++)
        {
            LevelTaken level = character.Levels[index];
            replay.Levels.Add(new LevelTaken(level.Class, level.Gain, []));
            List<ModuleDiagnostic> found = [];
            try
            {
                List<Grant> grants = [.. index == 0 ? CreationGrants(character.Creation) : [], .. LevelGrants(rules, replay, evaluator)];
                List<Definition> choices = [.. level.Features];
                if (Choose(rules, replay, grants, choices, evaluator, found) && NoneLeft(choices, found))
                {
                    int granted = BoostsGranted(rules, replay, evaluator);
                    if (granted != level.Boosts.Count)
                    {
                        found.Add(new ModuleDiagnostic("character.boosts", $"Level {index + 1} grants {granted} boosts, but the file records {level.Boosts.Count}."));
                    }
                }
            }
            catch (RuleFailure failure)
            {
                found.Add(failure.Diagnostic);
            }

            problems.AddRange(found.Select(problem => problem with { Message = $"Level {index + 1} ({level.Class.Name}): {problem.Message}" }));
            replay.Levels[^1] = level;
            foreach (string boosted in level.Boosts)
            {
                replay.Attributes[boosted] += BoostAmount(rules, replay.Attributes[boosted]) ?? 0;
            }
        }
    }

    /// <summary>
    /// The character's scores before its level boosts: each boost, last first,
    /// undone by finding the score its amounts table raised to the one after.
    /// </summary>
    private static Dictionary<string, decimal>? ScoresBeforeBoosts(RuleSet rules, Character character, List<ModuleDiagnostic> problems)
    {
        Dictionary<string, decimal> scores = new(character.Attributes);
        foreach ((LevelTaken level, int index) in character.Levels.Select((level, index) => (level, index)).Reverse())
        {
            foreach (string boosted in level.Boosts.Reverse())
            {
                decimal after = scores[boosted];
                decimal min = rules.Stats[boosted].Definition.Json.GetProperty("min").GetDecimal();
                decimal? found = null;
                for (decimal score = after - 1; score >= min && found is null; score--)
                {
                    if (BoostAmount(rules, score) is decimal amount && score + amount == after)
                    {
                        found = score;
                    }
                }

                if (found is not decimal was)
                {
                    problems.Add(new ModuleDiagnostic("character.boosts", $"Level {index + 1}: no boost raises {boosted} to {after}, so the file's scores and boosts don't agree."));
                    return null;
                }

                scores[boosted] = was;
            }
        }

        return scores;
    }

    /// <summary>What a level boost adds to a score, from the advancement's first level boost table.</summary>
    private static decimal? BoostAmount(RuleSet rules, decimal score)
    {
        if (rules.Advancement is not Definition advancement || !advancement.Json.TryGetProperty("level_boosts", out JsonElement grants) || grants.GetArrayLength() == 0)
        {
            return null;
        }

        Definition amounts = rules.Reference(advancement, "$.level_boosts[0].amounts");
        return rules.Tables[amounts].Lookup([Value.Of(score)])?.Number;
    }

    /// <summary>How many boosts the advancement grants at the character's latest level.</summary>
    private static int BoostsGranted(RuleSet rules, Character character, Evaluator evaluator)
    {
        if (rules.Advancement is not Definition advancement || !advancement.Json.TryGetProperty("level_boosts", out JsonElement grants))
        {
            return 0;
        }

        int total = 0;
        for (int index = 0; index < grants.GetArrayLength(); index++)
        {
            if (Holds(rules, evaluator, advancement, $"$.level_boosts[{index}].when", character.ToCreature()))
            {
                total += grants[index].GetProperty("count").GetInt32();
            }
        }

        return total;
    }

    /// <summary>
    /// Why the character may not equip <paramref name="item"/>, or null when it
    /// may: its classes' equipment rules (a class without one allows anything),
    /// any one of them or every one as its race's multiclass_equipment says.
    /// Classes waiting after a class change don't count.
    /// </summary>
    public static string? EquipmentProblem(RuleSet rules, Character character, Definition item)
    {
        Creature creature = character.ToCreature();
        Evaluator evaluator = new(rules, null);
        List<Definition> refusing = [];
        foreach (Definition characterClass in creature.ClassLevels.Keys)
        {
            if (rules.TryExpression(characterClass, "$.equipment", out CompiledExpression? rule)
                && !evaluator.Evaluate(rule!, new Scope(creature, null, Item: item)).Boolean)
            {
                refusing.Add(characterClass);
            }
        }

        bool every = character.Race.Json.TryGetProperty("multiclass_equipment", out JsonElement mode) && mode.GetString() == "all";
        bool refused = every ? refusing.Count > 0 : refusing.Count == creature.ClassLevels.Count && refusing.Count > 0;
        if (!refused)
        {
            return null;
        }

        string classes = string.Join(" and ", refusing.Select(each => each.Name));
        return $"{character.Name} can't equip {item.Name}: {classes} {(refusing.Count == 1 ? "doesn't" : "don't")} allow it.";
    }

    /// <summary>
    /// Why the character can't know <paramref name="spell"/>, or null when it
    /// can: the spell must be on one of its classes' lists, castable (it has an
    /// effect), and payable from its tracks at their maximum.
    /// </summary>
    public static string? SpellProblem(RuleSet rules, Character character, Definition spell)
    {
        Creature creature = character.ToCreature();
        bool listed = false;
        if (spell.Json.TryGetProperty("lists", out JsonElement lists))
        {
            foreach (JsonProperty entry in lists.EnumerateObject())
            {
                listed |= creature.ClassLevels.ContainsKey(rules.Reference(spell, $"$.lists.{entry.Name}"));
            }
        }

        if (!listed)
        {
            return $"{spell.Name} isn't on the spell list of any of {character.Name}'s classes.";
        }

        if (!spell.Json.TryGetProperty("effect", out _))
        {
            return $"{spell.Name} has no effect to cast in combat.";
        }

        Evaluator evaluator = new(rules, null);
        if (spell.Json.TryGetProperty("cost", out JsonElement cost))
        {
            foreach (JsonProperty entry in cost.EnumerateObject())
            {
                Definition track = rules.Reference(spell, $"$.cost.{entry.Name}");
                decimal needed = evaluator.Evaluate(rules.Expression(spell, $"$.cost.{entry.Name}"), creature, null).Number;
                decimal max = evaluator.KnownTrackMax(creature, track) ?? 0;
                if (max < needed)
                {
                    return $"{character.Name} can't cast {spell.Name}: it needs {needed} {track.Name.ToLowerInvariant()}, and {character.Name} has at most {max}.";
                }
            }
        }

        return null;
    }

    /// <summary>Whether casting the spell needs a prepared copy: it is on the list of one of the character's classes that prepares spells.</summary>
    public static bool NeedsPreparing(RuleSet rules, Character character, Definition spell)
    {
        if (!spell.Json.TryGetProperty("lists", out JsonElement lists))
        {
            return false;
        }

        foreach (JsonProperty entry in lists.EnumerateObject())
        {
            Definition listing = rules.Reference(spell, $"$.lists.{entry.Name}");
            if (character.ClassLevels().ContainsKey(listing) && listing.Json.TryGetProperty("prepares_spells", out JsonElement prepares) && prepares.GetBoolean())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The copies the character prepares each day: its memorised list, or
    /// without one, its known spells that need preparing, taken in order and
    /// round again while its tracks can pay for another.
    /// </summary>
    public static List<Definition> MemorisedPlan(RuleSet rules, Character character)
    {
        if (character.Memorised.Count > 0)
        {
            return character.Memorised.ToList();
        }

        List<Definition> preparing = character.Spells.Where(spell => NeedsPreparing(rules, character, spell)).ToList();
        List<Definition> plan = [];
        Dictionary<Definition, decimal> left = Budgets(rules, character);
        bool added = true;
        for (int pass = 0; added; pass++)
        {
            added = false;
            foreach (Definition spell in preparing)
            {
                // A spell that costs nothing is prepared once, or the passes would never end.
                bool free = !spell.Json.TryGetProperty("cost", out JsonElement cost) || !cost.EnumerateObject().Any();
                if ((pass == 0 || !free) && Pay(rules, character, spell, left))
                {
                    plan.Add(spell);
                    added |= !free;
                }
            }
        }

        return plan;
    }

    /// <summary>The prepared copies the character has left to cast.</summary>
    public static List<Definition> PreparedLeft(RuleSet rules, Character character) => character.Prepared?.ToList() ?? MemorisedPlan(rules, character);

    /// <summary>
    /// Sets the copies the character memorises each day, in order, and
    /// prepares them now: each a spell it knows that needs preparing, all of
    /// them together payable from its tracks at their maximum.
    /// </summary>
    public static bool SetMemorised(RuleSet rules, Character character, IReadOnlyList<string> ids, List<ModuleDiagnostic> problems)
    {
        int before = problems.Count;
        List<Definition> plan = [];
        Dictionary<Definition, decimal> left = Budgets(rules, character);
        foreach (string id in ids)
        {
            if (Find(rules, DefinitionTypes.Spell, id, "spell", problems) is not Definition spell)
            {
                continue;
            }

            if (MemorisedProblem(rules, character, spell, left) is string problem)
            {
                problems.Add(new ModuleDiagnostic("character.spell", problem, spell.Module, spell.File, "$"));
                continue;
            }

            plan.Add(spell);
        }

        if (problems.Count > before)
        {
            return false;
        }

        character.Memorised.Clear();
        character.Memorised.AddRange(plan);
        character.Prepared = null;
        return true;
    }

    /// <summary>Why the character can't memorise another copy of the spell with what <paramref name="left"/> of its tracks remains, or null (and pays for it) when it can.</summary>
    public static string? MemorisedProblem(RuleSet rules, Character character, Definition spell, Dictionary<Definition, decimal> left)
    {
        if (!character.Spells.Contains(spell))
        {
            return $"{character.Name} doesn't know {spell.Name}; it can memorise only spells it knows.";
        }

        if (!NeedsPreparing(rules, character, spell))
        {
            return $"{spell.Name} isn't prepared: none of {character.Name}'s classes that have it on their list prepares spells.";
        }

        return Pay(rules, character, spell, left) ? null : $"{character.Name} can't memorise another {spell.Name}: its tracks can't pay for every copy.";
    }

    /// <summary>The character's tracks at their maximum, for paying a day's prepared spells from.</summary>
    public static Dictionary<Definition, decimal> Budgets(RuleSet rules, Character character)
    {
        Creature creature = character.ToCreature();
        Evaluator evaluator = new(rules, null);
        return rules.Tracks.Values.ToDictionary(track => track, track => evaluator.KnownTrackMax(creature, track) ?? 0);
    }

    /// <summary>Takes the spell's cost from <paramref name="left"/> when all of it is there; returns whether it was.</summary>
    private static bool Pay(RuleSet rules, Character character, Definition spell, Dictionary<Definition, decimal> left)
    {
        if (!spell.Json.TryGetProperty("cost", out JsonElement cost))
        {
            return true;
        }

        Creature creature = character.ToCreature();
        Evaluator evaluator = new(rules, null);
        Dictionary<Definition, decimal> costs = cost.EnumerateObject().ToDictionary(
            entry => rules.Reference(spell, $"$.cost.{entry.Name}"),
            entry => evaluator.Evaluate(rules.Expression(spell, $"$.cost.{entry.Name}"), creature, null).Number);
        if (costs.Any(entry => left[entry.Key] < entry.Value))
        {
            return false;
        }

        foreach ((Definition track, decimal amount) in costs)
        {
            left[track] -= amount;
        }

        return true;
    }

    /// <summary>The spells the character could know, in the module set's order: those <see cref="SpellProblem"/> has nothing against.</summary>
    public static List<Definition> CastableSpells(RuleSet rules, Character character)
    {
        return rules.OfType(DefinitionTypes.Spell).Where(spell => SpellProblem(rules, character, spell) is null).ToList();
    }

    /// <summary>Gives the character these spells to know, in order; problems name each one it can't.</summary>
    public static bool SetSpells(RuleSet rules, Character character, IReadOnlyList<string> ids, List<ModuleDiagnostic> problems)
    {
        int before = problems.Count;
        List<Definition> spells = [];
        foreach (string id in ids)
        {
            if (Find(rules, DefinitionTypes.Spell, id, "spell", problems) is not Definition spell)
            {
                continue;
            }

            if (SpellProblem(rules, character, spell) is string problem)
            {
                problems.Add(new ModuleDiagnostic("character.spell", problem, spell.Module, spell.File, "$.lists"));
            }
            else if (!spells.Contains(spell))
            {
                spells.Add(spell);
            }
        }

        if (problems.Count > before)
        {
            return false;
        }

        character.Spells.Clear();
        character.Spells.AddRange(spells);
        // Copies of spells it no longer knows are forgotten; the rest are prepared afresh.
        character.Memorised.RemoveAll(spell => !spells.Contains(spell));
        character.Prepared = null;
        return true;
    }

    /// <summary>The character-creation definition used when none is named, or null when the set has none or several without a default.</summary>
    public static Definition? DefaultCreation(RuleSet rules) => FindCreation(rules, null, []);

    /// <summary>The choices creation grants every new character.</summary>
    public static List<Grant> CreationChoices(Definition creation) => CreationGrants(creation);

    /// <summary>
    /// The choices a new character's first level in <paramref name="characterClass"/>
    /// grants besides creation's: the advancement's grants whose "when" holds
    /// for a first-level creature of the class, then the class's first level.
    /// A "when" that needs more than that (such as an attribute) is left out.
    /// </summary>
    public static List<Grant> FirstLevelChoices(RuleSet rules, Definition characterClass)
    {
        Creature probe = new("self") { Class = characterClass, Level = 1, AdvancingClasses = 1 };
        probe.ClassLevels[characterClass] = 1;
        Evaluator evaluator = new(rules, null);
        List<Grant> grants = [];
        if (rules.Advancement is Definition advancement && advancement.Json.TryGetProperty("grants", out JsonElement advancementGrants))
        {
            for (int index = 0; index < advancementGrants.GetArrayLength(); index++)
            {
                try
                {
                    if (evaluator.Evaluate(rules.Expression(advancement, $"$.grants[{index}].when"), probe, null).Boolean)
                    {
                        grants.Add(Grant(advancementGrants[index], $"level 1 ({advancement.QualifiedId})"));
                    }
                }
                catch (ExpressionException)
                {
                }
            }
        }

        grants.AddRange(Grants(characterClass.Json.GetProperty("levels")[0], "grants", $"{characterClass.Name} level 1"));
        return grants;
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
        IReadOnlyList<string>? features = null,
        IReadOnlyList<string>? boosts = null)
    {
        // All or nothing: a level that can't be taken leaves the character as it was.
        LevelSnapshot before = LevelSnapshot.Of(character);
        List<LevelGain>? gains = Gain(rules, character, experience, dice, problems, nextClass, features, boosts);
        if (gains is null)
        {
            before.Restore(character);
        }

        return gains;
    }

    /// <summary>
    /// Adds experience earned in play. Levels it reaches are taken at once when
    /// they need no choice; otherwise they wait (<see cref="ReadyToLevel"/>)
    /// for <see cref="AddExperience"/> with no more experience and the choices.
    /// Where each level's class is chosen (experience by character), every
    /// level waits.
    /// </summary>
    public static List<LevelGain> Award(RuleSet rules, Character character, decimal experience, DiceRoller dice)
    {
        bool classChosen = rules.Advancement?.Json.GetProperty("experience").GetString() == "character";
        if (!classChosen && AddExperience(rules, character, experience, dice, []) is List<LevelGain> gains)
        {
            return gains;
        }

        character.Experience = checked(character.Experience + experience);
        if (rules.ExperienceSplit)
        {
            Share(character, experience);
        }

        return [];
    }

    /// <summary>Whether the character has the experience for a level it hasn't taken.</summary>
    public static bool ReadyToLevel(RuleSet rules, Character character)
    {
        if (rules.ExperienceSplit)
        {
            return character.ClassProgress().Any(progress => progress.Next is decimal needed && progress.Experience >= needed);
        }

        return character.NextLevelExperience(rules) is decimal next && character.Experience >= next;
    }

    /// <summary>With experience split, divides experience evenly (rounding down) between the classes the character advances in.</summary>
    private static void Share(Character character, decimal experience)
    {
        List<Definition> advancing = character.AdvancingClasses();
        decimal share = decimal.Floor(experience / advancing.Count);
        foreach (Definition each in advancing)
        {
            character.ClassExperience[each] = checked(character.ClassExperience.GetValueOrDefault(each) + share);
        }
    }

    /// <summary>What levelling changes, kept so a level that can't be taken can be undone.</summary>
    private sealed record LevelSnapshot(
        List<LevelTaken> Levels,
        Dictionary<Definition, decimal> ClassExperience,
        List<Definition> LeftClasses,
        decimal Experience,
        Dictionary<string, decimal> Attributes,
        Dictionary<string, (decimal? Current, decimal? Max)> Tracks)
    {
        public static LevelSnapshot Of(Character character)
        {
            return new LevelSnapshot(
                [.. character.Levels],
                new(character.ClassExperience),
                [.. character.LeftClasses],
                character.Experience,
                new(character.Attributes),
                character.Tracks.ToDictionary(entry => entry.Key, entry => (entry.Value.Current, entry.Value.Max)));
        }

        public void Restore(Character character)
        {
            character.Levels.Clear();
            character.Levels.AddRange(Levels);
            character.ClassExperience.Clear();
            foreach ((Definition each, decimal experience) in ClassExperience)
            {
                character.ClassExperience[each] = experience;
            }

            character.LeftClasses.Clear();
            character.LeftClasses.AddRange(LeftClasses);
            character.Experience = Experience;
            character.Attributes.Clear();
            foreach ((string id, decimal score) in Attributes)
            {
                character.Attributes[id] = score;
            }

            character.Tracks.Clear();
            foreach ((string id, (decimal? current, decimal? max)) in Tracks)
            {
                character.Tracks[id] = new TrackValue { Current = current, Max = max };
            }
        }
    }

    private static List<LevelGain>? Gain(
        RuleSet rules,
        Character character,
        decimal experience,
        DiceRoller dice,
        List<ModuleDiagnostic> problems,
        string? nextClass,
        IReadOnlyList<string>? features,
        IReadOnlyList<string>? boosts)
    {
        Queue<string> boostChoices = new(boosts ?? []);
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
            if (rules.ExperienceSplit)
            {
                if (chosen is not null && !character.AdvancingClasses().Contains(chosen))
                {
                    // Changing class: the old classes stop, and the new one starts at its first level.
                    character.LeftClasses.AddRange(character.AdvancingClasses());
                    character.ClassExperience[chosen] = 0;
                    if (!Advance(rules, character, chosen, evaluator, gains, choices, boostChoices, problems))
                    {
                        return null;
                    }
                }

                Share(character, experience);
                foreach (Definition each in character.AdvancingClasses())
                {
                    while (character.ClassProgress().First(progress => progress.Class == each) is { Next: decimal needed } progress && progress.Experience >= needed)
                    {
                        if (!Advance(rules, character, each, evaluator, gains, choices, boostChoices, problems))
                        {
                            return null;
                        }
                    }
                }
            }

            while (!rules.ExperienceSplit && character.NextLevelExperience(rules) is decimal needed && character.Experience >= needed)
            {
                // A class with no levels left stops here; the level waits for another class (see LevelWaiting).
                Definition characterClass = chosen ?? character.LatestClass;
                if (character.ClassLevels().GetValueOrDefault(characterClass) >= characterClass.Json.GetProperty("levels").GetArrayLength())
                {
                    break;
                }

                if (!Advance(rules, character, characterClass, evaluator, gains, choices, boostChoices, problems))
                {
                    return null;
                }
            }

            if (boostChoices.Count > 0)
            {
                problems.Add(new ModuleDiagnostic("character.boosts", $"No level reached grants boosts for {string.Join(", ", boostChoices)}; leave them out."));
                return null;
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

    /// <summary>Takes one level in <paramref name="characterClass"/>, raising the level track, then makes that level's choices.</summary>
    private static bool Advance(
        RuleSet rules,
        Character character,
        Definition characterClass,
        Evaluator evaluator,
        List<LevelGain> gains,
        List<Definition> choices,
        Queue<string> boostChoices,
        List<ModuleDiagnostic> problems)
    {
        Dictionary<Definition, decimal?> before = Maxima(rules, character, evaluator);
        (decimal kept, decimal bonus) = TakeLevels(rules, character, [characterClass], evaluator)[0];
        decimal gain = checked(kept + bonus);
        if (rules.LevelTrack is Definition levelTrack)
        {
            TrackValue value = character.Tracks[levelTrack.Id];
            value.Max = checked((value.Max ?? 0) + kept);
        }

        gains.Add(new LevelGain(character.Level, characterClass, gain));
        if (!Choose(rules, character, LevelGrants(rules, character, evaluator), choices, evaluator, problems)
            || !LevelBoosts(rules, character, evaluator, boostChoices, problems))
        {
            return false;
        }

        // Whatever the level raised a maximum by (its gain, a toughness feat, a boosted stat) raises the current value too.
        foreach ((Definition track, decimal? after) in Maxima(rules, character, evaluator))
        {
            if (before[track] is decimal was && after is decimal now && now > was)
            {
                TrackValue value = character.Tracks[track.Id];
                value.Current = checked((value.Current ?? 0) + now - was);
            }
        }

        return true;
    }

    /// <summary>Each track's maximum for the character now, or null where none is known.</summary>
    private static Dictionary<Definition, decimal?> Maxima(RuleSet rules, Character character, Evaluator evaluator)
    {
        Creature creature = character.ToCreature();
        return rules.Tracks.Values.ToDictionary(track => track, track => evaluator.KnownTrackMax(creature, track));
    }

    /// <summary>A multi-classed character's classes must be a combination its race lists in multiclasses.</summary>
    private static void CheckMulticlass(RuleSet rules, Definition race, List<Definition> classes, List<ModuleDiagnostic> problems)
    {
        if (!rules.ExperienceSplit)
        {
            problems.Add(new ModuleDiagnostic("character.multiclass", "Starting with several classes needs an advancement definition with experience \"split\"."));
            return;
        }

        List<List<Definition>> allowed = [];
        if (race.Json.TryGetProperty("multiclasses", out JsonElement combinations))
        {
            for (int index = 0; index < combinations.GetArrayLength(); index++)
            {
                allowed.Add(Enumerable.Range(0, combinations[index].GetArrayLength()).Select(item => rules.Reference(race, $"$.multiclasses[{index}][{item}]")).ToList());
            }
        }

        if (!allowed.Any(combination => combination.Count == classes.Count && combination.All(classes.Contains)))
        {
            string listed = allowed.Count == 0 ? "none" : string.Join("; ", allowed.Select(combination => string.Join("/", combination.Select(each => each.Id))));
            problems.Add(new ModuleDiagnostic("character.multiclass", $"{race.QualifiedId} can't combine {string.Join("/", classes.Select(each => each.Id))}. Its multi-class combinations: {listed}.", race.Module, race.File, "$.multiclasses"));
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

        if (rules.ExperienceSplit)
        {
            Definition advancement = rules.Advancement!;
            if (!rules.TryExpression(advancement, "$.class_change", out CompiledExpression? change))
            {
                problems.Add(new ModuleDiagnostic("character.multiclass", $"{character.Name} can't change to {characterClass.QualifiedId}: {advancement.QualifiedId} has no class_change.", advancement.Module, advancement.File, "$"));
                return false;
            }

            Evaluator evaluator = new(rules, null);
            bool allowed;
            try
            {
                allowed = evaluator.Evaluate(change!, new Scope(character.ToCreature(), null, ClassLevel: 0, Class: characterClass)).Boolean;
            }
            catch (ExpressionException exception)
            {
                problems.Add(new ModuleDiagnostic("character.evaluate", exception.Message, advancement.Module, advancement.File, "$.class_change"));
                return false;
            }

            if (!allowed)
            {
                problems.Add(new ModuleDiagnostic("character.multiclass", $"{character.Name} can't change to {characterClass.QualifiedId}: {change!.Text}.", advancement.Module, advancement.File, "$.class_change"));
                return false;
            }

            int checkedBefore = problems.Count;
            CheckRaceClass(rules, character.Race, characterClass, problems);
            CheckClass(characterClass, character.Attributes, problems);
            return problems.Count == checkedBefore;
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
    /// Adds a level in each of <paramref name="classes"/> (a new
    /// multi-classed character's first levels, or one level), then works out
    /// each one's hp, which is kept, and its hp_bonus as the character is now,
    /// which the level track's maximum recomputes. Gains read the character
    /// with all the new levels, so a multi-classed character's first levels
    /// see every class.
    /// </summary>
    private static List<(decimal Kept, decimal Bonus)> TakeLevels(RuleSet rules, Character character, List<Definition> classes, Evaluator evaluator)
    {
        int first = character.Levels.Count;
        foreach (Definition characterClass in classes)
        {
            character.Levels.Add(new LevelTaken(characterClass, 0, []));
        }

        Creature creature = character.ToCreature();
        Dictionary<Definition, int> levels = character.ClassLevels();
        List<(decimal Kept, decimal Bonus)> gains = [];
        for (int index = 0; index < classes.Count; index++)
        {
            Definition characterClass = classes[index];
            int classLevel = levels[characterClass];
            Scope scope = new(creature, null, ClassLevel: classLevel, Class: characterClass);
            string keptPath = $"$.levels[{classLevel - 1}].hp";
            decimal kept = rules.TryExpression(characterClass, keptPath, out _) ? EvaluateValue(rules, evaluator, characterClass, keptPath, scope).Number : 0;
            string bonusPath = $"$.levels[{classLevel - 1}].hp_bonus";
            decimal bonus = rules.TryExpression(characterClass, bonusPath, out _) ? EvaluateValue(rules, evaluator, characterClass, bonusPath, scope).Number : 0;
            character.Levels[first + index] = character.Levels[first + index] with { Gain = kept };
            gains.Add((kept, bonus));
        }

        return gains;
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
        IReadOnlyList<string> kinds = grant.TryGetProperty("kinds", out JsonElement several)
            ? several.EnumerateArray().Select(kind => kind.GetString()!).ToList()
            : [grant.GetProperty("kind").GetString()!];
        return new Grant(kinds, count, from);
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
                Definition? feature = choices.FirstOrDefault(grant.Takes);
                if (feature is null)
                {
                    List<string> offered = rules.OfType(DefinitionTypes.Feature)
                        .Where(candidate => grant.Takes(candidate) && Problem(rules, character, candidate, evaluator) is null)
                        .Select(candidate => candidate.QualifiedId)
                        .ToList();
                    problems.Add(new ModuleDiagnostic(
                        "character.feature",
                        $"{grant.From} grants {grant.Count} {grant.KindText} for {character.Name}, so choose {(grant.Count - made == 1 ? "one" : $"{grant.Count - made} more")} with --feature. {Offer(grant.KindText, offered)}"));
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

    /// <summary>
    /// The advancement's level boosts for the latest level: each grant whose
    /// "when" holds takes its count of attributes from <paramref name="chosen"/>,
    /// all different, and raises each by its amounts table at the current score.
    /// </summary>
    private static bool LevelBoosts(RuleSet rules, Character character, Evaluator evaluator, Queue<string> chosen, List<ModuleDiagnostic> problems)
    {
        if (rules.Advancement is not Definition advancement || !advancement.Json.TryGetProperty("level_boosts", out JsonElement grants))
        {
            return true;
        }

        List<string> boosted = [];
        for (int index = 0; index < grants.GetArrayLength(); index++)
        {
            string path = $"$.level_boosts[{index}]";
            if (!Holds(rules, evaluator, advancement, $"{path}.when", character.ToCreature()))
            {
                continue;
            }

            Definition amounts = rules.Reference(advancement, $"{path}.amounts");
            int count = grants[index].GetProperty("count").GetInt32();
            HashSet<string> thisGrant = [];
            for (int made = 0; made < count; made++)
            {
                string from = $"level {character.Level} ({advancement.QualifiedId})";
                if (!chosen.TryDequeue(out string? attribute))
                {
                    List<string> open = character.Attributes.Keys.Where(id => !thisGrant.Contains(id)).ToList();
                    problems.Add(new ModuleDiagnostic("character.boosts", $"{from} grants {count} boosts for {character.Name}, so give {count - made} more with --boosts; any of {string.Join(", ", open)}, each once.", advancement.Module, advancement.File, path));
                    return false;
                }

                if (!character.Attributes.TryGetValue(attribute, out decimal score))
                {
                    problems.Add(new ModuleDiagnostic("character.boosts", $"'{attribute}' is not an attribute. Attributes: {string.Join(", ", character.Attributes.Keys)}."));
                    return false;
                }

                if (!thisGrant.Add(attribute))
                {
                    problems.Add(new ModuleDiagnostic("character.boosts", $"{from} boosts {count} different attributes, but {attribute} is given twice.", advancement.Module, advancement.File, path));
                    return false;
                }

                if (rules.Tables[amounts].Lookup([Value.Of(score)]) is not Value raise)
                {
                    problems.Add(new ModuleDiagnostic("character.boosts", $"{amounts.QualifiedId} has no amount for {attribute} {score}.", amounts.Module, amounts.File, "$.rows"));
                    return false;
                }

                decimal max = rules.Stats[attribute].Definition.Json.GetProperty("max").GetDecimal();
                if (score + raise.Number > max)
                {
                    problems.Add(new ModuleDiagnostic("character.boosts", $"A boost would take {attribute} to {score + raise.Number}, above its maximum {max}; boost another attribute."));
                    return false;
                }

                character.Attributes[attribute] = score + raise.Number;
                boosted.Add(attribute);
            }
        }

        if (boosted.Count > 0)
        {
            character.Levels[^1] = character.Levels[^1] with { Boosts = boosted };
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
        return EvaluateValue(rules, evaluator, definition, path, self).Number;
    }

    /// <summary>Evaluates a definition's boolean expression, such as a requirement.</summary>
    private static bool Holds(RuleSet rules, Evaluator evaluator, Definition definition, string path, Creature self)
    {
        return EvaluateValue(rules, evaluator, definition, path, self).Boolean;
    }

    private static Value EvaluateValue(RuleSet rules, Evaluator evaluator, Definition definition, string path, Creature? self)
    {
        return EvaluateValue(rules, evaluator, definition, path, new Scope(self, null));
    }

    private static Value EvaluateValue(RuleSet rules, Evaluator evaluator, Definition definition, string path, Scope scope)
    {
        try
        {
            return evaluator.Evaluate(rules.Expression(definition, path), scope);
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

    /// <summary>The creation's starting gold for the character's class; with several classes, the wealthiest of them.</summary>
    private static decimal StartingGold(RuleSet rules, Definition creation, Character character, Evaluator evaluator, List<ModuleDiagnostic> problems)
    {
        List<decimal> amounts = [];
        foreach (Definition characterClass in character.ClassLevels().Keys)
        {
            string? found = null;
            foreach (JsonProperty entry in creation.Json.GetProperty("starting_gold").EnumerateObject())
            {
                if (rules.Reference(creation, $"$.starting_gold.{entry.Name}") == characterClass)
                {
                    found = $"$.starting_gold.{entry.Name}";
                }
            }

            if (found is null)
            {
                problems.Add(new ModuleDiagnostic(
                    "character.gold",
                    $"{creation.QualifiedId} has no starting_gold for {characterClass.QualifiedId}. Add \"{characterClass.Id}\" to its starting_gold.",
                    creation.Module,
                    creation.File,
                    "$.starting_gold"));
                continue;
            }

            amounts.Add(Evaluate(rules, evaluator, creation, found, character.ToCreature()));
        }

        return amounts.DefaultIfEmpty(0).Max();
    }

    private static Dictionary<string, decimal>? RollAttributes(
        RuleSet rules,
        Definition creation,
        List<Definition> attributes,
        IReadOnlyList<string>? priority,
        Evaluator evaluator,
        List<ModuleDiagnostic> problems)
    {
        bool arrange = creation.Json.TryGetProperty("assignment", out JsonElement assignment) && assignment.GetString() == "arrange";
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
        return Arrange(attributes, rolls, priority, problems);
    }

    /// <summary>
    /// Scores in attribute order, or with a priority the highest to its first
    /// attribute, the next highest to its second, and so on.
    /// </summary>
    private static Dictionary<string, decimal>? Arrange(List<Definition> attributes, List<decimal> values, IReadOnlyList<string>? priority, List<ModuleDiagnostic> problems)
    {
        Dictionary<string, decimal> scores = [];
        if (priority is null)
        {
            for (int i = 0; i < attributes.Count; i++)
            {
                scores[attributes[i].Id] = values[i];
            }

            return scores;
        }

        if (!IsPermutation(priority, attributes))
        {
            problems.Add(new ModuleDiagnostic("character.priority", $"The priority must list every attribute once: {string.Join(", ", attributes.Select(attribute => attribute.Id))}."));
            return null;
        }

        List<decimal> best = values.OrderDescending().ToList();
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

    private static Dictionary<string, decimal>? Missing(Definition creation, string what, List<ModuleDiagnostic> problems)
    {
        problems.Add(new ModuleDiagnostic("character.attributes", $"{creation.QualifiedId}: {what}."));
        return null;
    }

    /// <summary>The array's scores, highest to the first attribute of the priority (or in attribute order without one).</summary>
    private static Dictionary<string, decimal>? ArrayAttributes(Definition creation, List<Definition> attributes, IReadOnlyList<string>? priority, List<ModuleDiagnostic> problems)
    {
        List<decimal> values = creation.Json.GetProperty("array").EnumerateArray().Select(value => value.GetDecimal()).ToList();
        return Arrange(attributes, values, priority, problems);
    }

    /// <summary>Given scores, each with a cost in the creation's table, spending no more than its budget.</summary>
    private static Dictionary<string, decimal>? PointBuy(
        RuleSet rules,
        Definition creation,
        List<Definition> attributes,
        IReadOnlyDictionary<string, decimal> given,
        IReadOnlyList<string>? priority,
        List<ModuleDiagnostic> problems)
    {
        Dictionary<string, decimal>? scores = GivenAttributes(attributes, given, priority, problems);
        if (scores is null)
        {
            return null;
        }

        Definition costs = rules.Reference(creation, "$.costs");
        decimal budget = creation.Json.GetProperty("budget").GetDecimal();
        decimal spent = 0;
        foreach ((string id, decimal score) in scores)
        {
            if (rules.Tables[costs].Lookup([Value.Of(score)]) is not Value cost)
            {
                problems.Add(new ModuleDiagnostic("character.point-buy", $"{id} {score} has no cost in {costs.QualifiedId}; choose a score it lists.", costs.Module, costs.File, "$.rows"));
                continue;
            }

            spent += cost.Number;
        }

        if (problems.Count == 0 && spent > budget)
        {
            problems.Add(new ModuleDiagnostic("character.point-buy", $"These scores cost {spent} points, but {creation.QualifiedId} gives {budget}. Lower some scores.", creation.Module, creation.File, "$.budget"));
        }

        return problems.Count > 0 ? null : scores;
    }

    /// <summary>The features creation grants, as the matching in <see cref="Choose"/> will take them: the first of each grant's kind, in order.</summary>
    private static List<Definition> CreationFeatures(Definition creation, List<Definition> choices)
    {
        List<Definition> left = [.. choices];
        List<Definition> taken = [];
        foreach (Grant grant in CreationGrants(creation))
        {
            for (int made = 0; made < grant.Count; made++)
            {
                if (left.FirstOrDefault(grant.Takes) is Definition feature)
                {
                    left.Remove(feature);
                    taken.Add(feature);
                }
            }
        }

        return taken;
    }

    /// <summary>
    /// Every attribute at the creation's base, then each source's boosts in
    /// turn: the race, the creation features, the class and the creation. A
    /// boost with one attribute is fixed; any other takes the next of
    /// <paramref name="chosen"/>, which must be one it offers and not one the
    /// same source already raised.
    /// </summary>
    private static Dictionary<string, decimal>? BoostAttributes(
        RuleSet rules,
        Definition creation,
        List<Definition> attributes,
        Definition race,
        Definition characterClass,
        List<Definition> features,
        IReadOnlyList<string> chosen,
        List<ModuleDiagnostic> problems)
    {
        decimal start = creation.Json.GetProperty("base").GetDecimal();
        decimal boost = creation.Json.GetProperty("boost").GetDecimal();
        Dictionary<string, decimal> scores = attributes.ToDictionary(attribute => attribute.Id, _ => start);
        Queue<string> left = new(chosen);
        foreach (Definition source in new[] { race }.Concat(features).Append(characterClass).Append(creation))
        {
            if (!source.Json.TryGetProperty("boosts", out JsonElement boosts))
            {
                continue;
            }

            HashSet<string> raised = [];
            int index = 0;
            foreach (JsonElement entry in boosts.EnumerateArray())
            {
                string at = $"$.boosts[{index}]";
                index++;
                List<string> offered = entry.TryGetProperty("from", out JsonElement from)
                    ? from.EnumerateArray().Select(attribute => attribute.GetString()!).ToList()
                    : attributes.Select(attribute => attribute.Id).ToList();
                string what = $"{source.Name}'s boost {index} ({(entry.TryGetProperty("from", out _) ? string.Join(" or ", offered) : "any attribute")})";
                string attribute;
                if (offered.Count == 1)
                {
                    attribute = offered[0];
                }
                else if (!left.TryDequeue(out string? next))
                {
                    List<string> open = offered.Where(id => !raised.Contains(id)).ToList();
                    problems.Add(new ModuleDiagnostic("character.boosts", $"{what} needs a choice; add one of {string.Join(", ", open)} to --boosts.", source.Module, source.File, at));
                    return null;
                }
                else if (!offered.Contains(next))
                {
                    problems.Add(new ModuleDiagnostic("character.boosts", $"{what} can't raise '{next}'; it offers {string.Join(", ", offered)}.", source.Module, source.File, at));
                    return null;
                }
                else
                {
                    attribute = next;
                }

                if (!raised.Add(attribute))
                {
                    problems.Add(new ModuleDiagnostic("character.boosts", $"{source.Name} already boosted {attribute}; one source boosts each attribute at most once. Choose another for {what}.", source.Module, source.File, at));
                    return null;
                }

                scores[attribute] += boost;
            }
        }

        if (left.Count > 0)
        {
            problems.Add(new ModuleDiagnostic("character.boosts", $"There are more boosts given than choices to make; nothing takes {string.Join(", ", left)}."));
            return null;
        }

        foreach (Definition attribute in attributes)
        {
            decimal max = attribute.Json.GetProperty("max").GetDecimal();
            if (scores[attribute.Id] > max)
            {
                problems.Add(new ModuleDiagnostic("character.boosts", $"Boosts take {attribute.Id} to {scores[attribute.Id]}, above its maximum {max}."));
            }
        }

        return problems.Count > 0 ? null : scores;
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
        List<Definition> defaults = all.Where(creation => creation.Json.TryGetProperty("default", out JsonElement isDefault) && isDefault.GetBoolean()).ToList();
        if (all.Count == 1 || defaults.Count == 1)
        {
            return all.Count == 1 ? all[0] : defaults[0];
        }

        string message = all.Count == 0
            ? "The module set has no character-creation definition. Add one (see `goldbox schema character-creation`)."
            : $"The module set has more than one character-creation definition ({string.Join(", ", all.Select(definition => definition.QualifiedId))}) and none is the default; choose one by its ID.";
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
