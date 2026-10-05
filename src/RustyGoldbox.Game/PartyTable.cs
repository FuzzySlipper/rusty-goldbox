using System.Text.Json;
using System.Text.Json.Nodes;
using RustyGoldbox.Core.Campaigns;

namespace RustyGoldbox.Game;

/// <summary>One player at a hosted game: an Engine session member and the party members it owns.</summary>
/// <param name="Member">The Engine member number (the host is 1).</param>
/// <param name="Key">The member's stable key, which a rejoin keeps.</param>
internal sealed class Seat(uint member, string key, string name)
{
    public uint Member { get; set; } = member;

    public string Key { get; } = key;

    public string Name { get; set; } = name;

    public bool Connected { get; set; } = true;

    /// <summary>Party indices this seat plays, in party order.</summary>
    public List<int> Characters { get; } = [];
}

/// <summary>
/// Who plays what at a hosted game, kept by the host: seats, the party
/// leader, and which party members each seat owns. Every guest action passes
/// <see cref="Allows"/> before it reaches the ordinary commands; Core never
/// sees roles. Party members no seat owns belong to the leader.
/// </summary>
internal sealed class PartyTable
{
    public const uint HostMember = 1;

    /// <summary>Character commands whose second word is the 1-based party member they act on.</summary>
    private static readonly HashSet<string> CharacterVerbs = ["equip", "unequip", "use", "level", "milestone", "former", "improve"];

    /// <summary>Commands anyone may send: they only show something.</summary>
    private static readonly HashSet<string> LookVerbs = ["look", "status", "view"];

    public List<Seat> Seats { get; } = [];

    public uint Leader { get; private set; } = HostMember;

    /// <summary>Party members put on automatic control while their player is away, to hand back on rejoin.</summary>
    public HashSet<int> Covered { get; } = [];

    /// <summary>
    /// The host sequence from which the current menu or combat decision has
    /// been on screen; a choice made against an older view is refused.
    /// </summary>
    public ulong DecisionSince { get; private set; }

    private string? _decision;

    private readonly Dictionary<uint, int> _votes = [];
    private string? _voteMenu;

    /// <summary>The open vote on the current event menu: member → option number.</summary>
    public IReadOnlyDictionary<uint, int> Votes => _votes;

    /// <summary>Who votes on event menus: seats with characters, and the leader.</summary>
    public IEnumerable<Seat> Voters => Seats.Where(seat => seat.Characters.Count > 0 || seat.Member == Leader);

    public Seat? SeatOf(uint member) => Seats.FirstOrDefault(seat => seat.Member == member);

    /// <summary>The table as players see it: seats, who is here, who plays what, the leader, and the open vote.</summary>
    public JsonObject ToJson() => new()
    {
        ["leader"] = Leader,
        ["votes"] = new JsonObject(_votes.Select(vote => KeyValuePair.Create(vote.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), (JsonNode?)vote.Value))),
        ["seats"] = new JsonArray(Seats.Select(seat => (JsonNode)new JsonObject
        {
            ["member"] = seat.Member,
            ["key"] = seat.Key,
            ["name"] = seat.Name,
            ["connected"] = seat.Connected,
            ["characters"] = new JsonArray(seat.Characters.Select(index => (JsonNode)index).ToArray()),
        }).ToArray()),
    };

    /// <summary>A guest's copy of the host's table, read from a view.</summary>
    public static PartyTable FromJson(JsonElement json)
    {
        PartyTable table = new() { Leader = json.GetProperty("leader").GetUInt32() };
        foreach (JsonElement entry in json.GetProperty("seats").EnumerateArray())
        {
            Seat seat = new(entry.GetProperty("member").GetUInt32(), entry.GetProperty("key").GetString()!, entry.GetProperty("name").GetString()!)
            {
                Connected = entry.GetProperty("connected").GetBoolean(),
            };
            seat.Characters.AddRange(entry.GetProperty("characters").EnumerateArray().Select(index => index.GetInt32()));
            table.Seats.Add(seat);
        }

        if (json.TryGetProperty("votes", out JsonElement votes))
        {
            foreach (JsonProperty vote in votes.EnumerateObject())
            {
                table._votes[uint.Parse(vote.Name, System.Globalization.CultureInfo.InvariantCulture)] = vote.Value.GetInt32();
            }
        }

        return table;
    }

    /// <summary>Seats a member, or gives a rejoining member (same key) its seat and characters back.</summary>
    public Seat Join(uint member, string key, string name)
    {
        if (Seats.FirstOrDefault(seat => seat.Key == key) is Seat known)
        {
            known.Member = member;
            known.Connected = true;
            return known;
        }

        Seat seat = new(member, key, name);
        Seats.Add(seat);
        return seat;
    }

    /// <summary>A member left or was lost: its seat stays for a rejoin, and an absent leader hands the lead back to the host.</summary>
    public void Leave(uint member)
    {
        if (SeatOf(member) is Seat seat)
        {
            seat.Connected = false;
        }

        if (Leader == member)
        {
            Leader = HostMember;
        }
    }

    /// <summary>The seat that plays a party member: its owner, else the leader.</summary>
    public uint OwnerOf(int partyIndex) => Seats.FirstOrDefault(seat => seat.Characters.Contains(partyIndex))?.Member ?? Leader;

    /// <summary>A party member was added on the party screen for <paramref name="member"/>.</summary>
    public void Claim(uint member, int partyIndex)
    {
        foreach (Seat seat in Seats)
        {
            seat.Characters.Remove(partyIndex);
        }

        SeatOf(member)?.Characters.Add(partyIndex);
    }

    /// <summary>A party member was dropped on the party screen; later indices move down.</summary>
    public void Dropped(int partyIndex)
    {
        foreach (Seat seat in Seats)
        {
            seat.Characters.Remove(partyIndex);
            for (int index = 0; index < seat.Characters.Count; index++)
            {
                if (seat.Characters[index] > partyIndex)
                {
                    seat.Characters[index]--;
                }
            }
        }
    }

    /// <summary>
    /// Notes the host sequence a view went out with, so a choice made against
    /// an earlier menu or decision can be told apart from one made against
    /// the current one.
    /// </summary>
    public void ViewSent(GameSession session, ulong sequence)
    {
        string? decision = session.Runner?.State.PendingMenu is not null
            ? $"menu:{session.Runner.State.Commands}"
            : session.Combat?.PendingDecision?.Id;
        if (decision != _decision)
        {
            _decision = decision;
            DecisionSince = sequence;
        }
    }

    /// <summary>
    /// Whether <paramref name="member"/> may send <paramref name="action"/>
    /// now: null when it may, else the reason, in words, for that player.
    /// </summary>
    /// <param name="seen">The last host sequence that member had received.</param>
    public string? Allows(GameSession session, uint member, JsonElement action, ulong seen)
    {
        bool host = member == HostMember;
        bool leader = member == Leader;
        string name = action.TryGetProperty("action", out JsonElement value) ? value.GetString() ?? "" : "";
        switch (name)
        {
            case "open" or "begin" or "save" or "load" or "quit" or "refresh":
                return host ? null : "Only the host can do that.";
            case "pass-lead":
                return leader || host ? null : "Only the leader can pass the lead.";
            case "decide":
                return leader ? null : "Only the leader can settle the vote early.";
            case "roll":
                return session.Screen == Screen.Party ? null : "Characters are made before the adventure begins.";
            case "drop" or "equip" or "skills" or "spells" or "memorise":
                return Owns(member, Index(action, "member")) || host ? null : "That character isn't yours.";
            case "continue":
                return leader || host ? null : "The leader moves the party on after a fight.";
            case "play":
                return Play(session, member, leader, action, seen);
            case "combat-action" or "combat-end-turn" or "combat-control":
                return Stale(name, seen) ?? Combatant(session, member, host, Text(action, "actor"));
            case "combat-decide":
                return Stale(name, seen)
                    ?? (session.Combat?.PendingDecision is { } decision && decision.Id == Text(action, "decision")
                        ? Combatant(session, member, host, decision.ActorId)
                        : "That choice is no longer waiting.");
            default:
                return $"'{name}' isn't something a player at the table sends.";
        }
    }

    /// <summary>Records a member's vote on the current event menu; a new menu starts a new vote.</summary>
    public string? Vote(GameSession session, uint member, int option)
    {
        SyncVote(session);
        if (_voteMenu is null)
        {
            return "There is no choice to vote on.";
        }

        if (session.Runner!.MenuOptions().All(choice => choice.Number != option))
        {
            return $"{option} isn't one of the choices.";
        }

        if (Voters.All(seat => seat.Member != member) && member != Leader)
        {
            return "Only players with characters vote.";
        }

        _votes[member] = option;
        return null;
    }

    /// <summary>
    /// The party's choice, once it is settled: when every voter who is here
    /// has voted, or when the leader calls it (<paramref name="called"/>)
    /// counting only votes cast. Most votes win and the leader's vote breaks
    /// a tie; null while it isn't settled.
    /// </summary>
    public int? Settled(GameSession session, bool called)
    {
        SyncVote(session);
        if (_voteMenu is null || _votes.Count == 0)
        {
            return null;
        }

        if (!called && Voters.Any(seat => (seat.Connected || seat.Member == HostMember) && !_votes.ContainsKey(seat.Member)))
        {
            return null;
        }

        List<IGrouping<int, KeyValuePair<uint, int>>> tallies = _votes.GroupBy(vote => vote.Value).ToList();
        int most = tallies.Max(group => group.Count());
        List<int> tied = tallies.Where(group => group.Count() == most).Select(group => group.Key).ToList();
        if (tied.Count == 1)
        {
            return tied[0];
        }

        return _votes.TryGetValue(Leader, out int leaderChoice) && tied.Contains(leaderChoice) ? leaderChoice : null;
    }

    /// <summary>The vote has been acted on; the next menu starts a new one.</summary>
    public void VoteClosed()
    {
        _votes.Clear();
        _voteMenu = null;
    }

    /// <summary>The log line for a settled vote: "The party chose 2 (Ann, Bo; Cy chose 1)."</summary>
    public string Tally(int choice)
    {
        string Names(IEnumerable<KeyValuePair<uint, int>> votes) => string.Join(", ", votes.Select(vote => SeatOf(vote.Key)?.Name ?? $"player {vote.Key}"));
        List<string> others = _votes.Where(vote => vote.Value != choice).GroupBy(vote => vote.Value)
            .Select(group => $"{Names(group)} chose {group.Key}").ToList();
        return $"The party chose {choice} ({Names(_votes.Where(vote => vote.Value == choice))}{(others.Count == 0 ? "" : "; " + string.Join("; ", others))}).";
    }

    private void SyncVote(GameSession session)
    {
        string? menu = session.Runner?.State.PendingMenu is not null ? $"menu:{session.Runner.State.Commands}" : null;
        if (menu != _voteMenu)
        {
            _voteMenu = menu;
            _votes.Clear();
        }
    }

    /// <summary>Passes the lead; only to a connected seat.</summary>
    public string? PassLead(uint to)
    {
        if (to != HostMember && SeatOf(to) is not { Connected: true })
        {
            return "The lead can only pass to a player who is here.";
        }

        Leader = to;
        return null;
    }

    private string? Play(GameSession session, uint member, bool leader, JsonElement action, ulong seen)
    {
        string command = Text(action, "command") ?? "";
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string verb = words.Length > 0 ? words[0] : "";
        if (LookVerbs.Contains(verb))
        {
            return null;
        }

        if (CharacterVerbs.Contains(verb))
        {
            return words.Length > 1 && int.TryParse(words[1], out int number) && Owns(member, number - 1)
                ? null
                : "That character isn't yours.";
        }

        if (verb == "choose")
        {
            // A choice at an event menu is this member's vote; anywhere else it is the leader's.
            return Stale("choose", seen)
                ?? (session.Runner?.State.PendingMenu is not null || leader ? null : "Only the leader moves the party and deals for it.");
        }

        return leader ? null : "Only the leader moves the party and deals for it.";
    }

    private string? Stale(string action, ulong seen)
    {
        return seen < DecisionSince ? $"The party has moved on since you chose ({action})." : null;
    }

    private string? Combatant(GameSession session, uint member, bool host, string? actorId)
    {
        PendingCombatState? pending = session.CombatMetadata;
        if (pending?.Participants.FirstOrDefault(participant => participant.Id == actorId) is not PendingCombatantSource source)
        {
            return "There is no such fighter now.";
        }

        if (source.PartyIndex is not int index)
        {
            return "That fighter isn't in the party.";
        }

        return host || OwnerOf(index) == member ? null : "That fighter isn't yours.";
    }

    private bool Owns(uint member, int partyIndex) => partyIndex >= 0 && OwnerOf(partyIndex) == member;

    private static int Index(JsonElement action, string field) =>
        action.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int index) ? index : -1;

    private static string? Text(JsonElement action, string field) =>
        action.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
