using System.Text;
using System.Text.Json;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class OriginalRulesetExplorationTests
{
    [Fact]
    public void PercentileFixtureSupportsAreaVariablesSecretSearchAndLockedDoors()
    {
        using TempModules modules = new();
        modules.Module("art", "assets");
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("percentile", "*")}, {Require("art", "*")}");
        modules.Write("tale/opened.json", """{ "type": "variable", "id": "opened", "value_type": "boolean", "scope": "area", "initial": "false" }""");
        modules.Write("tale/search.json", """{ "type": "check", "id": "search", "name": "Search", "roll": "1", "target": "1", "succeeds": "at-least" }""");
        modules.Write("tale/pick.json", """{ "type": "check", "id": "pick", "name": "Pick", "roll": "1", "target": "1", "succeeds": "at-least" }""");
        modules.Write("tale/hall.json", """
            {
              "type": "area", "id": "hall", "name": "Hall",
              "map": ["+--+--+--+", "|  S  D  |", "+--+--+--+"],
              "search": "search",
              "doors": [{ "id": "gate", "at": [1, 0], "facing": "east", "pick": "pick" }],
              "cells": [{ "at": [1, 0], "event": "mark" }],
              "entries": { "in": { "at": [0, 0], "facing": "east" } }
            }
            """);
        modules.Write("tale/mark.json", """{ "type": "event", "id": "mark", "kind": "set", "variable": "opened", "value": "true", "next": "check_opened" }""");
        modules.Write("tale/check_opened.json", """{ "type": "event", "id": "check_opened", "kind": "branch", "branches": [{ "when": "area.var.opened", "next": "open_text" }], "otherwise": "closed_text" }""");
        modules.Write("tale/open_text.json", """{ "type": "event", "id": "open_text", "kind": "text", "text": "The area is open." }""");
        modules.Write("tale/closed_text.json", """{ "type": "event", "id": "closed_text", "kind": "text", "text": "The area is closed." }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 1 } }""");

        string fixtureRoot = Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, fixtureRoot]);
        Assert.Empty(set.Diagnostics);

        using TempModules scratch = new();
        string percentile = Path.Combine(fixtureRoot, "percentile");
        (int code, string output) = CampaignTests.Run(scratch, "character", "new", "--module", percentile,
            "--class", "soldier", "--race", "human", "--name", "Scout", "--attributes", "body=18,agility=18,mind=18", "--seed", "2", "--out", "scout.json");
        Assert.True(code == 0, output);
        List<ModuleDiagnostic> characterProblems = [];
        Character party = CharacterFile.Read(Path.Combine(scratch.Root, "scout.json"), set, characterProblems)!;
        Assert.Empty(characterProblems);

        RuleSet rules = set.Rules!;
        Definition tale = rules.Find(DefinitionTypes.Campaign, "tale", out _)!;
        Definition hall = rules.Find(DefinitionTypes.Area, "tale:hall", out _)!;
        CampaignState state = CampaignRunner.NewState(rules, tale, [party], 7);
        CampaignRunner runner = new(rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        List<PlayFact> facts = host.Call(engine => runner.Execute("forward", engine.Random));
        Assert.Contains(facts, fact => fact is RefusedFact refused && refused.Reason.Contains("wall", StringComparison.Ordinal));

        facts = host.Call(engine => runner.Execute("search", engine.Random));
        Assert.Contains(facts, fact => fact is SearchFact { Found: true });
        Assert.Equal(Edge.Door, runner.PlayerMap(hall).EdgeOf(0, 0, Facing.East));

        facts = host.Call(engine => runner.Execute("forward", engine.Random));
        Assert.Contains(facts, fact => fact is VariableFact { Variable: "opened", Value.Boolean: true });
        Assert.Contains(facts, fact => fact is TextFact { Text: "The area is open." });
        Assert.True(state.ValuesFor(hall)["opened"].Boolean);

        facts = host.Call(engine => runner.Execute("pick", engine.Random));
        Assert.Contains(facts, fact => fact is DoorFact { Method: "pick", Opened: true });
        Assert.Equal(Edge.Open, runner.PlayerMap(hall).EdgeOf(1, 0, Facing.East));
        Assert.Contains(host.Call(engine => runner.Execute("forward", engine.Random)), fact => fact is MovedFact { X: 2, Y: 0 });

        string json = SaveFile.ToJson(state, set);
        using JsonDocument save = JsonDocument.Parse(json);
        Assert.Equal(1, save.RootElement.GetProperty("format").GetInt32());
        Assert.True(save.RootElement.GetProperty("variables").GetProperty("areas").GetProperty("tale:hall").GetProperty("opened").GetBoolean());
        List<ModuleDiagnostic> saveProblems = [];
        CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(json), "save.json", set, saveProblems)!;
        Assert.Empty(saveProblems);
        Assert.True(restored.ValuesFor(hall)["opened"].Boolean);
        Assert.Equal(state.FoundSecrets, restored.FoundSecrets);
        Assert.Equal(state.OpenedDoors, restored.OpenedDoors);
    }
}
