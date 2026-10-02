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

    private static readonly string[] Fields =
    [
        "format", "name", "modules", "race", "creation", "levels", "experience", "attributes", "tracks", "gold", "equipment", "conditions", "portrait",
    ];

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
            writer.WriteStartArray("modules");
            foreach (ModuleStamp module in character.Modules)
            {
                writer.WriteStartObject();
                writer.WriteString("id", module.Id);
                writer.WriteString("version", module.Version.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("race", character.Race.QualifiedId);
            writer.WriteString("creation", character.Creation.QualifiedId);
            writer.WriteStartArray("levels");
            foreach (LevelTaken level in character.Levels)
            {
                writer.WriteStartObject();
                writer.WriteString("class", level.Class.QualifiedId);
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
            Definition? race = Reference(root, "race", DefinitionTypes.Race);
            Definition? creation = Reference(root, "creation", DefinitionTypes.CharacterCreation);
            decimal? experience = Number(root, "experience");
            decimal? gold = Number(root, "gold");
            if (problems.Count > _before || name is null || race is null || creation is null)
            {
                return null;
            }

            Character character = new() { Name = name, Modules = modules, Race = race, Creation = creation };
            if (!ReadLevels(root, character))
            {
                return null;
            }

            character.Experience = experience ?? 0;
            character.Gold = gold ?? 0;
            ReadAttributes(root, character);
            ReadTracks(root, character);
            if (character.Experience < 0)
            {
                Error("$.experience", "experience can't be negative.");
            }

            ReadList(root, "equipment", DefinitionTypes.Item, character.Equipment);
            ReadList(root, "conditions", DefinitionTypes.Condition, character.Conditions);
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

                Definition? characterClass = Reference(level, "class", DefinitionTypes.Class, at);
                List<Definition> features = [];
                ReadList(level, "features", DefinitionTypes.Feature, features, at);
                if (Number(level, "gain", at) is not decimal gain || characterClass is null)
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
                int classLevels = characterClass.Json.GetProperty("levels").GetArrayLength();
                if (character.ClassLevels()[characterClass] > classLevels)
                {
                    Error(at, $"This would be level {classLevels + 1} of {characterClass.QualifiedId}, which has {classLevels}.");
                }
            }

            return problems.Count == before;
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
            foreach (ModuleStamp stamp in stamps)
            {
                ModuleStamp? match = loaded.FirstOrDefault(module => module.Id == stamp.Id);
                VersionRange.TryParse($"^{stamp.Version}", out VersionRange? compatible);
                if (match is null)
                {
                    differences.Add($"{stamp.Id} {stamp.Version} is not loaded");
                }
                else if (!compatible!.Contains(match.Version))
                {
                    differences.Add($"{stamp.Id} is {match.Version}, which is not compatible with {stamp.Version} (needs {compatible.Text})");
                }
            }

            if (differences.Count > 0)
            {
                Error("$.modules", $"The character needs modules that aren't loaded: {string.Join("; ", differences)}. Load the modules it lists, or a set that requires them.");
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

            Definition? found = _rules.Find(type, value.GetString()!, out string? problem);
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

        private void Error(string jsonPath, string message)
        {
            problems.Add(new ModuleDiagnostic("character.file", message, File: path, JsonPath: prefix + jsonPath[1..]));
        }
    }
}
