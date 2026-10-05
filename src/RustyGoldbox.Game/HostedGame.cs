using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;

namespace RustyGoldbox.Game;

/// <summary>
/// Playing together over the Engine <c>Session</c> service. The host runs
/// the only campaign: it seats players, applies their actions through the
/// table's rules and sends everyone views. A guest shows those views, sends
/// its commands and installs the modules the host plays when it lacks them.
/// </summary>
/// <remarks>
/// Messages are UTF-8 JSON. A guest sends <c>{ "type": "hello", "name" }</c>
/// once and then <c>{ "type": "action", "action": { ... } }</c>. The host sends
/// <c>{ "kind": "view", ... }</c> (a <see cref="GameView"/>) and
/// <c>{ "kind": "notice", "text" }</c> when it refuses an action.
/// </remarks>
internal sealed class HostedGame(GameSession game)
{
    /// <summary>The Engine compatibility tag: a guest of another protocol is refused.</summary>
    public const string Application = "rusty-goldbox/1";

    private Session? _session;
    private string _name = "";
    private string _lastView = "";
    private JsonDocument? _pendingView;
    private bool _fetching;
    private ulong _chatRevision;

    public bool Active => _session is not null;

    /// <summary>The player's name, which is also the Engine identity, so the same name rejoins as the same member.</summary>
    public string Name => _name;

    /// <summary>Why the last session ended, in words, for the title screen.</summary>
    public string? Ended { get; private set; }

    /// <summary>Hosts the game this player is playing (or will open), reaching guests through <paramref name="relay"/>.</summary>
    /// <param name="relay">Empty for direct connections only (a LAN), "n0" for number 0's development relays, or a relay URL.</param>
    public string? Host(IEngineContext engine, string name, string relay, string relayToken)
    {
        if (TakeName(name) is string refused)
        {
            return refused;
        }

        Leave(engine);
        try
        {
            _session = engine.Session.Host(new SessionHostRequest(_name, Application, relay.Trim(), relayToken, false));
        }
        catch (EngineCallException exception)
        {
            return $"Couldn't host: {exception.Message}";
        }

        game.NewTable(engine);
        game.LocalMember = PartyTable.HostMember;
        return null;
    }

    /// <summary>Joins a host's game with a pasted invitation.</summary>
    public string? Join(IEngineContext engine, string name, string invitation)
    {
        if (TakeName(name) is string refused)
        {
            return refused;
        }

        Leave(engine);
        try
        {
            _session = engine.Session.Join(new SessionJoinRequest(_name, Application, invitation.Trim(), false));
        }
        catch (EngineCallException exception)
        {
            // Text that isn't an invitation is refused at once.
            return exception.Message.Contains("not a session invitation", StringComparison.Ordinal)
                ? "That isn't an invitation; paste the whole text the host copied."
                : $"Couldn't join: {exception.Message}";
        }

        return null;
    }

    /// <summary>Leaves the session; a guest goes back to the title screen, a host keeps playing alone.</summary>
    public void Leave(IEngineContext engine)
    {
        _session?.Dispose();
        _session = null;
        _lastView = "";
        _pendingView?.Dispose();
        _pendingView = null;
        game.Table = null;
        game.LocalMember = PartyTable.HostMember;
        if (game.Guest)
        {
            game.LeaveAsGuest();
        }
    }

    /// <summary>
    /// A save was loaded while hosting: take its seats and seat everyone who
    /// is here again by their key, so returning players get their characters.
    /// </summary>
    public void Reseat(IEngineContext engine)
    {
        if (_session is null || engine.Session.Read(_session).Role != SessionRole.Host)
        {
            return;
        }

        Dictionary<string, string> names = game.Table?.Seats.ToDictionary(seat => seat.Key, seat => seat.Name) ?? [];
        game.NewTable(engine);
        foreach (SessionMember member in engine.Session.ReadMembers(_session).ToArray().Where(member => member.Connected))
        {
            game.SeatJoined(engine, member.Member, member.Key, names.GetValueOrDefault(member.Key) ?? (member.IsLocal ? _name : $"player {member.Member}"));
        }

        _lastView = "";
    }

    /// <summary>The invitation to share, once the hosted session is open.</summary>
    public string? Invitation(IEngineContext engine)
    {
        return _session is not null && engine.Session.Read(_session) is { State: SessionState.Open, Role: SessionRole.Host }
            ? engine.Session.ReadInvitationText(_session)
            : null;
    }

    /// <summary>Says something to everyone at the table.</summary>
    public string? Chat(IEngineContext engine, string text)
    {
        text = text.Trim();
        if (_session is null || text.Length == 0)
        {
            return _session is null ? "You aren't playing with anyone." : null;
        }

        engine.Session.SendChat(_session, text.Length > 1000 ? text[..1000] : text);
        return null;
    }

    /// <summary>What the title screen and panels show about the session.</summary>
    public JsonObject? Readout(IEngineContext engine)
    {
        if (_session is null)
        {
            return Ended is null ? null : new JsonObject { ["ended"] = Ended };
        }

        SessionReadout readout = engine.Session.Read(_session);
        Dictionary<uint, string> names = engine.Session.ReadMembers(_session).ToArray()
            .ToDictionary(member => member.Member, member => game.Table?.SeatOf(member.Member)?.Name ?? (member.IsLocal ? _name : $"player {member.Member}"));
        return new JsonObject
        {
            ["name"] = _name,
            ["chatRevision"] = readout.ChatRevision,
            // The newest lines, oldest first; each is the sender's words as plain text.
            ["chat"] = new JsonArray(engine.Session.ReadChat(_session).ToArray().TakeLast(100).Select(line => (JsonNode)new JsonObject
            {
                ["from"] = names.TryGetValue(line.Member, out string? name) ? name : $"player {line.Member}",
                ["text"] = line.Text,
                ["state"] = line.State.ToString().ToLowerInvariant(),
            }).ToArray()),
            ["role"] = readout.Role.ToString().ToLowerInvariant(),
            ["state"] = readout.State.ToString().ToLowerInvariant(),
            ["invitation"] = Invitation(engine),
            ["members"] = new JsonArray(engine.Session.ReadMembers(_session).ToArray().Select(member => (JsonNode)new JsonObject
            {
                ["member"] = member.Member,
                ["name"] = game.Table?.SeatOf(member.Member)?.Name ?? (member.IsLocal ? _name : $"player {member.Member}"),
                ["host"] = member.IsHost,
                ["you"] = member.IsLocal,
                ["connected"] = member.Connected,
                ["path"] = member.Path.ToString().ToLowerInvariant(),
                ["ms"] = member.RttMicros / 1000,
            }).ToArray()),
        };
    }

    /// <summary>Sends a guest's queued commands to the host.</summary>
    public void SendOutbox(IEngineContext engine)
    {
        if (_session is null || !game.Guest || game.Outbox.Count == 0)
        {
            game.Outbox.Clear();
            return;
        }

        // A new action replaces what the last one noted.
        game.Notes.Clear();
        foreach (JsonElement action in game.Outbox)
        {
            engine.Session.SendToHost(_session, Encode(new JsonObject { ["type"] = "action", ["action"] = JsonNode.Parse(action.GetRawText()) }));
        }

        game.Outbox.Clear();
    }

    /// <summary>
    /// Takes what the session observed and acts on it; the host then shares
    /// its view if the game changed. True when anything shown changed.
    /// </summary>
    public bool Tick(IEngineContext engine)
    {
        if (_session is null)
        {
            return false;
        }

        SessionReadout readout = engine.Session.Read(_session);
        bool changed = readout.ChatRevision != _chatRevision;
        _chatRevision = readout.ChatRevision;
        foreach (SessionEvent observed in engine.Session.TakeEvents(_session).Span)
        {
            changed = true;
            switch (observed.Kind)
            {
                case SessionEventKind.Opened when readout.Role == SessionRole.Host:
                    game.LocalMember = readout.LocalMember;
                    game.SeatJoined(engine, readout.LocalMember, LocalKey(engine), _name);
                    break;
                case SessionEventKind.Opened when readout.Role == SessionRole.Guest:
                    game.LocalMember = readout.LocalMember;
                    engine.Session.SendToHost(_session, Encode(new JsonObject { ["type"] = "hello", ["name"] = _name }));
                    break;
                case SessionEventKind.MemberJoined or SessionEventKind.MemberRejoined when readout.Role == SessionRole.Host:
                    game.SeatJoined(engine, observed.Member, KeyOf(engine, observed.Member), game.Table?.Seats.FirstOrDefault(seat => seat.Key == KeyOf(engine, observed.Member))?.Name ?? $"player {observed.Member}");
                    SendView(engine, observed.Member);
                    break;
                case SessionEventKind.MemberLeft when readout.Role == SessionRole.Host:
                    game.SeatLeft(engine, observed.Member);
                    break;
                case SessionEventKind.Message when readout.Role == SessionRole.Host:
                    Apply(engine, observed);
                    break;
                case SessionEventKind.View or SessionEventKind.Message:
                    Show(engine, observed.Payload);
                    break;
                case SessionEventKind.Ended:
                    Ended = $"{Reason(observed.EndReason)} {engine.Session.ReadDiagnosticText(_session)}".Trim();
                    if (readout.Role == SessionRole.Guest || game.Guest)
                    {
                        Leave(engine);
                        game.Notes.Add(Ended);
                    }
                    else
                    {
                        _session.Dispose();
                        _session = null;
                        game.Table = null;
                    }

                    return true;
            }
        }

        if (readout.Role == SessionRole.Guest)
        {
            SendOutbox(engine);
            RetryPendingView(engine);
        }
        else
        {
            changed |= ShareView(engine);
        }

        return changed;
    }

    private void Apply(IEngineContext engine, SessionEvent observed)
    {
        JsonNode? message;
        try
        {
            message = JsonNode.Parse(observed.Payload.Span);
        }
        catch (JsonException)
        {
            return;
        }

        switch ((string?)message?["type"])
        {
            case "hello" when message!["name"]?.GetValue<string>() is string name && game.Table?.SeatOf(observed.Member) is Seat seat:
                seat.Name = name.Length > 24 ? name[..24] : name;
                break;
            case "action" when message!["action"] is JsonObject action:
                using (JsonDocument document = JsonDocument.Parse(action.ToJsonString()))
                {
                    if (GameCommands.RunFor(game, engine, observed.Member, document.RootElement, observed.Seen).Message is string reply)
                    {
                        engine.Session.Send(new SessionSendRequest(_session!, observed.Member, Encode(new JsonObject { ["kind"] = "notice", ["text"] = reply })));
                    }
                }

                break;
        }
    }

    private void Show(IEngineContext engine, ReadOnlyMemory<byte> payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return;
        }

        string? kind = document.RootElement.TryGetProperty("kind", out JsonElement value) ? value.GetString() : null;
        if (kind == "notice")
        {
            game.Notes.Clear();
            game.Notes.Add(document.RootElement.GetProperty("text").GetString() ?? "");
            document.Dispose();
            return;
        }

        if (kind != "view")
        {
            document.Dispose();
            return;
        }

        _pendingView?.Dispose();
        _pendingView = document;
        RetryPendingView(engine);
    }

    /// <summary>
    /// Shows the latest view; when the host plays a module this player lacks,
    /// installs it from where the view says it is published and shows the
    /// view once that is done.
    /// </summary>
    private void RetryPendingView(IEngineContext engine)
    {
        if (_pendingView is null || game.Installer.Busy)
        {
            return;
        }

        game.ShowView(engine, _pendingView.RootElement);
        if (game.Missing is not (string id, string version))
        {
            _fetching = false;
            _pendingView.Dispose();
            _pendingView = null;
            return;
        }

        if (_fetching)
        {
            // An install was tried and the module still isn't here: stop, with the view's notes saying why.
            _pendingView.Dispose();
            _pendingView = null;
            _fetching = false;
            return;
        }

        if (_pendingView.RootElement.TryGetProperty("sources", out JsonElement sources) && sources.TryGetProperty(id, out JsonElement source) && source.GetString() is string releases)
        {
            game.Notes.Add($"Installing {id} {version} from {releases} to join…");
            game.Installer.Install(releases, id, version);
            _fetching = true;
        }
    }

    /// <summary>The host's view to everyone with theirs, when the game changed since the last one.</summary>
    private bool ShareView(IEngineContext engine)
    {
        string view = ViewText();
        if (view == _lastView || _session is null)
        {
            return false;
        }

        _lastView = view;
        SessionSendReceipt receipt = engine.Session.Broadcast(new SessionBroadcastRequest(_session, Encoding.UTF8.GetBytes(view)));
        game.Table?.ViewSent(game, receipt.Sequence);
        return true;
    }

    private void SendView(IEngineContext engine, uint member)
    {
        string view = ViewText();
        SessionSendReceipt receipt = engine.Session.SendView(new SessionSendRequest(_session!, member, Encoding.UTF8.GetBytes(view)));
        game.Table?.ViewSent(game, receipt.Sequence);
    }

    private string ViewText()
    {
        JsonObject view = GameView.Of(game);
        view["kind"] = "view";
        return view.ToJsonString();
    }

    private string LocalKey(IEngineContext engine) =>
        engine.Session.ReadMembers(_session!).ToArray().FirstOrDefault(member => member.IsLocal).Key ?? "host";

    private string KeyOf(IEngineContext engine, uint member) =>
        engine.Session.ReadMembers(_session!).ToArray().FirstOrDefault(entry => entry.Member == member).Key ?? $"member-{member}";

    private string? TakeName(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 24 || !name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            return "Choose a name of letters, digits, - or _ (up to 24); it is how the others see you, and how you rejoin.";
        }

        _name = name;
        Ended = null;
        return null;
    }

    private static string Reason(SessionEndReason reason) => reason switch
    {
        SessionEndReason.HostClosed => "The host closed the game.",
        SessionEndReason.HostLost => "Lost the host.",
        SessionEndReason.Refused => "The host refused this game (a different version or session).",
        SessionEndReason.Unreachable => "Couldn't reach the host.",
        SessionEndReason.Left => "You left.",
        _ => "The game ended.",
    };

    private static byte[] Encode(JsonObject message) => Encoding.UTF8.GetBytes(message.ToJsonString());
}
