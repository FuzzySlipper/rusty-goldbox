using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class SearchTests
{
    [Fact]
    public void SearchUsesTheAreaCheckRevealsASecretAndSavesIt()
    {
        using TempModules modules = new();
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("classic", "*")}, {Require("placeholder-art", "*")}");
        modules.Write("tale/search.json", """{ "type": "check", "id": "search", "name": "Search", "roll": "1", "target": "1", "succeeds": "at-least" }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|  S  |", "+--+--+"], "search": "search", "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }""");
        ModuleSet set = ModuleLoader.Load(campaign, [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);

        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        List<ModuleDiagnostic> characterProblems = [];
        Character party = CharacterFile.Read(Path.Combine(scratch.Root, "ada.json"), set, characterProblems)!;
        Assert.Empty(characterProblems);
        Definition tale = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        Definition hall = set.Rules.Find(DefinitionTypes.Area, "tale:hall", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, tale, [party], 11);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        List<PlayFact> blocked = host.Call(engine => runner.Execute("forward", engine.Random));
        Assert.Contains(blocked, fact => fact is RefusedFact refused && refused.Reason.Contains("wall", StringComparison.Ordinal));
        List<PlayFact> facts = host.Call(engine => runner.Execute("search", engine.Random));
        SearchFact result = Assert.Single(facts.OfType<SearchFact>());
        Assert.True(result.Found);
        AreaMap playerMap = runner.PlayerMap(hall);
        Assert.Equal(Edge.Door, playerMap.EdgeOf(0, 0, Facing.East));

        string json = SaveFile.ToJson(state, set);
        List<ModuleDiagnostic> saveProblems = [];
        CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(json), "save.json", set, saveProblems)!;
        Assert.Empty(saveProblems);
        Assert.Equal(state.FoundSecrets, restored.FoundSecrets);

        JsonObject invalid = JsonNode.Parse(json)!.AsObject();
        invalid["found_secrets"] = new JsonArray("tale:hall|0,0,west");
        saveProblems = [];
        Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(invalid.ToJsonString()), "bad-save.json", set, saveProblems));
        Assert.Contains(saveProblems, problem => problem.Message.Contains("not a secret door", StringComparison.Ordinal));
    }

    [Fact]
    public void SearchCheckFailuresNameTheCheckDefinition()
    {
        using TempModules modules = new();
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("classic", "*")}, {Require("placeholder-art", "*")}");
        modules.Write("tale/search.json", """{ "type": "check", "id": "search", "name": "Search", "roll": "1", "bonus": "1 / (self.str - self.str)", "target": "1", "succeeds": "at-least" }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|  S  |", "+--+--+"], "search": "search", "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }""");
        ModuleSet set = ModuleLoader.Load(campaign, [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);

        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        List<ModuleDiagnostic> characterProblems = [];
        Character party = CharacterFile.Read(Path.Combine(scratch.Root, "ada.json"), set, characterProblems)!;
        Assert.Empty(characterProblems);
        Definition tale = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, tale, [party], 11);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        RuleFailure failure = Assert.Throws<RuleFailure>(() => host.Call(engine => runner.Execute("search", engine.Random)));
        Assert.Equal("event.evaluate", failure.Diagnostic.Rule);
        Assert.Equal("tale", failure.Diagnostic.Module);
        Assert.Equal(Path.Combine(modules.Root, "tale/search.json"), failure.Diagnostic.File);
        Assert.Equal("$", failure.Diagnostic.JsonPath);
    }
}
