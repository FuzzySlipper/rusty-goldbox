using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
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
        "format", "modules", "extensions", "campaign", "seed", "commands", "combat_sequence", "area", "x", "y", "facing", "variables", "found_secrets", "opened_doors", "fired", "pending_menu", "pending_shop", "pending_temple", "pending_training", "pending_combat", "elapsed_days", "picture", "music", "view_event", "inventory", "ended", "party", "absent_npcs",
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
            writer.WriteNumber("combat_sequence", state.CombatSequence);
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
            writer.WriteStartArray("opened_doors");
            foreach (string opened in state.OpenedDoors.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(opened);
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
            WritePendingCombat(writer, state.PendingCombat);
            writer.WriteNumber("elapsed_days", state.ElapsedDays);
            writer.WriteString("picture", state.Picture?.QualifiedId);
            writer.WriteString("music", state.Music?.QualifiedId);
            writer.WriteString("view_event", state.ViewEvent?.QualifiedId);
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

    private static void WritePendingCombat(Utf8JsonWriter writer, PendingCombatState? pending)
    {
        if (pending is null)
        {
            writer.WriteNull("pending_combat");
            return;
        }

        writer.WriteStartObject("pending_combat");
        writer.WriteString("event", pending.Event.QualifiedId);
        writer.WriteString("encounter", pending.Encounter.QualifiedId);
        writer.WriteString("combat", pending.Combat.QualifiedId);
        writer.WriteBoolean("finalized", pending.Finalized);
        writer.WritePropertyName("continuation");
        JsonSerializer.Serialize(writer, pending.Continuation);
        writer.WriteStartArray("participants");
        foreach (PendingCombatantSource participant in pending.Participants)
        {
            writer.WriteStartObject();
            writer.WriteString("id", participant.Id);
            writer.WriteNumber("side", participant.Side);
            writer.WriteString("name", participant.Name);
            writer.WriteString("monster", participant.MonsterId);
            if (participant.PartyIndex is int partyIndex)
            {
                writer.WriteNumber("party_index", partyIndex);
            }
            else
            {
                writer.WriteNull("party_index");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("members");
        foreach (PendingFightMember member in pending.Members)
        {
            writer.WriteStartObject();
            writer.WriteString("name", member.Name);
            writer.WriteNumber("side", member.Side);
            writer.WriteString("monster", member.MonsterId);
            writer.WriteString("class", member.ClassId);
            writer.WriteNumber("start", member.Start);
            if (member.Max is decimal maximum)
            {
                writer.WriteNumber("max", maximum);
            }
            else
            {
                writer.WriteNull("max");
            }

            if (member.Position is Cell position)
            {
                writer.WriteStartObject("position");
                writer.WriteNumber("x", position.X);
                writer.WriteNumber("y", position.Y);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("position");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
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
            long? combatSequence = OptionalLong(root, "combat_sequence", 0);
            int? x = Integer(root, "x");
            int? y = Integer(root, "y");
            Facing facing = Facing.North;
            if (Text(root, "facing") is string facingText && !Facings.TryParse(facingText, out facing))
            {
                Error("$.facing", $"'{facingText}' is not a facing: {string.Join(", ", Facings.Names)}.");
            }

            if (campaign is null || area is null || commands is null || combatSequence is null || x is null || y is null || problems.Count > _before)
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

            if (combatSequence < 0)
            {
                Error("$.combat_sequence", "combat_sequence can't be negative.");
            }

            if (problems.Count > _before)
            {
                return null;
            }

            CampaignState state = new() { Campaign = campaign, Seed = seed, Area = area, X = x.Value, Y = y.Value, Facing = facing, Commands = commands.Value, CombatSequence = combatSequence.Value };
            ReadVariables(root, state);
            ReadFoundSecrets(root, state);
            ReadOpenedDoors(root, state);
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

            ReadPendingCombat(root, state);

            state.Picture = Media(root, "picture");
            state.Music = Media(root, "music");
            ReadViewEvent(root, state);

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

            if (state.PendingCombat is PendingCombatState pendingCombat)
            {
                foreach (PendingCombatantSource participant in pendingCombat.Participants.Where(participant => participant.PartyIndex is int))
                {
                    if (participant.PartyIndex!.Value >= state.Party.Count)
                    {
                        Error("$.pending_combat.participants", $"Combatant '{participant.Id}' names party_index {participant.PartyIndex.Value}, but the save has {state.Party.Count} party members.");
                    }
                }
            }

            JsonElement size = state.Campaign.Json.GetProperty("party");
            int min = size.GetProperty("min").GetInt32();
            int max = size.GetProperty("max").GetInt32();
            if (member < min || member > max)
            {
                Error("$.party", $"{state.Campaign.Name} takes a party of {min} to {max}; the save has {member}.");
            }
        }

        private void ReadViewEvent(JsonElement root, CampaignState state)
        {
            if (!root.TryGetProperty("view_event", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                Error("$.view_event", "view_event must be a text event ID with authored views, or null.");
                return;
            }

            Definition? eventDefinition = Find(root, "view_event", DefinitionTypes.Event);
            if (eventDefinition is null)
            {
                return;
            }

            if (eventDefinition.Json.GetProperty("kind").GetString() != "text"
                || !eventDefinition.Json.TryGetProperty("views", out JsonElement views)
                || views.ValueKind != JsonValueKind.Array
                || views.GetArrayLength() == 0)
            {
                Error("$.view_event", $"{eventDefinition.QualifiedId} is not a text event with authored views.");
                return;
            }

            state.ViewEvent = eventDefinition;
        }

        private void ReadPendingCombat(JsonElement root, CampaignState state)
        {
            if (!root.TryGetProperty("pending_combat", out JsonElement data) || data.ValueKind == JsonValueKind.Null)
            {
                return;
            }

            if (data.ValueKind != JsonValueKind.Object)
            {
                Error("$.pending_combat", "pending_combat must be an object or null.");
                return;
            }

            if (state.PendingMenu is not null || state.PendingShop is not null || state.PendingTemple is not null || state.PendingTraining is not null)
            {
                Error("$.pending_combat", "A save can wait at only one interactive event or combat.");
            }

            Definition? evt = ResolveProperty(data, "event", "$.pending_combat.event", DefinitionTypes.Event);
            Definition? encounter = ResolveProperty(data, "encounter", "$.pending_combat.encounter", DefinitionTypes.Encounter);
            Definition? combat = ResolveProperty(data, "combat", "$.pending_combat.combat", DefinitionTypes.Combat);
            if (evt is not null && evt.Json.GetProperty("kind").GetString() != "combat")
            {
                Error("$.pending_combat.event", "The pending event must have kind combat.");
            }

            if (!data.TryGetProperty("continuation", out JsonElement continuationData) || continuationData.ValueKind != JsonValueKind.Object)
            {
                Error("$.pending_combat.continuation", "continuation must be a combat continuation object.");
                return;
            }

            CombatContinuationState? continuation = null;
            try
            {
                continuation = JsonSerializer.Deserialize<CombatContinuationState>(continuationData.GetRawText());
            }
            catch (JsonException exception)
            {
                Error("$.pending_combat.continuation", $"continuation is not valid combat state: {exception.Message}");
            }

            if (continuation is null || evt is null || encounter is null || combat is null)
            {
                return;
            }

            bool finalized = false;
            if (data.TryGetProperty("finalized", out JsonElement finalizedValue))
            {
                if (finalizedValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    finalized = finalizedValue.GetBoolean();
                }
                else
                {
                    Error("$.pending_combat.finalized", "finalized must be true or false.");
                }
            }

            PendingCombatState pending = new()
            {
                Event = evt,
                Encounter = encounter,
                Combat = combat,
                Continuation = continuation,
                Finalized = finalized,
            };

            if (continuation.NextRandomKey < 0)
            {
                Error("$.pending_combat.continuation.NextRandomKey", "NextRandomKey cannot be negative.");
            }

            if (string.IsNullOrWhiteSpace(continuation.RandomScope))
            {
                Error("$.pending_combat.continuation.RandomScope", "A saved campaign combat needs its nonempty RandomScope.");
            }

            if (!data.TryGetProperty("participants", out JsonElement participants) || participants.ValueKind != JsonValueKind.Array)
            {
                Error("$.pending_combat.participants", "participants must be an array.");
            }
            else
            {
                int index = 0;
                HashSet<string> ids = new(StringComparer.Ordinal);
                foreach (JsonElement participant in participants.EnumerateArray())
                {
                    string at = $"$.pending_combat.participants[{index}]";
                    if (participant.ValueKind != JsonValueKind.Object
                        || !participant.TryGetProperty("id", out JsonElement id)
                        || id.ValueKind != JsonValueKind.String
                        || !participant.TryGetProperty("side", out JsonElement side)
                        || side.ValueKind != JsonValueKind.Number || !side.TryGetInt32(out int sideNumber)
                        || !participant.TryGetProperty("name", out JsonElement name)
                        || name.ValueKind != JsonValueKind.String)
                    {
                        Error(at, "Each participant must contain string id/name and integer side.");
                        index++;
                        continue;
                    }

                    if (sideNumber is not (0 or 1))
                    {
                        Error($"{at}.side", "Campaign combat participants must belong to side 0 (party) or side 1 (encounter).");
                    }

                    string participantId = id.GetString()!;
                    if (string.IsNullOrWhiteSpace(participantId))
                    {
                        Error($"{at}.id", "Participant IDs must not be empty.");
                    }

                    if (string.IsNullOrWhiteSpace(name.GetString()))
                    {
                        Error($"{at}.name", "Participant names must not be empty.");
                    }

                    if (!ids.Add(participantId))
                    {
                        Error($"{at}.id", "Participant IDs must be unique.");
                    }

                    string? monster = participant.TryGetProperty("monster", out JsonElement monsterValue) && monsterValue.ValueKind != JsonValueKind.Null
                        ? ResolveText(monsterValue, $"{at}.monster")
                        : null;
                    if (monster is not null && _rules.Find(DefinitionTypes.Monster, monster, out string? monsterProblem) is null)
                    {
                        Error($"{at}.monster", monsterProblem!);
                    }

                    int? partyIndex = null;
                    if (participant.TryGetProperty("party_index", out JsonElement partyValue) && partyValue.ValueKind != JsonValueKind.Null)
                    {
                        if (!partyValue.TryGetInt32(out int parsedPartyIndex) || parsedPartyIndex < 0)
                        {
                            Error($"{at}.party_index", "party_index must be a nonnegative integer or null.");
                        }
                        else
                        {
                            partyIndex = parsedPartyIndex;
                        }
                    }

                    if (sideNumber == 0 && partyIndex is null)
                    {
                        Error($"{at}.party_index", "A party-side combatant must name its party_index.");
                    }

                    if (sideNumber == 1 && monster is null)
                    {
                        Error($"{at}.monster", "An encounter-side combatant must name its monster definition.");
                    }

                    if (partyIndex is not null && monster is not null)
                    {
                        Error(at, "A combatant source must be either a party member or a monster, not both.");
                    }

                    pending.Participants.Add(new PendingCombatantSource(participantId, sideNumber, name.GetString()!, monster, partyIndex));
                    index++;
                }
            }

            if (!data.TryGetProperty("members", out JsonElement members) || members.ValueKind != JsonValueKind.Array)
            {
                Error("$.pending_combat.members", "members must be an array.");
            }
            else
            {
                int index = 0;
                foreach (JsonElement member in members.EnumerateArray())
                {
                    string at = $"$.pending_combat.members[{index}]";
                    if (member.ValueKind != JsonValueKind.Object
                        || !member.TryGetProperty("name", out JsonElement name) || name.ValueKind != JsonValueKind.String
                        || !member.TryGetProperty("side", out JsonElement side) || !side.TryGetInt32(out int sideNumber)
                        || !member.TryGetProperty("start", out JsonElement start) || !start.TryGetDecimal(out decimal startValue))
                    {
                        Error(at, "Each member must contain string name and numeric side/start.");
                        index++;
                        continue;
                    }

                    if (sideNumber is not (0 or 1))
                    {
                        Error($"{at}.side", "Campaign combat members must belong to side 0 (party) or side 1 (encounter).");
                    }

                    if (string.IsNullOrWhiteSpace(name.GetString()))
                    {
                        Error($"{at}.name", "Fight member names must not be empty.");
                    }

                    string? monster = ReadOptionalReference(member, "monster", $"{at}.monster", DefinitionTypes.Monster);
                    string? characterClass = ReadOptionalReference(member, "class", $"{at}.class", DefinitionTypes.Class);
                    decimal? maximum = ReadOptionalDecimal(member, "max", $"{at}.max");
                    Cell? position = ReadPosition(member, $"{at}.position");
                    pending.Members.Add(new PendingFightMember(name.GetString()!, sideNumber, monster, characterClass, startValue, maximum, position));
                    index++;
                }
            }

            if (continuation.Format != 1)
            {
                Error("$.pending_combat.continuation.Format", $"Combat continuation format {continuation.Format} is not supported; expected 1.");
            }

            if (continuation.MaxRounds < 1)
            {
                Error("$.pending_combat.continuation.MaxRounds", "Combat continuation MaxRounds must be positive.");
            }

            if (!string.IsNullOrEmpty(continuation.CombatId) && continuation.CombatId != combat.QualifiedId)
            {
                Error("$.pending_combat.continuation.CombatId", $"Combat continuation belongs to {continuation.CombatId}, not {combat.QualifiedId}.");
            }

            HashSet<string> continuationIds = new(StringComparer.Ordinal);
            if (continuation.Combatants is null)
            {
                Error("$.pending_combat.continuation.Combatants", "Combatants must be an array.");
            }
            else
            {
                for (int index = 0; index < continuation.Combatants.Count; index++)
                {
                    CombatantState combatant = continuation.Combatants[index];
                    string at = $"$.pending_combat.continuation.Combatants[{index}]";
                    if (combatant is null)
                    {
                        Error(at, "Each combatant continuation must contain a nonempty ID.");
                    }
                    else if (string.IsNullOrWhiteSpace(combatant.Id))
                    {
                        Error($"{at}.Id", "Each combatant continuation must contain a nonempty ID.");
                    }
                    else if (!continuationIds.Add(combatant.Id))
                    {
                        Error($"{at}.Id", $"Combat continuation ID '{combatant.Id}' is duplicated.");
                    }

                    if (combatant is not null && combatant.Side is not (0 or 1))
                    {
                        Error($"{at}.Side", "Combatant continuation side must be 0 (party) or 1 (encounter).");
                    }
                }
            }

            HashSet<string> participantIds = pending.Participants.Select(participant => participant.Id).ToHashSet(StringComparer.Ordinal);
            if (continuation.Combatants is not null && (pending.Participants.Count != continuation.Combatants.Count || !participantIds.SetEquals(continuationIds)))
            {
                Error("$.pending_combat", "participants and continuation.combatants must contain exactly the same combatant IDs.");
            }

            if (continuation.Combatants is not null)
            {
                Dictionary<string, PendingCombatantSource> sources = pending.Participants
                    .GroupBy(participant => participant.Id, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                for (int index = 0; index < continuation.Combatants.Count; index++)
                {
                    CombatantState combatant = continuation.Combatants[index];
                    if (combatant is not null
                        && !string.IsNullOrWhiteSpace(combatant.Id)
                        && sources.TryGetValue(combatant.Id, out PendingCombatantSource? source)
                        && combatant.Side != source.Side)
                    {
                        Error(
                            $"$.pending_combat.continuation.Combatants[{index}].Side",
                            $"Combatant '{combatant.Id}' has continuation side {combatant.Side}, but its participant source is on side {source.Side}.");
                    }
                }
            }

            if (continuation.TurnOrder is null)
            {
                Error("$.pending_combat.continuation.TurnOrder", "TurnOrder must be an array of known combatant IDs.");
            }
            else
            {
                HashSet<string> turnOrderIds = new(StringComparer.Ordinal);
                for (int index = 0; index < continuation.TurnOrder.Count; index++)
                {
                    string? id = continuation.TurnOrder[index];
                    string at = $"$.pending_combat.continuation.TurnOrder[{index}]";
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        Error(at, "TurnOrder entries must be nonempty combatant IDs.");
                    }
                    else if (!turnOrderIds.Add(id))
                    {
                        Error(at, $"TurnOrder contains duplicate combatant ID '{id}'.");
                    }
                    else if (!participantIds.Contains(id))
                    {
                        Error(at, $"TurnOrder names unknown combatant '{id}'.");
                    }
                }

                if (continuation.TurnIndex < 0 || continuation.TurnIndex > continuation.TurnOrder.Count)
                {
                    Error(
                        "$.pending_combat.continuation.TurnIndex",
                        $"TurnIndex must be between 0 and TurnOrder length ({continuation.TurnOrder.Count}), inclusive.");
                }
            }

            if (continuation.TookTurns is null)
            {
                Error("$.pending_combat.continuation.TookTurns", "TookTurns must be an array of known combatant IDs.");
            }
            else
            {
                HashSet<string> tookTurnIds = new(StringComparer.Ordinal);
                for (int index = 0; index < continuation.TookTurns.Count; index++)
                {
                    string? id = continuation.TookTurns[index];
                    string at = $"$.pending_combat.continuation.TookTurns[{index}]";
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        Error(at, "TookTurns entries must be nonempty combatant IDs.");
                    }
                    else if (!tookTurnIds.Add(id))
                    {
                        Error(at, $"TookTurns contains duplicate combatant ID '{id}'.");
                    }
                    else if (!participantIds.Contains(id))
                    {
                        Error(at, $"TookTurns names unknown combatant '{id}'.");
                    }
                }
            }

            if (pending.Members.Count != pending.Participants.Count)
            {
                Error("$.pending_combat.members", "members and participants must contain the same number of combatants.");
            }

            ValidateContinuation(continuation, participantIds);
            state.PendingCombat = pending;
        }

        private void ValidateContinuation(CombatContinuationState continuation, IReadOnlySet<string> participantIds)
        {
            const string root = "$.pending_combat.continuation";
            KnownParticipant(continuation.ActiveActorId, $"{root}.ActiveActorId", participantIds);
            KnownParticipant(continuation.LastActorId, $"{root}.LastActorId", participantIds);
            if (continuation.FledSide is int fledSide && fledSide is not (0 or 1))
            {
                Error($"{root}.FledSide", "FledSide must be side 0, side 1 or null.");
            }

            if (continuation.Winner is int winner && winner is not (0 or 1))
            {
                Error($"{root}.Winner", "Winner must be side 0, side 1 or null.");
            }

            KnownParticipant(continuation.CommittedActorId, $"{root}.CommittedActorId", participantIds);
            if (continuation.CommittedActionId is string committedActionId)
            {
                if (string.IsNullOrWhiteSpace(committedActionId))
                {
                    Error($"{root}.CommittedActionId", "CommittedActionId must be nonempty when present.");
                }
                else if (!committedActionId.Contains("/use/", StringComparison.Ordinal)
                    && !committedActionId.Contains("/reaction/", StringComparison.Ordinal))
                {
                    ResolveReference(committedActionId, $"{root}.CommittedActionId", DefinitionTypes.Action);
                }
            }

            ValidateFactStates(continuation.Facts, $"{root}.Facts");

            ValidateDiceRolls(continuation.PendingInterruptRolls, $"{root}.PendingInterruptRolls");
            ValidateDiceRolls(continuation.PendingCheckRolls, $"{root}.PendingCheckRolls");
            if (continuation.CommittedTargetRolls is not null)
            {
                ValidateDiceRolls(continuation.CommittedTargetRolls, $"{root}.CommittedTargetRolls");
            }

            if (continuation.Controllers is null)
            {
                Error($"{root}.Controllers", "Controllers must be an object keyed by known combatant IDs.");
            }
            else
            {
                foreach ((string actorId, CombatControlMode controller) in continuation.Controllers)
                {
                    KnownParticipant(actorId, $"{root}.Controllers.{actorId}", participantIds);
                    if (!Enum.IsDefined(controller))
                    {
                        Error($"{root}.Controllers.{actorId}", $"Controller value {(int)controller} is not valid.");
                    }
                }
            }

            ValidateCombatants(continuation.Combatants, $"{root}.Combatants");
            ValidateBehaviorContinuation(continuation, participantIds, root);
            ValidateDecision(continuation.PendingDecision, participantIds, $"{root}.PendingDecision");
            ValidateContinuationFrames(continuation, participantIds, root);
        }

        private void ValidateFactStates(IReadOnlyList<CombatFactState>? facts, string at)
        {
            if (facts is null)
            {
                Error(at, "Facts must be an array.");
                return;
            }

            for (int index = 0; index < facts.Count; index++)
            {
                CombatFactState? fact = facts[index];
                string factAt = $"{at}[{index}]";
                if (fact is null)
                {
                    Error(factAt, "A combat fact continuation must be an object.");
                    continue;
                }

                if (fact.Kind is null)
                {
                    Error($"{factAt}.Kind", "A combat fact continuation must have a Kind.");
                }

                if (fact.Description is null)
                {
                    Error($"{factAt}.Description", "A combat fact continuation must have a Description.");
                }

                ValidateDiceRolls(fact.Rolls, $"{factAt}.Rolls");

                if (fact.SubjectIds is null)
                {
                    Error($"{factAt}.SubjectIds", "Fact subject IDs must be an array.");
                }

                if (fact.TargetIds is null)
                {
                    Error($"{factAt}.TargetIds", "Fact target IDs must be an array.");
                }
            }
        }

        private void ValidateDiceRolls(IReadOnlyList<DiceRoll>? rolls, string at)
        {
            if (rolls is null)
            {
                Error(at, "Rolls must be an array.");
                return;
            }

            for (int index = 0; index < rolls.Count; index++)
            {
                DiceRoll? roll = rolls[index];
                string rollAt = $"{at}[{index}]";
                if (roll is null)
                {
                    Error(rollAt, "A roll must be an object.");
                    continue;
                }

                if (roll.Faces is null)
                {
                    Error($"{rollAt}.Faces", "Roll faces must be an array.");
                }
            }
        }

        private void ValidateCombatants(IReadOnlyList<CombatantState>? combatants, string at)
        {
            if (combatants is null)
            {
                return;
            }

            for (int index = 0; index < combatants.Count; index++)
            {
                CombatantState? combatant = combatants[index];
                string combatantAt = $"{at}[{index}]";
                if (combatant is null)
                {
                    Error(combatantAt, "A combatant continuation must be an object.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(combatant.Id))
                {
                    continue;
                }

                if (!Enum.IsDefined(combatant.Controller))
                {
                    Error($"{combatantAt}.Controller", $"Controller value {(int)combatant.Controller} is not valid.");
                }

                ValidateTrackMap(combatant.Tracks, $"{combatantAt}.Tracks");
                ValidateTrackMap(combatant.TrackMaximums, $"{combatantAt}.TrackMaximums");
                ValidateConditionList(combatant.Conditions, $"{combatantAt}.Conditions");
                ValidateConditionReferences(combatant.AppliedThisTurn, $"{combatantAt}.AppliedThisTurn");
                ValidateSpellReferences(combatant.Preparing, $"{combatantAt}.Preparing");
                ValidateSpellReferences(combatant.Prepared, $"{combatantAt}.Prepared");
                if (combatant.CastsLeft is null)
                {
                    Error($"{combatantAt}.CastsLeft", "CastsLeft must be an object keyed by spell IDs.");
                }
                else
                {
                    foreach (string spellId in combatant.CastsLeft.Keys)
                    {
                        ResolveReference(spellId, $"{combatantAt}.CastsLeft.{spellId}", DefinitionTypes.Spell);
                    }
                }
            }
        }

        private void ValidateTrackMap<T>(IReadOnlyDictionary<string, T>? values, string at)
        {
            if (values is null)
            {
                Error(at, "Track values must be an object keyed by track IDs.");
                return;
            }

            foreach (string trackId in values.Keys)
            {
                if (!_rules.TryTrack(trackId, out _, out _))
                {
                    Error($"{at}.{trackId}", $"There is no track '{trackId}'.");
                }
            }
        }

        private void ValidateConditionList(IReadOnlyList<CombatConditionState>? conditions, string at)
        {
            if (conditions is null)
            {
                Error(at, "Conditions must be an array of condition state objects.");
                return;
            }

            for (int index = 0; index < conditions.Count; index++)
            {
                CombatConditionState? condition = conditions[index];
                string conditionAt = $"{at}[{index}]";
                if (condition is null)
                {
                    Error(conditionAt, "A condition continuation must be an object.");
                    continue;
                }

                ResolveReference(condition.ConditionId, $"{conditionAt}.ConditionId", DefinitionTypes.Condition);
                if (condition.Values is null)
                {
                    Error($"{conditionAt}.Values", "Condition values must be an object.");
                }
            }
        }

        private void ValidateConditionReferences(IReadOnlyList<string>? conditionIds, string at)
        {
            if (conditionIds is null)
            {
                Error(at, "Condition IDs must be an array.");
                return;
            }

            for (int index = 0; index < conditionIds.Count; index++)
            {
                ResolveReference(conditionIds[index], $"{at}[{index}]", DefinitionTypes.Condition);
            }
        }

        private void ValidateSpellReferences(IReadOnlyList<string>? spellIds, string at)
        {
            if (spellIds is null)
            {
                Error(at, "Spell IDs must be an array.");
                return;
            }

            for (int index = 0; index < spellIds.Count; index++)
            {
                ResolveReference(spellIds[index], $"{at}[{index}]", DefinitionTypes.Spell);
            }
        }

        private void ValidateBehaviorContinuation(
            CombatContinuationState continuation,
            IReadOnlySet<string> participantIds,
            string root)
        {
            if (continuation.BehaviorAssignments is null)
            {
                Error($"{root}.BehaviorAssignments", "BehaviorAssignments must be an object keyed by combatant IDs.");
            }
            else
            {
                foreach ((string actorId, string? behaviorId) in continuation.BehaviorAssignments)
                {
                    string at = $"{root}.BehaviorAssignments.{actorId}";
                    KnownParticipant(actorId, at, participantIds);
                    if (behaviorId is not null)
                    {
                        ResolveBehavior(behaviorId, at);
                    }
                }
            }

            if (continuation.BehaviorStates is null)
            {
                Error($"{root}.BehaviorStates", "BehaviorStates must be an object keyed by combatant IDs.");
            }
            else
            {
                foreach ((string actorId, CombatBehaviorState? behaviorState) in continuation.BehaviorStates)
                {
                    string at = $"{root}.BehaviorStates.{actorId}";
                    KnownParticipant(actorId, at, participantIds);
                    if (behaviorState is null)
                    {
                        Error(at, "A behavior state must be an object.");
                        continue;
                    }

                    CombatBehaviorProfile? profile = behaviorState.BehaviorId is string behaviorId
                        ? ResolveBehavior(behaviorId, $"{at}.BehaviorId")
                        : null;
                    ValidateBehaviorPosition(profile, behaviorState, at, requireCommitted: true);
                }
            }

            bool anyPending = continuation.BehaviorPendingActorId is not null
                || continuation.BehaviorPendingId is not null
                || continuation.BehaviorPendingRuleIndex is not null
                || continuation.BehaviorPendingStepIndex is not null;
            if (!anyPending)
            {
                if (continuation.BehaviorPendingMovementOnly)
                {
                    Error($"{root}.BehaviorPendingMovementOnly", "BehaviorPendingMovementOnly requires a pending behavior proposal.");
                }

                return;
            }

            string pendingAt = $"{root}.BehaviorPending";
            if (continuation.BehaviorPendingActorId is not string pendingActorId)
            {
                Error($"{pendingAt}ActorId", "BehaviorPendingActorId is required when a behavior proposal is pending.");
            }
            else
            {
                KnownParticipant(pendingActorId, $"{root}.BehaviorPendingActorId", participantIds);
            }

            CombatBehaviorProfile? pendingProfile = continuation.BehaviorPendingId is string pendingId
                ? ResolveBehavior(pendingId, $"{root}.BehaviorPendingId")
                : null;
            if (continuation.BehaviorPendingId is null)
            {
                Error($"{root}.BehaviorPendingId", "BehaviorPendingId is required when a behavior proposal is pending.");
            }

            if (continuation.BehaviorPendingRuleIndex is not int pendingRuleIndex)
            {
                Error($"{root}.BehaviorPendingRuleIndex", "BehaviorPendingRuleIndex is required when a behavior proposal is pending.");
            }
            else if (pendingProfile is not null)
            {
                ValidateBehaviorPosition(pendingProfile, new CombatBehaviorState
                {
                    BehaviorId = continuation.BehaviorPendingId,
                    RuleIndex = pendingRuleIndex,
                    StepIndex = continuation.BehaviorPendingStepIndex ?? -1,
                    Committed = true,
                }, root, requireCommitted: true, pending: true);
            }

            if (continuation.BehaviorPendingStepIndex is null)
            {
                Error($"{root}.BehaviorPendingStepIndex", "BehaviorPendingStepIndex is required when a behavior proposal is pending.");
            }
        }

        private CombatBehaviorProfile? ResolveBehavior(string behaviorId, string at)
        {
            Definition? definition = ResolveReference(behaviorId, at, DefinitionTypes.CombatBehavior);
            if (definition is null)
            {
                return null;
            }

            CombatBehaviorProfile? profile = _rules.CombatBehaviorOf(definition);
            if (profile is null)
            {
                Error(at, $"Combat behavior '{behaviorId}' is not a checked behavior definition.");
            }

            return profile;
        }

        private void ValidateBehaviorPosition(
            CombatBehaviorProfile? profile,
            CombatBehaviorState state,
            string at,
            bool requireCommitted,
            bool pending = false)
        {
            if (state.BehaviorId is null)
            {
                Error($"{at}.BehaviorId", "A behavior state must name its behavior definition.");
                return;
            }

            if (requireCommitted && !state.Committed)
            {
                Error($"{at}.Committed", "A saved behavior state must be committed.");
            }

            if (state.RuleIndex < 0)
            {
                Error($"{at}.{(pending ? "BehaviorPendingRuleIndex" : "RuleIndex")}", "The behavior rule index cannot be negative.");
                return;
            }

            if (profile is null || state.RuleIndex >= profile.Rules.Count)
            {
                if (profile is not null)
                {
                    Error($"{at}.{(pending ? "BehaviorPendingRuleIndex" : "RuleIndex")}", $"The behavior rule index {state.RuleIndex} is outside the {profile.Rules.Count}-rule behavior.");
                }

                return;
            }

            if (state.StepIndex < 0)
            {
                Error($"{at}.{(pending ? "BehaviorPendingStepIndex" : "StepIndex")}", "The behavior step index cannot be negative.");
            }
            else if (state.StepIndex >= profile.Rules[state.RuleIndex].Steps.Count)
            {
                Error($"{at}.{(pending ? "BehaviorPendingStepIndex" : "StepIndex")}", $"The behavior step index {state.StepIndex} is outside rule {state.RuleIndex}, which has {profile.Rules[state.RuleIndex].Steps.Count} steps.");
            }
        }

        private void ValidateDecision(
            CombatDecision? decision,
            IReadOnlySet<string> participantIds,
            string at)
        {
            if (decision is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(decision.Id))
            {
                Error($"{at}.Id", "A pending decision must have a nonempty ID.");
            }

            RequiredParticipant(decision.ActorId, $"{at}.ActorId", participantIds);
            if (!Enum.IsDefined(decision.Kind))
            {
                Error($"{at}.Kind", $"Decision kind {(int)decision.Kind} is not valid.");
            }

            if (decision.Actions is null)
            {
                Error($"{at}.Actions", "Actions must be an array.");
            }
            else
            {
                for (int index = 0; index < decision.Actions.Count; index++)
                {
                    CombatActionChoice? action = decision.Actions[index];
                    string actionAt = $"{at}.Actions[{index}]";
                    if (action is null)
                    {
                        Error(actionAt, "An action choice must be an object.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(action.Id))
                    {
                        Error($"{actionAt}.Id", "Action choice IDs must be nonempty.");
                    }

                    ResolveReference(action.ActionId, $"{actionAt}.ActionId", DefinitionTypes.Action);
                    Definition? spell = action.SpellId is string spellId
                        ? ResolveReference(spellId, $"{actionAt}.SpellId", DefinitionTypes.Spell)
                        : null;

                    if (action.Cost is null)
                    {
                        Error($"{actionAt}.Cost", "Action cost must be an object.");
                    }

                    ValidateDecisionTargets(action.Targets, participantIds, $"{actionAt}.Targets");
                    ValidateMoveChoices(action.Moves, $"{actionAt}.Moves");
                    ValidateSpellCosts(action, spell, actionAt);
                }
            }

            ValidateMoveChoices(decision.Moves, $"{at}.Moves");

            if (decision.Options is not null)
            {
                for (int index = 0; index < decision.Options.Count; index++)
                {
                    CombatDecisionOption? option = decision.Options[index];
                    string optionAt = $"{at}.Options[{index}]";
                    if (option is null)
                    {
                        Error(optionAt, "A decision option must be an object.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(option.Id))
                    {
                        Error($"{optionAt}.Id", "Decision option IDs must not be empty.");
                    }

                    if (option.TargetId is string targetId)
                    {
                        KnownParticipant(targetId, $"{optionAt}.TargetId", participantIds);
                    }

                    if (option.TrackId is string trackId)
                    {
                        ResolveReference(trackId, $"{optionAt}.TrackId", DefinitionTypes.Track);
                    }

                    if (option.Index is int optionIndex && optionIndex < 0)
                    {
                        Error($"{optionAt}.Index", "Decision option indices cannot be negative.");
                    }

                    if (decision.Kind == CombatDecisionKind.Interrupt && option.QualifiedId is string reactionId)
                    {
                        ResolveReference(reactionId, $"{optionAt}.QualifiedId", DefinitionTypes.Reaction);
                    }
                    else if (decision.Kind == CombatDecisionKind.PostRoll && option.QualifiedId is string checkId)
                    {
                        ResolveReference(checkId, $"{optionAt}.QualifiedId", DefinitionTypes.Check);
                    }
                    else if (decision.Kind == CombatDecisionKind.Initiative && option.QualifiedId is string initiativeTarget)
                    {
                        KnownParticipant(initiativeTarget, $"{optionAt}.QualifiedId", participantIds);
                    }
                }
            }

            if ((decision.Kind is CombatDecisionKind.Interrupt or CombatDecisionKind.Initiative)
                && decision.Interrupt is null)
            {
                Error($"{at}.Interrupt", "An interrupt or initiative decision must carry its continuation frame.");
            }
            else
            {
                ValidateInterrupt(decision.Interrupt, participantIds, $"{at}.Interrupt");
            }

            if (decision.Kind == CombatDecisionKind.PostRoll && decision.Check is null)
            {
                Error($"{at}.Check", "A post-roll decision must carry its committed check.");
            }
            ValidateCheck(decision.Check, participantIds, $"{at}.Check");
            if (decision.OperationOwner is string operationOwner)
            {
                ResolveAnyDefinition(operationOwner, $"{at}.OperationOwner");
            }

            if (decision.OperationPath is not null && string.IsNullOrWhiteSpace(decision.OperationPath))
            {
                Error($"{at}.OperationPath", "OperationPath must be nonempty when present.");
            }
        }

        private void ValidateSpellCosts(
            CombatActionChoice action,
            Definition? spell,
            string at)
        {
            if (action.SpellCosts is null)
            {
                return;
            }

            if (spell is null)
            {
                Error($"{at}.SpellCosts", "Only a spell action choice may carry a committed spell cost quote.");
                return;
            }

            HashSet<string> required = [];
            if (spell.Json.TryGetProperty("cost", out JsonElement costDefinition)
                && costDefinition.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty entry in costDefinition.EnumerateObject())
                {
                    required.Add(entry.Name);
                }
            }

            foreach (string key in required)
            {
                if (!action.SpellCosts.ContainsKey(key))
                {
                    Error($"{at}.SpellCosts.{key}", $"The committed spell cost quote is missing authored cost entry '{key}'.");
                }
            }

            foreach (string key in action.SpellCosts.Keys)
            {
                if (!required.Contains(key))
                {
                    Error($"{at}.SpellCosts.{key}", $"The committed spell cost quote has no authored cost entry '{key}'.");
                }
            }
        }

        private void ValidateDecisionTargets(IReadOnlyList<CombatTargetChoice>? targets, IReadOnlySet<string> participantIds, string at)
        {
            if (targets is null)
            {
                Error(at, "Targets must be an array.");
                return;
            }

            for (int index = 0; index < targets.Count; index++)
            {
                CombatTargetChoice? target = targets[index];
                string targetAt = $"{at}[{index}]";
                if (target is null)
                {
                    Error(targetAt, "A target choice must be an object.");
                    continue;
                }

                RequiredParticipant(target.Id, $"{targetAt}.Id", participantIds);
                if (target.Side is not (0 or 1))
                {
                    Error($"{targetAt}.Side", "Target choice side must be 0 or 1.");
                }
            }
        }

        private void ValidateMoveChoices(IReadOnlyList<CombatMoveChoice>? moves, string at)
        {
            if (moves is null)
            {
                Error(at, "Moves must be an array.");
                return;
            }

            for (int index = 0; index < moves.Count; index++)
            {
                CombatMoveChoice? move = moves[index];
                string moveAt = $"{at}[{index}]";
                if (move is null)
                {
                    Error(moveAt, "A move choice must be an object.");
                    continue;
                }

                if (move.Path is null)
                {
                    Error($"{moveAt}.Path", "A move choice path must be an array.");
                }
            }
        }

        private void ValidateContinuationFrames(CombatContinuationState continuation, IReadOnlySet<string> participantIds, string root)
        {
            if (continuation.PendingInterrupt is CombatInterruptState interrupt)
            {
                ValidateInterrupt(interrupt, participantIds, $"{root}.PendingInterrupt");
            }

            ValidateCheck(continuation.PendingCheck, participantIds, $"{root}.PendingCheck");
            ValidateOperation(continuation.PendingOperation, participantIds, $"{root}.PendingOperation");
            if (continuation.OperationStack is null)
            {
                Error($"{root}.OperationStack", "OperationStack must be an array of operation states.");
            }
            else
            {
                for (int index = 0; index < continuation.OperationStack.Count; index++)
                {
                    ValidateOperation(continuation.OperationStack[index], participantIds, $"{root}.OperationStack[{index}]");
                }
            }

            if (continuation.PendingMovement is CombatMovementState movement)
            {
                ValidateKnownAction(movement.OwnerId, $"{root}.PendingMovement.OwnerId");
                RequiredParticipant(movement.ActorId, $"{root}.PendingMovement.ActorId", participantIds);
                RequiredParticipant(movement.TargetId, $"{root}.PendingMovement.TargetId", participantIds);
                if (movement.Steps < 0 || movement.EnemyIndex < 0 || movement.SelectedIndex < 0)
                {
                    Error($"{root}.PendingMovement", "Movement continuation indices cannot be negative.");
                }
            }

            if (continuation.ParentInterrupts is null)
            {
                Error($"{root}.ParentInterrupts", "ParentInterrupts must be an array of interrupt frames.");
            }
            else
            {
                for (int index = 0; index < continuation.ParentInterrupts.Count; index++)
                {
                    CombatInterruptFrameState? frame = continuation.ParentInterrupts[index];
                    string at = $"{root}.ParentInterrupts[{index}]";
                    if (frame is null)
                    {
                        Error(at, "A parent interrupt frame must be an object.");
                        continue;
                    }

                    if (frame.Interrupt is null)
                    {
                        Error($"{at}.Interrupt", "A parent interrupt frame must carry its interrupt state.");
                    }
                    else
                    {
                        ValidateInterrupt(frame.Interrupt, participantIds, $"{at}.Interrupt");
                    }

                    if (frame.Operation is null)
                    {
                        Error($"{at}.Operation", "A parent interrupt frame must carry its operation state.");
                    }
                    else
                    {
                        ValidateOperation(frame.Operation, participantIds, $"{at}.Operation");
                    }
                    ValidateOperation(frame.Action, participantIds, $"{at}.Action");
                    if (frame.Movement is CombatMovementState parentMovement)
                    {
                        ValidateKnownAction(parentMovement.OwnerId, $"{at}.Movement.OwnerId");
                        RequiredParticipant(parentMovement.ActorId, $"{at}.Movement.ActorId", participantIds);
                        RequiredParticipant(parentMovement.TargetId, $"{at}.Movement.TargetId", participantIds);
                    }

                    if (frame.Options is null)
                    {
                        Error($"{at}.Options", "Parent interrupt options must be an array.");
                    }
                    else
                    {
                        for (int optionIndex = 0; optionIndex < frame.Options.Count; optionIndex++)
                        {
                            CombatDecisionOption? option = frame.Options[optionIndex];
                            string optionAt = $"{at}.Options[{optionIndex}]";
                            if (option is null)
                            {
                                Error(optionAt, "A parent interrupt option must be an object.");
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(option.Id))
                            {
                                Error($"{optionAt}.Id", "Decision option IDs must not be empty.");
                            }

                            if (option.QualifiedId is string reactionId)
                            {
                                ResolveReference(reactionId, $"{optionAt}.QualifiedId", DefinitionTypes.Reaction);
                            }

                            KnownParticipant(option.TargetId, $"{optionAt}.TargetId", participantIds);
                            if (option.TrackId is string trackId)
                            {
                                ResolveReference(trackId, $"{optionAt}.TrackId", DefinitionTypes.Track);
                            }
                        }
                    }
                }
            }
        }

        private void ValidateInterrupt(CombatInterruptState? interrupt, IReadOnlySet<string> participantIds, string at)
        {
            if (interrupt is null)
            {
                return;
            }

            RequiredParticipant(interrupt.ReactorId, $"{at}.ReactorId", participantIds);
            RequiredParticipant(interrupt.SourceId, $"{at}.SourceId", participantIds);
            KnownParticipant(interrupt.TargetId, $"{at}.TargetId", participantIds);
            if (interrupt.ReactionId is string reactionId)
            {
                ResolveReference(reactionId, $"{at}.ReactionId", DefinitionTypes.Reaction);
            }

            if (interrupt.ActionId is string actionId)
            {
                ResolveReference(actionId, $"{at}.ActionId", DefinitionTypes.Action);
            }

            if (interrupt.TrackId is string trackId)
            {
                ResolveReference(trackId, $"{at}.TrackId", DefinitionTypes.Track);
            }

            if (interrupt.OperationOwner is string operationOwner)
            {
                ResolveAnyDefinition(operationOwner, $"{at}.OperationOwner");
            }

            if (interrupt.OperationPath is not null && string.IsNullOrWhiteSpace(interrupt.OperationPath))
            {
                Error($"{at}.OperationPath", "OperationPath must be nonempty when present.");
            }
        }

        private void ValidateCheck(CombatCheckState? check, IReadOnlySet<string> participantIds, string at)
        {
            if (check is null)
            {
                return;
            }

            ResolveReference(check.CheckId, $"{at}.CheckId", DefinitionTypes.Check);
            RequiredParticipant(check.ById, $"{at}.ById", participantIds);
            KnownParticipant(check.AgainstId, $"{at}.AgainstId", participantIds);
        }

        private void ValidateOperation(CombatOperationState? operation, IReadOnlySet<string> participantIds, string at)
        {
            if (operation is null)
            {
                return;
            }

            ResolveAnyDefinition(operation.OwnerId, $"{at}.OwnerId");
            RequiredParticipant(operation.ActorId, $"{at}.ActorId", participantIds);
            KnownParticipant(operation.TargetId, $"{at}.TargetId", participantIds);
            KnownParticipant(operation.SourceId, $"{at}.SourceId", participantIds);
            if (operation.ActionId is string actionId)
            {
                ResolveReference(actionId, $"{at}.ActionId", DefinitionTypes.Action);
            }

            if (operation.UseId is string useId && string.IsNullOrWhiteSpace(useId))
            {
                Error($"{at}.UseId", "UseId must be nonempty when present.");
            }

            if (operation.TargetIds is not null)
            {
                for (int index = 0; index < operation.TargetIds.Count; index++)
                {
                    KnownParticipant(operation.TargetIds[index], $"{at}.TargetIds[{index}]", participantIds);
                }
            }

            if (operation.Index < 0 || operation.TargetIndex < 0)
            {
                Error(at, "Operation indices cannot be negative.");
            }
        }

        private void ValidateKnownAction(string id, string at)
        {
            ResolveReference(id, at, DefinitionTypes.Action);
        }

        private Definition? ResolveReference(string? id, string at, DefinitionType type)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Error(at, $"Expected a nonempty {type.Name} ID.");
                return null;
            }

            Definition? definition = _rules.Find(type, id, out string? problem);
            if (definition is null)
            {
                Error(at, problem!);
            }

            return definition;
        }

        private void ResolveAnyDefinition(string id, string at)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Error(at, "Expected a nonempty definition ID.");
                return;
            }

            if (_rules.Definitions.All(definition => !string.Equals(definition.QualifiedId, id, StringComparison.Ordinal)))
            {
                Error(at, $"There is no definition '{id}' in the loaded module set.");
            }
        }

        private void KnownParticipant(string? id, string at, IReadOnlySet<string> participantIds)
        {
            if (id is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(id) || !participantIds.Contains(id))
            {
                Error(at, $"Combat continuation names unknown combatant '{id}'.");
            }
        }

        private void RequiredParticipant(string? id, string at, IReadOnlySet<string> participantIds)
        {
            if (id is null)
            {
                Error(at, "A combat continuation must name a combatant.");
                return;
            }

            KnownParticipant(id, at, participantIds);
        }

        private Definition? ResolveProperty(JsonElement parent, string name, string at, DefinitionType type)
        {
            return parent.TryGetProperty(name, out JsonElement value) ? Resolve(value, at, type) : Fail<Definition?>(at, $"Missing {name}: a {type.Name} ID.");
        }

        private string? ReadOptionalReference(JsonElement parent, string name, string at, DefinitionType type)
        {
            if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return Resolve(value, at, type)?.QualifiedId;
        }

        private string? ResolveText(JsonElement value, string at)
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : Fail<string?>(at, "Expected text.");
        }

        private decimal? ReadOptionalDecimal(JsonElement parent, string name, string at)
        {
            if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return value.TryGetDecimal(out decimal number) ? number : Fail<decimal?>(at, "Expected a decimal number or null.");
        }

        private Cell? ReadPosition(JsonElement parent, string at)
        {
            if (!parent.TryGetProperty("position", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("x", out JsonElement x)
                || !x.TryGetInt32(out int xValue)
                || !value.TryGetProperty("y", out JsonElement y)
                || !y.TryGetInt32(out int yValue))
            {
                return Fail<Cell?>(at, "position must be an object with integer x and y coordinates, or null.");
            }

            return new Cell(xValue, yValue);
        }

        private void ReadFoundSecrets(JsonElement root, CampaignState state)
        {
            ReadEdges(root, "found_secrets", Edge.Secret, state.FoundSecrets, "secret doors discovered by searching");
        }

        private void ReadOpenedDoors(JsonElement root, CampaignState state)
        {
            ReadEdges(root, "opened_doors", Edge.Door, state.OpenedDoors, "doors opened by the party");
        }

        private void ReadEdges(JsonElement root, string field, Edge expected, HashSet<string> destination, string description)
        {
            if (!root.TryGetProperty(field, out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
            {
                Error($"$.{field}", $"Missing \"{field}\": the {description}.");
                return;
            }

            int index = 0;
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                string at = $"$.{field}[{index}]";
                if (entry.ValueKind != JsonValueKind.String)
                {
                    Error(at, $"{field} entries must be text area edge keys such as \"tale:hall|1,0,west\".");
                    index++;
                    continue;
                }

                string key = entry.GetString()!;
                int separator = key.IndexOf('|');
                if (separator <= 0 || separator == key.Length - 1 || key.IndexOf('|', separator + 1) >= 0)
                {
                    Error(at, $"An {field} key must be '<area qualified ID>|<x>,<y>,<facing>'.");
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

                if (map.EdgeOf(edge.X, edge.Y, edge.Facing) != expected)
                {
                    string kind = expected == Edge.Secret ? "a secret door" : "a DD door";
                    Error(at, $"{edgeText} is not {kind} in {area.QualifiedId}.");
                    index++;
                    continue;
                }

                string canonical = $"{area.QualifiedId}|{edge.Key}";
                if (!destination.Add(canonical))
                {
                    Error(at, $"The {field} key is duplicated.");
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

        private long? OptionalLong(JsonElement root, string name, long defaultValue)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
            {
                return defaultValue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
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
