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
        "format", "name", "modules", "race", "class", "level", "experience", "attributes", "tracks", "level_gains", "gold", "equipment", "conditions", "portrait",
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
            writer.WriteString("class", character.Class.QualifiedId);
            writer.WriteNumber("level", character.Level);
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
            writer.WriteStartArray("level_gains");
            foreach (decimal gain in character.LevelGains)
            {
                writer.WriteNumberValue(gain);
            }

            writer.WriteEndArray();
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

    private static void WriteReferences(Utf8JsonWriter writer, string name, List<Definition> definitions)
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
            Definition? characterClass = Reference(root, "class", DefinitionTypes.Class);
            decimal? level = Number(root, "level");
            decimal? experience = Number(root, "experience");
            decimal? gold = Number(root, "gold");
            if (problems.Count > _before || name is null || race is null || characterClass is null)
            {
                return null;
            }

            int levels = characterClass.Json.GetProperty("levels").GetArrayLength();
            if (level is not decimal whole || whole != decimal.Truncate(whole) || whole < 1 || whole > levels)
            {
                Error("$.level", $"level must be a whole number from 1 to {levels} (the levels of {characterClass.QualifiedId}).");
                return null;
            }

            Character character = new() { Name = name, Modules = modules, Race = race, Class = characterClass, Level = (int)whole };
            character.Experience = experience ?? 0;
            character.Gold = gold ?? 0;
            ReadAttributes(root, character);
            ReadTracks(root, character);
            if (character.Experience < 0)
            {
                Error("$.experience", "experience can't be negative.");
            }

            if (problems.Count == _before && character.LevelGains.Count != character.Level)
            {
                Error("$.level_gains", $"level_gains must have one entry per level: {character.Level} for level {character.Level}, but it has {character.LevelGains.Count}.");
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

            return problems.Count > _before ? null : character;
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

            if (!root.TryGetProperty("level_gains", out JsonElement gains) || gains.ValueKind != JsonValueKind.Array)
            {
                Error("$.level_gains", "\"level_gains\" must be an array with what the level track gained at each level.");
                return;
            }

            foreach (JsonElement gain in gains.EnumerateArray())
            {
                if (gain.ValueKind == JsonValueKind.Number && gain.TryGetDecimal(out decimal amount))
                {
                    character.LevelGains.Add(amount);
                }
                else
                {
                    Error("$.level_gains", "level_gains must be an array of numbers.");
                    return;
                }
            }
        }

        private void ReadList(JsonElement root, string name, DefinitionType type, List<Definition> into)
        {
            if (!root.TryGetProperty(name, out JsonElement list))
            {
                return;
            }

            if (list.ValueKind != JsonValueKind.Array)
            {
                Error($"$.{name}", $"\"{name}\" must be an array of {type.Name} IDs.");
                return;
            }

            int index = 0;
            foreach (JsonElement entry in list.EnumerateArray())
            {
                if (Resolve(entry, $"$.{name}[{index}]", type) is Definition definition)
                {
                    into.Add(definition);
                }

                index++;
            }
        }

        private Definition? Reference(JsonElement root, string name, DefinitionType type)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
            {
                Error("$", $"Missing \"{name}\": a {type.Name} ID.");
                return null;
            }

            return Resolve(value, $"$.{name}", type);
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
