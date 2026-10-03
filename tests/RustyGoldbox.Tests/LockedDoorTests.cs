using System.Text;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class LockedDoorTests
{
    [Fact]
    public void LockedDoorNeedsARealDoorEdgeAndAnOpeningMechanism()
    {
        using TempModules modules = new();
        string campaign = modules.Module("tale", "campaign", requires: Require("classic", "*"));
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|     |", "+--+--+"], "doors": [{ "id": "gate", "at": [0, 0], "facing": "east" }], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 1 } }""");

        ModuleSet set = ModuleLoader.Load(campaign, [Path.Combine(Rules.RepositoryRoot, "modules")]);

        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.Rule == "area.door" && diagnostic.Message.Contains("locked door needs", StringComparison.Ordinal));
    }

    [Fact]
    public void KeyChecksAndEventsOpenDoorsAndPersist()
    {
        using TempModules modules = new();
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("classic", "*")}, {Require("placeholder-art", "*")}");
        modules.Write("tale/key.json", """{ "type": "item", "id": "key", "name": "Iron key", "kind": "key", "currency": "classic:gold", "cost": 1, "weight": 0 }""");
        modules.Write("tale/pick_lock.json", """{ "type": "check", "id": "pick_lock", "name": "Pick lock", "roll": "20", "target": "1", "succeeds": "at-least" }""");
        modules.Write("tale/force_door.json", """{ "type": "check", "id": "force_door", "name": "Force door", "roll": "20", "target": "1", "succeeds": "at-least" }""");
        modules.Write("tale/unlock_event.json", """{ "type": "event", "id": "unlock_event", "kind": "open", "door": "event_gate" }""");
        modules.Write("tale/hall.json", """
            {
              "type": "area", "id": "hall", "name": "Hall",
              "map": ["+--+--+--+--+--+", "|  D  D  D  D  |", "+--+--+--+--+--+"],
              "doors": [
                { "id": "key_gate", "at": [0, 0], "facing": "east", "key": "key" },
                { "id": "pick_gate", "at": [1, 0], "facing": "east", "pick": "pick_lock" },
                { "id": "force_gate", "at": [2, 0], "facing": "east", "force": "force_door" },
                { "id": "event_gate", "at": [3, 0], "facing": "east", "event": "unlock_event" }
              ],
              "entries": { "in": { "at": [0, 0], "facing": "east" } }
            }
            """);
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
        Definition key = set.Rules.Find(DefinitionTypes.Item, "tale:key", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, tale, [party], 17);
        state.Inventory.Add(key);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        List<PlayFact> facts = host.Call(engine => runner.Execute("open", engine.Random));
        Assert.Contains(facts, fact => fact is DoorFact { Method: "key", Opened: true });
        Assert.Equal(Edge.Open, runner.PlayerMap(hall).EdgeOf(0, 0, Facing.East));
        host.Call(engine => runner.Execute("forward", engine.Random));

        facts = host.Call(engine => runner.Execute("pick east", engine.Random));
        Assert.Contains(facts, fact => fact is DoorFact { Method: "pick", Opened: true });
        host.Call(engine => runner.Execute("forward", engine.Random));

        facts = host.Call(engine => runner.Execute("open east", engine.Random));
        Assert.Contains(facts, fact => fact is RefusedFact refused && refused.Reason.Contains("force east", StringComparison.Ordinal));
        facts = host.Call(engine => runner.Execute("force", engine.Random));
        Assert.Contains(facts, fact => fact is DoorFact { Method: "force", Opened: true });
        host.Call(engine => runner.Execute("forward", engine.Random));

        facts = host.Call(engine => runner.Execute("open", engine.Random));
        Assert.Contains(facts, fact => fact is DoorFact { Method: "event", Opened: true });
        Assert.Equal(4, state.OpenedDoors.Count);
        Assert.Equal(Edge.Open, runner.PlayerMap(hall).EdgeOf(3, 0, Facing.East));

        string json = SaveFile.ToJson(state, set);
        List<ModuleDiagnostic> saveProblems = [];
        CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(json), "save.json", set, saveProblems)!;
        Assert.Empty(saveProblems);
        Assert.Equal(state.OpenedDoors, restored.OpenedDoors);
    }

    [Fact]
    public void DoorCheckFailuresNameTheCheckDefinition()
    {
        using TempModules modules = new();
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("classic", "*")}, {Require("placeholder-art", "*")}");
        modules.Write("tale/pick_lock.json", """{ "type": "check", "id": "pick_lock", "name": "Pick lock", "roll": "1", "bonus": "1 / (self.str - self.str)", "target": "1", "succeeds": "at-least" }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|  D  |", "+--+--+"], "doors": [{ "id": "gate", "at": [0, 0], "facing": "east", "pick": "pick_lock" }], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }""");
        ModuleSet set = ModuleLoader.Load(campaign, [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);

        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        List<ModuleDiagnostic> characterProblems = [];
        Character party = CharacterFile.Read(Path.Combine(scratch.Root, "ada.json"), set, characterProblems)!;
        Assert.Empty(characterProblems);
        Definition tale = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, tale, [party], 19);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        RuleFailure failure = Assert.Throws<RuleFailure>(() => host.Call(engine => runner.Execute("pick", engine.Random)));
        Assert.Equal("event.evaluate", failure.Diagnostic.Rule);
        Assert.Equal("tale", failure.Diagnostic.Module);
        Assert.Equal(Path.Combine(modules.Root, "tale/pick_lock.json"), failure.Diagnostic.File);
        Assert.Equal("$", failure.Diagnostic.JsonPath);
    }

    [Fact]
    public void EventDoorTeleportDoesNotContinueTheOriginalMove()
    {
        using TempModules modules = new();
        string campaign = EventDoorCampaign(modules);
        modules.Write("tale/unlock_event.json", """{ "type": "event", "id": "unlock_event", "kind": "open", "door": "gate", "next": "warp" }""");
        modules.Write("tale/warp.json", """{ "type": "event", "id": "warp", "kind": "teleport", "area": "yard", "entry": "in" }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|  D  |", "+--+--+"], "doors": [{ "id": "gate", "at": [0, 0], "facing": "east", "event": "unlock_event" }], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/yard.json", """{ "type": "area", "id": "yard", "name": "Yard", "map": ["+--+--+", "|     |", "+--+--+"], "cells": [{ "at": [1, 0], "event": "arrived" }], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/arrived.json", """{ "type": "event", "id": "arrived", "kind": "text", "text": "The yard cell fires." }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }""");

        (ModuleSet set, Character party) = LoadEventDoorParty(modules, campaign);
        RuleSet rules = set.Rules!;
        Definition tale = rules.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(rules, tale, [party], 23);
        CampaignRunner runner = new(rules, state);
        Definition hall = rules.Find(DefinitionTypes.Area, "tale:hall", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        List<PlayFact> facts = host.Call(engine => runner.Execute("forward", engine.Random));
        Assert.Contains(facts, fact => fact is ArrivedFact { Area: "Yard", X: 0, Y: 0 });
        Assert.DoesNotContain(facts, fact => fact is MovedFact);
        Assert.DoesNotContain(facts, fact => fact is TextFact { Text: "The yard cell fires." });
        Assert.Equal("tale:yard", state.Area.QualifiedId);
        Assert.Equal((0, 0), (state.X, state.Y));
        Assert.Contains(state.OpenedDoors, key => key == state.EdgeKey(hall, new AreaEdge(0, 0, Facing.East)));
    }

    [Fact]
    public void EventDoorEndDoesNotContinueTheOriginalMove()
    {
        using TempModules modules = new();
        string campaign = EventDoorCampaign(modules);
        modules.Write("tale/unlock_event.json", """{ "type": "event", "id": "unlock_event", "kind": "open", "door": "gate", "next": "finish" }""");
        modules.Write("tale/finish.json", """{ "type": "event", "id": "finish", "kind": "end", "text": "The gate closes behind you." }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|  D  |", "+--+--+"], "doors": [{ "id": "gate", "at": [0, 0], "facing": "east", "event": "unlock_event" }], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }""");

        (ModuleSet set, Character party) = LoadEventDoorParty(modules, campaign);
        RuleSet rules = set.Rules!;
        Definition tale = rules.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(rules, tale, [party], 29);
        CampaignRunner runner = new(rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        List<PlayFact> facts = host.Call(engine => runner.Execute("forward", engine.Random));
        Assert.Contains(facts, fact => fact is EndedFact { Text: "The gate closes behind you." });
        Assert.DoesNotContain(facts, fact => fact is MovedFact);
        Assert.True(state.Ended);
        Assert.Equal((0, 0), (state.X, state.Y));
    }

    [Fact]
    public void EventDoorMenuDoesNotContinueTheOriginalMove()
    {
        using TempModules modules = new();
        string campaign = EventDoorCampaign(modules);
        modules.Write("tale/unlock_event.json", """{ "type": "event", "id": "unlock_event", "kind": "open", "door": "gate", "next": "choice" }""");
        modules.Write("tale/choice.json", """{ "type": "event", "id": "choice", "kind": "menu", "text": "Continue?", "options": [{ "label": "Yes" }] }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|  D  |", "+--+--+"], "doors": [{ "id": "gate", "at": [0, 0], "facing": "east", "event": "unlock_event" }], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }""");

        (ModuleSet set, Character party) = LoadEventDoorParty(modules, campaign);
        RuleSet rules = set.Rules!;
        Definition tale = rules.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(rules, tale, [party], 31);
        CampaignRunner runner = new(rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        List<PlayFact> facts = host.Call(engine => runner.Execute("forward", engine.Random));
        Assert.Contains(facts, fact => fact is MenuFact { Text: "Continue?" });
        Assert.DoesNotContain(facts, fact => fact is MovedFact);
        Assert.NotNull(state.PendingMenu);
        Assert.Equal((0, 0), (state.X, state.Y));
    }

    private static string EventDoorCampaign(TempModules modules)
    {
        modules.Module("art", "assets");
        return modules.Module("tale", "campaign", requires: $"{Require("classic", "*")}, {Require("art", "*")}");
    }

    private static (ModuleSet Set, Character Party) LoadEventDoorParty(TempModules modules, string campaign)
    {
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        List<ModuleDiagnostic> characterProblems = [];
        Character party = CharacterFile.Read(Path.Combine(scratch.Root, "ada.json"), set, characterProblems)!;
        Assert.Empty(characterProblems);
        return (set, party);
    }
}
