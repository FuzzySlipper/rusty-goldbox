using System.Text;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Game;

namespace RustyGoldbox.Tests;

/// <summary>
/// The Game's session and its input boundary, run against the sample modules
/// packed as Engine containers (the bundle surface the product opens).
/// </summary>
public sealed class GameTests
{
    private static readonly string[] SampleSet = ["classic", "placeholder-art", "sample-crypt"];

    [Theory]
    [InlineData("""{ "action": "equip", "member": 0, "item": null }""", "\"item\" must be non-empty text")]
    [InlineData("""{ "action": "play", "command": null }""", "\"command\" must be non-empty text")]
    [InlineData("""{ "action": "roll", "name": " ", "race": "classic:human", "class": "classic:fighter" }""", "\"name\" must be non-empty text")]
    [InlineData("""{ "action": "drop", "member": "0" }""", "\"member\" must be a whole number")]
    [InlineData("""{ "action": "open", "campaign": "x", "seed": 7 }""", "\"seed\" must be non-empty text")]
    [InlineData("""{ "action": "save", "slot": "../escape" }""", "isn't a save slot name")]
    [InlineData("""{ "action": "dance" }""", "'dance' is not an action")]
    [InlineData("""[1, 2]""", "must be an object")]
    public void BadPayloadsBecomeNotes(string payload, string note)
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            Run(session, engine, payload);
            Assert.Contains(note, Assert.Single(session.Notes), StringComparison.Ordinal);
            Assert.Empty(session.Party);
        });
    }

    [Fact]
    public void TheLibraryListsInstalledCampaignsAndNamesBrokenContainers()
    {
        using TempModules scratch = new();
        string library = Path.Combine(scratch.Root, "library");
        foreach (string id in SampleSet)
        {
            EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch, Path.Combine(library, $"{id}-0.1.0.rpak"));
        }

        // A second copy of the same campaign (as when the product ships it and it is also installed).
        File.Copy(Path.Combine(library, "sample-crypt-0.1.0.rpak"), Path.Combine(library, "sample-crypt-copy.rpak"));
        scratch.Write("library/zzz-broken-0.1.0.rpak", "not a container");
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = new(new ModuleLibrary(problems =>
            {
                List<ProductContentBundle> opened = [];
                InstalledModules.Open(engine.Content, library, opened, problems);
                return opened;
            }));
            session.Refresh();

            Assert.Equal("sample-crypt", Assert.Single(session.Campaigns).Id);
            Assert.Contains("zzz-broken-0.1.0.rpak isn't a usable module container", Assert.Single(session.Notes), StringComparison.Ordinal);
            Run(session, engine, JsonSerializer.Serialize(new { action = "open", campaign = session.Campaigns[0].Bundle, seed = "1" }));
            Assert.Equal(Screen.Party, session.Screen);
        });
    }

    [Fact]
    public void KeyIntentsActOnPressesOnly()
    {
        Assert.True(GameCommands.IsPress(Digital(InputProvenance.DirectUi, InputEdge.None, 1)));
        Assert.False(GameCommands.IsPress(Digital(InputProvenance.DirectUi, InputEdge.None, 0)));
        Assert.True(GameCommands.IsPress(Digital(default, InputEdge.Pressed, 0)));
        Assert.False(GameCommands.IsPress(Digital(default, InputEdge.Released, 0)));
    }

    [Fact]
    public void ThePartyPlaysSavesAndLoadsThroughCommands()
    {
        using TempModules scratch = new();
        string store = Path.Combine(scratch.Root, "persistence");
        List<string> script = File.ReadAllLines(CampaignTests.Script("crypt.script"))
            .Select(line => line.Split('#')[0].Trim())
            .Where(line => line.Length > 0)
            .ToList();
        int split = script.IndexOf("choose 1");

        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = store });
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            foreach ((string name, string characterClass, string[] items) in new[]
            {
                ("Ada", "classic:fighter", new[] { "classic:long_sword", "classic:chain_mail" }),
                ("Brom", "classic:cleric", new[] { "classic:heavy_mace" }),
            })
            {
                // Rolls fail class requirements now and then; each try rolls on its own scope.
                for (int attempt = 0; attempt < 50 && session.Party.All(member => member.Name != name); attempt++)
                {
                    Run(session, engine, $$"""{ "action": "roll", "name": "{{name}}", "race": "classic:human", "class": "{{characterClass}}" }""");
                }

                int member = session.Party.FindIndex(character => character.Name == name);
                Assert.True(member >= 0, string.Join(" ", session.Notes));
                foreach (string item in items)
                {
                    Run(session, engine, $$"""{ "action": "equip", "member": {{member}}, "item": "{{item}}" }""");
                }
            }

            Run(session, engine, """{ "action": "begin" }""");
            Assert.Equal(Screen.Play, session.Screen);
            foreach (string command in script[..split])
            {
                Run(session, engine, JsonSerializer.Serialize(new { action = "play", command }));
            }

            Assert.Equal([(1, "Lift the bar"), (2, "Leave it")], session.Runner!.MenuOptions());
            Run(session, engine, """{ "action": "save", "slot": "game" }""");
            Assert.Equal("Saved to save slot 'game'.", Assert.Single(session.Notes));
            string saved = SaveFile.ToJson(session.Runner.State, session.Set!);

            Run(session, engine, """{ "action": "quit" }""");
            Run(session, engine, """{ "action": "load", "slot": "game" }""");
            Assert.Equal(Screen.Play, session.Screen);
            Assert.Equal(saved, SaveFile.ToJson(session.Runner!.State, session.Set!));
            Assert.Equal(2, session.Runner.MenuOptions().Count);
        });

        // The CLI picks the Game's slot up from the same persistence root.
        File.WriteAllLines(Path.Combine(scratch.Root, "rest.script"), script[split..]);
        (int code, string output) = CampaignTests.Run(scratch, "play", "--campaign", CampaignTests.SampleCrypt, "--store", store, "--load", "game", "--script", "rest.script");
        Assert.True(code == 0, output);
        Assert.Contains("The adventure ends.", output, StringComparison.Ordinal);
    }

    /// <summary>A session over the packed sample modules, with the sample campaign open on seed 11.</summary>
    private static GameSession OpenSession(TempModules scratch, IEngineContext engine)
    {
        List<string> containers = SampleSet
            .Select(id => File.Exists(Path.Combine(scratch.Root, id + ".rpak"))
                ? Path.Combine(scratch.Root, id + ".rpak")
                : EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
            .ToList();
        GameSession session = new(new ModuleLibrary(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList()));
        session.Refresh();
        string campaign = Assert.Single(session.Campaigns).Bundle;
        Run(session, engine, JsonSerializer.Serialize(new { action = "open", campaign, seed = "11" }));
        Assert.Equal(Screen.Party, session.Screen);
        return session;
    }

    private static void Run(GameSession session, IEngineContext engine, string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        GameCommands.Run(session, engine, document.RootElement);
    }

    private static ProductInputEvent Digital(InputProvenance provenance, InputEdge edge, float x)
    {
        return new ProductInputEvent(
            InputEventKind.DirectDigital, edge, default, default, default, default, default, default, default, default,
            InputValueKind.Digital, default, provenance, default, default, default, x, 0,
            default, default, Encoding.UTF8.GetBytes("party.forward"), default, default);
    }
}
