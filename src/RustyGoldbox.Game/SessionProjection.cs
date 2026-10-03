using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Game;

/// <summary>
/// The debug readout of a <see cref="GameSession"/>: what the DOM companion
/// shows and the choices it can claim intents for. Built as JSON, then
/// copied into the Engine's structured UI value.
/// </summary>
internal static class SessionProjection
{
    /// <param name="imageUrl">Where the panels can show an image asset, when it can be shown.</param>
    public static JsonObject Build(GameSession session, Func<Definition, string?>? imageUrl = null)
    {
        imageUrl ??= _ => null;
        JsonObject projection = new()
        {
            ["screen"] = session.Screen.ToString().ToLowerInvariant(),
            ["status"] = Status(session),
            ["notes"] = Strings(session.Notes),
            ["volumes"] = new JsonObject { ["music"] = session.MusicVolume, ["sound"] = session.SoundVolume },
            ["campaigns"] = new JsonArray(session.Campaigns.Select(campaign => (JsonNode)new JsonObject
            {
                ["bundle"] = campaign.Bundle,
                ["id"] = campaign.Id,
                ["title"] = campaign.Title,
                ["version"] = campaign.Version.ToString(),
                ["extensions"] = new JsonArray(campaign.Extensions.Select(extension => (JsonNode)new JsonObject
                {
                    ["id"] = extension.Id,
                    ["title"] = extension.Title,
                    ["version"] = extension.Version.ToString(),
                }).ToArray()),
            }).ToArray()),
        };

        if (session.Screen != Screen.Title)
        {
            projection["seed"] = session.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            projection["extensions"] = new JsonArray(session.Set!.Extensions.Select(id => (JsonNode)id).ToArray());
        }

        if (session.Screen == Screen.Party)
        {
            RuleSet rules = session.Set!.Rules!;
            JsonElement size = session.Campaign!.Json.GetProperty("party");
            projection["partySize"] = new JsonObject { ["min"] = size.GetProperty("min").GetInt32(), ["max"] = size.GetProperty("max").GetInt32() };
            projection["races"] = Choices(rules, DefinitionTypes.Race);
            projection["classes"] = Choices(rules, DefinitionTypes.Class);
            Creation(rules, projection);
            projection["items"] = Choices(rules, DefinitionTypes.Item);
            projection["portraits"] = new JsonArray(rules.OfType(DefinitionTypes.Asset)
                .Where(asset => asset.Json.TryGetProperty("tags", out JsonElement tags) && tags.EnumerateArray().Any(tag => tag.GetString() == "portrait"))
                .Select(asset => (JsonNode)new JsonObject { ["id"] = asset.QualifiedId, ["name"] = asset.Id, ["picture"] = Picture(rules, asset, imageUrl) }).ToArray());
            projection["party"] = new JsonArray(session.Party.Select(character => (JsonNode)Member(rules, character, imageUrl)).ToArray());
        }

        if (session.Screen == Screen.Combat)
        {
            FightReplay fight = session.Fight!;
            projection["fight"] = new JsonObject
            {
                ["encounter"] = fight.Fight.Encounter,
                ["track"] = fight.Fight.Track.Name,
                ["done"] = fight.Done,
                ["outcome"] = fight.Done ? fight.Fight.Describe() : null,
                ["members"] = new JsonArray(fight.Fight.Members.Select(member => (JsonNode)new JsonObject
                {
                    ["name"] = member.Name,
                    ["side"] = member.Side,
                    ["value"] = (double)fight.Values[member.Name],
                    ["max"] = member.Max is decimal max ? (double)max : null,
                    ["defeated"] = fight.Defeated.Contains(member.Name),
                    ["acting"] = fight.Acting.Who == member.Name,
                    ["icon"] = (member.Monster ?? member.Class) is Definition kind && session.Set!.Rules!.Icons.TryGetValue(kind, out Definition? icon) ? icon.QualifiedId : null,
                    ["iconPicture"] = (member.Monster ?? member.Class) is Definition shown && session.Set!.Rules!.Icons.TryGetValue(shown, out Definition? picture) ? Picture(session.Set.Rules, picture, imageUrl) : null,
                }).ToArray()),
                ["log"] = Strings(fight.Lines.TakeLast(14)),
            };
        }

        if (session.Screen == Screen.Play)
        {
            CampaignRunner runner = session.Runner!;
            CampaignState state = runner.State;
            projection["position"] = new JsonObject
            {
                ["area"] = state.Area.QualifiedId,
                ["name"] = state.Area.Name,
                ["x"] = state.X,
                ["y"] = state.Y,
                ["facing"] = Facings.Name(state.Facing),
            };
            projection["map"] = Map(state);
            projection["menu"] = new JsonArray(runner.MenuOptions().Select(option => (JsonNode)new JsonObject
            {
                ["number"] = option.Number,
                ["label"] = option.Label,
            }).ToArray());
            projection["commands"] = CampaignRunner.CommandList;
            projection["party"] = new JsonArray(state.Party.Select(character => (JsonNode)Member(session.Set!.Rules!, character, imageUrl)).ToArray());
            projection["ended"] = state.Ended;
            projection["log"] = Strings(session.Log);
            projection["picture"] = state.Picture is Definition shown ? Picture(session.Set!.Rules!, shown, imageUrl) : null;
            projection["music"] = state.Music?.QualifiedId;
        }

        return projection;
    }

    /// <summary>Copies a JSON value into the Engine's node, edge and UTF-8 arrays.</summary>
    public static UiValue ToUiValue(JsonNode root)
    {
        List<StructuredValueNode> nodes = [];
        List<uint> edges = [];
        List<byte> text = [];
        uint index = Add(root, null, nodes, edges, text);
        return new UiValue(nodes.ToArray(), edges.ToArray(), index, text.ToArray());
    }

    private static uint Add(JsonNode? node, string? key, List<StructuredValueNode> nodes, List<uint> edges, List<byte> text)
    {
        (uint keyOffset, uint keyLength) = key is null ? (0u, 0u) : Text(key, text);
        StructuredValueNode value;
        switch (node)
        {
            case null:
                value = new(StructuredValueKind.Null, 0, 0, keyOffset, keyLength, 0, 0, 0, 0);
                break;
            case JsonObject members:
                List<uint> fields = members.Select(member => Add(member.Value, member.Key, nodes, edges, text)).ToList();
                value = new(StructuredValueKind.Object, 0, 0, keyOffset, keyLength, 0, 0, (uint)edges.Count, (uint)fields.Count);
                edges.AddRange(fields);
                break;
            case JsonArray elements:
                List<uint> items = elements.Select(element => Add(element, null, nodes, edges, text)).ToList();
                value = new(StructuredValueKind.Array, 0, 0, keyOffset, keyLength, 0, 0, (uint)edges.Count, (uint)items.Count);
                edges.AddRange(items);
                break;
            default:
                value = node.GetValueKind() switch
                {
                    JsonValueKind.String => StringNode(node.GetValue<string>(), keyOffset, keyLength, text),
                    JsonValueKind.Number => new(StructuredValueKind.Number, 0, double.Parse(node.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture), keyOffset, keyLength, 0, 0, 0, 0),
                    JsonValueKind.True => new(StructuredValueKind.Bool, 1, 0, keyOffset, keyLength, 0, 0, 0, 0),
                    JsonValueKind.False => new(StructuredValueKind.Bool, 0, 0, keyOffset, keyLength, 0, 0, 0, 0),
                    _ => new(StructuredValueKind.Null, 0, 0, keyOffset, keyLength, 0, 0, 0, 0),
                };
                break;
        }

        nodes.Add(value);
        return (uint)nodes.Count - 1;
    }

    private static StructuredValueNode StringNode(string value, uint keyOffset, uint keyLength, List<byte> text)
    {
        (uint offset, uint length) = Text(value, text);
        return new(StructuredValueKind.String, 0, 0, keyOffset, keyLength, offset, length, 0, 0);
    }

    private static (uint Offset, uint Length) Text(string value, List<byte> text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        uint offset = (uint)text.Count;
        text.AddRange(bytes);
        return (offset, (uint)bytes.Length);
    }

    private static string Status(GameSession session)
    {
        return session.Screen switch
        {
            Screen.Title => $"{session.Campaigns.Count} campaign(s) available",
            Screen.Party => $"{session.Campaign!.Name}: making a party ({session.Party.Count})",
            Screen.Combat => session.Fight!.Done
                ? $"{session.Campaign!.Name}: {session.Fight.Fight.Describe()}"
                : $"{session.Campaign!.Name}: fighting {session.Fight.Fight.Encounter}",
            _ => session.Runner!.State.Ended
                ? $"{session.Campaign!.Name}: the adventure is over"
                : $"{session.Campaign!.Name}: {session.Runner.State.Area.Name} [{session.Runner.State.X}, {session.Runner.State.Y}] facing {Facings.Name(session.Runner.State.Facing)}",
        };
    }

    private static JsonArray Strings(IEnumerable<string> lines) => new(lines.Select(line => (JsonNode)line).ToArray());

    private static JsonArray Choices(RuleSet rules, DefinitionType type)
    {
        return new JsonArray(rules.OfType(type).Select(definition => (JsonNode)new JsonObject
        {
            ["id"] = definition.QualifiedId,
            ["name"] = definition.Name,
            ["boosts"] = Boosts(definition),
        }).ToArray());
    }

    /// <summary>
    /// What the party screen needs to ask for at a roll: the default creation's
    /// method, attributes, grants and boosts, each class's first-level grants,
    /// and every feature with its kind and boosts. Core checks the roll itself.
    /// </summary>
    private static void Creation(RuleSet rules, JsonObject projection)
    {
        if (CharacterRules.DefaultCreation(rules) is not Definition creation)
        {
            return;
        }

        projection["creation"] = new JsonObject
        {
            ["method"] = creation.Json.TryGetProperty("method", out JsonElement method) ? method.GetString() : "roll",
            ["attributes"] = new JsonArray(creation.Json.GetProperty("attributes").EnumerateArray().Select(attribute => (JsonNode)JsonValue.Create(attribute.GetString())!).ToArray()),
            ["grants"] = Grants(CharacterRules.CreationChoices(creation)),
            ["boosts"] = Boosts(creation),
        };
        foreach (JsonNode? entry in projection["classes"]!.AsArray())
        {
            Definition characterClass = rules.Find(DefinitionTypes.Class, entry!["id"]!.GetValue<string>(), out _)!;
            entry["grants"] = Grants(CharacterRules.FirstLevelChoices(rules, characterClass));
        }

        projection["features"] = new JsonArray(rules.OfType(DefinitionTypes.Feature).Select(feature => (JsonNode)new JsonObject
        {
            ["id"] = feature.QualifiedId,
            ["name"] = feature.Name,
            ["kind"] = feature.Json.GetProperty("kind").GetString(),
            ["boosts"] = Boosts(feature),
        }).ToArray());
    }

    private static JsonArray Grants(List<Grant> grants)
    {
        return new JsonArray(grants.Select(grant => (JsonNode)new JsonObject
        {
            ["kinds"] = new JsonArray(grant.Kinds.Select(kind => (JsonNode)JsonValue.Create(kind)!).ToArray()),
            ["count"] = grant.Count,
        }).ToArray());
    }

    /// <summary>A definition's boosts as lists of the attributes each may raise; an empty list is any attribute.</summary>
    private static JsonArray Boosts(Definition definition)
    {
        if (!definition.Json.TryGetProperty("boosts", out JsonElement boosts))
        {
            return [];
        }

        return new JsonArray(boosts.EnumerateArray().Select(boost => (JsonNode)new JsonArray(
            boost.TryGetProperty("from", out JsonElement from)
                ? from.EnumerateArray().Select(attribute => (JsonNode)JsonValue.Create(attribute.GetString())!).ToArray()
                : [])).ToArray());
    }

    /// <summary>
    /// How the panels show an asset in a picture slot, whatever its media: the
    /// image URL, the image's pixel size, and for a sheet the size of a frame
    /// and the first animation it plays. Null when the image can't be shown.
    /// </summary>
    private static JsonObject? Picture(RuleSet rules, Definition asset, Func<Definition, string?> imageUrl)
    {
        if (imageUrl(asset) is not string url)
        {
            return null;
        }

        (int width, int height) = rules.ImageSizes[asset];
        JsonArray? frame = Media.MediaOf(asset) == "sheet" && asset.Json.GetProperty("frame_size") is JsonElement size
            ? new JsonArray(size[0].GetInt32(), size[1].GetInt32())
            : null;
        JsonObject? animation = null;
        if (frame is not null && asset.Json.TryGetProperty("animations", out JsonElement animations) && animations.EnumerateObject().FirstOrDefault() is { Value.ValueKind: JsonValueKind.Object } first)
        {
            animation = new JsonObject
            {
                ["frames"] = new JsonArray(first.Value.GetProperty("frames").EnumerateArray().Select(played => (JsonNode)played.GetInt32()).ToArray()),
                ["fps"] = first.Value.GetProperty("fps").GetDouble(),
                ["loop"] = !first.Value.TryGetProperty("loop", out JsonElement repeat) || repeat.GetBoolean(),
            };
        }

        return new JsonObject { ["url"] = url, ["width"] = width, ["height"] = height, ["frame"] = frame, ["animation"] = animation };
    }

    private static JsonArray Spells(IEnumerable<Definition> spells)
    {
        return new JsonArray(spells.Select(spell => (JsonNode)new JsonObject { ["id"] = spell.QualifiedId, ["name"] = spell.Name }).ToArray());
    }

    private static JsonObject Member(RuleSet rules, Character character, Func<Definition, string?> imageUrl)
    {
        return new JsonObject
        {
            ["name"] = character.Name,
            ["race"] = character.Race?.Name,
            ["class"] = character.ClassLevels().Count > 1 ? character.ClassText : character.Class?.Name,
            ["level"] = character.Level,
            ["tracks"] = Strings(CharacterSheet.Tracks(rules, character).Select(track => $"{track.Track.Name} {Number(track.Current)}/{Number(track.Max)}")),
            ["attributes"] = Strings(character.Attributes.Select(attribute => $"{attribute.Key} {Number(attribute.Value)}")),
            ["features"] = Strings(character.Features.Select(feature => feature.Name)),
            ["equipment"] = new JsonArray(character.Equipment.Select(item => (JsonNode)new JsonObject { ["id"] = item.QualifiedId, ["name"] = item.Name }).ToArray()),
            ["spells"] = new JsonArray(character.Spells.Select(spell => (JsonNode)new JsonObject { ["id"] = spell.QualifiedId, ["name"] = spell.Name }).ToArray()),
            ["memorisable"] = Spells(character.Spells.Where(spell => CharacterRules.NeedsPreparing(rules, character, spell))),
            ["memorised"] = Spells(CharacterRules.MemorisedPlan(rules, character)),
            ["memorisedChosen"] = character.Memorised.Count > 0,
            ["prepared"] = Spells(CharacterRules.PreparedLeft(rules, character)),
            ["castable"] = new JsonArray(CharacterRules.CastableSpells(rules, character).Select(spell => (JsonNode)new JsonObject { ["id"] = spell.QualifiedId, ["name"] = spell.Name }).ToArray()),
            ["gold"] = (double)character.Gold,
            ["experience"] = (double)character.Experience,
            ["levelReady"] = CharacterRules.ReadyToLevel(rules, character),
            ["formerClasses"] = !character.HasDormantClasses() ? null : character.UsesFormerClasses ? "called" : "waiting",
            ["portrait"] = character.Portrait?.QualifiedId,
            ["portraitPicture"] = character.Portrait is Definition portrait ? Picture(rules, portrait, imageUrl) : null,
        };
    }

    /// <summary>The area as the CLI's player view draws it, with the party as an arrow.</summary>
    private static string Map(CampaignState state)
    {
        AreaMap map = AreaMap.Parse(state.Area.Json.GetProperty("map").EnumerateArray().Select(row => row.GetString()!).ToList(), [])!;
        string arrow = state.Facing switch
        {
            Facing.North => "^^",
            Facing.East => ">>",
            Facing.South => "vv",
            _ => "<<",
        };
        return map.Render(new Dictionary<(int X, int Y), string> { [(state.X, state.Y)] = arrow }, playerView: true);
    }

    private static string Number(decimal? value) => value?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "-";
}
