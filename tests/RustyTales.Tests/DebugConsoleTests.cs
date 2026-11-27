using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Testing;
using RustyTales.Core.Campaigns;
using RustyTales.Game;

namespace RustyTales.Tests;

/// <summary>Live-debug console shortcuts in <see cref="TalesDebugModule"/>.</summary>
public sealed class DebugConsoleTests
{
    [Fact]
    public void StartTeleportPlayEventHealAndQuitWorkThroughTheDebugModule()
    {
        using TempModules scratch = new();
        List<string> containers = new[] { "classic", "placeholder-art", "sample-crypt" }
            .Select(id => EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
            .ToList();

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            ModuleLibrary library = new(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList());
            GameSession session = new(library);
            int publishes = 0;
            TalesDebugModule debug = new(session, engine, () => publishes++);

            Assert.True(debug.Help().Succeeded);
            DebugCommandResult campaigns = debug.Campaigns();
            Assert.True(campaigns.Succeeded);
            Assert.Contains("sample-crypt", campaigns.Message, StringComparison.Ordinal);

            // Quickstart Sample Crypt from title straight into play.
            DebugCommandResult started = debug.Start("sample-crypt", "3");
            Assert.True(started.Succeeded, started.Message);
            Assert.Equal(Screen.Play, session.Screen);
            Assert.Equal(1, publishes);

            // Inspect JSON state.
            DebugCommandResult stateResult = debug.State();
            Assert.True(stateResult.Succeeded);
            using (JsonDocument doc = JsonDocument.Parse(stateResult.Message))
            {
                Assert.Equal("play", doc.RootElement.GetProperty("screen").GetString());
                Assert.Equal(0, doc.RootElement.GetProperty("position").GetProperty("x").GetInt32());
                Assert.Equal(2, doc.RootElement.GetProperty("position").GetProperty("y").GetInt32());
            }

            // Teleport directly to [3, 0] facing south without walking.
            DebugCommandResult moved = debug.TeleportLocal(3, 0, "south");
            Assert.True(moved.Succeeded, moved.Message);
            Assert.Equal(3, session.Runner!.State.X);
            Assert.Equal(0, session.Runner.State.Y);
            Assert.Equal(Facing.South, session.Runner.State.Facing);

            // Execute an ordinary play command and verify quiet step doesn't pollute prose log with media tags.
            DebugCommandResult played = debug.Play("left");
            Assert.True(played.Succeeded, played.Message);
            Assert.Equal(Facing.East, session.Runner.State.Facing);
            Assert.DoesNotContain(session.Log, line => line.StartsWith("[music ", StringComparison.Ordinal) || line.StartsWith("[sound ", StringComparison.Ordinal));

            // Trigger an authored combat event directly.
            DebugCommandResult fight = debug.Event("guards");
            Assert.True(fight.Succeeded, fight.Message);
            Assert.Equal(Screen.Combat, session.Screen);

            // Heal restores party tracks.
            session.Runner.State.Party[0].Tracks["hit_points"].Current = 1;
            Assert.True(debug.Heal().Succeeded);
            Assert.True(session.Runner.State.Party[0].Tracks["hit_points"].Current > 1);

            // Quit returns to title.
            Assert.True(debug.Quit().Succeeded);
            Assert.Equal(Screen.Title, session.Screen);
        });
    }

    [Fact]
    public void WorkbenchBridgeCommandsAndProjectionExposeLiveStateAndJumps()
    {
        using TempModules scratch = new();
        List<string> containers = new[] { "classic", "placeholder-art", "sample-crypt" }
            .Select(id => EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
            .ToList();

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            ModuleLibrary library = new(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList());
            GameSession session = new(library);
            TalesDebugModule debug = new(session, engine, () => { });

            // Default workbench URL and update via debug console & command payload.
            Assert.Contains("http://127.0.0.1:", debug.Workbench().Message, StringComparison.Ordinal);
            Assert.True(debug.Workbench("http://127.0.0.1:4199/").Succeeded);
            Assert.Equal("http://127.0.0.1:4199", session.WorkbenchUrl);

            using (JsonDocument urlCmd = JsonDocument.Parse("""{"action":"workbench-url","url":"http://127.0.0.1:4188/"}"""))
            {
                GameCommands.Run(session, engine, urlCmd.RootElement);
            }
            Assert.Equal("http://127.0.0.1:4188", session.WorkbenchUrl);

            using (JsonDocument badUrlCmd = JsonDocument.Parse("""{"action":"workbench-url","url":"ftp://bad"}"""))
            {
                GameCommands.Run(session, engine, badUrlCmd.RootElement);
            }
            Assert.Equal("http://127.0.0.1:4188", session.WorkbenchUrl);
            Assert.Contains(session.Notes, n => n.Contains("http://", StringComparison.Ordinal));

            // Start Sample Crypt and check SessionProjection["workbench"].
            Assert.True(debug.Start("sample-crypt", "3").Succeeded);
            using (JsonDocument doc = JsonDocument.Parse(SessionProjection.Build(session).ToJsonString()))
            {
                JsonElement wb = doc.RootElement.GetProperty("workbench");
                Assert.Equal("http://127.0.0.1:4188", wb.GetProperty("url").GetString());
                Assert.Equal("play", wb.GetProperty("screen").GetString());
                Assert.Equal("sample-crypt", wb.GetProperty("module").GetString());
                Assert.Equal("sample-crypt:entrance", wb.GetProperty("area").GetString());
                Assert.Equal(0, wb.GetProperty("x").GetInt32());
                Assert.Equal(2, wb.GetProperty("y").GetInt32());
                Assert.Equal("north", wb.GetProperty("facing").GetString());
            }

            // Jump live from Workbench via debug-goto command.
            using (JsonDocument gotoCmd = JsonDocument.Parse("""{"action":"debug-goto","area":"sample-crypt:entrance","x":3,"y":1,"facing":"east"}"""))
            {
                GameCommands.Run(session, engine, gotoCmd.RootElement);
            }
            Assert.Equal(3, session.Runner!.State.X);
            Assert.Equal(1, session.Runner.State.Y);
            Assert.Equal(Facing.East, session.Runner.State.Facing);

            using (JsonDocument doc = JsonDocument.Parse(SessionProjection.Build(session).ToJsonString()))
            {
                JsonElement wb = doc.RootElement.GetProperty("workbench");
                Assert.Equal(3, wb.GetProperty("x").GetInt32());
                Assert.Equal(1, wb.GetProperty("y").GetInt32());
                Assert.Equal("east", wb.GetProperty("facing").GetString());
            }

            // Trigger an event live from Workbench via debug-event command.
            using (JsonDocument evtCmd = JsonDocument.Parse("""{"action":"debug-event","event":"sample-crypt:guards"}"""))
            {
                GameCommands.Run(session, engine, evtCmd.RootElement);
            }
            Assert.Equal(Screen.Combat, session.Screen);

            using (JsonDocument doc = JsonDocument.Parse(SessionProjection.Build(session).ToJsonString()))
            {
                JsonElement wb = doc.RootElement.GetProperty("workbench");
                Assert.Equal("combat", wb.GetProperty("screen").GetString());
                Assert.False(string.IsNullOrWhiteSpace(wb.GetProperty("activeActor").GetString()));
            }
        });
    }

    [Fact]
    public void RollDraftScoresPopulatesPartyProjectionAndRerollsDeterministically()
    {
        using TempModules scratch = new();
        List<string> containers = new[] { "classic", "placeholder-art", "sample-crypt" }
            .Select(id => EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
            .ToList();

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            ModuleLibrary library = new(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList());
            GameSession session = new(library);
            session.Refresh();
            CampaignChoice crypt = Assert.Single(session.Campaigns, c => c.Id == "sample-crypt");

            session.Open(crypt.Bundle, seed: 42);
            Assert.Equal(Screen.Party, session.Screen);
            Assert.NotNull(session.DraftScores);
            Assert.Equal(6, session.DraftScores!.Count);

            Dictionary<string, decimal> firstRoll = new(session.DraftScores);
            using (JsonDocument doc = JsonDocument.Parse(SessionProjection.Build(session).ToJsonString()))
            {
                JsonElement draft = doc.RootElement.GetProperty("draftScores");
                Assert.Equal(1, draft.GetProperty("roll").GetInt32());
                Assert.Equal((double)firstRoll["str"], draft.GetProperty("scores").GetProperty("str").GetDouble());
            }

            // Re-roll via GameCommands ("roll-scores")
            using (JsonDocument cmd = JsonDocument.Parse("""{"action":"roll-scores"}"""))
            {
                GameCommands.Run(session, engine, cmd.RootElement);
            }

            Assert.Equal(2, session.DraftRollNumber);
            Assert.NotNull(session.DraftScores);
            Assert.False(firstRoll.All(kv => session.DraftScores![kv.Key] == kv.Value));
        });
    }
}
