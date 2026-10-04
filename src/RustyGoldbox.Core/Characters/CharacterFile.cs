using System.Text.Encodings.Web;
using System.Text.Json;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Characters;

/// <summary>
/// Reads and writes a character as JSON. The file names the modules it was
/// made under; each must be loaded at a compatible version (same major
/// version, or same minor below 1.0.0). Extra modules, such as a campaign
/// that requires the ruleset, are fine.
/// </summary>
public static class CharacterFile
{
    public const int CurrentFormat = 1;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly IReadOnlyList<Definitions.Field> LifepathRollFields =
    [
        new("kind", new Definitions.TextKind(), true, "Qualification, survival, commission, advancement or ageing."),
        new("roll", new Definitions.NumberKind(), true, "Raw dice result."),
        new("modifier", new Definitions.NumberKind(), true, "Stat and fixed modifiers included in the total."),
        new("total", new Definitions.NumberKind(), true, "Final result."),
        new("target", new Definitions.NumberKind(), true, "Target for a success check, or 0 for ageing."),
        new("success", new Definitions.BooleanKind(), true, "Whether the check succeeded."),
    ];

    public static IReadOnlyList<Definitions.Field> DataFields { get; } =
    [
        new("name", new Definitions.TextKind(), true, "Character name."),
        new("race", new Definitions.ReferenceKind("race"), false, "Race; omit in a ruleset without races."),
        new("creation", new Definitions.ReferenceKind("character-creation"), true, "The creation rules that granted its first level choices."),
        new("lifepath", new Definitions.ReferenceKind("lifepath"), false, "The term-by-term career rules used during creation."),
        new("age", new Definitions.IntegerKind(), false, "Age after the recorded career terms."),
        new("career_terms", new Definitions.ListKind(new Definitions.ObjectKind(
        [
            new("career", new Definitions.TextKind(), true, "Career ID within the lifepath."),
            new("number", new Definitions.IntegerKind(), true, "Term number, starting at 1."),
            new("age_before", new Definitions.IntegerKind(), true, "Age before this term."),
            new("age_after", new Definitions.IntegerKind(), true, "Age after this term."),
            new("rank_before", new Definitions.IntegerKind(), true, "Career rank before the term."),
            new("rank_after", new Definitions.IntegerKind(), true, "Career rank after the term."),
            new("qualification", new Definitions.ObjectKind(LifepathRollFields), false, "Qualification roll result."),
            new("survival", new Definitions.ObjectKind(LifepathRollFields), false, "Survival roll result."),
            new("commission", new Definitions.ObjectKind(LifepathRollFields), false, "Commission roll result."),
            new("advancement", new Definitions.ObjectKind(LifepathRollFields), false, "Advancement roll result."),
            new("aging", new Definitions.ObjectKind(LifepathRollFields), false, "Ageing roll result."),
            new("ended", new Definitions.BooleanKind(), true, "Whether this term ended prior history."),
            new("benefits_lost", new Definitions.BooleanKind(), true, "Whether this term contributes no benefits."),
            new("choices", new Definitions.ListKind(new Definitions.TextKind()), true, "Table choices supplied while resolving the term."),
            new("results", new Definitions.ListKind(new Definitions.TextKind()), true, "Human-readable gains and outcomes from the term."),
        ])), false, "Persisted term ledger; it explains the actual rolls and choices that produced the character."),
        new("levels", new Definitions.ListKind(new Definitions.ObjectKind(
        [
            new("class", new Definitions.ReferenceKind("class"), false, "Class taken; omit in a classless ruleset."),
            new("gain", new Definitions.NumberKind(), true, "Recorded level track gain, before hp_bonus."),
            new("features", new Definitions.ListKind(new Definitions.ReferenceKind("feature")), false, "Choices granted at this level."),
            new("boosts", new Definitions.ListKind(new Definitions.StatKind(true)), false, "Attributes boosted at this level."),
        ])), true, "Levels in order, as exported from a character."),
        new("class_experience", new Definitions.MapKind(new Definitions.ReferenceKind("class"), new Definitions.NumberKind()), false, "Experience per class for split advancement."),
        new("left_classes", new Definitions.ListKind(new Definitions.ReferenceKind("class")), false, "Classes left by a class change."),
        new("experience", new Definitions.NumberKind(), true, "Total experience."),
        new("attributes", new Definitions.MapKind(new Definitions.StatKind(true), new Definitions.NumberKind()), true, "Final attribute scores, including racial adjustments and boosts."),
        new("stat_bonuses", new Definitions.MapKind(new Definitions.StatKind(false), new Definitions.NumberKind()), false, "Persistent bonuses from staged creation, milestones or improvement checks."),
        new("skill_allocations", new Definitions.MapKind(new Definitions.StatKind(false), new Definitions.ObjectKind(
        [
            new("profession", new Definitions.NumberKind(), true, "Profession points spent on the skill."),
            new("personal", new Definitions.NumberKind(), true, "Personal points spent on the skill."),
        ])), false, "Profession and personal points already committed by staged creation."),
        new("skill_point_options", new Definitions.ObjectKind(
        [
            new("profession", new Definitions.NumberKind(), true, "The evaluated profession budget from the first creation stage."),
            new("personal", new Definitions.NumberKind(), true, "The evaluated personal budget from the first creation stage."),
            new("skills", new Definitions.ListKind(new Definitions.ObjectKind(
            [
                new("id", new Definitions.TextKind(), true, "Derived skill ID."),
                new("base", new Definitions.NumberKind(), true, "The evaluated skill base from the first creation stage."),
                new("current", new Definitions.NumberKind(), true, "The evaluated current skill value from the first creation stage."),
                new("profession", new Definitions.BooleanKind(), true, "Whether the selected profession allows points on this skill."),
            ])), true, "The evaluated skill choices."),
        ]), false, "Evaluated staged skill budgets, bases and profession eligibility."),
        new("skill_points_committed", new Definitions.BooleanKind(), false, "Whether the staged skill choices have been committed."),
        new("skill_marks", new Definitions.MapKind(new Definitions.StatKind(false), new Definitions.IntegerKind()), false, "Successful skill uses waiting for an improvement check."),
        new("milestone_features", new Definitions.ListKind(new Definitions.ReferenceKind("feature")), false, "Features granted by milestones outside class levels."),
        new("tracks", new Definitions.MapKind(new Definitions.TextKind(), new Definitions.ObjectKind(
        [
            new("current", new Definitions.NumberKind(), true, "Current track value."),
            new("max", new Definitions.NumberKind(), false, "Own maximum for the level track; other maxima come from data."),
        ])), true, "All track values by their shared stat IDs."),
        new("balances", new Definitions.MapKind(new Definitions.TextKind(), new Definitions.NumberKind()), true, "Money carried, by declared currency ID; an empty object is valid when the ruleset has no currencies."),
        new("equipment", new Definitions.ListKind(new Definitions.ReferenceKind("item")), true, "Carried equipment."),
        new("spells", new Definitions.ListKind(new Definitions.ReferenceKind("spell")), false, "Known spells."),
        new("memorised", new Definitions.ListKind(new Definitions.ReferenceKind("spell")), false, "Memorised copies."),
        new("prepared", new Definitions.ListKind(new Definitions.ReferenceKind("spell")), false, "Unspent prepared copies; omit for the full plan."),
        new("conditions", new Definitions.ListKind(new Definitions.ReferenceKind("condition")), true, "Held conditions."),
        new("portrait", new Definitions.ReferenceKind("asset", "portrait"), false, "Portrait art."),
        new("perception", new Definitions.ObjectKind(
        [
            new("scope", new Definitions.TextKind(), true, "The module-authored expedition scope."),
            new("mode", new Definitions.TextKind(), true, "The module-authored result mode."),
        ]), false, "The member's persisted perception result for the current expedition."),
        new("uses_former_classes", new Definitions.BooleanKind(), false, "Calling on dormant classes."),
        new("forfeits_experience", new Definitions.BooleanKind(), false, "Experience forfeited this adventure."),
        new("combat_control", new Definitions.EnumKind(["automatic", "manual"]), false, "Preferred controller for the next combat; omit to use the campaign or host default."),
    ];

    private static readonly string[] Fields = ["format", "modules", "npc", .. DataFields.Select(field => field.Name)];

    public static string ToJson(Character character)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, WriterOptions))
        {
            Write(writer, character);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>Writes the character as one JSON object, for a file of its own or inside a save.</summary>
    public static void Write(Utf8JsonWriter writer, Character character)
    {
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", CurrentFormat);
            writer.WriteString("name", character.Name);
            if (character.Npc is Definition npc)
            {
                writer.WriteString("npc", npc.QualifiedId);
            }
            writer.WriteStartArray("modules");
            foreach (ModuleStamp module in character.Modules)
            {
                writer.WriteStartObject();
                writer.WriteString("id", module.Id);
                writer.WriteString("version", module.Version.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (character.Race is Definition race)
            {
                writer.WriteString("race", race.QualifiedId);
            }

            writer.WriteString("creation", character.Creation.QualifiedId);
            if (character.Lifepath is Definition lifepath)
            {
                writer.WriteString("lifepath", lifepath.QualifiedId);
                writer.WriteNumber("age", character.Age);
                writer.WriteStartArray("career_terms");
                foreach (LifepathTerm term in character.CareerTerms)
                {
                    writer.WriteStartObject();
                    writer.WriteString("career", term.Career);
                    writer.WriteNumber("number", term.Number);
                    writer.WriteNumber("age_before", term.AgeBefore);
                    writer.WriteNumber("age_after", term.AgeAfter);
                    writer.WriteNumber("rank_before", term.RankBefore);
                    writer.WriteNumber("rank_after", term.RankAfter);
                    WriteLifepathRoll(writer, "qualification", term.Qualification);
                    WriteLifepathRoll(writer, "survival", term.Survival);
                    WriteLifepathRoll(writer, "commission", term.Commission);
                    WriteLifepathRoll(writer, "advancement", term.Advancement);
                    WriteLifepathRoll(writer, "aging", term.Aging);
                    writer.WriteBoolean("ended", term.Ended);
                    writer.WriteBoolean("benefits_lost", term.BenefitsLost);
                    WriteStrings(writer, "choices", term.Choices);
                    WriteStrings(writer, "results", term.Results);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }
            writer.WriteStartArray("levels");
            foreach (LevelTaken level in character.Levels)
            {
                writer.WriteStartObject();
                if (level.Class is Definition levelClass)
                {
                    writer.WriteString("class", levelClass.QualifiedId);
                }

                writer.WriteNumber("gain", level.Gain);
                if (level.Features.Count > 0)
                {
                    WriteReferences(writer, "features", level.Features);
                }

                if (level.Boosts.Count > 0)
                {
                    writer.WriteStartArray("boosts");
                    foreach (string boost in level.Boosts)
                    {
                        writer.WriteStringValue(boost);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteNumber("experience", character.Experience);
            if (character.ClassExperience.Count > 0)
            {
                writer.WriteStartObject("class_experience");
                foreach ((Definition characterClass, decimal points) in character.ClassExperience)
                {
                    writer.WriteNumber(characterClass.QualifiedId, points);
                }

                writer.WriteEndObject();
            }

            if (character.LeftClasses.Count > 0)
            {
                WriteReferences(writer, "left_classes", character.LeftClasses);
            }

            writer.WriteStartObject("attributes");
            foreach ((string id, decimal score) in character.Attributes)
            {
                writer.WriteNumber(id, score);
            }

            writer.WriteEndObject();
            if (character.StatBonuses.Count > 0)
            {
                writer.WriteStartObject("stat_bonuses");
                foreach ((string id, decimal bonus) in character.StatBonuses)
                {
                    writer.WriteNumber(id, bonus);
                }

                writer.WriteEndObject();
            }

            if (character.SkillAllocations.Count > 0)
            {
                writer.WriteStartObject("skill_allocations");
                foreach ((string id, SkillAllocation allocation) in character.SkillAllocations)
                {
                    writer.WriteStartObject(id);
                    writer.WriteNumber("profession", allocation.Profession);
                    writer.WriteNumber("personal", allocation.Personal);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            if (character.SkillPointData is SkillPointOptions options)
            {
                writer.WriteStartObject("skill_point_options");
                writer.WriteNumber("profession", options.Profession);
                writer.WriteNumber("personal", options.Personal);
                writer.WriteStartArray("skills");
                foreach (SkillPointOption skill in options.Skills)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", skill.Skill);
                    writer.WriteNumber("base", skill.Base);
                    writer.WriteNumber("current", skill.Current);
                    writer.WriteBoolean("profession", skill.ProfessionAllowed);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                if (character.SkillPointsCommitted)
                {
                    writer.WriteBoolean("skill_points_committed", true);
                }
            }

            if (character.SkillMarks.Count > 0)
            {
                writer.WriteStartObject("skill_marks");
                foreach ((string id, int marks) in character.SkillMarks)
                {
                    writer.WriteNumber(id, marks);
                }

                writer.WriteEndObject();
            }

            if (character.MilestoneFeatures.Count > 0)
            {
                WriteReferences(writer, "milestone_features", character.MilestoneFeatures);
            }

            writer.WriteStartObject("tracks");
            foreach ((string id, TrackValue value) in character.Tracks)
            {
                writer.WriteStartObject(id);
                if (value.Current is decimal current)
                {
                    writer.WriteNumber("current", current);
                }

                if (value.Max is decimal max)
                {
                    writer.WriteNumber("max", max);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteStartObject("balances");
            foreach ((string currency, decimal amount) in character.Balances.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WriteNumber(currency, amount);
            }

            writer.WriteEndObject();
            WriteReferences(writer, "equipment", character.Equipment);
            if (character.Spells.Count > 0)
            {
                WriteReferences(writer, "spells", character.Spells);
            }

            if (character.UsesFormerClasses)
            {
                writer.WriteBoolean("uses_former_classes", true);
            }

            if (character.ForfeitsExperience)
            {
                writer.WriteBoolean("forfeits_experience", true);
            }

            if (character.Memorised.Count > 0)
            {
                WriteReferences(writer, "memorised", character.Memorised);
            }

            if (character.Prepared is List<Definition> prepared)
            {
                WriteReferences(writer, "prepared", prepared);
            }

            WriteReferences(writer, "conditions", character.Conditions);
            if (character.Portrait is Definition portrait)
            {
                writer.WriteString("portrait", portrait.QualifiedId);
            }

            if (character.CombatControlPreference is CombatControlMode preference)
            {
                writer.WriteString("combat_control", preference == CombatControlMode.Manual ? "manual" : "automatic");
            }

            if (character.Perception is PerceptionState perception)
            {
                writer.WriteStartObject("perception");
                writer.WriteString("scope", perception.Scope);
                writer.WriteString("mode", perception.Mode);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }
    }

    /// <summary>Reads a character file against a loaded module set. Problems name the file and JSON path.</summary>
    public static Character? Read(string path, ModuleSet set, List<ModuleDiagnostic> problems)
    {
        using JsonDocument? document = JsonFiles.Parse(path, null, problems);
        if (document is null)
        {
            return null;
        }

        return new Reader(path, set, problems, "$").Read(document.RootElement);
    }

    /// <summary>Reads a character from JSON inside another file, such as a save; problems are located at <paramref name="at"/>.</summary>
    public static Character? Read(JsonElement root, string file, string at, ModuleSet set, List<ModuleDiagnostic> problems)
    {
        return new Reader(file, set, problems, at).Read(root);
    }

    private static void WriteReferences(Utf8JsonWriter writer, string name, IEnumerable<Definition> definitions)
    {
        writer.WriteStartArray(name);
        foreach (Definition definition in definitions)
        {
            writer.WriteStringValue(definition.QualifiedId);
        }

        writer.WriteEndArray();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteLifepathRoll(Utf8JsonWriter writer, string name, LifepathRoll? roll)
    {
        if (roll is not LifepathRoll result)
        {
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("kind", result.Kind);
        writer.WriteNumber("roll", result.Roll);
        writer.WriteNumber("modifier", result.Modifier);
        writer.WriteNumber("total", result.Total);
        writer.WriteNumber("target", result.Target);
        writer.WriteBoolean("success", result.Success);
        writer.WriteEndObject();
    }

    private sealed class Reader(string path, ModuleSet set, List<ModuleDiagnostic> problems, string prefix)
    {
        private readonly RuleSet _rules = set.Rules!;
        private readonly int _before = problems.Count;

        public Character? Read(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                Error("$", "A character file must be a JSON object, as written by `goldbox character new`.");
                return null;
            }

            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!Fields.Contains(property.Name))
                {
                    Error($"$.{property.Name}", $"'{property.Name}' is not a character field. Fields: {string.Join(", ", Fields)}.");
                }
            }

            if (Number(root, "format") is decimal format && format != CurrentFormat)
            {
                Error("$.format", $"Character format {format} is not supported; this goldbox reads format {CurrentFormat}.");
            }

            List<ModuleStamp> modules = ReadModules(root);
            string? name = Text(root, "name");
            // A ruleset without races or classes leaves them out of its characters.
            bool raceless = !_rules.OfType(DefinitionTypes.Race).Any();
            Definition? race = raceless ? null : Reference(root, "race", DefinitionTypes.Race);
            Definition? creation = Reference(root, "creation", DefinitionTypes.CharacterCreation);
            Definition? lifepath = root.TryGetProperty("lifepath", out _) ? Reference(root, "lifepath", DefinitionTypes.Lifepath) : null;
            decimal? experience = Number(root, "experience");
            Dictionary<string, decimal> balances = ReadBalances(root);
            if (problems.Count > _before || name is null || (race is null && !raceless) || creation is null)
            {
                return null;
            }

            Character character = new() { Name = name, Modules = modules, Race = race, Creation = creation, Lifepath = lifepath };
            character.CombatControlPreference = ReadCombatControl(root);
            if (!ReadLevels(root, character))
            {
                return null;
            }

            ReadLifepath(root, character);

            character.Experience = experience ?? 0;
            ReadList(root, "left_classes", DefinitionTypes.Class, character.LeftClasses);
            if (root.TryGetProperty("class_experience", out JsonElement classExperience))
            {
                ReadClassExperience(classExperience, character);
            }

            foreach ((string currency, decimal amount) in balances)
            {
                character.Balances[currency] = amount;
            }
            ReadAttributes(root, character);
            ReadStatBonuses(root, character);
            ReadSkillAllocations(root, character);
            ReadSkillPointOptions(root, character);
            ReadSkillMarks(root, character);
            ReadList(root, "milestone_features", DefinitionTypes.Feature, character.MilestoneFeatures);
            ReadTracks(root, character);
            if (character.Experience < 0)
            {
                Error("$.experience", "experience can't be negative.");
            }

            // Before equipment: a character calling on a former class may hold what that class allows.
            character.UsesFormerClasses = Flag(root, "uses_former_classes");
            character.ForfeitsExperience = Flag(root, "forfeits_experience");
            if (character.UsesFormerClasses && !character.HasDormantClasses())
            {
                Error("$.uses_former_classes", $"{character.Name} has no class waiting for its new class to pass it, so it can't call on one. Leave the field out.");
            }

            ReadList(root, "equipment", DefinitionTypes.Item, character.Equipment);
            for (int index = 0; index < character.Equipment.Count; index++)
            {
                if (CharacterRules.EquipmentProblem(_rules, character, character.Equipment[index]) is string refused)
                {
                    Error($"$.equipment[{index}]", refused);
                }
            }

            if (root.TryGetProperty("npc", out _))
            {
                character.Npc = Reference(root, "npc", DefinitionTypes.Npc);
            }

            ReadList(root, "conditions", DefinitionTypes.Condition, character.Conditions);
            ReadList(root, "spells", DefinitionTypes.Spell, character.Spells);
            for (int index = 0; index < character.Spells.Count; index++)
            {
                if (CharacterRules.SpellProblem(_rules, character, character.Spells[index]) is string unknown)
                {
                    Error($"$.spells[{index}]", unknown);
                }
            }

            ReadList(root, "memorised", DefinitionTypes.Spell, character.Memorised);
            Dictionary<Definition, decimal> budgets = CharacterRules.Budgets(_rules, character);
            for (int index = 0; index < character.Memorised.Count; index++)
            {
                if (CharacterRules.MemorisedProblem(_rules, character, character.Memorised[index], budgets) is string unmemorised)
                {
                    Error($"$.memorised[{index}]", unmemorised);
                }
            }

            if (root.TryGetProperty("prepared", out _))
            {
                List<Definition> prepared = [];
                ReadList(root, "prepared", DefinitionTypes.Spell, prepared);
                // Copies prepared before the list changed in play stay until the next rest, so they need only be known spells it prepares.
                for (int index = 0; index < prepared.Count; index++)
                {
                    if (!character.Spells.Contains(prepared[index]) || !CharacterRules.NeedsPreparing(_rules, character, prepared[index]))
                    {
                        Error($"$.prepared[{index}]", $"{prepared[index].Name} isn't a spell {character.Name} knows and prepares. \"prepared\" lists the memorised copies not yet cast.");
                    }
                }

                character.Prepared = prepared;
            }

            if (root.TryGetProperty("portrait", out JsonElement portrait) && Resolve(portrait, "$.portrait", DefinitionTypes.Asset) is Definition asset)
            {
                if (CharacterRules.PortraitProblem(asset) is string problem)
                {
                    Error("$.portrait", problem);
                }

                character.Portrait = asset;
            }

            ReadPerception(root, character);

            List<ModuleDiagnostic> stagedProblems = [];
            CharacterRules.ValidateSkillPointState(_rules, character, stagedProblems);
            foreach (ModuleDiagnostic problem in stagedProblems)
            {
                Error(problem.JsonPath ?? "$.skill_point_options", problem.Message);
            }

            if (problems.Count == _before)
            {
                List<ModuleDiagnostic> history = [];
                CharacterRules.CheckHistory(_rules, character, history);
                foreach (ModuleDiagnostic problem in history)
                {
                    Error("$.levels", problem.Message);
                }
            }

            return problems.Count > _before ? null : character;
        }

        private void ReadPerception(JsonElement root, Character character)
        {
            if (!root.TryGetProperty("perception", out JsonElement perception))
            {
                return;
            }

            if (perception.ValueKind != JsonValueKind.Object)
            {
                Error("$.perception", "perception must be an object with nonempty scope and mode text.");
                return;
            }

            foreach (JsonProperty property in perception.EnumerateObject())
            {
                if (property.Name is not ("scope" or "mode"))
                {
                    Error($"$.perception.{property.Name}", $"'{property.Name}' is not a perception field. Fields: scope, mode.");
                }
            }

            string? scope = perception.TryGetProperty("scope", out JsonElement scopeValue) && scopeValue.ValueKind == JsonValueKind.String
                ? scopeValue.GetString()
                : null;
            string? mode = perception.TryGetProperty("mode", out JsonElement modeValue) && modeValue.ValueKind == JsonValueKind.String
                ? modeValue.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(scope))
            {
                Error("$.perception.scope", "perception.scope must be nonempty text.");
            }

            if (string.IsNullOrWhiteSpace(mode))
            {
                Error("$.perception.mode", "perception.mode must be nonempty text.");
            }

            if (scope is not null && mode is not null && !string.IsNullOrWhiteSpace(scope) && !string.IsNullOrWhiteSpace(mode))
            {
                character.Perception = new PerceptionState(scope, mode);
            }
        }

        /// <summary>Reads "levels": one { "class", "gain", "features"? } per character level, none past its class's last level.</summary>
        private bool ReadLevels(JsonElement root, Character character)
        {
            if (!root.TryGetProperty("levels", out JsonElement levels) || levels.ValueKind != JsonValueKind.Array || levels.GetArrayLength() == 0)
            {
                Error("$.levels", "\"levels\" must be an array with one { \"class\", \"gain\", \"features\"?, \"boosts\"? } per character level, first to last, and at least one.");
                return false;
            }

            int before = problems.Count;
            int index = 0;
            foreach (JsonElement level in levels.EnumerateArray())
            {
                string at = $"$.levels[{index}]";
                index++;
                if (level.ValueKind != JsonValueKind.Object)
                {
                    Error(at, "Each level must be an object { \"class\", \"gain\", \"features\"? }.");
                    continue;
                }

                bool classless = !_rules.OfType(DefinitionTypes.Class).Any();
                Definition? characterClass = classless ? null : Reference(level, "class", DefinitionTypes.Class, at);
                List<Definition> features = [];
                ReadList(level, "features", DefinitionTypes.Feature, features, at);
                if (Number(level, "gain", at) is not decimal gain || (characterClass is null && !classless))
                {
                    continue;
                }

                List<string> boosts = [];
                if (level.TryGetProperty("boosts", out JsonElement boosted))
                {
                    if (boosted.ValueKind != JsonValueKind.Array || boosted.EnumerateArray().Any(entry => entry.ValueKind != JsonValueKind.String || !_rules.Stats.TryGetValue(entry.GetString()!, out Stat? stat) || !stat.IsAttribute))
                    {
                        Error($"{at}.boosts", "\"boosts\" must be an array of the attribute IDs boosted at this level.");
                        continue;
                    }

                    boosts.AddRange(boosted.EnumerateArray().Select(entry => entry.GetString()!));
                }

                character.Levels.Add(new LevelTaken(characterClass, gain, features) { Boosts = boosts });
                if (characterClass is null)
                {
                    continue;
                }

                int classLevels = characterClass.Json.GetProperty("levels").GetArrayLength();
                if (character.ClassLevels()[characterClass] > classLevels)
                {
                    Error(at, $"This would be level {classLevels + 1} of {characterClass.QualifiedId}, which has {classLevels}.");
                }
            }

            return problems.Count == before;
        }

        private void ReadLifepath(JsonElement root, Character character)
        {
            bool hasAge = root.TryGetProperty("age", out JsonElement age);
            bool hasTerms = root.TryGetProperty("career_terms", out JsonElement terms);
            if (character.Lifepath is null)
            {
                if (hasAge || hasTerms)
                {
                    Error("$.lifepath", "A character with age or career_terms must name its lifepath definition.");
                }

                return;
            }

            int? recordedAge = null;
            if (!hasAge || age.ValueKind != JsonValueKind.Number || !age.TryGetInt32(out int years) || years < 0)
            {
                Error("$.age", "A lifepath character needs a nonnegative whole-number age.");
            }
            else
            {
                character.Age = years;
                recordedAge = years;
            }

            if (!hasTerms || terms.ValueKind != JsonValueKind.Array)
            {
                Error("$.career_terms", "A lifepath character needs career_terms: an array of recorded terms.");
                return;
            }

            int index = 0;
            int? previousAgeAfter = null;
            int startAge = character.Lifepath.Json.GetProperty("start_age").GetInt32();
            foreach (JsonElement term in terms.EnumerateArray())
            {
                string at = $"$.career_terms[{index}]";
                int expectedNumber = index + 1;
                index++;
                if (term.ValueKind != JsonValueKind.Object)
                {
                    Error(at, "Each career term must be an object with its career, ages, ranks, rolls, choices and results.");
                    continue;
                }

                if (Text(term, "career", at) is not string career
                    || Number(term, "number", at) is not decimal number
                    || Number(term, "age_before", at) is not decimal ageBefore
                    || Number(term, "age_after", at) is not decimal ageAfter
                    || Number(term, "rank_before", at) is not decimal rankBefore
                    || Number(term, "rank_after", at) is not decimal rankAfter
                    || FlagRequired(term, "ended", at) is not bool ended
                    || FlagRequired(term, "benefits_lost", at) is not bool benefitsLost)
                {
                    continue;
                }

                if (!character.Lifepath.Json.GetProperty("careers").EnumerateArray().Any(candidate => candidate.GetProperty("id").GetString() == career))
                {
                    Error($"{at}.career", $"'{career}' is not a career in {character.Lifepath.QualifiedId}; the term ledger must use one of {string.Join(", ", character.Lifepath.Json.GetProperty("careers").EnumerateArray().Select(candidate => candidate.GetProperty("id").GetString()))}.");
                }

                bool numberValid = TryWholeInt(number, 1, out int termNumber);
                if (!numberValid)
                {
                    Error($"{at}.number", "A career term number must be a positive whole number.");
                }

                bool ageBeforeValid = TryWholeInt(ageBefore, 0, out int termAgeBefore);
                bool ageAfterValid = TryWholeInt(ageAfter, 0, out int termAgeAfter);
                bool agesValid = ageBeforeValid && ageAfterValid;
                if (!agesValid || (ageAfterValid && termAgeAfter < termAgeBefore))
                {
                    Error(at, "A career term must have nonnegative whole-number ages, with age_after no less than age_before.");
                }

                bool rankBeforeValid = TryWholeInt(rankBefore, 0, out int termRankBefore);
                bool rankAfterValid = TryWholeInt(rankAfter, 0, out int termRankAfter);
                bool ranksValid = rankBeforeValid && rankAfterValid;
                if (!ranksValid)
                {
                    Error(at, "A career term must have nonnegative whole-number ranks.");
                }

                if (numberValid && termNumber != expectedNumber)
                {
                    Error($"{at}.number", $"Career term numbers must be sequential starting at 1; this term is numbered {termNumber}, expected {expectedNumber}.");
                }

                if (agesValid)
                {
                    if (expectedNumber == 1 && termAgeBefore != startAge)
                    {
                        Error($"{at}.age_before", $"The first career term must start at the lifepath start age {startAge}, not {termAgeBefore}.");
                    }
                    else if (previousAgeAfter is int previous && termAgeBefore != previous)
                    {
                        Error($"{at}.age_before", $"This career term starts at age {termAgeBefore}, but the previous term ends at age {previous}; term ages must be continuous.");
                    }

                    previousAgeAfter = termAgeAfter;
                }

                List<string> choices = Strings(term, "choices", at);
                List<string> results = Strings(term, "results", at);
                LifepathRoll? qualification = ReadLifepathRoll(term, "qualification", at);
                LifepathRoll? survival = ReadLifepathRoll(term, "survival", at);
                LifepathRoll? commission = ReadLifepathRoll(term, "commission", at);
                LifepathRoll? advancement = ReadLifepathRoll(term, "advancement", at);
                LifepathRoll? aging = ReadLifepathRoll(term, "aging", at);
                if (numberValid && agesValid && ranksValid)
                {
                    character.CareerTerms.Add(new LifepathTerm(career, termNumber, termAgeBefore, termAgeAfter, termRankBefore, termRankAfter, qualification, survival, commission, advancement, aging, ended, benefitsLost, choices, results));
                }
            }

            if (previousAgeAfter is int lastAge && recordedAge is int actualAge && actualAge != lastAge)
            {
                Error("$.age", $"The character age {actualAge} must equal the last career term's age_after {lastAge}.");
            }

            character.LifepathEnded = character.CareerTerms.Any(term => term.Ended) || character.CareerTerms.Count > 0;
        }

        private LifepathRoll? ReadLifepathRoll(JsonElement term, string name, string at)
        {
            if (!term.TryGetProperty(name, out JsonElement roll))
            {
                return null;
            }

            string path = $"{at}.{name}";
            if (roll.ValueKind != JsonValueKind.Object
                || Text(roll, "kind", path) is not string kind
                || Number(roll, "roll", path) is not decimal raw
                || Number(roll, "modifier", path) is not decimal modifier
                || Number(roll, "total", path) is not decimal total
                || Number(roll, "target", path) is not decimal target
                || FlagRequired(roll, "success", path) is not bool success)
            {
                Error(path, "A lifepath roll must contain kind, roll, modifier, total, target and success.");
                return null;
            }

            try
            {
                decimal expectedTotal = checked(raw + modifier);
                if (expectedTotal != total)
                {
                    Error($"{path}.total", $"A lifepath roll total must equal roll + modifier ({raw} + {modifier} = {expectedTotal}), but it is {total}.");
                }
            }
            catch (OverflowException)
            {
                Error($"{path}.total", "A lifepath roll's roll plus modifier is too large to calculate safely.");
            }

            return new LifepathRoll(kind, raw, modifier, total, target, success);
        }

        private static bool TryWholeInt(decimal value, int minimum, out int result)
        {
            if (value < minimum || value > int.MaxValue || value != decimal.Truncate(value))
            {
                result = 0;
                return false;
            }

            result = (int)value;
            return true;
        }

        private List<string> Strings(JsonElement root, string name, string at)
        {
            if (!root.TryGetProperty(name, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
            {
                Error($"{at}.{name}", $"\"{name}\" must be an array of text.");
                return [];
            }

            List<string> result = [];
            int index = 0;
            foreach (JsonElement value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    Error($"{at}.{name}[{index}]", "The value must be text.");
                }
                else
                {
                    result.Add(value.GetString()!);
                }

                index++;
            }

            return result;
        }

        private bool? FlagRequired(JsonElement root, string name, string at)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                Error($"{at}.{name}", $"\"{name}\" must be true or false.");
                return null;
            }

            return value.GetBoolean();
        }

        /// <summary>"class_experience": each class's own experience, by class ID; only classes the character has.</summary>
        private void ReadClassExperience(JsonElement given, Character character)
        {
            if (given.ValueKind != JsonValueKind.Object)
            {
                Error("$.class_experience", "\"class_experience\" must be an object of experience by class ID.");
                return;
            }

            foreach (JsonProperty entry in given.EnumerateObject())
            {
                string at = $"$.class_experience.{entry.Name}";
                if (Resolve(entry.Name, at, DefinitionTypes.Class) is not Definition characterClass)
                {
                    continue;
                }

                if (!character.ClassLevels().ContainsKey(characterClass))
                {
                    Error(at, $"The character has no levels in {characterClass.QualifiedId}.");
                }
                else if (Number(given, entry.Name, "$.class_experience") is decimal points)
                {
                    character.ClassExperience[characterClass] = points;
                }
            }
        }

        private List<ModuleStamp> ReadModules(JsonElement root)
        {
            List<ModuleStamp> stamps = [];
            if (!root.TryGetProperty("modules", out JsonElement modules) || modules.ValueKind != JsonValueKind.Array)
            {
                Error("$.modules", "Missing \"modules\": the [{ \"id\", \"version\" }] list of modules the character was made under.");
                return stamps;
            }

            int index = 0;
            foreach (JsonElement entry in modules.EnumerateArray())
            {
                string at = $"$.modules[{index}]";
                index++;
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    Error(at, "Each modules entry must be an object { \"id\", \"version\" }.");
                    continue;
                }

                if (Text(entry, "id", at) is not string id
                    || Text(entry, "version", at) is not string versionText)
                {
                    continue;
                }

                if (!ModuleVersion.TryParse(versionText, out ModuleVersion version))
                {
                    Error($"{at}.version", $"'{versionText}' is not a module version.");
                    continue;
                }

                stamps.Add(new ModuleStamp(id, version));
            }

            List<ModuleStamp> loaded = Character.StampsOf(set).ToList();
            List<string> differences = [];
            List<string> missing = [];
            foreach (ModuleStamp stamp in stamps)
            {
                ModuleStamp? match = loaded.FirstOrDefault(module => module.Id == stamp.Id);
                VersionRange.TryParse($"^{stamp.Version}", out VersionRange? compatible);
                if (match is null)
                {
                    differences.Add($"{stamp.Id} {stamp.Version} is not loaded");
                    missing.Add(stamp.Id);
                }
                else if (!compatible!.Contains(match.Version))
                {
                    differences.Add($"{stamp.Id} is {match.Version}, which is not compatible with {stamp.Version} (needs {compatible.Text})");
                }
            }

            if (differences.Count > 0)
            {
                Error("$.modules", $"The character needs modules that aren't loaded: {string.Join("; ", differences)}. Load the modules it lists: a set that requires them{(missing.Count > 0 ? $", or add extensions with --extension {string.Join(",", missing)}" : "")}.");
            }

            return stamps;
        }

        private void ReadAttributes(JsonElement root, Character character)
        {
            List<Definition> attributes = _rules.OfType(DefinitionTypes.Attribute).ToList();
            // Keep the file's order: it is the creation order the character was made with.
            if (!root.TryGetProperty("attributes", out JsonElement given) || given.ValueKind != JsonValueKind.Object)
            {
                Error("$.attributes", "Missing \"attributes\": an object of attribute scores.");
                return;
            }

            foreach (JsonProperty entry in given.EnumerateObject().Where(property => attributes.Any(attribute => attribute.Id == property.Name)))
            {
                if (Number(given, entry.Name, "$.attributes") is decimal score)
                {
                    character.Attributes[entry.Name] = score;
                }
            }

            foreach (Definition missing in attributes.Where(attribute => !given.TryGetProperty(attribute.Id, out _)))
            {
                Error("$.attributes", $"Missing attribute {missing.Id}.");
            }

            foreach (JsonProperty extra in given.EnumerateObject().Where(property => attributes.All(attribute => attribute.Id != property.Name)))
            {
                Error($"$.attributes.{extra.Name}", $"'{extra.Name}' is not an attribute of this module set.");
            }
        }

        private void ReadTracks(JsonElement root, Character character)
        {
            if (!root.TryGetProperty("tracks", out JsonElement tracks) || tracks.ValueKind != JsonValueKind.Object)
            {
                Error("$.tracks", "Missing \"tracks\": { <track>: { \"current\", \"max\"? } } for each track of the module set.");
                return;
            }

            foreach (JsonProperty entry in tracks.EnumerateObject())
            {
                string at = $"$.tracks.{entry.Name}";
                if (!_rules.Tracks.ContainsKey(entry.Name))
                {
                    Error(at, $"'{entry.Name}' is not a track of this module set. Tracks: {string.Join(", ", _rules.Tracks.Keys)}.");
                    continue;
                }

                if (entry.Value.ValueKind != JsonValueKind.Object)
                {
                    Error(at, "A track entry must be an object { \"current\", \"max\"? }.");
                    continue;
                }

                TrackValue value = new() { Current = Number(entry.Value, "current", at) };
                if (entry.Value.TryGetProperty("max", out _))
                {
                    value.Max = Number(entry.Value, "max", at);
                }

                character.Tracks[entry.Name] = value;
            }

            foreach (string missing in _rules.Tracks.Keys.Where(id => !character.Tracks.ContainsKey(id)))
            {
                Error("$.tracks", $"Missing track {missing}.");
            }

            if (_rules.LevelTrack is Definition levelTrack && character.Tracks.TryGetValue(levelTrack.Id, out TrackValue? level) && level.Max is null)
            {
                Error($"$.tracks.{levelTrack.Id}", $"{levelTrack.Id} is built from level gains, so it needs \"max\".");
            }
        }

        private Dictionary<string, decimal> ReadBalances(JsonElement root)
        {
            Dictionary<string, decimal> balances = [];
            if (!root.TryGetProperty("balances", out JsonElement given) || given.ValueKind != JsonValueKind.Object)
            {
                Error("$.balances", "Missing \"balances\": an object mapping each declared currency ID to its carried amount.");
                return balances;
            }

            foreach (JsonProperty entry in given.EnumerateObject())
            {
                string at = $"$.balances.{entry.Name}";
                if (!_rules.Currencies.ContainsKey(entry.Name))
                {
                    Error(at, $"'{entry.Name}' is not a currency of this module set. Currencies: {(_rules.Currencies.Count == 0 ? "none" : string.Join(", ", _rules.Currencies.Keys))}.");
                    continue;
                }

                if (Number(given, entry.Name, "$.balances") is decimal amount)
                {
                    if (amount < 0)
                    {
                        Error(at, "A currency balance cannot be negative.");
                    }
                    else
                    {
                        balances[entry.Name] = amount;
                    }
                }
            }

            return balances;
        }

        private void ReadStatBonuses(JsonElement root, Character character)
        {
            if (!root.TryGetProperty("stat_bonuses", out JsonElement bonuses))
            {
                return;
            }

            if (bonuses.ValueKind != JsonValueKind.Object)
            {
                Error("$.stat_bonuses", "\"stat_bonuses\" must be an object of stat IDs and numbers.");
                return;
            }

            foreach (JsonProperty entry in bonuses.EnumerateObject())
            {
                string at = $"$.stat_bonuses.{entry.Name}";
                if (!_rules.Stats.ContainsKey(entry.Name))
                {
                    Error(at, $"'{entry.Name}' is not a stat of this module set.");
                    continue;
                }

                if (entry.Value.ValueKind != JsonValueKind.Number || !entry.Value.TryGetDecimal(out decimal bonus))
                {
                    Error(at, "A stat bonus must be a number.");
                    continue;
                }

                character.StatBonuses[entry.Name] = bonus;
            }
        }

        private void ReadSkillAllocations(JsonElement root, Character character)
        {
            if (!root.TryGetProperty("skill_allocations", out JsonElement allocations))
            {
                return;
            }

            if (allocations.ValueKind != JsonValueKind.Object)
            {
                Error("$.skill_allocations", "\"skill_allocations\" must be an object of stat IDs and profession/personal numbers.");
                return;
            }

            foreach (JsonProperty entry in allocations.EnumerateObject())
            {
                string at = $"$.skill_allocations.{entry.Name}";
                if (!_rules.Stats.TryGetValue(entry.Name, out Stat? stat))
                {
                    Error(at, $"'{entry.Name}' is not a stat of this module set.");
                    continue;
                }

                if (stat.IsAttribute)
                {
                    Error(at, $"'{entry.Name}' is an attribute; staged allocations must name derived skills.");
                    continue;
                }

                if (entry.Value.ValueKind != JsonValueKind.Object
                    || !entry.Value.TryGetProperty("profession", out JsonElement profession)
                    || !entry.Value.TryGetProperty("personal", out JsonElement personal)
                    || profession.ValueKind != JsonValueKind.Number
                    || personal.ValueKind != JsonValueKind.Number
                    || !profession.TryGetDecimal(out decimal professionPoints)
                    || !personal.TryGetDecimal(out decimal personalPoints)
                    || professionPoints < 0
                    || personalPoints < 0)
                {
                    Error(at, "A staged skill allocation needs nonnegative numeric profession and personal fields.");
                    continue;
                }

                character.SkillAllocations[entry.Name] = new SkillAllocation(entry.Name, professionPoints, personalPoints);
            }
        }

        private void ReadSkillPointOptions(JsonElement root, Character character)
        {
            character.SkillPointsCommitted = Flag(root, "skill_points_committed");
            if (!root.TryGetProperty("skill_point_options", out JsonElement options))
            {
                return;
            }

            if (options.ValueKind != JsonValueKind.Object)
            {
                Error("$.skill_point_options", "\"skill_point_options\" must be an object with profession, personal and skills.");
                return;
            }

            decimal? profession = Number(options, "profession", "$.skill_point_options");
            decimal? personal = Number(options, "personal", "$.skill_point_options");
            if (!options.TryGetProperty("skills", out JsonElement skills) || skills.ValueKind != JsonValueKind.Array)
            {
                Error("$.skill_point_options.skills", "\"skills\" must be an array of { \"id\", \"base\", \"current\", \"profession\" }.");
                return;
            }

            List<SkillPointOption> offered = [];
            int index = 0;
            foreach (JsonElement entry in skills.EnumerateArray())
            {
                string at = $"$.skill_point_options.skills[{index}]";
                index++;
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("id", out JsonElement id)
                    || id.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(id.GetString())
                    || Number(entry, "base", at) is not decimal baseChance
                    || Number(entry, "current", at) is not decimal current
                    || !entry.TryGetProperty("profession", out JsonElement allowed)
                    || (allowed.ValueKind != JsonValueKind.True && allowed.ValueKind != JsonValueKind.False))
                {
                    Error(at, "Each staged skill option needs text id, numeric base/current and a true/false profession field.");
                    continue;
                }

                offered.Add(new SkillPointOption(id.GetString()!, baseChance, current, allowed.GetBoolean()));
            }

            if (profession is decimal professionPoints && personal is decimal personalPoints && problems.Count == _before)
            {
                character.SkillPointData = new SkillPointOptions(professionPoints, personalPoints, offered);
            }
        }

        private void ReadSkillMarks(JsonElement root, Character character)
        {
            if (!root.TryGetProperty("skill_marks", out JsonElement marks))
            {
                return;
            }

            if (marks.ValueKind != JsonValueKind.Object)
            {
                Error("$.skill_marks", "\"skill_marks\" must be an object of stat IDs and whole numbers.");
                return;
            }

            foreach (JsonProperty entry in marks.EnumerateObject())
            {
                string at = $"$.skill_marks.{entry.Name}";
                if (!_rules.Stats.ContainsKey(entry.Name))
                {
                    Error(at, $"'{entry.Name}' is not a stat of this module set.");
                    continue;
                }

                if (entry.Value.ValueKind != JsonValueKind.Number || !entry.Value.TryGetInt32(out int count) || count < 0)
                {
                    Error(at, "A skill mark count must be a nonnegative whole number.");
                    continue;
                }

                character.SkillMarks[entry.Name] = count;
            }
        }

        private void ReadList(JsonElement root, string name, DefinitionType type, List<Definition> into, string at = "$")
        {
            if (!root.TryGetProperty(name, out JsonElement list))
            {
                return;
            }

            if (list.ValueKind != JsonValueKind.Array)
            {
                Error($"{at}.{name}", $"\"{name}\" must be an array of {type.Name} IDs.");
                return;
            }

            int index = 0;
            foreach (JsonElement entry in list.EnumerateArray())
            {
                if (Resolve(entry, $"{at}.{name}[{index}]", type) is Definition definition)
                {
                    into.Add(definition);
                }

                index++;
            }
        }

        private Definition? Reference(JsonElement root, string name, DefinitionType type, string at = "$")
        {
            if (!root.TryGetProperty(name, out JsonElement value))
            {
                Error(at, $"Missing \"{name}\": a {type.Name} ID.");
                return null;
            }

            return Resolve(value, $"{at}.{name}", type);
        }

        private Definition? Resolve(JsonElement value, string at, DefinitionType type)
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                Error(at, $"Expected a {type.Name} ID.");
                return null;
            }

            return Resolve(value.GetString()!, at, type);
        }

        private Definition? Resolve(string reference, string at, DefinitionType type)
        {
            Definition? found = _rules.Find(type, reference, out string? problem);
            if (found is null)
            {
                Error(at, problem!);
            }

            return found;
        }

        private string? Text(JsonElement element, string name, string at = "$")
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                Error(at, $"Missing \"{name}\" (text).");
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                Error($"{at}.{name}", $"\"{name}\" must be text.");
                return null;
            }

            return value.GetString();
        }

        private decimal? Number(JsonElement element, string name, string at = "$")
        {
            if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number))
            {
                return number;
            }

            Error($"{at}.{name}", $"\"{name}\" must be a number.");
            return null;
        }

        /// <summary>An optional true/false field; false when it is left out.</summary>
        private bool Flag(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
            {
                return false;
            }

            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                Error($"$.{name}", $"\"{name}\" must be true or false.");
                return false;
            }

            return value.GetBoolean();
        }

        private CombatControlMode? ReadCombatControl(JsonElement root)
        {
            if (!root.TryGetProperty("combat_control", out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                Error("$.combat_control", "\"combat_control\" must be automatic or manual.");
                return null;
            }

            return value.GetString() switch
            {
                "automatic" => CombatControlMode.Automatic,
                "manual" => CombatControlMode.Manual,
                _ => InvalidCombatControl(),
            };
        }

        private CombatControlMode? InvalidCombatControl()
        {
            Error("$.combat_control", "\"combat_control\" must be automatic or manual.");
            return null;
        }

        private void Error(string jsonPath, string message)
        {
            problems.Add(new ModuleDiagnostic("character.file", message, File: path, JsonPath: prefix + jsonPath[1..]));
        }
    }
}
