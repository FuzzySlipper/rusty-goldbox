using System.Text.Encodings.Web;
using System.Text.Json;
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

    public static IReadOnlyList<Definitions.Field> DataFields { get; } =
    [
        new("name", new Definitions.TextKind(), true, "Character name."),
        new("race", new Definitions.ReferenceKind("race"), false, "Race; omit in a ruleset without races."),
        new("creation", new Definitions.ReferenceKind("character-creation"), true, "The creation rules that granted its first level choices."),
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
        new("tracks", new Definitions.MapKind(new Definitions.TextKind(), new Definitions.ObjectKind(
        [
            new("current", new Definitions.NumberKind(), true, "Current track value."),
            new("max", new Definitions.NumberKind(), false, "Own maximum for the level track; other maxima come from data."),
        ])), true, "All track values by their shared stat IDs."),
        new("gold", new Definitions.NumberKind(), true, "Gold carried."),
        new("equipment", new Definitions.ListKind(new Definitions.ReferenceKind("item")), true, "Carried equipment."),
        new("spells", new Definitions.ListKind(new Definitions.ReferenceKind("spell")), false, "Known spells."),
        new("memorised", new Definitions.ListKind(new Definitions.ReferenceKind("spell")), false, "Memorised copies."),
        new("prepared", new Definitions.ListKind(new Definitions.ReferenceKind("spell")), false, "Unspent prepared copies; omit for the full plan."),
        new("conditions", new Definitions.ListKind(new Definitions.ReferenceKind("condition")), true, "Held conditions."),
        new("portrait", new Definitions.ReferenceKind("asset", "portrait"), false, "Portrait art."),
        new("uses_former_classes", new Definitions.BooleanKind(), false, "Calling on dormant classes."),
        new("forfeits_experience", new Definitions.BooleanKind(), false, "Experience forfeited this adventure."),
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
            writer.WriteNumber("gold", character.Gold);
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
            decimal? experience = Number(root, "experience");
            decimal? gold = Number(root, "gold");
            if (problems.Count > _before || name is null || (race is null && !raceless) || creation is null)
            {
                return null;
            }

            Character character = new() { Name = name, Modules = modules, Race = race, Creation = creation };
            if (!ReadLevels(root, character))
            {
                return null;
            }

            character.Experience = experience ?? 0;
            ReadList(root, "left_classes", DefinitionTypes.Class, character.LeftClasses);
            if (root.TryGetProperty("class_experience", out JsonElement classExperience))
            {
                ReadClassExperience(classExperience, character);
            }

            character.Gold = gold ?? 0;
            ReadAttributes(root, character);
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

        private void Error(string jsonPath, string message)
        {
            problems.Add(new ModuleDiagnostic("character.file", message, File: path, JsonPath: prefix + jsonPath[1..]));
        }
    }
}
