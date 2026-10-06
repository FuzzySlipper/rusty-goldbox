using System.Diagnostics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Game;

namespace RustyGoldbox.Tests;

/// <summary>A host and a guest, each with its own Engine host, playing together over the real Session service.</summary>
public sealed class HostedGameTests
{
    [Fact]
    public void AGuestJoinsByInvitationSeesTheHostsGameAndMakesItsOwnCharacter()
    {
        using TempModules scratch = new();
        List<string> containers = new[] { "classic", "placeholder-art", "sample-crypt" }
            .Select(id => EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
            .ToList();
        using EngineTestHost hostEngine = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = Path.Combine(scratch.Root, "host-store") });
        using EngineTestHost guestEngine = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = Path.Combine(scratch.Root, "guest-store") });
        GameSession? leader = null;
        GameSession? guest = null;
        hostEngine.Call(engine =>
        {
            leader = new GameSession(Library(engine, containers));
            leader.Refresh();
            // Direct by default; GOLDBOX_TEST_RELAY=n0 runs the same check through number 0's internet relays.
            string relay = Environment.GetEnvironmentVariable("GOLDBOX_TEST_RELAY") ?? "";
            Run(leader, engine, JsonSerializer.Serialize(new { action = "host", name = "Hana", relay }));
            Run(leader, engine, JsonSerializer.Serialize(new { action = "open", campaign = Assert.Single(leader.Campaigns).Bundle, seed = "12" }));
        });
        guestEngine.Call(engine =>
        {
            guest = new GameSession(Library(engine, containers));
            guest.Refresh();
        });

        string invitation = Until(hostEngine, engine => { leader!.Hosting.Tick(engine); return leader.Hosting.Invitation(engine); });
        guestEngine.Call(engine => Run(guest!, engine, JsonSerializer.Serialize(new { action = "join", name = "Ann", invitation })));

        // The guest is seated and shows the host's party screen.
        Until(hostEngine, guestEngine, leader!, guest!, () => guest!.Screen == Screen.Party && leader!.Table!.Seats.Any(seat => seat.Name == "Ann"));
        Assert.True(guest!.Guest);

        GameSession host = leader!;
        GameSession player = guest!;

        // The guest makes a character; it runs on the host, belongs to the guest's seat and shows on both.
        guestEngine.Call(engine => Run(player, engine, """{ "action": "roll", "name": "Ann", "race": "classic:human", "class": "classic:fighter" }"""));
        Until(hostEngine, guestEngine, host, player, () => player.Party.Any(member => member.Name == "Ann"));
        Seat ann = host.Table!.Seats.Single(seat => seat.Name == "Ann");
        Assert.Equal(ann.Member, host.Table.OwnerOf(host.Party.FindIndex(member => member.Name == "Ann")));

        // A guest can't begin; the refusal comes back to them.
        guestEngine.Call(engine => Run(player, engine, """{ "action": "begin" }"""));
        Until(hostEngine, guestEngine, host, player, () => player.Notes.Contains("Only the host can do that."));
        Assert.Equal(Screen.Party, host.Screen);

        // The guest leaves; the host keeps the seat for a rejoin.
        guestEngine.Call(engine => Run(player, engine, """{ "action": "leave" }"""));
        Until(hostEngine, guestEngine, host, player, () => !ann.Connected);
        Assert.Equal(Screen.Title, player.Screen);
        hostEngine.Call(engine => host.Hosting.Leave(engine));
    }

    [Fact]
    public void ThreePlayersCrossTheSampleCryptTogether()
    {
        using TempModules scratch = new();
        List<string> containers = new[] { "classic", "placeholder-art", "sample-crypt" }
            .Select(id => EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
            .ToList();
        List<string> script = File.ReadAllLines(CampaignTests.Script("crypt.script"))
            .Select(line => line.Split('#')[0].Trim())
            .Where(line => line.Length > 0)
            .ToList();
        string relay = Environment.GetEnvironmentVariable("GOLDBOX_TEST_RELAY") ?? "";
        List<(EngineTestHost Engine, GameSession Game)> table = new[] { "hana", "ann", "bo" }
            .Select(name =>
            {
                EngineTestHost engine = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = Path.Combine(scratch.Root, name) });
                GameSession game = engine.Call(context => new GameSession(Library(context, containers)));
                game.Refresh();
                return (engine, game);
            })
            .ToList();
        try
        {
            (EngineTestHost hostEngine, GameSession host) = table[0];
            (EngineTestHost annEngine, GameSession ann) = table[1];
            (EngineTestHost boEngine, GameSession bo) = table[2];
            void Tick() => table.ForEach(player => player.Engine.Call(engine => player.Game.Hosting.Tick(engine)));
            void Settle(Func<bool> done)
            {
                Stopwatch waited = Stopwatch.StartNew();
                while (!done())
                {
                    Assert.True(waited.Elapsed < TimeSpan.FromSeconds(30), "The table didn't get there within 30 s.");
                    Tick();
                    Thread.Sleep(20);
                }
            }

            void Do(int player, object action) => table[player].Engine.Call(engine => Run(table[player].Game, engine, JsonSerializer.Serialize(action)));

            // Hana hosts the crypt; Ann and Bo join by invitation.
            Do(0, new { action = "host", name = "Hana", relay });
            Do(0, new { action = "open", campaign = Assert.Single(host.Campaigns).Bundle, seed = "12" });
            string invitation = Until(hostEngine, engine => { host.Hosting.Tick(engine); return host.Hosting.Invitation(engine); });
            Do(1, new { action = "join", name = "Ann", invitation });
            Do(2, new { action = "join", name = "Bo", invitation });
            Settle(() => ann.Screen == Screen.Party && bo.Screen == Screen.Party && host.Table!.Seats.Count(seat => seat.Connected && seat.Name is "Ann" or "Bo") == 2);

            // Everyone makes a character.
            foreach ((int player, string name, string characterClass) in new[] { (0, "Hana", "classic:cleric"), (1, "Ann", "classic:fighter"), (2, "Bo", "classic:fighter") })
            {
                for (int attempt = 0; attempt < 50 && host.Party.All(member => member.Name != name); attempt++)
                {
                    Do(player, new { action = "roll", name, race = "classic:human", @class = characterClass });
                    Settle(() => table[player].Game.Outbox.Count == 0);
                    Tick();
                }
            }

            Settle(() => ann.Party.Count == 3 && bo.Party.Count == 3);
            Seat annSeat = host.Table!.Seats.Single(seat => seat.Name == "Ann");
            Seat boSeat = host.Table.Seats.Single(seat => seat.Name == "Bo");
            Do(0, new { action = "begin" });
            Settle(() => ann.Screen == Screen.Play && bo.Screen == Screen.Play);

            // Everyone chats.
            Do(0, new { action = "chat", text = "Stay close." });
            Do(1, new { action = "chat", text = "Behind you." });
            Do(2, new { action = "chat", text = "Torch is lit." });
            Settle(() => table.All(player => player.Engine.Call(engine => player.Game.Hosting.Readout(engine)!["chat"]!.AsArray().Count) == 3));

            // Hana walks the first steps and passes the lead to Ann, who walks to the barred door and hands it back.
            int door = script.IndexOf("dance");
            for (int step = 0; step < door; step++)
            {
                if (step == 4)
                {
                    Do(0, new { action = "pass-lead", to = annSeat.Member });
                    Settle(() => ann.Table?.Leader == annSeat.Member);
                }

                int walker = step < 4 ? 0 : 1;
                Do(walker, new { action = "play", command = script[step] });
                Settle(() => table[walker].Game.Outbox.Count == 0 && ann.Log.Count == host.Log.Count && bo.Log.Count == host.Log.Count);
            }

            Assert.Equal(annSeat.Member, host.Table.Leader);
            Do(2, new { action = "play", command = "forward" });
            Settle(() => bo.Notes.Contains("Only the leader moves the party and deals for it."));
            Do(1, new { action = "pass-lead", to = PartyTable.HostMember });
            Settle(() => host.Table.Leader == PartyTable.HostMember && ann.Table!.Leader == PartyTable.HostMember);

            // The barred door: Ann and Bo split, Hana's decide is refused as a tie, and her vote breaks it.
            List<int> options = host.Runner!.MenuOptions().Select(option => option.Number).ToList();
            (int lift, int leave) = (options[0], options[1]);
            Do(1, new { action = "play", command = $"choose {lift}" });
            Do(2, new { action = "play", command = $"choose {leave}" });
            Settle(() => host.Table.Votes.Count == 2 && bo.Table!.Votes.Count == 2);
            Do(0, new { action = "decide" });
            Assert.Contains("The vote is tied: vote to break it, then decide.", host.Notes);
            Do(0, new { action = "play", command = $"choose {lift}" });
            string chose = $"The party chose {lift} (Ann, Hana; Bo chose {leave}).";
            Settle(() => ann.Log.Contains(chose) && bo.Log.Contains(chose));

            // Through the door to the guards.
            Do(0, new { action = "play", command = "forward" });
            Settle(() => host.Screen == Screen.Combat && ann.Screen == Screen.Combat && bo.Screen == Screen.Combat);

            // Each player answers for their own fighter until all three have taken a turn.
            HashSet<string> acted = [];
            bool boLeft = false;
            Stopwatch fighting = Stopwatch.StartNew();
            while (host.Screen == Screen.Combat && acted.Count < 3)
            {
                Assert.True(fighting.Elapsed < TimeSpan.FromSeconds(60), "The fight didn't come round to everyone within 60 s.");
                if (host.Combat!.PendingDecision is not CombatDecision decision
                    || host.CombatMetadata!.Participants.Single(participant => participant.Id == decision.ActorId).PartyIndex is not int index)
                {
                    Tick();
                    Thread.Sleep(20);
                    continue;
                }

                uint owner = host.Table.OwnerOf(index);
                int player = owner == annSeat.Member ? 1 : owner == boSeat.Member ? 2 : 0;
                Settle(() => table[player].Game.Combat?.PendingDecision?.Id == decision.Id);
                if (player == 2 && !boLeft)
                {
                    // Bo drops out mid-fight: his fighter goes automatic and the fight carries on; he rejoins and has it back.
                    boLeft = true;
                    Do(2, new { action = "leave" });
                    Settle(() => !boSeat.Connected && host.Combat?.PendingDecision?.Id != decision.Id);
                    Assert.Contains(index, host.Table.Covered);
                    Do(2, new { action = "join", name = "Bo", invitation });
                    Settle(() => boSeat.Connected && bo.Screen is Screen.Combat or Screen.Play && host.Table.Covered.Count == 0);
                    Assert.Equal(CombatControlMode.Manual, host.Runner!.State.Party[index].CombatControlPreference);
                    continue;
                }

                Do(player, new { action = "combat-end-turn", actor = decision.ActorId });
                Settle(() => host.Combat?.PendingDecision?.Id != decision.Id || host.Screen != Screen.Combat);
                acted.Add(decision.ActorId);
            }

            Assert.True(boLeft, "Bo's fighter never came up.");
            Do(2, new { action = "chat", text = "Back, sorry." });
            Settle(() => annEngine.Call(engine => ann.Hosting.Readout(engine)!["chat"]!.AsArray().Any(line => (string?)line!["text"] == "Back, sorry.")));
        }
        finally
        {
            foreach ((EngineTestHost engine, GameSession game) in table)
            {
                engine.Call(context => game.Hosting.Leave(context));
                engine.Dispose();
            }
        }
    }

    private static ModuleLibrary Library(IEngineContext engine, List<string> containers) =>
        new(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList());

    /// <summary>Ticks both games, as their updates would, until <paramref name="done"/> holds.</summary>
    private static void Until(EngineTestHost hostEngine, EngineTestHost guestEngine, GameSession leader, GameSession guest, Func<bool> done)
    {
        Stopwatch waited = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(30), "The two games didn't get there within 30 s.");
            hostEngine.Call(engine => leader.Hosting.Tick(engine));
            guestEngine.Call(engine => guest.Hosting.Tick(engine));
            Thread.Sleep(20);
        }
    }

    private static T Until<T>(EngineTestHost engineHost, Func<IEngineContext, T?> value)
        where T : class
    {
        Stopwatch waited = Stopwatch.StartNew();
        while (true)
        {
            if (engineHost.Call(value) is T found)
            {
                return found;
            }

            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(30), "Nothing within 30 s.");
            Thread.Sleep(20);
        }
    }

    private static void Run(GameSession session, IEngineContext engine, string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        GameCommands.Run(session, engine, document.RootElement);
    }
}
