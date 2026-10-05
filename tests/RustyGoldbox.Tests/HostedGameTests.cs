using System.Diagnostics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Testing;
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
            Run(leader, engine, """{ "action": "host", "name": "Hana", "relay": "" }""");
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
