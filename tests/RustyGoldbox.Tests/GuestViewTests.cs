using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Game;
using RustyGoldbox.Game.Presentation;

namespace RustyGoldbox.Tests;

/// <summary>A multiplayer guest shows what the host shows from the host's views alone, and never runs the campaign.</summary>
public sealed class GuestViewTests
{
    [Fact]
    public void AGuestMirrorsTheHostThroughCreationPlayALiveFightAndItsEnd()
    {
        using TempModules scratch = new();
        List<string> containers = new[] { "classic", "placeholder-art", "sample-crypt" }
            .Select(id => EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
            .ToList();
        List<string> script = File.ReadAllLines(CampaignTests.Script("crypt.script"))
            .Select(line => line.Split('#')[0].Trim())
            .Where(line => line.Length > 0)
            .ToList();

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            ModuleLibrary Library() => new(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList());
            GameSession leader = new(Library());
            GameSession guest = new(Library());
            leader.Refresh();
            Run(leader, engine, JsonSerializer.Serialize(new { action = "open", campaign = Assert.Single(leader.Campaigns).Bundle, seed = "12" }));
            Mirror(leader, guest, engine);

            foreach ((string name, string characterClass, string item) in new[] { ("Ada", "classic:fighter", "classic:long_sword"), ("Brom", "classic:cleric", "classic:heavy_mace") })
            {
                for (int attempt = 0; attempt < 50 && leader.Party.All(member => member.Name != name); attempt++)
                {
                    Run(leader, engine, JsonSerializer.Serialize(new { action = "roll", name, race = "classic:human", @class = characterClass }));
                }

                Run(leader, engine, JsonSerializer.Serialize(new { action = "equip", member = leader.Party.FindIndex(member => member.Name == name), item }));
                Mirror(leader, guest, engine);
            }

            Run(leader, engine, """{ "action": "begin" }""");
            Mirror(leader, guest, engine);

            // Walk to the first fight, comparing every step.
            foreach (string command in script)
            {
                Run(leader, engine, JsonSerializer.Serialize(new { action = "play", command }));
                Mirror(leader, guest, engine);
                if (leader.Screen == Screen.Combat)
                {
                    break;
                }
            }

            // A live fight waiting on a party decision, drawn by the guest's own scene.
            Assert.Equal(Screen.Combat, guest.Screen);
            CombatObservation waiting = Assert.IsType<CombatObservation>(guest.Combat);
            Assert.NotNull(waiting.PendingDecision);
            using (SceneView scene = new(engine, Library()))
            {
                scene.Show(guest);
            }

            // A guest's choice goes to the host and changes nothing here.
            Run(guest, engine, JsonSerializer.Serialize(new { action = "combat-end-turn", actor = waiting.PendingDecision!.ActorId }));
            JsonElement queued = Assert.Single(guest.Outbox);
            Assert.Equal("combat-end-turn", queued.GetProperty("action").GetString());
            Assert.Same(waiting.PendingDecision, guest.Combat!.PendingDecision);

            // Hand the party to Core so the fight finishes, then the finished fight and the return to play.
            for (int step = 0; step < 20 && leader.Runner!.State.PendingCombat is not null; step++)
            {
                string actor = leader.Combat!.PendingDecision!.ActorId;
                Run(leader, engine, JsonSerializer.Serialize(new { action = "combat-control", actor, mode = "auto" }));
            }

            Assert.Equal(CombatPhase.Ended, leader.Combat!.Phase);
            Mirror(leader, guest, engine);
            Assert.Equal(CombatPhase.Ended, guest.Combat!.Phase);
            using (SceneView scene = new(engine, Library()))
            {
                scene.Show(guest);
            }

            Run(leader, engine, """{ "action": "continue" }""");
            Mirror(leader, guest, engine);
            Assert.Equal(Screen.Play, guest.Screen);
        });
    }

    /// <summary>Sends the host's view through JSON text, as the session does, and checks the guest now projects what the host projects.</summary>
    private static void Mirror(GameSession leader, GameSession guest, IEngineContext engine)
    {
        using JsonDocument view = JsonDocument.Parse(GameView.Of(leader).ToJsonString());
        guest.ShowView(engine, view.RootElement);
        Assert.Equal(Projection(leader), Projection(guest));
    }

    /// <summary>The projection without what is each player's own: their settings, skins, campaign list and module library.</summary>
    private static string Projection(GameSession session)
    {
        JsonObject projection = SessionProjection.Build(session);
        foreach (string own in new[] { "modules", "skins", "skinPicked", "skin", "volumes", "layout", "layoutPicked", "uiScale", "layoutParts", "campaigns", "notes" })
        {
            projection.Remove(own);
        }

        return projection.ToJsonString();
    }

    private static void Run(GameSession session, IEngineContext engine, string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        GameCommands.Run(session, engine, document.RootElement);
    }
}
