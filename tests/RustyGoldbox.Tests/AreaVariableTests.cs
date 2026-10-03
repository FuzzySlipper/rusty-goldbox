using System.Text;
using System.Text.Json;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class AreaVariableTests
{
    [Fact]
    public void AreaVariablesAreSeparateAndRoundTripInTheFormatOneSave()
    {
        using TempModules modules = new();
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("classic", "*")}, {Require("placeholder-art", "*")}" );
        modules.Write("tale/flag.json", """{ "type": "variable", "id": "opened", "value_type": "boolean", "scope": "area", "initial": "false" }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/yard.json", """{ "type": "area", "id": "yard", "name": "Yard", "map": ["+--+", "|  |", "+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }""");

        ModuleSet set = ModuleLoader.Load(campaign, [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition tale = rules.Find(DefinitionTypes.Campaign, "tale", out _)!;
        Definition hall = rules.Find(DefinitionTypes.Area, "tale:hall", out _)!;
        Definition yard = rules.Find(DefinitionTypes.Area, "tale:yard", out _)!;
        CampaignState state = CampaignRunner.NewState(rules, tale, [], 9);
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        List<ModuleDiagnostic> characterProblems = [];
        state.Party.Add(CharacterFile.Read(Path.Combine(scratch.Root, "ada.json"), set, characterProblems)!);
        Assert.Empty(characterProblems);
        state.ValuesFor(hall)["opened"] = Value.Of(true);
        Assert.False(state.ValuesFor(yard)["opened"].Boolean);

        string json = SaveFile.ToJson(state, set);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("variables").GetProperty("campaign").ValueKind);
        Assert.True(document.RootElement.GetProperty("variables").GetProperty("areas").GetProperty("tale:hall").GetProperty("opened").GetBoolean());
        Assert.False(document.RootElement.GetProperty("variables").GetProperty("areas").GetProperty("tale:yard").GetProperty("opened").GetBoolean());

        List<ModuleDiagnostic> problems = [];
        CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(json), "save.json", set, problems)!;
        Assert.Empty(problems);
        Assert.True(restored.ValuesFor(hall)["opened"].Boolean);
        Assert.False(restored.ValuesFor(yard)["opened"].Boolean);
    }

    [Fact]
    public void AreaExpressionsMustNameAreaScopedVariables()
    {
        using TempModules modules = new();
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("classic", "*")}, {Require("placeholder-art", "*")}" );
        modules.Write("tale/flag.json", """{ "type": "variable", "id": "opened", "value_type": "boolean", "initial": "false" }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "cells": [{ "at": [0, 0], "event": "gate" }], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "gate" }""");
        modules.Write("tale/gate.json", """{ "type": "event", "id": "gate", "kind": "menu", "text": "Gate", "options": [{ "label": "Open", "when": "area.var.opened" }, { "label": "Leave" }] }""");

        ModuleSet set = ModuleLoader.Load(campaign, [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.Rule == "expression.type" && diagnostic.Message.Contains("not a declared area variable", StringComparison.Ordinal));
    }
}
