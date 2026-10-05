using System.Text;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class ConsumableItemTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseConsumesOneRealCopyAndSurvivesSaveLoadForBothRulesetShapes(bool fifthSrd)
    {
        using TempModules modules = new();
        (string campaign, ModuleSet set, List<Character> party, Definition item) = fifthSrd
            ? FifthFixture(modules)
            : OriginalFixture(modules);
        Definition hitPoints = set.Rules!.Find(DefinitionTypes.Track, "hit_points", out _)!;
        party[0].Tracks[hitPoints.Id].Current = 0;
        CampaignState state = CampaignRunner.NewState(
            set.Rules,
            set.Rules.Find(DefinitionTypes.Campaign, fifthSrd ? "camp" : "tale", out _)!,
            party,
            7);
        state.Inventory.Add(item);
        state.Inventory.Add(item);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            List<PlayFact> facts = runner.Execute($"use 1 {item.QualifiedId}", engine.Random);
            Assert.Single(facts.OfType<ItemUseFact>());
            SceneHealFact healing = Assert.Single(facts.OfType<SceneHealFact>());
            Assert.Equal(1, healing.Amount);
            Assert.Single(state.Inventory);
            Assert.Equal(1, party[0].Tracks[hitPoints.Id].Current);

            List<ModuleDiagnostic> saveProblems = [];
            CampaignState restored = SaveFile.Read(
                Encoding.UTF8.GetBytes(SaveFile.ToJson(state, set)),
                "consumable-save.json",
                set,
                saveProblems)!;
            Assert.Empty(saveProblems);
            Assert.Single(restored.Inventory);
            CampaignRunner resumed = new(set.Rules, restored);
            List<PlayFact> second = resumed.Execute($"use 1 {item.QualifiedId}", engine.Random);
            Assert.Single(second.OfType<ItemUseFact>());
            Assert.Empty(restored.Inventory);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionUsePersistsAbsoluteExpiryAndRestEndsIt(bool fifthSrd)
    {
        using TempModules modules = new();
        (string campaign, ModuleSet set, List<Character> party, Definition item) = fifthSrd
            ? FifthFixture(modules, timed: true)
            : OriginalFixture(modules, timed: true);
        Definition condition = set.Rules!.Find(DefinitionTypes.Condition, fifthSrd ? "prone" : "ready", out _)!;
        CampaignState state = CampaignRunner.NewState(
            set.Rules,
            set.Rules.Find(DefinitionTypes.Campaign, fifthSrd ? "camp" : "tale", out _)!,
            party,
            11);
        state.Inventory.Add(item);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            List<PlayFact> used = runner.Execute($"use 1 {item.QualifiedId}", engine.Random);
            Assert.Contains(used, fact => fact is SceneConditionFact { Applied: true });
            Assert.Contains(condition, party[0].Conditions);
            Assert.Equal(1, party[0].ConditionExpiryDays[condition]);

            List<ModuleDiagnostic> saveProblems = [];
            CampaignState restored = SaveFile.Read(
                Encoding.UTF8.GetBytes(SaveFile.ToJson(state, set)),
                "timed-consumable-save.json",
                set,
                saveProblems)!;
            Assert.Empty(saveProblems);
            Assert.Equal(1, restored.Party[0].ConditionExpiryDays[condition]);

            CampaignRunner resumed = new(set.Rules, restored);
            List<PlayFact> rested = resumed.Execute("forward", engine.Random);
            Assert.Equal(1, resumed.State.ElapsedDays);
            Assert.DoesNotContain(condition, resumed.State.Party[0].Conditions);
            Assert.Contains(rested, fact => fact is SceneConditionFact { Applied: false, Condition: "Ready" or "Prone" });
        });
    }

    [Fact]
    public void PaidTempleRemovalPrunesConsumableExpiryBeforeSaveLoad()
    {
        using TempModules modules = new();
        modules.Write("tale/shrine.json", """
            { "type": "event", "id": "shrine", "kind": "temple", "text": "Welcome.",
              "services": [{ "label": "Cure", "cost": "1", "currency": "rules:gold",
                "operations": [{ "op": "remove_condition", "condition": "rules:ready" }] }] }
            """);
        (string campaign, ModuleSet set, List<Character> party, Definition item) = OriginalFixture(modules, timed: true);
        Definition condition = set.Rules!.Find(DefinitionTypes.Condition, "rules:ready", out _)!;
        Definition temple = set.Rules.Find(DefinitionTypes.Event, "tale:shrine", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, set.Rules.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 29);
        state.Inventory.Add(item);
        party[0].Balances["gold"] = 1;
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            Assert.Contains(runner.Execute("use 1 rules:draught", engine.Random), fact => fact is SceneConditionFact { Applied: true });
            Assert.Equal(1, party[0].ConditionExpiryDays[condition]);

            state.PendingTemple = temple;
            List<PlayFact> facts = runner.Execute("serve 1 1", engine.Random);
            Assert.Contains(facts, fact => fact is TextFact { Text: "A receives Cure for 1 gold." });
            Assert.DoesNotContain(condition, party[0].Conditions);
            Assert.DoesNotContain(condition, party[0].ConditionExpiryDays.Keys);

            List<ModuleDiagnostic> problems = [];
            CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(state, set)), "temple-expiry-save.json", set, problems)!;
            Assert.Empty(problems);
            Assert.DoesNotContain(condition, restored.Party[0].Conditions);
            Assert.DoesNotContain(condition, restored.Party[0].ConditionExpiryDays.Keys);
        });
    }

    [Fact]
    public void CombatConditionRemovalPrunesConsumableExpiryBeforeSaveLoad()
    {
        using TempModules modules = new();
        string campaign = LiveCampaignCombatTests.CampaignFixture(modules);
        modules.Write("tale/campaign.json", """
            { "type": "campaign", "id": "tale", "name": "Live combat tale", "start": { "area": "hall", "entry": "in" },
              "party": { "min": 2, "max": 2 } }
            """);
        modules.Write("tale/hall.json", """
            { "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+--+--+", "|           |", "+--+--+--+--+"],
              "cells": [{ "at": [1, 0], "event": "fight" }], "entries": { "in": { "at": [0, 0], "facing": "east" } } }
            """);
        modules.Write("tale/fight.json", """
            { "type": "event", "id": "fight", "kind": "combat", "encounter": "tale:dummy_encounter",
              "combat": "tale:cleanse_combat", "party_start": [0, 0], "monsters_start": [1, 0],
              "on_win": "done", "on_lose": "done", "on_draw": "done" }
            """);
        modules.Write("tale/cleanse_combat.json", """
            { "type": "combat", "id": "cleanse_combat", "name": "Cleanse combat", "initiative": "self.str",
              "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "round", "round_seconds": 60,
              "budget": [{ "id": "action", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 }],
              "field": { "width": 10, "height": 6, "terrain": { "#": { "name": "Pillar", "passable": false, "blocks_sight": true } } },
              "track": "classic:hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("tale/fight2.json", """
            { "type": "event", "id": "fight2", "kind": "combat", "encounter": "tale:dummy_encounter", "combat": "tale:cleanse_combat",
              "party_start": [0, 0], "monsters_start": [1, 0], "on_win": "done", "on_lose": "done", "on_draw": "done" }
            """);
        modules.Write("tale/draught.json", """
            { "type": "item", "id": "draught", "name": "Draught", "kind": "consumable", "cost": 1, "currency": "classic:gold", "weight": 1,
              "use": { "duration_days": "20", "operations": [{ "op": "apply_condition", "condition": "tale:combat_cleanse" }] } }
            """);
        modules.Write("tale/cleanse.json", """
            { "type": "action", "id": "cleanse", "name": "Cleanse", "cost": { "action": 1 }, "target": "enemy",
              "always": [{ "op": "remove_condition", "condition": "tale:combat_cleanse" }] }
            """);
        modules.Write("tale/dummy.json", """
            { "type": "monster", "id": "dummy", "name": "Dummy", "class": "classic:fighter", "level": 1,
              "tracks": { "classic:hit_points": "10" }, "stats": { "ac": "20", "movement": "120", "str": "100", "size": "'medium'" },
              "actions": [{ "action": "tale:cleanse" }], "xp": 10 }
            """);
        modules.Write("tale/combat_cleanse.json", """
            { "type": "condition", "id": "combat_cleanse", "name": "Combat cleanse", "modifiers": [] }
            """);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        CampaignTests.WriteParty(modules, Rules.ClassicPath);
        List<Character> party = [];
        foreach (string name in new[] { "ada", "brom" })
        {
            List<ModuleDiagnostic> problems = [];
            party.Add(CharacterFile.Read(Path.Combine(modules.Root, $"{name}.json"), set, problems)!);
            Assert.Empty(problems);
        }

        Definition condition = set.Rules!.Find(DefinitionTypes.Condition, "tale:combat_cleanse", out _)!;
        Definition item = set.Rules.Find(DefinitionTypes.Item, "tale:draught", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, set.Rules.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 31);
        state.Inventory.Add(item);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            Assert.Contains(runner.Execute("use 1 tale:draught", engine.Random), fact => fact is SceneConditionFact { Applied: true });
            Assert.Equal(20, party[0].ConditionExpiryDays[condition]);

            List<PlayFact> facts = runner.Execute("forward", engine.Random);
            Assert.Contains(facts, fact => fact is FightFact);
            Assert.DoesNotContain(condition, party[0].Conditions);
            Assert.DoesNotContain(condition, party[0].ConditionExpiryDays.Keys);

            List<ModuleDiagnostic> problems = [];
            CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(state, set)), "combat-expiry-save.json", set, problems)!;
            Assert.Empty(problems);
            Assert.DoesNotContain(condition, restored.Party[0].Conditions);
            Assert.DoesNotContain(condition, restored.Party[0].ConditionExpiryDays.Keys);
        });
    }

    [Fact]
    public void UseRefusesInvalidMemberMissingItemEquippedItemMenuCombatAndEndedWithoutConsuming()
    {
        using TempModules modules = new();
        (string campaign, ModuleSet set, List<Character> party, Definition item) = FifthFixture(modules);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "camp", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 13);
        state.Inventory.Add(item);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            AssertRefused(runner.Execute($"use 0 {item.QualifiedId}", engine.Random), "party member");
            Assert.Single(state.Inventory);
            AssertRefused(runner.Execute("use 1 camp:missing", engine.Random), "missing");
            Assert.Single(state.Inventory);

            Definition tool = set.Rules.Find(DefinitionTypes.Item, "camp:tool", out _)
                ?? set.Rules.Find(DefinitionTypes.Item, "fifth-srd:dagger", out _)!;
            party[0].Equipment.Add(tool);
            AssertRefused(runner.Execute($"use 1 {tool.QualifiedId}", engine.Random), "inventory");
            Assert.Single(party[0].Equipment);

            Definition wait = set.Rules.Find(DefinitionTypes.Event, "camp:wait", out _)!;
            state.PendingMenu = wait;
            AssertRefused(runner.Execute($"use 1 {item.QualifiedId}", engine.Random), "choose");
            state.PendingMenu = null;

            state.PendingCombat = new PendingCombatState
            {
                Event = wait,
                Encounter = set.Rules.Find(DefinitionTypes.Encounter, "fifth-srd:goblin_ambush", out _)!,
                Combat = set.Rules.Find(DefinitionTypes.Combat, "fifth-srd:standard", out _)!,
                Continuation = new CombatContinuationState(),
            };
            AssertRefused(runner.Execute($"use 1 {item.QualifiedId}", engine.Random), "combat");
            state.PendingCombat = null;

            state.Ended = true;
            AssertRefused(runner.Execute($"use 1 {item.QualifiedId}", engine.Random), "ended");
            Assert.Single(state.Inventory);
        });
    }

    [Fact]
    public void BadConsumableUseIsLocatedAndSchemaAndHelpDescribeTheContract()
    {
        using TempModules modules = new();
        string campaign = OriginalFixture(modules).Campaign;
        modules.Write("rules/bad_draught.json", """
            { "type": "item", "id": "bad_draught", "name": "Bad draught", "kind": "consumable", "cost": 1, "currency": "gold", "weight": 1,
              "use": { "duration_days": "1", "operations": [
                { "op": "apply_condition", "condition": "rules:ready", "rounds": 1, "values": { "amount": 2 } }
              ] } }
            """);
        ModuleSet invalid = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Contains(invalid.Diagnostics, problem => problem.Rule == "item.use" && problem.JsonPath == "$.use.operations[0].rounds");
        Assert.Contains(invalid.Diagnostics, problem => problem.Rule == "item.use" && problem.JsonPath == "$.use.operations[0].values");
        Assert.DoesNotContain(invalid.Diagnostics, problem => problem.Rule == "item.use" && problem.JsonPath == "$.use.duration_days");

        (int schemaCode, string schemaText) = CampaignTests.Run(modules, "schema", "item", "--json");
        Assert.Equal(0, schemaCode);
        using JsonDocument schema = JsonDocument.Parse(schemaText);
        Assert.Contains(schema.RootElement.GetProperty("fields").EnumerateArray(), field => field.GetProperty("name").GetString() == "use");
        (int helpCode, string helpText) = CampaignTests.Run(modules, "help");
        Assert.Equal(0, helpCode);
        Assert.Contains("use <member> <item-id>", helpText, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeConsumableExpressionFailureNamesUsePath()
    {
        using TempModules modules = new();
        string campaign = OriginalFixture(modules).Campaign;
        modules.Write("rules/divisor.json", """{ "type": "variable", "id": "divisor", "value_type": "number", "initial": "0" }""");
        modules.Write("rules/runtime_bad.json", """
            { "type": "item", "id": "runtime_bad", "name": "Runtime bad", "kind": "consumable", "cost": 1, "currency": "gold", "weight": 1,
              "use": { "operations": [{ "op": "heal", "track": "rules:hit_points", "amount": "1 / campaign.var.divisor" }] } }
            """);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = ShopTests.Party(modules, campaign, set);
        Definition item = set.Rules!.Find(DefinitionTypes.Item, "runtime_bad", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, set.Rules.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 17);
        state.Inventory.Add(item);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            RuleFailure failure = Assert.Throws<RuleFailure>(() => runner.Execute("use 1 rules:runtime_bad", engine.Random));
            Assert.Equal("$.use.operations[0].amount", failure.Diagnostic.JsonPath);
            Assert.Equal(Path.Combine(modules.Root, "rules/runtime_bad.json"), failure.Diagnostic.File);
        });
    }

    [Fact]
    public void NegativeConsumableDurationFailsAtItsPathBeforeConsuming()
    {
        using TempModules modules = new();
        string campaign = OriginalFixture(modules).Campaign;
        modules.Write("rules/negative_duration.json", """
            { "type": "item", "id": "negative_duration", "name": "Negative duration", "kind": "consumable", "cost": 1, "currency": "gold", "weight": 1,
              "use": { "duration_days": "-1", "operations": [{ "op": "apply_condition", "condition": "rules:ready" }] } }
            """);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = ShopTests.Party(modules, campaign, set);
        Definition item = set.Rules!.Find(DefinitionTypes.Item, "rules:negative_duration", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, set.Rules.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 19);
        state.Inventory.Add(item);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            RuleFailure failure = Assert.Throws<RuleFailure>(() => runner.Execute("use 1 rules:negative_duration", engine.Random));
            Assert.Equal("$.use.duration_days", failure.Diagnostic.JsonPath);
            Assert.Single(state.Inventory);
            Assert.Empty(party[0].Conditions);
        });
    }

    [Fact]
    public void CliPlayUsesADeclaredConsumableAndReportsItsEffect()
    {
        using TempModules modules = new();
        string campaign = OriginalFixture(modules).Campaign;
        modules.Write("tale/campaign.json", """
            { "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "give" }
            """);
        modules.Write("tale/give.json", """{ "type": "event", "id": "give", "kind": "give", "item": "rules:draught", "next": "ready" }""");
        modules.Write("tale/ready.json", """{ "type": "event", "id": "ready", "kind": "text", "text": "Ready." }""");
        ShopTests.Party(modules, campaign, ModuleLoader.Load(campaign, [modules.Root]));
        modules.Write("use.script", "use 1 rules:draught\n");

        (int code, string output) = CampaignTests.Run(
            modules,
            "play",
            "--campaign", campaign,
            "--modules", ".",
            "--party", "a.json,b.json",
            "--seed", "3",
            "--script", "use.script",
            "--json");
        Assert.Equal(0, code);
        using JsonDocument transcript = JsonDocument.Parse(output);
        JsonElement used = transcript.RootElement.GetProperty("transcript")
            .EnumerateArray()
            .SelectMany(step => step.GetProperty("facts").EnumerateArray())
            .Single(fact => fact.GetProperty("kind").GetString() == "used");
        Assert.Equal("rules:draught", used.GetProperty("used").GetProperty("item").GetString());
    }

    private static void AssertRefused(IEnumerable<PlayFact> facts, string expected)
    {
        RefusedFact refusal = Assert.Single(facts.OfType<RefusedFact>());
        Assert.Contains(expected, refusal.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Campaign, ModuleSet Set, List<Character> Party, Definition Item) OriginalFixture(TempModules modules, bool timed = false)
    {
        string campaign = ShopTests.Fixture(modules);
        modules.Write("tale/campaign.json", """
            { "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }
            """);
        modules.Write("rules/draught.json", $$"""
            { "type": "item", "id": "draught", "name": "Draught", "kind": "consumable", "cost": 2, "currency": "gold", "weight": 1,
              "use": { {{(timed ? "\"duration_days\": \"1\", " : "")}}"operations": [
                {{(timed ? """{ "op": "apply_condition", "condition": "rules:ready" }""" : """{ "op": "heal", "track": "rules:hit_points", "amount": "1" }""")}}
              ] } }
            """);
        if (timed)
        {
            modules.Write("rules/slow.json", """{ "type": "resting", "id": "slow", "unit": "days", "restore": [] }""");
            modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|     |", "+--+--+"], "cells": [{ "at": [1, 0], "event": "rest" }], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
            modules.Write("tale/rest.json", """{ "type": "event", "id": "rest", "kind": "rest", "text": "The party rests.", "tracks": ["rules:hit_points"], "resting": "rules:slow", "periods": 1, "next": "farewell" }""");
            modules.Write("tale/farewell.json", """{ "type": "event", "id": "farewell", "kind": "text", "text": "Morning." }""");
        }

        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = ShopTests.Party(modules, campaign, set);
        return (campaign, set, party, set.Rules!.Find(DefinitionTypes.Item, "rules:draught", out _)!);
    }

    private static (string Campaign, ModuleSet Set, List<Character> Party, Definition Item) FifthFixture(TempModules modules, bool timed = false)
    {
        string campaign = modules.Module("camp", "campaign", requires: $"{Require("fifth-srd", "*")}, {Require("placeholder-art", "*")}");
        modules.Write("camp/campaign.json", """
            { "type": "campaign", "id": "camp", "name": "Camp", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }
            """);
        modules.Write("camp/wait.json", """{ "type": "event", "id": "wait", "kind": "menu", "text": "Wait?", "options": [{ "label": "Stay" }] }""");
        modules.Write("camp/hall.json", timed
            ? """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|     |", "+--+--+"], "cells": [{ "at": [1, 0], "event": "rest" }], "entries": { "in": { "at": [0, 0], "facing": "east" } } }"""
            : """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|     |", "+--+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("camp/draught.json", $$"""
            { "type": "item", "id": "draught", "name": "Draught", "kind": "consumable", "cost": 2, "currency": "fifth-srd:gold", "weight": 1,
              "use": { {{(timed ? "\"duration_days\": \"1\", " : "")}}"operations": [
                {{(timed ? """{ "op": "apply_condition", "condition": "fifth-srd:prone" }""" : """{ "op": "heal", "track": "fifth-srd:hit_points", "amount": "1" }""")}}
              ] } }
            """);
        if (timed)
        {
            modules.Write("camp/slow.json", """{ "type": "resting", "id": "slow", "unit": "days", "restore": [] }""");
            modules.Write("camp/rest.json", """{ "type": "event", "id": "rest", "kind": "rest", "text": "The party rests.", "tracks": ["fifth-srd:hit_points"], "resting": "camp:slow", "periods": 1, "next": "farewell" }""");
            modules.Write("camp/farewell.json", """{ "type": "event", "id": "farewell", "kind": "text", "text": "Morning." }""");
        }

        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, repositoryModules]);
        Assert.Empty(set.Diagnostics);
        using EngineTestHost host = EngineTestHost.Create();
        List<Character> party = [];
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(9395, "consumable.fifth"));
            Character? character = CharacterRules.Create(
                set.Rules!,
                Character.StampsOf(set),
                new CreationRequest(
                    "Ada",
                    "fighter",
                    "human",
                    Attributes: new Dictionary<string, decimal> { ["str"] = 12, ["dex"] = 12, ["con"] = 14, ["int"] = 10, ["wis"] = 10, ["cha"] = 10 },
                    Features: ["soldier", "savage_attacker", "defense"]),
                new DiceRoller(engine.Random, stream),
                []);
            Assert.NotNull(character);
            party.Add(character!);
        });

        return (campaign, set, party, set.Rules!.Find(DefinitionTypes.Item, "camp:draught", out _)!);
    }
}
