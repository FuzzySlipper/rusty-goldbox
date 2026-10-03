using System.Text;
using System.Text.Json;
using Rusty.Engine;
using RustyGoldbox.Core.Campaigns;

namespace RustyGoldbox.Game;

/// <summary>
/// Turns Engine input into <see cref="GameSession"/> commands. Input is the
/// declared <c>goldbox.command</c> intent, whose <c>goldbox.command.v1</c>
/// payload the DOM claims (an object with an <c>action</c> and its fields),
/// plus key-mapped digital intents for moving and choosing. Payloads come
/// from the page, so every field is checked here, once.
/// </summary>
internal static class GameCommands
{
    public const string CommandIntent = "goldbox.command";
    public const string CommandContract = "goldbox.command.v1";
    public const string DefaultSlot = "slot-1";

    /// <summary>Key-mapped intents and the play command each one runs.</summary>
    public static IReadOnlyDictionary<string, string> KeyCommands { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["party.forward"] = "forward",
        ["party.back"] = "back",
        ["party.left"] = "left",
        ["party.right"] = "right",
        ["party.around"] = "around",
        ["party.look"] = "look",
        ["menu.choose-1"] = "choose 1",
        ["menu.choose-2"] = "choose 2",
        ["menu.choose-3"] = "choose 3",
        ["menu.choose-4"] = "choose 4",
        ["menu.choose-5"] = "choose 5",
        ["menu.choose-6"] = "choose 6",
        ["menu.choose-7"] = "choose 7",
        ["menu.choose-8"] = "choose 8",
        ["menu.choose-9"] = "choose 9",
    };

    /// <summary>The key intent that continues past a fight on the combat screen.</summary>
    public const string ContinueIntent = "combat.continue";

    /// <summary>Applies one input event; returns whether the session may have changed.</summary>
    public static bool Apply(GameSession session, IEngineContext engine, in ProductInputEvent input)
    {
        string intent = Encoding.UTF8.GetString(input.Intent.Span);
        if (input.ValueKind == InputValueKind.Digital && intent == ContinueIntent)
        {
            if (IsPress(input) && session.Screen == Screen.Combat)
            {
                session.Continue();
                return true;
            }

            return false;
        }

        if (input.ValueKind == InputValueKind.Digital && KeyCommands.TryGetValue(intent, out string? command))
        {
            if (IsPress(input) && session.Screen == Screen.Play)
            {
                session.Execute(engine, command);
                return true;
            }

            return false;
        }

        if (input.ValueKind != InputValueKind.ProductPayload
            || intent != CommandIntent
            || Encoding.UTF8.GetString(input.PayloadContract.Span) != CommandContract)
        {
            return false;
        }

        try
        {
            using JsonDocument payload = JsonDocument.Parse(input.PayloadData);
            Run(session, engine, payload.RootElement);
        }
        catch (JsonException exception)
        {
            Refuse(session, $"Ignored a {CommandContract} payload that isn't JSON: {exception.Message}");
        }

        return true;
    }

    /// <summary>
    /// A key press, or a UI claim of the intent as active. A physical mapping
    /// fires on its pressed edge; a direct claim carries its value in X.
    /// </summary>
    public static bool IsPress(in ProductInputEvent input)
    {
        return input.Provenance == InputProvenance.DirectUi ? input.X > 0 : input.Edge == InputEdge.Pressed;
    }

    /// <summary>Runs one <c>goldbox.command.v1</c> payload: <c>{ "action": ..., fields }</c>.</summary>
    public static void Run(GameSession session, IEngineContext engine, JsonElement payload)
    {
        try
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                throw new PayloadException("the payload must be an object with an \"action\"");
            }

            string action = Text(payload, "action");
            switch (action)
            {
                case "refresh":
                    session.Refresh();
                    break;
                case "open":
                    // A new campaign gets one seed; everything after replays from it.
                    ulong seed = payload.TryGetProperty("seed", out _) ? Seed(payload) : (ulong)DateTime.UtcNow.Ticks;
                    IReadOnlyList<string>? extensions = payload.TryGetProperty("extensions", out _) ? Texts(payload, "extensions") : null;
                    session.Open(Text(payload, "campaign"), seed, extensions);
                    break;
                case "roll":
                    string? portrait = payload.TryGetProperty("portrait", out _) ? Text(payload, "portrait") : null;
                    IReadOnlyList<string>? features = payload.TryGetProperty("features", out _) ? Texts(payload, "features") : null;
                    IReadOnlyList<string>? boosts = payload.TryGetProperty("boosts", out _) ? Texts(payload, "boosts") : null;
                    string? race = payload.TryGetProperty("race", out _) ? Text(payload, "race") : null;
                    string? characterClass = payload.TryGetProperty("class", out _) ? Text(payload, "class") : null;
                    session.Roll(engine, Text(payload, "name").Trim(), race, characterClass, portrait, features, boosts);
                    break;
                case "drop":
                    session.Drop(Integer(payload, "member"));
                    break;
                case "equip":
                    session.Equip(Integer(payload, "member"), Text(payload, "item"));
                    break;
                case "spells":
                    session.SetSpells(Integer(payload, "member"), Texts(payload, "spells"));
                    break;
                case "memorise":
                    session.SetMemorised(Integer(payload, "member"), Texts(payload, "spells"));
                    break;
                case "begin":
                    session.Begin(engine);
                    break;
                case "play":
                    session.Execute(engine, Text(payload, "command"));
                    break;
                case "save":
                    session.Save(engine, Slot(payload));
                    break;
                case "load":
                    session.Load(engine, Slot(payload));
                    break;
                case "continue":
                    session.Continue();
                    break;
                case "quit":
                    session.Quit();
                    session.Refresh();
                    break;
                case "volume":
                    session.SetVolume(Text(payload, "bus"), Volume(payload));
                    break;
                default:
                    throw new PayloadException($"'{action}' is not an action; actions are refresh, open, roll, drop, equip, spells, memorise, begin, play, continue, save, load, quit and volume");
            }
        }
        catch (PayloadException exception)
        {
            Refuse(session, $"Ignored a {CommandContract} payload: {exception.Message}.");
        }
    }

    private static float Volume(JsonElement payload)
    {
        return payload.TryGetProperty("volume", out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.GetDouble() is >= 0 and <= 1
            ? (float)value.GetDouble()
            : throw new PayloadException("\"volume\" must be a number from 0 to 1");
    }

    private static string Slot(JsonElement payload)
    {
        string slot = payload.TryGetProperty("slot", out _) ? Text(payload, "slot") : DefaultSlot;
        return SaveSlots.IsValidName(slot)
            ? slot
            : throw new PayloadException($"'{slot}' isn't a save slot name. Use {SaveSlots.NameDescription}");
    }

    private static string Text(JsonElement payload, string field)
    {
        string? text = payload.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return string.IsNullOrWhiteSpace(text)
            ? throw new PayloadException($"\"{field}\" must be non-empty text")
            : text;
    }

    private static List<string> Texts(JsonElement payload, string field)
    {
        JsonElement value = payload.GetProperty(field);
        return value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(entry => entry.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.GetString()))
            ? value.EnumerateArray().Select(entry => entry.GetString()!).ToList()
            : throw new PayloadException($"\"{field}\" must be an array of non-empty text");
    }

    private static int Integer(JsonElement payload, string field)
    {
        return payload.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
            ? number
            : throw new PayloadException($"\"{field}\" must be a whole number");
    }

    /// <summary>A seed is decimal text, since JSON numbers lose precision past 2^53.</summary>
    private static ulong Seed(JsonElement payload)
    {
        return ulong.TryParse(Text(payload, "seed"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong seed)
            ? seed
            : throw new PayloadException($"\"seed\" must be a whole number from 0 to {ulong.MaxValue}, written as text");
    }

    private static void Refuse(GameSession session, string note)
    {
        session.Notes.Clear();
        session.Notes.Add(note);
    }

    private sealed class PayloadException(string message) : Exception(message);
}
