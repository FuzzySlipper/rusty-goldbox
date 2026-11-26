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
}
