using System.Text.Json;
using RustyTales.Core.Combat;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;
using RustyTales.Core.Rules;

namespace RustyTales.Core.Characters;

/// <summary>How a campaign accepts imported characters from a ruleset stable or external character file.</summary>
public sealed record CharacterImportPolicy(bool AllowHigherLevel = false, int? MaxLevel = null)
{
    public static CharacterImportPolicy Default { get; } = new();

    /// <summary>Reads a campaign's optional <c>import</c> policy, defaulting to level-1 start.</summary>
    public static CharacterImportPolicy FromCampaign(Definition? campaign)
    {
        if (campaign is null || !campaign.Json.TryGetProperty("import", out JsonElement policy) || policy.ValueKind != JsonValueKind.Object)
        {
            return Default;
        }

        bool allowHigher = policy.TryGetProperty("allow_higher_level", out JsonElement allow) && allow.ValueKind == JsonValueKind.True;
        int? maxLevel = policy.TryGetProperty("max_level", out JsonElement max) && max.ValueKind == JsonValueKind.Number && max.TryGetInt32(out int parsed) && parsed >= 1
            ? parsed
            : null;
        return new CharacterImportPolicy(allowHigher, maxLevel);
    }
}

/// <summary>Outcome of a best-effort ruleset-bound character import.</summary>
public sealed record CharacterImportResult(Character? Character, IReadOnlyList<string> Log, IReadOnlyList<ModuleDiagnostic> Problems)
{
    public bool Succeeded => Character is not null && Problems.Count == 0;
}

/// <summary>
/// Best-effort, fail-open character importer: brings a character built under a
/// baseline ruleset into any campaign (or ruleset + extensions) sharing that
/// ruleset. Items, spells, features, stats, portraits and levels are mapped to
/// the closest valid equivalents in the target module set, with every change
/// recorded in <see cref="CharacterImportResult.Log"/>. Only structural errors
/// or a mismatched baseline ruleset refuse import.
/// </summary>
public static class CharacterImport
{
    public static CharacterImportResult ImportFile(string path, ModuleSet targetSet, Definition? campaign = null, CharacterImportPolicy? policy = null)
    {
        List<ModuleDiagnostic> problems = [];
        using JsonDocument? document = JsonFiles.Parse(path, null, problems);
        if (document is null)
        {
            return new CharacterImportResult(null, [], problems);
        }

        return Import(document.RootElement, path, targetSet, campaign, policy);
    }

    public static CharacterImportResult Import(JsonElement root, string sourceLabel, ModuleSet targetSet, Definition? campaign = null, CharacterImportPolicy? policy = null, ModuleStamp? knownRuleset = null)
    {
        List<ModuleDiagnostic> problems = [];
        List<string> log = [];
        RuleSet? rules = targetSet.Rules;
        if (rules is null)
        {
            problems.Add(new ModuleDiagnostic("character.import", "The target module set has no loaded ruleset.", File: sourceLabel));
            return new CharacterImportResult(null, log, problems);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new ModuleDiagnostic("character.import", "Character data must be a JSON object.", File: sourceLabel, JsonPath: "$"));
            return new CharacterImportResult(null, log, problems);
        }

        if (root.TryGetProperty("format", out JsonElement formatElement)
            && (formatElement.ValueKind != JsonValueKind.Number || !formatElement.TryGetDecimal(out decimal format) || format != CharacterFile.CurrentFormat))
        {
            problems.Add(new ModuleDiagnostic("character.import", $"Unsupported character format; expected {CharacterFile.CurrentFormat}.", File: sourceLabel, JsonPath: "$.format"));
            return new CharacterImportResult(null, log, problems);
        }

        if (!root.TryGetProperty("name", out JsonElement nameElement) || nameElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nameElement.GetString()))
        {
            problems.Add(new ModuleDiagnostic("character.import", "Character is missing a valid name.", File: sourceLabel, JsonPath: "$.name"));
            return new CharacterImportResult(null, log, problems);
        }

        string name = nameElement.GetString()!;
        ModuleStamp? targetRuleset = Character.RulesetStampOf(targetSet);
        if (targetRuleset is null)
        {
            problems.Add(new ModuleDiagnostic("character.import", "The target module set does not contain a ruleset module.", File: sourceLabel));
            return new CharacterImportResult(null, log, problems);
        }

        List<ModuleStamp> sourceStamps = ReadStamps(root);
        if (!CheckRulesetCompatibility(targetRuleset, knownRuleset, sourceStamps, sourceLabel, problems))
        {
            return new CharacterImportResult(null, log, problems);
        }

        CharacterImportPolicy effectivePolicy = policy ?? CharacterImportPolicy.FromCampaign(campaign);

        // 1. Creation definition (exact -> local ID -> ruleset default).
        Definition? creation = ResolveOptional(rules, DefinitionTypes.CharacterCreation, StringOf(root, "creation"), "creation", log)
            ?? CharacterRules.DefaultCreation(rules);
        if (creation is null)
        {
            problems.Add(new ModuleDiagnostic("character.import", "The target ruleset declares no character-creation definition.", File: sourceLabel));
            return new CharacterImportResult(null, log, problems);
        }

        if (StringOf(root, "creation") is string rawCreation && creation.QualifiedId != rawCreation && creation.Id != LocalId(rawCreation))
        {
            log.Add($"Mapped creation '{rawCreation}' to '{creation.QualifiedId}'.");
        }

        // 2. Race (raceless -> null; else exact -> local ID -> first valid race).
        List<Definition> availableRaces = rules.OfType(DefinitionTypes.Race).ToList();
        Definition? race = null;
        string? rawRace = StringOf(root, "race");
        if (availableRaces.Count == 0)
        {
            if (rawRace is not null)
            {
                log.Add($"Ignored race '{rawRace}' (target ruleset does not use races).");
            }
        }
        else
        {
            race = ResolveOptional(rules, DefinitionTypes.Race, rawRace, "race", log);
            if (race is null)
            {
                race = availableRaces[0];
                log.Add($"Race '{rawRace ?? "(none)"}' not found in target ruleset; mapped to '{race.Name}'.");
            }
        }

        // 3. Optional lifepath.
        Definition? lifepath = null;
        string? rawLifepath = StringOf(root, "lifepath");
        if (rawLifepath is not null)
        {
            lifepath = ResolveOptional(rules, DefinitionTypes.Lifepath, rawLifepath, "lifepath", log);
            if (lifepath is null)
            {
                log.Add($"Dropped lifepath '{rawLifepath}' (not present in target ruleset).");
            }
        }

        Character character = new()
        {
            Name = name,
            Modules = Character.StampsOf(targetSet),
            Race = race,
            Creation = creation,
            Lifepath = lifepath,
            CombatControlPreference = ReadCombatControl(root),
        };

        // 4. Attributes (keep known, fill missing defaults, clamp to ruleset & race limits).
        ImportAttributes(root, rules, creation, race, character, log);

        // 5. Levels, classes, level policy scaling, and per-level features & boosts.
        ImportLevelsAndExperience(root, rules, race, character, effectivePolicy, log);
        if (character.Levels.Count == 0)
        {
            problems.Add(new ModuleDiagnostic("character.import", "Could not establish a valid starting level for the imported character.", File: sourceLabel));
            return new CharacterImportResult(null, log, problems);
        }

        // 6. Optional lifepath term ledger (only if lifepath matched cleanly).
        if (lifepath is not null)
        {
            ImportLifepathTerms(root, lifepath, character, log);
        }

        // 7. Persistent stat bonuses, skill allocations, marks, and milestone features.
        ImportStatBonuses(root, rules, character, log);
        ImportSkillAllocations(root, rules, character, log);
        ImportSkillMarks(root, rules, character, log);
        ImportMilestoneFeatures(root, rules, character, log);

        // 8. Balances (keep currencies declared in target ruleset).
        ImportBalances(root, rules, character, log);

        // 9. Equipment (best-effort exact -> local ID + class equipment rule check).
        ImportEquipment(root, rules, character, log);

        // 10. Tracks (recompute level-track maximum from kept levels, clamp current, initialize missing).
        ImportTracks(root, rules, character, log);

        // 11. Spells & memorised (re-validate against current level & budgets; reset prepared for fresh campaign).
        ImportSpells(root, rules, character, log);

        // 12. Conditions (drop campaign-transient expiring or unknown conditions).
        ImportConditions(root, rules, character, log);

        // 13. Portrait (exact -> local ID across target art modules -> clear if unavailable).
        ImportPortrait(root, rules, character, log);

        // Campaign-scoped transient fields (npc, perception, forfeits_experience) are intentionally cleared on import.
        if (root.TryGetProperty("perception", out _))
        {
            log.Add("Cleared previous campaign's perception state.");
        }

        return new CharacterImportResult(character, log, problems);
    }

    private static bool CheckRulesetCompatibility(
        ModuleStamp targetRuleset,
        ModuleStamp? knownRuleset,
        List<ModuleStamp> sourceStamps,
        string sourceLabel,
        List<ModuleDiagnostic> problems)
    {
        ModuleStamp? sourceRuleset = knownRuleset ?? sourceStamps.FirstOrDefault(stamp => stamp.Id == targetRuleset.Id);
        if (sourceRuleset is null)
        {
            string listed = sourceStamps.Count == 0 ? "none" : string.Join(", ", sourceStamps.Select(s => $"{s.Id} {s.Version}"));
            problems.Add(new ModuleDiagnostic(
                "character.import.ruleset",
                $"Character belongs to a different ruleset (modules: {listed}); this campaign requires '{targetRuleset.Id} {targetRuleset.Version}'.",
                File: sourceLabel,
                JsonPath: "$.modules"));
            return false;
        }

        if (sourceRuleset.Id != targetRuleset.Id)
        {
            problems.Add(new ModuleDiagnostic(
                "character.import.ruleset",
                $"Character uses ruleset '{sourceRuleset.Id} {sourceRuleset.Version}', which does not match this campaign's ruleset '{targetRuleset.Id} {targetRuleset.Version}'.",
                File: sourceLabel,
                JsonPath: "$.modules"));
            return false;
        }

        if (VersionRange.TryParse($"^{sourceRuleset.Version}", out VersionRange? forward)
            && VersionRange.TryParse($"^{targetRuleset.Version}", out VersionRange? backward)
            && !forward!.Contains(targetRuleset.Version)
            && !backward!.Contains(sourceRuleset.Version))
        {
            problems.Add(new ModuleDiagnostic(
                "character.import.ruleset",
                $"Character ruleset version '{sourceRuleset.Id} {sourceRuleset.Version}' is not compatible with '{targetRuleset.Id} {targetRuleset.Version}'.",
                File: sourceLabel,
                JsonPath: "$.modules"));
            return false;
        }

        return true;
    }

    private static void ImportAttributes(
        JsonElement root,
        RuleSet rules,
        Definition creation,
        Definition? race,
        Character character,
        List<string> log)
    {
        List<Definition> targetAttributes = rules.OfType(DefinitionTypes.Attribute).ToList();
        Dictionary<string, decimal> rawScores = [];
        if (root.TryGetProperty("attributes", out JsonElement given) && given.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entry in given.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetDecimal(out decimal number))
                {
                    rawScores[entry.Name] = number;
                }
            }
        }

        // Preserve source order for known attributes first, then append any newly added target attributes.
        decimal fallbackBase = creation.Json.TryGetProperty("base", out JsonElement baseVal) && baseVal.TryGetDecimal(out decimal parsedBase)
            ? parsedBase
            : 10;

        Dictionary<string, (decimal Min, decimal Max)> raceLimits = [];
        if (race is not null && race.Json.TryGetProperty("ability_limits", out JsonElement limits) && limits.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty limit in limits.EnumerateObject())
            {
                if (limit.Value.ValueKind == JsonValueKind.Array && limit.Value.GetArrayLength() == 2
                    && limit.Value[0].TryGetDecimal(out decimal low) && limit.Value[1].TryGetDecimal(out decimal high))
                {
                    raceLimits[limit.Name] = (low, high);
                }
            }
        }

        List<string> orderedIds = [
            .. rawScores.Keys.Where(id => targetAttributes.Any(attr => attr.Id == id)),
            .. targetAttributes.Select(attr => attr.Id).Where(id => !rawScores.ContainsKey(id)),
        ];

        foreach (string extra in rawScores.Keys.Where(id => targetAttributes.All(attr => attr.Id != id)))
        {
            log.Add($"Dropped attribute '{extra}' (not present in target ruleset).");
        }

        foreach (string id in orderedIds)
        {
            Definition definition = rules.Stats[id].Definition;
            decimal value;
            if (rawScores.TryGetValue(id, out decimal existing))
            {
                value = existing;
            }
            else
            {
                value = definition.Json.TryGetProperty("default", out JsonElement def) && def.TryGetDecimal(out decimal d)
                    ? d
                    : fallbackBase;
                log.Add($"Initialized missing attribute '{id}' to {value}.");
            }

            decimal original = value;
            if (definition.Json.TryGetProperty("min", out JsonElement minEl) && minEl.TryGetDecimal(out decimal minVal) && value < minVal)
            {
                value = minVal;
            }

            if (definition.Json.TryGetProperty("max", out JsonElement maxEl) && maxEl.TryGetDecimal(out decimal maxVal) && value > maxVal)
            {
                value = maxVal;
            }

            if (raceLimits.TryGetValue(id, out (decimal Low, decimal High) bound))
            {
                if (value < bound.Low)
                {
                    value = bound.Low;
                }
                else if (value > bound.High)
                {
                    value = bound.High;
                }
            }

            if (value != original)
            {
                log.Add($"Clamped attribute '{id}' from {original} to {value} (target ruleset/race limit).");
            }

            character.Attributes[id] = value;
        }
    }

    private static void ImportLevelsAndExperience(
        JsonElement root,
        RuleSet rules,
        Definition? race,
        Character character,
        CharacterImportPolicy policy,
        List<string> log)
    {
        List<Definition> availableClasses = rules.OfType(DefinitionTypes.Class).ToList();
        bool classless = availableClasses.Count == 0;
        HashSet<Definition>? raceAllowedClasses = null;
        if (race is not null && race.Json.TryGetProperty("classes", out JsonElement allowed) && allowed.ValueKind == JsonValueKind.Array)
        {
            raceAllowedClasses = [];
            for (int idx = 0; idx < allowed.GetArrayLength(); idx++)
            {
                raceAllowedClasses.Add(rules.Reference(race, $"$.classes[{idx}]"));
            }
        }

        List<(Definition? Class, decimal Gain, List<string> RawFeatures, List<string> RawBoosts)> parsedLevels = [];
        if (root.TryGetProperty("levels", out JsonElement levelsEl) && levelsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement levelEl in levelsEl.EnumerateArray())
            {
                if (levelEl.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                Definition? cls = null;
                if (!classless)
                {
                    string? rawClass = StringOf(levelEl, "class");
                    cls = ResolveOptional(rules, DefinitionTypes.Class, rawClass, "class", log);
                    if (cls is not null && raceAllowedClasses is not null && !raceAllowedClasses.Contains(cls))
                    {
                        Definition fallback = availableClasses.FirstOrDefault(raceAllowedClasses.Contains) ?? availableClasses[0];
                        log.Add($"Class '{cls.Name}' is not allowed for {race!.Name} under target rules; mapped to '{fallback.Name}'.");
                        cls = fallback;
                    }
                    else if (cls is null)
                    {
                        Definition fallback = (raceAllowedClasses is not null ? availableClasses.FirstOrDefault(raceAllowedClasses.Contains) : null) ?? availableClasses[0];
                        log.Add($"Class '{rawClass ?? "(none)"}' not found in target ruleset; mapped to '{fallback.Name}'.");
                        cls = fallback;
                    }
                }

                decimal gain = levelEl.TryGetProperty("gain", out JsonElement gainEl) && gainEl.ValueKind == JsonValueKind.Number && gainEl.TryGetDecimal(out decimal g)
                    ? g
                    : 0;
                List<string> rawFeatures = [];
                if (levelEl.TryGetProperty("features", out JsonElement featEl) && featEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in featEl.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is string featId)
                        {
                            rawFeatures.Add(featId);
                        }
                    }
                }

                List<string> rawBoosts = [];
                if (levelEl.TryGetProperty("boosts", out JsonElement boostEl) && boostEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in boostEl.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is string statId)
                        {
                            rawBoosts.Add(statId);
                        }
                    }
                }

                parsedLevels.Add((cls, gain, rawFeatures, rawBoosts));
            }
        }

        if (parsedLevels.Count == 0)
        {
            Definition? defaultClass = classless
                ? null
                : (raceAllowedClasses is not null ? availableClasses.FirstOrDefault(raceAllowedClasses.Contains) : null) ?? availableClasses[0];
            parsedLevels.Add((defaultClass, 1, [], []));
            log.Add("No valid levels found in source data; initialized at level 1.");
        }

        int sourceLevelCount = parsedLevels.Count;
        int allowedMaxLevel = policy.AllowHigherLevel
            ? (policy.MaxLevel ?? int.MaxValue)
            : 1;

        // Trim higher levels according to campaign import policy (undoing level boosts on trimmed levels).
        bool trimmedByPolicy = false;
        if (parsedLevels.Count > allowedMaxLevel)
        {
            for (int idx = parsedLevels.Count - 1; idx >= allowedMaxLevel; idx--)
            {
                UndoLevelBoosts(rules, character, parsedLevels[idx].RawBoosts);
            }

            parsedLevels.RemoveRange(allowedMaxLevel, parsedLevels.Count - allowedMaxLevel);
            trimmedByPolicy = true;
            log.Add(policy.AllowHigherLevel
                ? $"Scaled character from level {sourceLevelCount} down to level {allowedMaxLevel} (campaign max import level)."
                : $"Scaled character from level {sourceLevelCount} down to level 1 (campaign starts imported characters at level 1).");
        }

        // Also clamp per-class level counts to the target class's defined level table.
        Dictionary<Definition, int> classCounts = [];
        List<(Definition? Class, decimal Gain, List<string> RawFeatures, List<string> RawBoosts)> keptLevels = [];
        foreach (var entry in parsedLevels)
        {
            if (entry.Class is Definition cls)
            {
                int maxForClass = cls.Json.GetProperty("levels").GetArrayLength();
                int nextCount = classCounts.GetValueOrDefault(cls) + 1;
                if (nextCount > maxForClass)
                {
                    UndoLevelBoosts(rules, character, entry.RawBoosts);
                    log.Add($"Trimmed excess level in '{cls.Name}' (target ruleset defines {maxForClass} levels).");
                    continue;
                }

                classCounts[cls] = nextCount;
            }

            keptLevels.Add(entry);
        }

        // Replay grants & boosts level by level so the resulting character passes CharacterRules.CheckHistory cleanly.
        Evaluator evaluator = new(rules, null);
        for (int index = 0; index < keptLevels.Count; index++)
        {
            var entry = keptLevels[index];
            character.Levels.Add(new LevelTaken(entry.Class, entry.Gain, []));

            // Resolve candidate features from the source level.
            List<Definition> candidates = [];
            foreach (string rawFeat in entry.RawFeatures)
            {
                Definition? resolved = ResolveOptional(rules, DefinitionTypes.Feature, rawFeat, "feature", log);
                if (resolved is null)
                {
                    log.Add($"Dropped feature '{rawFeat}' (not present in target ruleset).");
                }
                else
                {
                    candidates.Add(resolved);
                }
            }

            List<Grant> grants = [
                .. index == 0 ? CharacterRules.CreationGrants(character.Creation, character.Lifepath is null) : [],
                .. CharacterRules.LevelGrants(rules, character, evaluator),
            ];
            List<Definition> accepted = [];
            foreach (Grant grant in grants)
            {
                for (int slot = 0; slot < grant.Count; slot++)
                {
                    Definition? picked = candidates.FirstOrDefault(candidate =>
                        grant.Takes(candidate) && CharacterRules.Problem(rules, character, candidate, evaluator) is null);
                    if (picked is not null)
                    {
                        candidates.Remove(picked);
                    }
                    else
                    {
                        // Fail-open: choose the first valid feature of the required kind so the imported character remains legal.
                        picked = rules.OfType(DefinitionTypes.Feature)
                            .FirstOrDefault(candidate => grant.Takes(candidate) && CharacterRules.Problem(rules, character, candidate, evaluator) is null);
                        if (picked is not null)
                        {
                            log.Add($"Level {index + 1}: assigned default {grant.KindText} '{picked.Name}' to satisfy target ruleset grant.");
                        }
                    }

                    if (picked is not null)
                    {
                        accepted.Add(picked);
                        character.Levels[^1] = character.Levels[^1] with { Features = [.. accepted] };
                    }
                }
            }

            foreach (Definition unused in candidates)
            {
                log.Add($"Dropped extra/ineligible feature '{unused.Name}' at level {index + 1}.");
            }

            int expectedBoosts = CharacterRules.BoostsGranted(rules, character, evaluator);
            List<string> validBoosts = entry.RawBoosts
                .Where(statId => rules.Stats.TryGetValue(statId, out Stat? stat) && stat.IsAttribute)
                .Take(expectedBoosts)
                .ToList();
            if (validBoosts.Count != entry.RawBoosts.Count)
            {
                log.Add($"Adjusted level {index + 1} attribute boosts to match target ruleset ({validBoosts.Count} kept).");
            }

            character.Levels[^1] = new LevelTaken(entry.Class, entry.Gain, accepted) { Boosts = validBoosts };
        }

        // Experience & split-class experience.
        decimal rawXp = root.TryGetProperty("experience", out JsonElement xpEl) && xpEl.ValueKind == JsonValueKind.Number && xpEl.TryGetDecimal(out decimal xp) && xp >= 0
            ? xp
            : 0;

        if (!policy.AllowHigherLevel && (trimmedByPolicy || rawXp > 0))
        {
            character.Experience = 0;
            if (!trimmedByPolicy && rawXp > 0)
            {
                log.Add($"Reset experience from {rawXp} to 0 (campaign starts imported characters at level 1).");
            }
        }
        else if (trimmedByPolicy)
        {
            decimal? capXp = LevelThresholdExperience(rules, character);
            character.Experience = capXp is decimal threshold ? Math.Min(rawXp, threshold) : 0;
        }
        else
        {
            character.Experience = rawXp;
        }

        if (rules.ExperienceSplit)
        {
            Dictionary<Definition, int> keptClassLevels = character.ClassLevels();
            foreach (Definition cls in keptClassLevels.Keys)
            {
                character.ClassExperience[cls] = 0;
            }

            if (policy.AllowHigherLevel && !trimmedByPolicy
                && root.TryGetProperty("class_experience", out JsonElement classXpEl) && classXpEl.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty entry in classXpEl.EnumerateObject())
                {
                    Definition? cls = ResolveOptional(rules, DefinitionTypes.Class, entry.Name, "class", log);
                    if (cls is not null && keptClassLevels.ContainsKey(cls)
                        && entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetDecimal(out decimal pts) && pts >= 0)
                    {
                        character.ClassExperience[cls] = pts;
                    }
                }
            }
        }

        // Left classes (dual-classing).
        if (policy.AllowHigherLevel && !trimmedByPolicy
            && root.TryGetProperty("left_classes", out JsonElement leftEl) && leftEl.ValueKind == JsonValueKind.Array)
        {
            Dictionary<Definition, int> keptClassLevels = character.ClassLevels();
            foreach (JsonElement item in leftEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is string rawLeft)
                {
                    Definition? cls = ResolveOptional(rules, DefinitionTypes.Class, rawLeft, "class", log);
                    if (cls is not null && keptClassLevels.ContainsKey(cls))
                    {
                        character.LeftClasses.Add(cls);
                    }
                }
            }

            if (root.TryGetProperty("uses_former_classes", out JsonElement formerEl) && formerEl.ValueKind == JsonValueKind.True && character.HasDormantClasses())
            {
                character.UsesFormerClasses = true;
            }
        }
    }

    private static void UndoLevelBoosts(RuleSet rules, Character character, IReadOnlyList<string> boosts)
    {
        foreach (string boosted in boosts.Reverse())
        {
            if (!character.Attributes.TryGetValue(boosted, out decimal after)
                || !rules.Stats.TryGetValue(boosted, out Stat? stat)
                || !stat.IsAttribute)
            {
                continue;
            }

            decimal min = stat.Definition.Json.TryGetProperty("min", out JsonElement minEl) && minEl.TryGetDecimal(out decimal m) ? m : 1;
            for (decimal score = after - 1; score >= min; score--)
            {
                if (CharacterRules.BoostAmount(rules, score) is decimal amount && score + amount == after)
                {
                    character.Attributes[boosted] = score;
                    break;
                }
            }
        }
    }

    private static decimal? LevelThresholdExperience(RuleSet rules, Character character)
    {
        if (rules.AdvancementKind != "experience" || rules.ExperienceSplit || character.Level <= 1)
        {
            return 0;
        }

        if (rules.ExperienceByCharacter && rules.Advancement is Definition advancement)
        {
            JsonElement levels = advancement.Json.GetProperty("levels");
            int idx = Math.Min(character.Level - 1, levels.GetArrayLength() - 1);
            return levels[idx].GetDecimal();
        }

        if (character.Class is Definition cls)
        {
            JsonElement levels = cls.Json.GetProperty("levels");
            int idx = Math.Min(character.Level - 1, levels.GetArrayLength() - 1);
            if (levels[idx].TryGetProperty("xp", out JsonElement xpEl))
            {
                return xpEl.GetDecimal();
            }
        }

        return 0;
    }

    private static void ImportLifepathTerms(JsonElement root, Definition lifepath, Character character, List<string> log)
    {
        int startAge = lifepath.Json.GetProperty("start_age").GetInt32();
        character.Age = root.TryGetProperty("age", out JsonElement ageEl) && ageEl.TryGetInt32(out int years) && years >= startAge
            ? years
            : startAge;
        HashSet<string> validCareers = lifepath.Json.GetProperty("careers")
            .EnumerateArray()
            .Select(c => c.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        if (!root.TryGetProperty("career_terms", out JsonElement termsEl) || termsEl.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        int number = 1;
        int currentAge = startAge;
        foreach (JsonElement term in termsEl.EnumerateArray())
        {
            if (term.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? career = StringOf(term, "career");
            if (career is null || !validCareers.Contains(career))
            {
                log.Add($"Dropped lifepath career term '{career ?? "?"}' (career not in target lifepath).");
                continue;
            }

            int ageBefore = currentAge;
            int ageAfter = term.TryGetProperty("age_after", out JsonElement afterEl) && afterEl.TryGetInt32(out int after) && after >= ageBefore
                ? after
                : ageBefore + lifepath.Json.GetProperty("term_years").GetInt32();
            int rankBefore = term.TryGetProperty("rank_before", out JsonElement rb) && rb.TryGetInt32(out int rbi) && rbi >= 0 ? rbi : 0;
            int rankAfter = term.TryGetProperty("rank_after", out JsonElement ra) && ra.TryGetInt32(out int rai) && rai >= 0 ? rai : rankBefore;
            bool ended = term.TryGetProperty("ended", out JsonElement endedEl) && endedEl.ValueKind == JsonValueKind.True;
            bool benefitsLost = term.TryGetProperty("benefits_lost", out JsonElement lostEl) && lostEl.ValueKind == JsonValueKind.True;
            List<string> choices = ReadStringArray(term, "choices");
            List<string> results = ReadStringArray(term, "results");
            character.CareerTerms.Add(new LifepathTerm(career, number++, ageBefore, ageAfter, rankBefore, rankAfter, null, null, null, null, null, ended, benefitsLost, choices, results));
            currentAge = ageAfter;
        }

        character.Age = currentAge;
        character.LifepathEnded = character.CareerTerms.Count > 0;
    }

    private static void ImportStatBonuses(JsonElement root, RuleSet rules, Character character, List<string> log)
    {
        if (!root.TryGetProperty("stat_bonuses", out JsonElement bonuses) || bonuses.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty entry in bonuses.EnumerateObject())
        {
            if (!rules.Stats.ContainsKey(entry.Name))
            {
                log.Add($"Dropped stat bonus '{entry.Name}' (stat not in target ruleset).");
                continue;
            }

            if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetDecimal(out decimal bonus))
            {
                character.StatBonuses[entry.Name] = bonus;
            }
        }
    }

    private static void ImportSkillAllocations(JsonElement root, RuleSet rules, Character character, List<string> log)
    {
        character.SkillPointsCommitted = root.TryGetProperty("skill_points_committed", out JsonElement committed) && committed.ValueKind == JsonValueKind.True;
        if (root.TryGetProperty("skill_allocations", out JsonElement allocs) && allocs.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entry in allocs.EnumerateObject())
            {
                if (!rules.Stats.TryGetValue(entry.Name, out Stat? stat) || stat.IsAttribute)
                {
                    log.Add($"Dropped skill allocation '{entry.Name}' (not a derived skill in target ruleset).");
                    continue;
                }

                if (entry.Value.ValueKind == JsonValueKind.Object
                    && entry.Value.TryGetProperty("profession", out JsonElement profEl) && profEl.TryGetDecimal(out decimal prof) && prof >= 0
                    && entry.Value.TryGetProperty("personal", out JsonElement persEl) && persEl.TryGetDecimal(out decimal pers) && pers >= 0)
                {
                    character.SkillAllocations[entry.Name] = new SkillAllocation(entry.Name, prof, pers);
                }
            }
        }
    }

    private static void ImportSkillMarks(JsonElement root, RuleSet rules, Character character, List<string> log)
    {
        if (!root.TryGetProperty("skill_marks", out JsonElement marks) || marks.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty entry in marks.EnumerateObject())
        {
            if (!rules.Stats.ContainsKey(entry.Name))
            {
                log.Add($"Dropped skill mark '{entry.Name}' (not in target ruleset).");
                continue;
            }

            if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out int count) && count >= 0)
            {
                character.SkillMarks[entry.Name] = count;
            }
        }
    }

    private static void ImportMilestoneFeatures(JsonElement root, RuleSet rules, Character character, List<string> log)
    {
        if (!root.TryGetProperty("milestone_features", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not string raw)
            {
                continue;
            }

            Definition? feature = ResolveOptional(rules, DefinitionTypes.Feature, raw, "milestone feature", log);
            if (feature is null)
            {
                log.Add($"Dropped milestone feature '{raw}' (not in target ruleset).");
            }
            else
            {
                character.MilestoneFeatures.Add(feature);
            }
        }
    }

    private static void ImportBalances(JsonElement root, RuleSet rules, Character character, List<string> log)
    {
        if (!root.TryGetProperty("balances", out JsonElement balances) || balances.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty entry in balances.EnumerateObject())
        {
            if (!rules.Currencies.ContainsKey(entry.Name))
            {
                log.Add($"Dropped currency '{entry.Name}' (not used by target ruleset).");
                continue;
            }

            if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetDecimal(out decimal amount) && amount >= 0)
            {
                character.Balances[entry.Name] = amount;
            }
        }
    }

    private static void ImportEquipment(JsonElement root, RuleSet rules, Character character, List<string> log)
    {
        if (!root.TryGetProperty("equipment", out JsonElement equipment) || equipment.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement entry in equipment.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String || entry.GetString() is not string raw)
            {
                continue;
            }

            Definition? item = ResolveOptional(rules, DefinitionTypes.Item, raw, "item", log);
            if (item is null)
            {
                log.Add($"Could not import item '{raw}' (not present in this campaign's modules).");
                continue;
            }

            if (CharacterRules.EquipmentProblem(rules, character, item) is string refused)
            {
                log.Add($"Dropped item '{item.Name}' ({refused}).");
                continue;
            }

            character.Equipment.Add(item);
        }
    }

    private static void ImportTracks(JsonElement root, RuleSet rules, Character character, List<string> log)
    {
        Evaluator evaluator = new(rules, null);

        // Rebuild level-track own maximum directly from the kept level gains so scaling levels adjusts HP accurately.
        if (rules.LevelTrack is Definition levelTrack)
        {
            decimal keptMax = character.Levels.Sum(level => level.Gain);
            character.Tracks[levelTrack.Id] = new TrackValue { Max = keptMax };
        }

        CharacterRules.StartTracks(rules, character, evaluator);

        if (!root.TryGetProperty("tracks", out JsonElement given) || given.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        Creature creature = character.ToCreature();
        foreach (JsonProperty entry in given.EnumerateObject())
        {
            if (!rules.Tracks.TryGetValue(entry.Name, out Definition? track))
            {
                log.Add($"Dropped track '{entry.Name}' (not present in target ruleset).");
                continue;
            }

            if (entry.Value.ValueKind != JsonValueKind.Object
                || !entry.Value.TryGetProperty("current", out JsonElement curEl)
                || curEl.ValueKind != JsonValueKind.Number
                || !curEl.TryGetDecimal(out decimal savedCurrent))
            {
                continue;
            }

            decimal? computedMax = evaluator.KnownTrackMax(creature, track);
            decimal clamped = savedCurrent;
            if (computedMax is decimal maxVal && clamped > maxVal)
            {
                clamped = maxVal;
                log.Add($"Clamped {track.Name} from {savedCurrent} to {clamped} (target max {maxVal}).");
            }

            character.Tracks[track.Id].Current = clamped;
        }
    }

    private static void ImportSpells(JsonElement root, RuleSet rules, Character character, List<string> log)
    {
        if (root.TryGetProperty("spells", out JsonElement spells) && spells.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in spells.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String || entry.GetString() is not string raw)
                {
                    continue;
                }

                Definition? spell = ResolveOptional(rules, DefinitionTypes.Spell, raw, "spell", log);
                if (spell is null)
                {
                    log.Add($"Dropped spell '{raw}' (not present in target modules).");
                    continue;
                }

                if (CharacterRules.SpellProblem(rules, character, spell) is string refused)
                {
                    log.Add($"Dropped spell '{spell.Name}' ({refused}).");
                    continue;
                }

                if (!character.Spells.Contains(spell))
                {
                    character.Spells.Add(spell);
                }
            }
        }

        if (root.TryGetProperty("memorised", out JsonElement memorised) && memorised.ValueKind == JsonValueKind.Array)
        {
            Dictionary<Definition, decimal> budgets = CharacterRules.Budgets(rules, character);
            foreach (JsonElement entry in memorised.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String || entry.GetString() is not string raw)
                {
                    continue;
                }

                Definition? spell = ResolveOptional(rules, DefinitionTypes.Spell, raw, "memorised spell", log);
                if (spell is null)
                {
                    continue;
                }

                if (CharacterRules.MemorisedProblem(rules, character, spell, budgets) is string refused)
                {
                    log.Add($"Unmemorised '{spell.Name}' ({refused}).");
                    continue;
                }

                character.Memorised.Add(spell);
            }
        }

        // Entering a new campaign refreshes prepared spell copies.
        character.Prepared = null;
    }

    private static void ImportConditions(JsonElement root, RuleSet rules, Character character, List<string> log)
    {
        if (!root.TryGetProperty("conditions", out JsonElement conditions) || conditions.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        HashSet<string> expiring = [];
        if (root.TryGetProperty("condition_expiry", out JsonElement expiry) && expiry.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entry in expiry.EnumerateObject())
            {
                expiring.Add(entry.Name);
            }
        }

        foreach (JsonElement entry in conditions.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String || entry.GetString() is not string raw)
            {
                continue;
            }

            if (expiring.Contains(raw))
            {
                log.Add($"Cleared temporary campaign condition '{raw}'.");
                continue;
            }

            Definition? condition = ResolveOptional(rules, DefinitionTypes.Condition, raw, "condition", log);
            if (condition is null)
            {
                log.Add($"Cleared condition '{raw}' (not present in target modules).");
                continue;
            }

            if (!character.Conditions.Contains(condition))
            {
                character.Conditions.Add(condition);
            }
        }
    }

    private static void ImportPortrait(JsonElement root, RuleSet rules, Character character, List<string> log)
    {
        string? rawPortrait = StringOf(root, "portrait");
        if (rawPortrait is null)
        {
            return;
        }

        Definition? asset = ResolveOptional(rules, DefinitionTypes.Asset, rawPortrait, "portrait", log);
        if (asset is not null && CharacterRules.PortraitProblem(asset) is null)
        {
            character.Portrait = asset;
            return;
        }

        log.Add($"Portrait '{rawPortrait}' is not available in this campaign's art modules (cleared).");
    }

    private static Definition? ResolveOptional(RuleSet rules, DefinitionType type, string? rawRef, string kindLabel, List<string> log)
    {
        if (string.IsNullOrWhiteSpace(rawRef))
        {
            return null;
        }

        if (rules.Find(type, rawRef, out _) is Definition exact)
        {
            return exact;
        }

        string local = LocalId(rawRef);
        Definition? byLocal = rules.OfType(type).FirstOrDefault(definition => definition.Id == local);
        if (byLocal is not null)
        {
            log.Add($"Remapped {kindLabel} '{rawRef}' to '{byLocal.QualifiedId}'.");
            return byLocal;
        }

        return null;
    }

    private static List<ModuleStamp> ReadStamps(JsonElement root)
    {
        List<ModuleStamp> stamps = [];
        if (!root.TryGetProperty("modules", out JsonElement modules) || modules.ValueKind != JsonValueKind.Array)
        {
            return stamps;
        }

        foreach (JsonElement entry in modules.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && StringOf(entry, "id") is string id
                && StringOf(entry, "version") is string versionText
                && ModuleVersion.TryParse(versionText, out ModuleVersion version))
            {
                stamps.Add(new ModuleStamp(id, version));
            }
        }

        return stamps;
    }

    private static CombatControlMode? ReadCombatControl(JsonElement root)
    {
        return StringOf(root, "combat_control") switch
        {
            "automatic" => CombatControlMode.Automatic,
            "manual" => CombatControlMode.Manual,
            _ => null,
        };
    }

    private static List<string> ReadStringArray(JsonElement owner, string name)
    {
        List<string> list = [];
        if (owner.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is string text)
                {
                    list.Add(text);
                }
            }
        }

        return list;
    }

    private static string? StringOf(JsonElement owner, string name)
    {
        return owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string LocalId(string reference)
    {
        int colon = reference.IndexOf(':');
        return colon >= 0 ? reference[(colon + 1)..] : reference;
    }
}
