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
    public static JsonObject Build(GameSession session)
    {
        JsonObject projection = new()
        {
            ["screen"] = session.Screen.ToString().ToLowerInvariant(),
            ["status"] = Status(session),
            ["notes"] = Strings(session.Notes),
            ["campaigns"] = new JsonArray(session.Campaigns.Select(campaign => (JsonNode)new JsonObject
            {
                ["bundle"] = campaign.Bundle,
                ["id"] = campaign.Id,
                ["title"] = campaign.Title,
                ["version"] = campaign.Version.ToString(),
            }).ToArray()),
        };

        if (session.Screen != Screen.Title)
        {
            projection["seed"] = session.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (session.Screen == Screen.Party)
        {
            RuleSet rules = session.Set!.Rules!;
            JsonElement size = session.Campaign!.Json.GetProperty("party");
            projection["partySize"] = new JsonObject { ["min"] = size.GetProperty("min").GetInt32(), ["max"] = size.GetProperty("max").GetInt32() };
            projection["races"] = Choices(rules, DefinitionTypes.Race);
            projection["classes"] = Choices(rules, DefinitionTypes.Class);
            projection["items"] = Choices(rules, DefinitionTypes.Item);
            projection["party"] = new JsonArray(session.Party.Select(character => (JsonNode)Member(rules, character)).ToArray());
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
            projection["party"] = new JsonArray(state.Party.Select(character => (JsonNode)Member(session.Set!.Rules!, character)).ToArray());
            projection["ended"] = state.Ended;
            projection["log"] = Strings(session.Log);
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
        }).ToArray());
    }

    private static JsonObject Member(RuleSet rules, Character character)
    {
        return new JsonObject
        {
            ["name"] = character.Name,
            ["race"] = character.Race.Name,
            ["class"] = character.Class.Name,
            ["level"] = character.Level,
            ["tracks"] = Strings(CharacterSheet.Tracks(rules, character).Select(track => $"{track.Track.Name} {Number(track.Current)}/{Number(track.Max)}")),
            ["attributes"] = Strings(character.Attributes.Select(attribute => $"{attribute.Key} {Number(attribute.Value)}")),
            ["equipment"] = new JsonArray(character.Equipment.Select(item => (JsonNode)new JsonObject { ["id"] = item.QualifiedId, ["name"] = item.Name }).ToArray()),
            ["gold"] = (double)character.Gold,
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
