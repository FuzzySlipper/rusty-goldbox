using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Campaigns;

/// <summary>
/// A saved campaign: the resolved module set (ID, version and content
/// identity for each), the campaign state and the party. Loading it under a
/// different module set is refused, naming every difference.
/// </summary>
public static class SaveFile
{
    public const int CurrentFormat = 1;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] Fields =
    [
        "format", "modules", "extensions", "campaign", "seed", "commands", "area", "x", "y", "facing", "variables", "found_secrets", "fired", "pending_menu", "pending_shop", "pending_temple", "pending_training", "elapsed_days", "picture", "music", "inventory", "ended", "party", "absent_npcs",
    ];

    public static string ToJson(CampaignState state, ModuleSet set)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", CurrentFormat);
            writer.WriteStartArray("modules");
            foreach (LoadedModule loaded in set.LoadOrder)
            {
                writer.WriteStartObject();
                writer.WriteString("id", loaded.Manifest.Id);
                writer.WriteString("version", loaded.Manifest.Version.ToString());
                writer.WriteString("identity", loaded.Manifest.Source.Identity);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("extensions");
            foreach (string extension in set.Extensions)
            {
                writer.WriteStringValue(extension);
            }

            writer.WriteEndArray();
            writer.WriteString("campaign", state.Campaign.QualifiedId);
            writer.WriteString("seed", state.Seed.ToString(CultureInfo.InvariantCulture));
            writer.WriteNumber("commands", state.Commands);
            writer.WriteString("area", state.Area.QualifiedId);
            writer.WriteNumber("x", state.X);
            writer.WriteNumber("y", state.Y);
            writer.WriteString("facing", Facings.Name(state.Facing));
            writer.WriteStartObject("variables");
            writer.WriteStartObject("campaign");
            foreach ((string id, Value value) in state.Variables.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                WriteValue(writer, id, value);
            }

            writer.WriteEndObject();
            writer.WriteStartObject("areas");
            foreach ((string area, Dictionary<string, Value> values) in state.AreaVariables.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(area);
                foreach ((string id, Value value) in values.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    WriteValue(writer, id, value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteStartArray("found_secrets");
            foreach (string found in state.FoundSecrets.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(found);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("fired");
            foreach (string fired in state.Fired.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(fired);
            }

            writer.WriteEndArray();
            if (state.PendingMenu is null)
            {
                writer.WriteNull("pending_menu");
            }
            else
            {
                writer.WriteString("pending_menu", state.PendingMenu.QualifiedId);
            }

            writer.WriteString("pending_shop", state.PendingShop?.QualifiedId);
            writer.WriteString("pending_temple", state.PendingTemple?.QualifiedId);
            writer.WriteString("pending_training", state.PendingTraining?.QualifiedId);
            writer.WriteNumber("elapsed_days", state.ElapsedDays);
            writer.WriteString("picture", state.Picture?.QualifiedId);
            writer.WriteString("music", state.Music?.QualifiedId);
            writer.WriteStartArray("inventory");
            foreach (Definition item in state.Inventory)
            {
                writer.WriteStringValue(item.QualifiedId);
            }

            writer.WriteEndArray();
            writer.WriteBoolean("ended", state.Ended);
            writer.WriteStartArray("party");
            foreach (Character character in state.Party)
            {
                CharacterFile.Write(writer, character);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("absent_npcs");
            foreach (Character character in state.AbsentNpcs.OrderBy(character => character.Npc!.QualifiedId, StringComparer.Ordinal))
            {
                CharacterFile.Write(writer, character);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteValue(Utf8JsonWriter writer, string id, Value value)
    {
        switch (value.Type)
        {
            case ExprType.Number:
                writer.WriteNumber(id, value.Number);
                break;
            case ExprType.Boolean:
                writer.WriteBoolean(id, value.Boolean);
                break;
            default:
                writer.WriteString(id, value.Text);
                break;
        }
    }

    /// <summary>Reads a save file against the loaded module set; problems name the file and JSON path.</summary>
    public static CampaignState? Read(string path, ModuleSet set, List<ModuleDiagnostic> problems)
    {
        using JsonDocument? document = JsonFiles.Parse(path, null, problems);
        return document is null ? null : new Reader(path, set, problems).Read(document.RootElement);
    }

    /// <summary>Reads save JSON from elsewhere (an Engine save slot) named <paramref name="location"/> in problems.</summary>
    public static CampaignState? Read(ReadOnlyMemory<byte> json, string location, ModuleSet set, List<ModuleDiagnostic> problems)
    {
        using JsonDocument? document = JsonFiles.Parse(json, location, null, problems);
        return document is null ? null : new Reader(location, set, problems).Read(document.RootElement);
    }

    private sealed class Reader(string path, ModuleSet set, List<ModuleDiagnostic> problems)
    {
        private readonly RuleSet _rules = set.Rules!;
        private readonly int _before = problems.Count;

        public CampaignState? Read(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                Error("$", "A save must be a JSON object, as written by `goldbox play --save`.");
                return null;
            }

            foreach (JsonProperty property in root.EnumerateObject().Where(property => !Fields.Contains(property.Name)))
            {
                Error($"$.{property.Name}", $"'{property.Name}' is not a save field. Fields: {string.Join(", ", Fields)}.");
            }

            if (Integer(root, "format") is int format && format != CurrentFormat)
            {
                Error("$.format", $"Save format {format} is not supported; this goldbox reads format {CurrentFormat}.");
            }

            CheckModules(root);
            if (problems.Count > _before)
            {
                return null;
            }

            Definition? campaign = Find(root, "campaign", DefinitionTypes.Campaign);
            Definition? area = Find(root, "area", DefinitionTypes.Area);
            ulong seed = 0;
            if (Text(root, "seed") is string seedText && !ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
            {
                Error("$.seed", "seed must be a whole number, written as text.");
            }

            int? commands = Integer(root, "commands");
            int? x = Integer(root, "x");
            int? y = Integer(root, "y");
            Facing facing = Facing.North;
            if (Text(root, "facing") is string facingText && !Facings.TryParse(facingText, out facing))
            {
                Error("$.facing", $"'{facingText}' is not a facing: {string.Join(", ", Facings.Names)}.");
            }

            if (campaign is null || area is null || commands is null || x is null || y is null || problems.Count > _before)
            {
                return null;
            }

            AreaMap map = AreaMap.Parse(area.Json.GetProperty("map").EnumerateArray().Select(row => row.GetString()!).ToList(), [])!;
            if (!map.Contains(x.Value, y.Value))
            {
                Error(x < 0 || x >= map.Width ? "$.x" : "$.y", $"[{x}, {y}] is outside {area.QualifiedId}, which is {map.Width} wide and {map.Height} high.");
            }

            if (commands < 0)
            {
                Error("$.commands", "commands can't be negative.");
            }

            if (problems.Count > _before)
            {
                return null;
            }

            CampaignState state = new() { Campaign = campaign, Seed = seed, Area = area, X = x.Value, Y = y.Value, Facing = facing, Commands = commands.Value };
            ReadVariables(root, state);
            ReadFoundSecrets(root, state);
            ReadRest(root, state);
            return problems.Count > _before ? null : state;
        }

        private void CheckModules(JsonElement root)
        {
            if (!root.TryGetProperty("modules", out JsonElement modules) || modules.ValueKind != JsonValueKind.Array)
            {
                Error("$.modules", "Missing \"modules\": the module set the save was made under.");
                return;
            }

            Dictionary<string, (string Version, string Identity)> saved = [];
            foreach (JsonElement module in modules.EnumerateArray())
            {
                if (module.ValueKind == JsonValueKind.Object
                    && module.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String
                    && module.TryGetProperty("version", out JsonElement version) && version.ValueKind == JsonValueKind.String
                    && module.TryGetProperty("identity", out JsonElement identity) && identity.ValueKind == JsonValueKind.String)
                {
                    saved[id.GetString()!] = (version.GetString()!, identity.GetString()!);
                }
                else
                {
                    Error("$.modules", "Each modules entry must be { \"id\", \"version\", \"identity\" }.");
                    return;
                }
            }

            List<string> differences = [];
            foreach (LoadedModule loaded in set.LoadOrder)
            {
                ModuleManifest manifest = loaded.Manifest;
                if (!saved.Remove(manifest.Id, out (string Version, string Identity) entry))
                {
                    differences.Add($"{manifest.Id} {manifest.Version} is loaded but wasn't in the saved set");
                }
                else if (entry.Version != manifest.Version.ToString())
                {
                    differences.Add($"{manifest.Id} is {manifest.Version}, but the save was made with {entry.Version}");
                }
                else if (entry.Identity != manifest.Source.Identity)
                {
                    differences.Add($"{manifest.Id} {manifest.Version} has different content from when the save was made");
                }
            }

            List<string> extensions = Extensions(root);
            differences.AddRange(saved.Select(entry => extensions.Contains(entry.Key)
                ? $"{entry.Key} {entry.Value.Version} was added to the saved set as an extension but isn't loaded (add it with --extension {entry.Key})"
                : $"{entry.Key} {entry.Value.Version} was in the saved set but isn't loaded"));
            if (differences.Count > 0)
            {
                Error("$.modules", $"The save was made under a different module set: {string.Join("; ", differences)}. Load the exact modules it was saved with.");
            }
        }

        /// <summary>The extensions the save's set added (none when it names none).</summary>
        private List<string> Extensions(JsonElement root)
        {
            List<string> extensions = [];
            if (!root.TryGetProperty("extensions", out JsonElement added))
            {
                return extensions;
            }

            if (added.ValueKind != JsonValueKind.Array || added.EnumerateArray().Any(entry => entry.ValueKind != JsonValueKind.String))
            {
                Error("$.extensions", "\"extensions\" must be a list of the module IDs added to the set as extensions.");
                return extensions;
            }

            extensions.AddRange(added.EnumerateArray().Select(entry => entry.GetString()!));
            return extensions;
        }

        private void ReadVariables(JsonElement root, CampaignState state)
        {
            if (!root.TryGetProperty("variables", out JsonElement variables) || variables.ValueKind != JsonValueKind.Object)
            {
                Error("$.variables", "Missing \"variables\": the campaign and area variables and their values.");
                return;
            }

            if (!variables.TryGetProperty("campaign", out JsonElement campaign) || campaign.ValueKind != JsonValueKind.Object)
            {
                Error("$.variables.campaign", "campaign must be an object containing the campaign-scoped variable values.");
            }
            else
            {
                ReadVariableObject(campaign, "$.variables.campaign", _rules.Variables, state.Variables, "campaign");
            }

            if (!variables.TryGetProperty("areas", out JsonElement areas) || areas.ValueKind != JsonValueKind.Object)
            {
                Error("$.variables.areas", "areas must be an object mapping each area ID to its area-scoped variable values.");
                return;
            }

            foreach (Definition area in _rules.OfType(DefinitionTypes.Area))
            {
                if (!areas.TryGetProperty(area.QualifiedId, out JsonElement values) || values.ValueKind != JsonValueKind.Object)
                {
                    Error($"$.variables.areas.{area.QualifiedId}", $"Missing values for area {area.QualifiedId}.");
                    continue;
                }

                ReadVariableObject(values, $"$.variables.areas.{area.QualifiedId}", _rules.AreaVariables, state.ValuesFor(area), "area");
            }

            foreach (JsonProperty extra in areas.EnumerateObject().Where(property => _rules.OfType(DefinitionTypes.Area).All(area => area.QualifiedId != property.Name)))
            {
                Error($"$.variables.areas.{extra.Name}", $"'{extra.Name}' is not an area of this module set.");
            }
        }

        private void ReadVariableObject(JsonElement values, string at, Dictionary<string, Definition> definitions, Dictionary<string, Value> destination, string scope)
        {
            foreach (Definition variable in definitions.Values)
            {
                string variableAt = $"{at}.{variable.Id}";
                if (!values.TryGetProperty(variable.Id, out JsonElement value))
                {
                    Error(at, $"Missing {scope} variable {variable.Id}.");
                    continue;
                }

                string type = variable.Json.GetProperty("value_type").GetString()!;
                Value? read = (type, value.ValueKind) switch
                {
                    ("number", JsonValueKind.Number) when value.TryGetDecimal(out decimal number) => Value.Of(number),
                    ("boolean", JsonValueKind.True or JsonValueKind.False) => Value.Of(value.GetBoolean()),
                    ("text", JsonValueKind.String) => Value.Of(value.GetString()!),
                    _ => null,
                };
                if (read is null)
                {
                    Error(variableAt, $"{variable.Id} is a {type} variable, but the saved value is {JsonFiles.Describe(value.ValueKind)}.");
                }
                else
                {
                    destination[variable.Id] = read.Value;
                }
            }

            foreach (JsonProperty extra in values.EnumerateObject().Where(property => !definitions.ContainsKey(property.Name)))
            {
                Error($"{at}.{extra.Name}", $"'{extra.Name}' is not a {scope} variable of this module set.");
            }
        }

        private void ReadRest(JsonElement root, CampaignState state)
        {
            if (root.TryGetProperty("fired", out JsonElement fired) && fired.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in fired.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        state.Fired.Add(entry.GetString()!);
                    }
                    else
                    {
                        Error("$.fired", "fired must be an array of text.");
                    }
                }
            }
            else
            {
                Error("$.fired", "Missing \"fired\": the once-only triggers that have run.");
            }

            if (!root.TryGetProperty("pending_menu", out JsonElement pending) || pending.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                Error("$.pending_menu", "pending_menu must be a menu event ID, or null when no menu is waiting.");
            }
            else if (pending.ValueKind == JsonValueKind.String)
            {
                state.PendingMenu = Find(root, "pending_menu", DefinitionTypes.Event);
                if (state.PendingMenu is not null && state.PendingMenu.Json.GetProperty("kind").GetString() != "menu")
                {
                    Error("$.pending_menu", $"{state.PendingMenu.QualifiedId} is not a menu event.");
                }
            }

            if (!root.TryGetProperty("pending_shop", out JsonElement shop) || shop.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                Error("$.pending_shop", "pending_shop must be a shop event ID, or null when no shop is open.");
            }
            else if (shop.ValueKind == JsonValueKind.String)
            {
                state.PendingShop = Find(root, "pending_shop", DefinitionTypes.Event);
                if (state.PendingShop is not null && state.PendingShop.Json.GetProperty("kind").GetString() != "shop")
                {
                    Error("$.pending_shop", $"{state.PendingShop.QualifiedId} is not a shop event.");
                }

                if (state.PendingMenu is not null)
                {
                    Error("$.pending_shop", "A save cannot wait at a menu and a shop at the same time; clear one pending event.");
                }
            }

            if (!root.TryGetProperty("pending_temple", out JsonElement temple) || temple.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                Error("$.pending_temple", "pending_temple must be a temple event ID, or null.");
            }
            else if (temple.ValueKind == JsonValueKind.String)
            {
                state.PendingTemple = Find(root, "pending_temple", DefinitionTypes.Event);
                if (state.PendingTemple is not null && state.PendingTemple.Json.GetProperty("kind").GetString() != "temple")
                {
                    Error("$.pending_temple", "The pending event must have kind temple.");
                }

                if (state.PendingMenu is not null || state.PendingShop is not null)
                {
                    Error("$.pending_temple", "A save can wait at only one menu, shop or temple.");
                }
            }

            if (!root.TryGetProperty("elapsed_days", out JsonElement elapsed) || elapsed.ValueKind != JsonValueKind.Number || !elapsed.TryGetDecimal(out decimal days) || days < 0)
            {
                Error("$.elapsed_days", "elapsed_days must be a nonnegative number of fictional campaign days.");
            }
            else
            {
                state.ElapsedDays = days;
            }

            if (!root.TryGetProperty("pending_training", out JsonElement training) || training.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                Error("$.pending_training", "pending_training must be a training event ID, or null.");
            }
            else if (training.ValueKind == JsonValueKind.String)
            {
                state.PendingTraining = Find(root, "pending_training", DefinitionTypes.Event);
                if (state.PendingTraining is not null && state.PendingTraining.Json.GetProperty("kind").GetString() != "training")
                {
                    Error("$.pending_training", "The pending event must have kind training.");
                }

                if (state.PendingMenu is not null || state.PendingShop is not null || state.PendingTemple is not null)
                {
                    Error("$.pending_training", "A save can wait at only one interactive event.");
                }
            }

            state.Picture = Media(root, "picture");
            state.Music = Media(root, "music");

            if (root.TryGetProperty("inventory", out JsonElement inventory) && inventory.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement item in inventory.EnumerateArray())
                {
                    if (Resolve(item, $"$.inventory[{index}]", DefinitionTypes.Item) is Definition found)
                    {
                        state.Inventory.Add(found);
                    }

                    index++;
                }
            }
            else
            {
                Error("$.inventory", "Missing \"inventory\": the items the party carries.");
            }

            state.Ended = root.TryGetProperty("ended", out JsonElement ended) && ended.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? ended.GetBoolean()
                : Fail<bool>("$.ended", "ended must be true or false.");
            if (!root.TryGetProperty("party", out JsonElement party) || party.ValueKind != JsonValueKind.Array)
            {
                Error("$.party", "Missing \"party\": the characters.");
                return;
            }

            int member = 0;
            foreach (JsonElement character in party.EnumerateArray())
            {
                if (CharacterFile.Read(character, path, $"$.party[{member}]", set, problems) is Character read)
                {
                    state.Party.Add(read);
                }

                member++;
            }

            if (!root.TryGetProperty("absent_npcs", out JsonElement absent) || absent.ValueKind != JsonValueKind.Array)
            {
                Error("$.absent_npcs", "absent_npcs must be an array of NPC characters outside the party.");
            }
            else
            {
                int index = 0;
                foreach (JsonElement data in absent.EnumerateArray())
                {
                    if (CharacterFile.Read(data, path, $"$.absent_npcs[{index}]", set, problems) is Character character)
                    {
                        if (character.Npc is null)
                        {
                            Error($"$.absent_npcs[{index}].npc", "An absent character must name its npc definition.");
                        }
                        else
                        {
                            state.AbsentNpcs.Add(character);
                        }
                    }

                    index++;
                }
            }

            foreach (var duplicate in state.Party.Concat(state.AbsentNpcs).Where(character => character.Npc is not null).GroupBy(character => character.Npc).Where(group => group.Count() > 1))
            {
                Error("$.absent_npcs", $"NPC {duplicate.Key!.QualifiedId} appears more than once; keep one copy, in the party or absent.");
            }

            JsonElement size = state.Campaign.Json.GetProperty("party");
            int min = size.GetProperty("min").GetInt32();
            int max = size.GetProperty("max").GetInt32();
            if (member < min || member > max)
            {
                Error("$.party", $"{state.Campaign.Name} takes a party of {min} to {max}; the save has {member}.");
            }
        }

        private void ReadFoundSecrets(JsonElement root, CampaignState state)
        {
            if (!root.TryGetProperty("found_secrets", out JsonElement found) || found.ValueKind != JsonValueKind.Array)
            {
                Error("$.found_secrets", "Missing \"found_secrets\": the secret doors discovered by searching.");
                return;
            }

            int index = 0;
            foreach (JsonElement entry in found.EnumerateArray())
            {
                string at = $"$.found_secrets[{index}]";
                if (entry.ValueKind != JsonValueKind.String)
                {
                    Error(at, "found_secrets entries must be text area edge keys such as \"tale:hall|1,0,west\".");
                }
                else
                {
                    string key = entry.GetString()!;
                    int separator = key.IndexOf('|');
                    if (separator <= 0 || separator == key.Length - 1 || key.IndexOf('|', separator + 1) >= 0)
                    {
                        Error(at, "A found secret key must be '<area qualified ID>|<x>,<y>,<facing>'.");
                        index++;
                        continue;
                    }

                    string areaId = key[..separator];
                    string edgeText = key[(separator + 1)..];
                    Definition? area = _rules.Find(DefinitionTypes.Area, areaId, out string? problem);
                    if (area is null)
                    {
                        Error(at, problem!);
                        index++;
                        continue;
                    }

                    if (!AreaEdge.TryParse(edgeText, out AreaEdge parsed))
                    {
                        Error(at, "The edge must be a canonical '<x>,<y>,<facing>' key; facing is north, east, south or west.");
                        index++;
                        continue;
                    }

                    AreaMap map = AreaMap.Parse(area.Json.GetProperty("map").EnumerateArray().Select(row => row.GetString()!).ToList(), [])!;
                    AreaEdge edge = parsed.Canonical;
                    if (!map.ContainsEdge(edge.X, edge.Y, edge.Facing))
                    {
                        Error(at, $"{edgeText} is outside {area.QualifiedId}'s {map.Width}x{map.Height} map.");
                        index++;
                        continue;
                    }

                    if (map.EdgeOf(edge.X, edge.Y, edge.Facing) != Edge.Secret)
                    {
                        Error(at, $"{edgeText} is not a secret door edge in {area.QualifiedId}.");
                        index++;
                        continue;
                    }

                    string canonical = $"{area.QualifiedId}|{edge.Key}";
                    if (!state.FoundSecrets.Add(canonical))
                    {
                        Error(at, "The found secret key is duplicated.");
                    }
                }

                index++;
            }
        }

        private Definition? Find(JsonElement root, string name, DefinitionType type)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
            {
                Error("$", $"Missing \"{name}\": a {type.Name} ID.");
                return null;
            }

            return Resolve(value, $"$.{name}", type);
        }

        /// <summary>The asset the save shows or plays as <paramref name="name"/>, which must fit that slot; null when it names none.</summary>
        private Definition? Media(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (Resolve(value, $"$.{name}", DefinitionTypes.Asset) is not Definition asset)
            {
                return null;
            }

            if (Definitions.Media.Problem(name, asset) is string problem)
            {
                Error($"$.{name}", problem);
                return null;
            }

            return asset;
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

        private int? Integer(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            {
                return number;
            }

            Error($"$.{name}", $"\"{name}\" must be a whole number.");
            return null;
        }

        private string? Text(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            Error($"$.{name}", $"\"{name}\" must be text.");
            return null;
        }

        private T Fail<T>(string at, string message)
        {
            Error(at, message);
            return default!;
        }

        private void Error(string jsonPath, string message)
        {
            problems.Add(new ModuleDiagnostic("save.file", message, File: path, JsonPath: jsonPath));
        }
    }
}
