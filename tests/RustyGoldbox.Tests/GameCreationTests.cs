using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using RustyGoldbox.Game;

namespace RustyGoldbox.Tests;

/// <summary>Game-bound creation controls carry authored score and priority choices into Core.</summary>
public sealed class GameCreationTests
{
    [Fact]
    public void PartyProjectionCarriesAuthoredFourAndSixAttributeCreationData()
    {
        using TempModules ascendScratch = new();
        using TempModules fifthScratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession ascend = OpenSession(ascendScratch, engine,
                Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "ascend"),
                Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "ascend-trial"));
            JsonObject ascendProjection = SessionProjection.Build(ascend);
            JsonObject ascendPointBuy = Creation(ascendProjection, "ascend:point_buy");
            Assert.Equal(8, ascendPointBuy["base"]!.GetValue<decimal>());
            Assert.Equal(22, ascendPointBuy["budget"]!.GetValue<decimal>());
            Assert.Equal(11, ascendPointBuy["costs"]!["rows"]!.AsArray().Count);
            Assert.Equal(["might", "grace", "grit", "wit"], ascendPointBuy["attributes"]!.AsArray().Select(attribute => attribute!.GetValue<string>()));
            Assert.Equal(["Might", "Grace", "Grit", "Wit"], ascendPointBuy["attributeDetails"]!.AsArray().Select(attribute => attribute!["name"]!.GetValue<string>()));
            JsonObject ascendArray = Creation(ascendProjection, "ascend:array");
            Assert.Equal([15, 14, 13, 12], ascendArray["array"]!.AsArray().Select(score => score!.GetValue<decimal>()));

            GameSession fifth = OpenSession(fifthScratch, engine,
                Path.Combine(Rules.RepositoryRoot, "modules", "fifth-srd"),
                FifthCampaign(fifthScratch));
            JsonObject fifthProjection = SessionProjection.Build(fifth);
            JsonObject fifthPointBuy = Creation(fifthProjection, "fifth-srd:point_buy");
            Assert.Equal(8, fifthPointBuy["base"]!.GetValue<decimal>());
            Assert.Equal(27, fifthPointBuy["budget"]!.GetValue<decimal>());
            Assert.Equal(6, fifthPointBuy["attributeDetails"]!.AsArray().Count);
            Assert.Equal("Intelligence", fifthPointBuy["attributeDetails"]!.AsArray().Single(attribute => attribute!["id"]!.GetValue<string>() == "int")!["name"]!.GetValue<string>());
            Assert.Equal([15, 14, 13, 12, 10, 8], Creation(fifthProjection, "fifth-srd:standard_array")["array"]!.AsArray().Select(score => score!.GetValue<decimal>()));
        });
    }

    [Fact]
    public void GameRollPassesPointBuyScoresAndLeavesPartyUntouchedForInvalidInput()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine,
                Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "ascend"),
                Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "ascend-trial"));

            Run(session, engine, """
                {
                  "action": "roll",
                  "name": "Broken",
                  "creation": "ascend:point_buy",
                  "race": "ascend:folk",
                  "class": "ascend:adept",
                  "attributes": { "wit": "sixteen" }
                }
                """);
            Assert.Empty(session.Party);
            Assert.Contains("numeric score", Assert.Single(session.Notes), StringComparison.Ordinal);

            Run(session, engine, """
                {
                  "action": "roll",
                  "name": "Too Expensive",
                  "creation": "ascend:point_buy",
                  "race": "ascend:folk",
                  "class": "ascend:adept",
                  "attributes": { "might": 16, "grace": 16, "grit": 11, "wit": 8 }
                }
                """);
            Assert.Empty(session.Party);
            Assert.Contains(session.Notes, note => note.Contains("cost 23", StringComparison.Ordinal));

            Run(session, engine, """
                {
                  "action": "roll",
                  "name": "Point Buyer",
                  "creation": "ascend:point_buy",
                  "race": "ascend:folk",
                  "class": "ascend:adept",
                  "features": ["ascend:iron_will"],
                  "attributes": { "might": 14, "grace": 12, "grit": 10, "wit": 16 }
                }
                """);
            Assert.Contains("Rolled Point Buyer", Assert.Single(session.Notes), StringComparison.Ordinal);
            Assert.Equal(16, session.Party.Single().Attributes["wit"]);
        });
    }

    [Fact]
    public void GameRollPassesSixAttributeArrayPriorityToCore()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine,
                Path.Combine(Rules.RepositoryRoot, "modules", "fifth-srd"),
                FifthCampaign(scratch));
            Run(session, engine, """
                {
                  "action": "roll",
                  "name": "Sage",
                  "creation": "fifth-srd:standard_array",
                  "race": "fifth-srd:human",
                  "class": "fifth-srd:wizard",
                  "priority": ["int", "str", "dex", "con", "wis", "cha"],
                  "features": ["fifth-srd:sage", "fifth-srd:tough"]
                }
                """);
            Assert.Contains("Rolled Sage", Assert.Single(session.Notes), StringComparison.Ordinal);
            Assert.Equal(15, session.Party.Single().Attributes["int"]);
        });
    }

    [Fact]
    public void GameRollArrangesOnlySharedAttributesWhenCreationHasAnOwnRoll()
    {
        using TempModules scratch = new();
        string ruleset = CopyFixture(scratch, Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "ascend"), "arranged-rules");
        scratch.Write("arranged-rules/creation/standard.json", """
            {
              "type": "character-creation",
              "id": "standard",
              "name": "Own roll arrange",
              "default": true,
              "attributes": ["might", "grace", "grit", "wit"],
              "attribute_roll": "roll(1, 6) + 10",
              "attribute_rolls": { "wit": "10" },
              "assignment": "arrange",
              "starting": {
                "warrior": { "gold": "roll(4, 4) * 10" },
                "adept": { "gold": "roll(3, 4) * 10" }
              }
            }
            """);
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine, ruleset,
                Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "ascend-trial"));
            JsonObject projection = SessionProjection.Build(session);
            JsonObject creation = Creation(projection, "ascend:standard");
            Assert.Equal(["might", "grace", "grit"], creation["arrangeableAttributes"]!.AsArray().Select(attribute => attribute!["id"]!.GetValue<string>()));

            Run(session, engine, """
                {
                  "action": "roll",
                  "name": "Shared Arranger",
                  "creation": "ascend:standard",
                  "race": "ascend:folk",
                  "class": "ascend:adept",
                  "priority": ["grace", "might", "grit"],
                  "features": ["ascend:iron_will"]
                }
                """);
            Assert.Contains("Rolled Shared Arranger", Assert.Single(session.Notes), StringComparison.Ordinal);
            Dictionary<string, decimal> scores = session.Party.Single().Attributes;
            Assert.Equal(10, scores["wit"]);
            Assert.Equal(scores.Where(entry => entry.Key is "might" or "grace" or "grit").Max(entry => entry.Value), scores["grace"]);
        });
    }

    private static JsonObject Creation(JsonObject projection, string id)
    {
        return projection["creations"]!.AsArray()
            .Select(choice => choice!.AsObject())
            .Single(choice => choice["id"]!.GetValue<string>() == id);
    }

    private static GameSession OpenSession(TempModules scratch, IEngineContext engine, string ruleset, string campaign)
    {
        string rules = EngineContentTests.Pack(ruleset, scratch);
        string art = EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", "placeholder-art"), scratch);
        string campaignBundle = Path.Combine(scratch.Root, Path.GetFileName(campaign) + ".rpak");
        (int code, string output) = CampaignTests.Run(scratch, "module", "pack", campaign, "--modules", scratch.Root, "--output", campaignBundle);
        Assert.True(code == 0, output);
        string[] bundles = [rules, art, campaignBundle];
        GameSession session = new(new ModuleLibrary(_ => bundles
            .Select(path => ProductContentBundle.OpenContainer(engine.Content, path))
            .ToList()));
        session.Refresh();
        Run(session, engine, JsonSerializer.Serialize(new { action = "open", campaign = Assert.Single(session.Campaigns).Bundle, seed = "7" }));
        Assert.Equal(Screen.Party, session.Screen);
        return session;
    }

    private static string FifthCampaign(TempModules scratch)
    {
        string campaign = scratch.Module("fifth-creation-trial", "campaign",
            requires: $"{TempModules.Require("fifth-srd", "^0.1.0")}, {TempModules.Require("placeholder-art", "^0.1.0")}",
            directory: "fifth-creation-trial");
        scratch.Write("fifth-creation-trial/campaign.json", """
            {
              "type": "campaign",
              "id": "trial",
              "name": "Fifth creation trial",
              "start": { "area": "hall", "entry": "door" },
              "party": { "min": 1, "max": 4 }
            }
            """);
        scratch.Write("fifth-creation-trial/areas/hall.json", """
            {
              "type": "area",
              "id": "hall",
              "name": "Hall",
              "map": ["+--+", "|  |", "+--+"],
              "entries": { "door": { "at": [0, 0], "facing": "east" } }
            }
            """);
        return campaign;
    }

    private static string CopyFixture(TempModules scratch, string source, string directory)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            scratch.Write(Path.Combine(directory, Path.GetRelativePath(source, file)), File.ReadAllText(file));
        }

        return Path.Combine(scratch.Root, directory);
    }

    private static void Run(GameSession session, IEngineContext engine, string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        GameCommands.Run(session, engine, document.RootElement);
    }
}
