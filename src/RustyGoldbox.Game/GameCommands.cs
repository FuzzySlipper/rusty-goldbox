using System.Text;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Rules;

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

    /// <summary>Actions that only change this player's own settings or library, which a guest still does itself.</summary>
    private static readonly HashSet<string> LocalActions =
        ["volume", "skin", "layout-config", "ui-scale", "module-preview", "module-install", "module-updates", "module-remove", "module-cancel"];

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
            if (session.Guest && !LocalActions.Contains(action))
            {
                // A guest's game choices are the host's to apply.
                session.Outbox.Add(payload.Clone());
                return;
            }

            if (session.Table is PartyTable table && !LocalActions.Contains(action))
            {
                // The host plays at its own table by the same rules as its guests.
                Apply(session, engine, table, PartyTable.HostMember, payload, ulong.MaxValue);
                return;
            }

            Dispatch(session, engine, payload, action);
        }
        catch (PayloadException exception)
        {
            Refuse(session, $"Ignored a {CommandContract} payload: {exception.Message}.");
        }
    }

    /// <summary>
    /// Runs one player's action at a hosted table: refused with a reason
    /// when their seat may not send it now, else applied through the
    /// ordinary commands, keeping which seat made which character.
    /// </summary>
    /// <param name="seen">The last host sequence that player had received.</param>
    /// <returns>Why it was refused, or null when it was applied.</returns>
    public static string? RunFor(GameSession session, IEngineContext engine, uint member, JsonElement payload, ulong seen)
    {
        try
        {
            if (payload.ValueKind != JsonValueKind.Object || session.Table is not PartyTable table)
            {
                throw new PayloadException("the payload must be an object with an \"action\"");
            }

            return Apply(session, engine, table, member, payload, seen);
        }
        catch (PayloadException exception)
        {
            return $"Ignored a {CommandContract} payload: {exception.Message}.";
        }
    }

    private static string? Apply(GameSession session, IEngineContext engine, PartyTable table, uint member, JsonElement payload, ulong seen)
    {
        string action = Text(payload, "action");
        if (table.Allows(session, member, payload, seen) is string refusal)
        {
            session.Notes.Clear();
            session.Notes.Add(member == PartyTable.HostMember ? refusal : $"{table.SeatOf(member)?.Name ?? "A player"}: {refusal}");
            return refusal;
        }

        if (action == "pass-lead")
        {
            return table.PassLead((uint)Integer(payload, "to"));
        }

        // At an event menu a choice is a vote; the party's choice runs once it is settled.
        bool calling = action == "decide";
        if (calling || (action == "play" && session.Runner?.State.PendingMenu is not null && Choice(Text(payload, "command")) is int))
        {
            if (!calling && table.Vote(session, member, Choice(Text(payload, "command"))!.Value) is string refused)
            {
                session.Notes.Clear();
                session.Notes.Add(refused);
                return refused;
            }

            if (table.Settled(session, calling) is not int choice)
            {
                return calling ? (table.Votes.Count == 0 ? "No one has voted yet." : "The vote is tied: vote to break it, then decide.") : null;
            }

            session.Log.Add(table.Tally(choice));
            table.VoteClosed();
            using JsonDocument chosen = JsonDocument.Parse(JsonSerializer.Serialize(new { action = "play", command = $"choose {choice}" }));
            Dispatch(session, engine, chosen.RootElement, "play");
            return null;
        }

        int partyBefore = session.Party.Count;
        Dispatch(session, engine, payload, action);
        if (action == "roll" && session.Party.Count > partyBefore)
        {
            table.Claim(member, session.Party.Count - 1);
        }
        else if (action == "drop" && session.Party.Count < partyBefore)
        {
            table.Dropped(Integer(payload, "member"));
        }

        return null;
    }

    /// <summary>The option of a <c>choose N</c> command, or null for any other command.</summary>
    private static int? Choice(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words is ["choose", string number] && int.TryParse(number, out int option) ? option : null;
    }

    private static void Dispatch(GameSession session, IEngineContext engine, JsonElement payload, string action)
    {
        {
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
                    string? lifepath = payload.TryGetProperty("lifepath", out _) ? Text(payload, "lifepath") : null;
                    IReadOnlyList<string>? careers = payload.TryGetProperty("careers", out _) ? Texts(payload, "careers") : null;
                    IReadOnlyList<string>? skillTables = payload.TryGetProperty("skillTables", out _) ? Texts(payload, "skillTables") : null;
                    IReadOnlyList<string>? benefits = payload.TryGetProperty("benefits", out _) ? Texts(payload, "benefits") : null;
                    IReadOnlyList<SkillAllocation>? skillPoints = payload.TryGetProperty("skills", out _) ? Skills(payload) : null;
                    IReadOnlyDictionary<string, decimal>? attributes = payload.TryGetProperty("attributes", out _) ? Attributes(payload) : null;
                    IReadOnlyList<string>? priority = payload.TryGetProperty("priority", out _) ? Texts(payload, "priority") : null;
                    string? creation = payload.TryGetProperty("creation", out _) ? Text(payload, "creation") : null;
                    string? race = payload.TryGetProperty("race", out _) ? Text(payload, "race") : null;
                    string? characterClass = payload.TryGetProperty("class", out _) ? Text(payload, "class") : null;
                    int terms = payload.TryGetProperty("terms", out _) ? Integer(payload, "terms") : 0;
                    session.Roll(engine, Text(payload, "name").Trim(), race, characterClass, portrait, features, boosts, skillPoints, creation, lifepath, careers, skillTables, benefits, terms, attributes, priority);
                    break;
                case "drop":
                    session.Drop(Integer(payload, "member"));
                    break;
                case "skills":
                    session.SpendSkillPoints(engine, Integer(payload, "member"), Skills(payload));
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
                case "combat-control":
                    session.SetCombatController(engine, Text(payload, "actor"), CombatMode(payload));
                    break;
                case "combat-action":
                    session.CombatAction(engine, Text(payload, "actor"), CombatActionId(payload), CombatTargets(payload), CombatPath(payload));
                    break;
                case "combat-end-turn":
                    session.CombatEndTurn(engine, Text(payload, "actor"));
                    break;
                case "combat-decide":
                    string? option = payload.TryGetProperty("option", out JsonElement optionValue) && optionValue.ValueKind != JsonValueKind.Null
                        ? Text(payload, "option") : null;
                    session.CombatDecide(engine, Text(payload, "decision"), option);
                    break;
                case "quit":
                    session.Quit();
                    session.Refresh();
                    break;
                case "volume":
                    session.SetVolume(Text(payload, "bus"), Volume(payload));
                    SaveSettings(session, engine);
                    break;
                case "skin":
                    session.PickSkin(payload.TryGetProperty("skin", out JsonElement skin) && skin.ValueKind != JsonValueKind.Null ? Text(payload, "skin") : null);
                    SaveSettings(session, engine);
                    break;
                case "layout-config":
                    session.SetLayout(LayoutConfig(payload));
                    SaveSettings(session, engine);
                    break;
                case "ui-scale":
                    session.SetUiScale(Number(payload, "scale"));
                    SaveSettings(session, engine);
                    break;
                case "module-preview":
                    session.Installer.PreviewFrom(Text(payload, "source"));
                    break;
                case "module-install":
                    session.Installer.Install(
                        Text(payload, "source"),
                        payload.TryGetProperty("id", out _) ? Text(payload, "id") : null,
                        payload.TryGetProperty("version", out _) ? Text(payload, "version") : null);
                    break;
                case "module-updates":
                    session.Installer.CheckUpdates();
                    break;
                case "module-remove":
                    session.Installer.Remove(engine, Text(payload, "id"), Text(payload, "version"));
                    break;
                case "module-cancel":
                    session.Installer.Cancel(engine);
                    break;
                default:
                    throw new PayloadException($"'{action}' is not an action; actions are refresh, open, roll, skills, drop, equip, spells, memorise, begin, play, continue, combat-control, combat-action, combat-end-turn, combat-decide, save, load, quit, volume, skin, layout-config, ui-scale, module-preview, module-install, module-updates, module-remove, module-cancel");
            }
        }
    }

    private static float Volume(JsonElement payload)
    {
        return payload.TryGetProperty("volume", out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.GetDouble() is >= 0 and <= 1
            ? (float)value.GetDouble()
            : throw new PayloadException("\"volume\" must be a number from 0 to 1");
    }

    private static CombatControlMode CombatMode(JsonElement payload)
    {
        return Text(payload, "mode").ToLowerInvariant() switch
        {
            "auto" or "automatic" => CombatControlMode.Automatic,
            "manual" => CombatControlMode.Manual,
            string mode => throw new PayloadException($"\"mode\" must be auto or manual, not '{mode}'"),
        };
    }

    private static string CombatActionId(JsonElement payload)
    {
        if (payload.TryGetProperty("choice", out JsonElement choice) && choice.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(choice.GetString()))
        {
            return choice.GetString()!;
        }

        throw new PayloadException("\"choice\" must be non-empty text");
    }

    private static IReadOnlyList<string> CombatTargets(JsonElement payload)
    {
        if (!payload.TryGetProperty("targets", out JsonElement targets) || targets.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        return targets.ValueKind == JsonValueKind.Array
            && targets.EnumerateArray().All(target => target.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(target.GetString()))
            ? targets.EnumerateArray().Select(target => target.GetString()!).ToList()
            : throw new PayloadException("\"targets\" must be an array of non-empty actor IDs");
    }

    private static IReadOnlyList<Cell>? CombatPath(JsonElement payload)
    {
        if (!payload.TryGetProperty("path", out JsonElement path) || path.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (path.ValueKind != JsonValueKind.Array)
        {
            throw new PayloadException("\"path\" must be an array of {x,y} combat cells");
        }

        List<Cell> cells = [];
        foreach (JsonElement entry in path.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("x", out JsonElement x)
                || !entry.TryGetProperty("y", out JsonElement y)
                || !x.TryGetInt32(out int cellX)
                || !y.TryGetInt32(out int cellY))
            {
                throw new PayloadException("Each \"path\" entry must have whole-number x and y fields");
            }

            cells.Add(new Cell(cellX, cellY));
        }

        return cells;
    }

    /// <summary>The player's layout: <c>"layout": { part: number, ... }</c>, or null to go back to the skin's.</summary>
    private static Dictionary<string, double>? LayoutConfig(JsonElement payload)
    {
        if (!payload.TryGetProperty("layout", out JsonElement layout) || layout.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (layout.ValueKind != JsonValueKind.Object || layout.EnumerateObject().Any(part => part.Value.ValueKind != JsonValueKind.Number))
        {
            throw new PayloadException("\"layout\" must be null or an object of numbers by layout part");
        }

        return layout.EnumerateObject().ToDictionary(part => part.Name, part => part.Value.GetDouble());
    }

    /// <summary>
    /// Keeps the player's settings for the next run; a failure is a note, and
    /// the change still holds for this one. A host without a persistence root
    /// (a tool or test host) has nowhere to keep them, which is no failure.
    /// </summary>
    private static void SaveSettings(GameSession session, IEngineContext engine)
    {
        try
        {
            PlayerSettings.Save(engine, session);
        }
        catch (EngineCallException)
        {
        }
        catch (PersistenceStorageException exception)
        {
            session.Notes.Add($"Can't keep the settings for next time: {exception.Message}");
        }
    }

    private static double Number(JsonElement payload, string field)
    {
        return payload.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : throw new PayloadException($"\"{field}\" must be a number");
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

    private static Dictionary<string, decimal> Attributes(JsonElement payload)
    {
        if (!payload.TryGetProperty("attributes", out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new PayloadException("\"attributes\" must be an object of numeric attribute scores");
        }

        Dictionary<string, decimal> scores = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in value.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                throw new PayloadException("Each \"attributes\" entry must have a non-empty attribute ID");
            }

            if (entry.Value.ValueKind != JsonValueKind.Number || !entry.Value.TryGetDecimal(out decimal score))
            {
                throw new PayloadException("Each \"attributes\" entry must have a numeric score");
            }

            if (!scores.TryAdd(entry.Name, score))
            {
                throw new PayloadException($"\"attributes\" contains the attribute ID '{entry.Name}' more than once");
            }
        }

        return scores;
    }

    private static List<SkillAllocation> Skills(JsonElement payload)
    {
        if (!payload.TryGetProperty("skills", out JsonElement value))
        {
            throw new PayloadException("\"skills\" must be an array of objects with skill, profession and personal numbers");
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new PayloadException("\"skills\" must be an array of objects with skill, profession and personal numbers");
        }

        List<SkillAllocation> allocations = [];
        foreach (JsonElement entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("skill", out JsonElement skill)
                || skill.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(skill.GetString()))
            {
                throw new PayloadException("Each \"skills\" entry must have a non-empty text \"skill\".");
            }

            decimal profession = Amount(entry, "profession");
            decimal personal = Amount(entry, "personal");
            if (profession < 0 || personal < 0)
            {
                throw new PayloadException("Skill point allocations must be nonnegative numbers.");
            }

            allocations.Add(new SkillAllocation(skill.GetString()!, profession, personal));
        }

        return allocations;
    }

    private static decimal Amount(JsonElement value, string field)
    {
        if (!value.TryGetProperty(field, out JsonElement amount))
        {
            return 0;
        }

        return amount.ValueKind == JsonValueKind.Number && amount.TryGetDecimal(out decimal number)
            ? number
            : throw new PayloadException($"Skill \"{field}\" allocations must be numbers.");
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
