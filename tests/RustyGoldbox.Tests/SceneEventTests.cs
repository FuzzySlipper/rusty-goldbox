using System.Text;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class SceneEventTests
{
    [Fact]
    public void FifthSrdSceneCheckDamageConditionAndSaveContinueThroughAWaitingMenu()
    {
        using TempModules modules = new();
        string campaign = FifthSrdFixture(modules);
        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, repositoryModules]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Character member = CreateFifth(set, engine.Random, "Ada");
            member.Tracks["hit_points"].Current = 10;
            Definition tale = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            CampaignRunner runner = new(set.Rules, CampaignRunner.NewState(set.Rules, tale, [member], 19));

            List<PlayFact> facts = runner.Begin(engine.Random);
            SceneCheckFact check = Assert.Single(facts.OfType<SceneCheckFact>());
            Assert.True(check.Result.Success);
            Assert.Single(check.Rolls);
            SceneDamageFact damage = Assert.Single(facts.OfType<SceneDamageFact>());
            Assert.Equal(3, damage.Amount);
            Assert.Equal(7, member.Tracks["hit_points"].Current);
            SceneConditionFact condition = Assert.Single(facts.OfType<SceneConditionFact>());
            Assert.True(condition.Applied);
            Assert.Contains(set.Rules.Find(DefinitionTypes.Condition, "prone", out _)!, member.Conditions);
            Assert.Collection(runner.MenuOptions(), _ => { }, _ => { }, _ => { }, _ => { });

            string save = SaveFile.ToJson(runner.State, set);
            List<ModuleDiagnostic> problems = [];
            CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(save), "scene-save", set, problems)!;
            Assert.Empty(problems);
            Assert.Equal(7, restored.Party[0].Tracks["hit_points"].Current);
            Assert.Contains(set.Rules.Find(DefinitionTypes.Condition, "prone", out _)!, restored.Party[0].Conditions);

            CampaignRunner resumed = new(set.Rules, restored);
            List<PlayFact> continued = resumed.Execute("choose 1", engine.Random);
            Assert.Contains(continued, fact => fact is TextFact { Text: "The ledge is safe." });
        });
    }

    [Theory]
    [InlineData(20, 0, 7, true, "The constitution holds.")]
    [InlineData(8, -20, 1, false, "The constitution fails.")]
    public void FifthSrdSceneCheckUsesSelectedMembersDerivedSaveAndEngineD20(
        int constitution, int modifier, int expectedBonus, bool success, string outcome)
    {
        using TempModules modules = new();
        string campaign = FifthSrdFixture(modules);
        modules.Write("tale/campaign.json", """
            { "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" },
              "party": { "min": 1, "max": 4 }, "intro": "constitution_save" }
            """);
        modules.Write("tale/constitution_save.json", $$"""
            { "type": "event", "id": "constitution_save", "kind": "check", "check": "fifth-srd:con_save", "member": 2,
              "modifier": "{{modifier}}", "on_success": "save_success", "on_failure": "save_failure" }
            """);
        modules.Write("tale/save_success.json", """{ "type": "event", "id": "save_success", "kind": "text", "text": "The constitution holds." }""");
        modules.Write("tale/save_failure.json", """{ "type": "event", "id": "save_failure", "kind": "text", "text": "The constitution fails." }""");

        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, repositoryModules]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Character first = CreateFifth(set, engine.Random, "First", 14);
            Character selected = CreateFifth(set, engine.Random, "Selected", constitution);
            Evaluator evaluator = new(set.Rules!, null);
            Assert.Equal(4, evaluator.Stat(first.ToCreature(), "con_save").Number);
            Assert.Equal(expectedBonus, evaluator.Stat(selected.ToCreature(), "con_save").Number);

            Definition tale = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            CampaignRunner runner = new(set.Rules, CampaignRunner.NewState(set.Rules, tale, [first, selected], 9374));
            List<PlayFact> facts = runner.Begin(engine.Random);
            SceneCheckFact check = Assert.Single(facts.OfType<SceneCheckFact>());
            DiceRoll roll = Assert.Single(check.Rolls);
            Assert.Equal(1, roll.Count);
            Assert.Equal(20, roll.Sides);
            Assert.InRange(roll.Total, 1L, 20L);
            Assert.Equal((decimal)roll.Total, check.Result.Roll);
            Assert.Equal(2, check.Member);
            Assert.Equal("Selected", check.Who);
            Assert.Equal((decimal)expectedBonus, check.Result.Bonus);
            Assert.Equal((decimal)modifier, check.Result.Modifier);
            Assert.Equal(check.Result.Roll + check.Result.Bonus + check.Result.Modifier, check.Result.Total);
            Assert.Equal(success, check.Result.Success);
            Assert.Contains(facts, fact => fact is TextFact { Text: var text } && text == outcome);
        });
    }

    [Fact]
    public void OriginalRulesetUsesSceneVocabularyAndLivePartySizeAcrossJoinAndDismiss()
    {
        using TempModules modules = new();
        string campaign = OriginalFixture(modules);
        string fixtures = Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures");
        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");
        ModuleSet firstSet = ModuleLoader.Load(campaign, [modules.Root, fixtures, repositoryModules]);
        Assert.Empty(firstSet.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Character template = CreateOriginal(firstSet, engine.Random, "Guide");
            modules.Write("tale/guide.json", NpcFile.ToJson(template, "guide"));
            WriteOriginalPartyEvents(modules);
            ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, fixtures, repositoryModules]);
            Assert.Empty(set.Diagnostics);
            Character member = CreateOriginal(set, engine.Random, "Wren");
            Definition tale = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            CampaignRunner runner = new(set.Rules, CampaignRunner.NewState(set.Rules, tale, [member], 23));

            List<PlayFact> facts = runner.Begin(engine.Random);
            Assert.Contains(facts, fact => fact is PartyFact { Joined: true, Members: 2 });
            Assert.Contains(facts, fact => fact is PartyFact { Joined: false, Members: 1 });
            Assert.Single(facts.OfType<SceneCheckFact>());
            Assert.Single(facts.OfType<SceneDamageFact>());
            Assert.Single(facts.OfType<SceneConditionFact>());
            Assert.Single(runner.State.Party);
            Assert.Single(runner.MenuOptions());

            List<PlayFact> continued = runner.Execute("choose 1", engine.Random);
            Assert.Contains(continued, fact => fact is TextFact { Text: "The road is clear." });

            CampaignRunner refusal = new(set.Rules, CampaignRunner.NewState(set.Rules, tale, [CreateOriginal(set, engine.Random, "Sable")], 23));
            refusal.Begin(engine.Random);
            Assert.Contains(refusal.Execute("choose 1", engine.Random), fact => fact is TextFact { Text: "The road is clear." });
        });
    }

    [Fact]
    public void InvalidSceneMemberRefusesAndSchemaExposesFactsAndPartySize()
    {
        using TempModules modules = new();
        string campaign = FifthSrdFixture(modules);
        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, repositoryModules]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Character member = CreateFifth(set, engine.Random, "Ada");
            Definition tale = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            CampaignRunner runner = new(set.Rules, CampaignRunner.NewState(set.Rules, tale, [member], 7));
            runner.Begin(engine.Random);
            List<PlayFact> facts = runner.Execute("choose 2", engine.Random);
            Assert.Contains(facts, fact => fact is RefusedFact refused && refused.Reason.Contains("active party member", StringComparison.Ordinal));

            CampaignRunner avoided = new(set.Rules, CampaignRunner.NewState(set.Rules, tale, [CreateFifth(set, engine.Random, "Brom")], 7));
            avoided.Begin(engine.Random);
            List<PlayFact> safeFacts = avoided.Execute("choose 3", engine.Random);
            Assert.Contains(safeFacts, fact => fact is SceneCheckFact { Result.Success: false });
            Assert.Contains(safeFacts, fact => fact is TextFact { Text: "The ledge is safe." });

            CampaignRunner capped = new(set.Rules, CampaignRunner.NewState(set.Rules, tale, [CreateFifth(set, engine.Random, "Cyra")], 7));
            capped.Begin(engine.Random);
            List<PlayFact> cappedFacts = capped.Execute("choose 4", engine.Random);
            SceneDamageFact floor = Assert.Single(cappedFacts.OfType<SceneDamageFact>());
            SceneHealFact cap = Assert.Single(cappedFacts.OfType<SceneHealFact>());
            Assert.Equal(0, floor.Left);
            Assert.Equal(0, cap.Now - cap.Amount);
            Definition hitPoints = set.Rules.Find(DefinitionTypes.Track, "hit_points", out _)!;
            Assert.Equal(new Evaluator(set.Rules, null).TrackRestoreCap(capped.State.Party[0].ToCreature(), hitPoints), cap.Now);
        });

        (int schemaCode, string schemaText) = CampaignTests.Run(modules, "schema", "events", "--json");
        Assert.Equal(0, schemaCode);
        using JsonDocument schema = JsonDocument.Parse(schemaText);
        JsonElement kinds = schema.RootElement.GetProperty("kinds");
        Assert.Contains(kinds.EnumerateArray(), kind => kind.GetProperty("name").GetString() == "check");
        Assert.Contains(kinds.EnumerateArray(), kind => kind.GetProperty("name").GetString() == "effect");
        (int expressionCode, string expressionText) = CampaignTests.Run(modules, "schema", "expressions", "--json");
        Assert.Equal(0, expressionCode);
        Assert.Contains("party_size()", expressionText, StringComparison.Ordinal);

        modules.Write("scene.script", "choose 1\n");
        string transcript = CliTranscript.Run(modules.Root,
            ["character", "new", "--module", campaign, "--modules", ".", "--modules", repositoryModules, "--class", "fighter", "--race", "human", "--name", "Ada", "--priority", "int,con,dex,wis,cha,str", "--feature", "soldier,savage_attacker,defense", "--seed", "3", "--out", "ada.json"],
            ["play", "--campaign", campaign, "--modules", ".", "--modules", repositoryModules, "--party", "ada.json", "--seed", "19", "--script", "scene.script"]);
        Assert.Contains("rolls Safe landing", transcript, StringComparison.Ordinal);
        Assert.Contains("loses 3 hit points", transcript, StringComparison.Ordinal);
        Golden.Verify("fixture-scene-play.txt", transcript);

        (int playCode, string jsonText) = CampaignTests.Run(modules, "play", "--campaign", campaign, "--modules", ".", "--modules", repositoryModules, "--party", "ada.json", "--seed", "19", "--script", "scene.script", "--json");
        Assert.Equal(0, playCode);
        using JsonDocument json = JsonDocument.Parse(jsonText);
        JsonElement damage = json.RootElement.GetProperty("transcript").EnumerateArray()
            .SelectMany(step => step.GetProperty("facts").EnumerateArray())
            .Single(fact => fact.GetProperty("kind").GetString() == "damage");
        Assert.Equal("fifth-srd:hit_points", damage.GetProperty("damage").GetProperty("track").GetString());
        Assert.Equal(3, damage.GetProperty("damage").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public void SceneEffectsRejectTimedOrInstantConditionState()
    {
        using TempModules modules = new();
        string campaign = FifthSrdFixture(modules);
        modules.Write("tale/bad_timed.json", """
            { "type": "event", "id": "bad_timed", "kind": "effect", "operations": [
              { "op": "apply_condition", "condition": "fifth-srd:prone", "rounds": 1 }
            ] }
            """);
        modules.Write("tale/bad_instant.json", """
            { "type": "event", "id": "bad_instant", "kind": "effect", "operations": [
              { "op": "apply_condition", "condition": "fifth-srd:hit", "values": { "amount": 3 } }
            ] }
            """);
        modules.Write("tale/turn_condition.json", """
            { "type": "condition", "id": "turn_condition", "name": "Turn condition", "modifiers": [], "each_turn": [] }
            """);
        modules.Write("tale/bad_turn_hook.json", """
            { "type": "event", "id": "bad_turn_hook", "kind": "effect", "operations": [
              { "op": "apply_condition", "condition": "tale:turn_condition" }
            ] }
            """);
        modules.Write("tale/hook_condition.json", """
            { "type": "condition", "id": "hook_condition", "name": "Hook condition", "modifiers": [],
              "on_apply": [{ "op": "heal", "track": "fifth-srd:hit_points", "amount": "1", "to": "self" }] }
            """);
        modules.Write("tale/bad_apply_hook.json", """
            { "type": "event", "id": "bad_apply_hook", "kind": "effect", "operations": [
              { "op": "apply_condition", "condition": "tale:hook_condition" }
            ] }
            """);

        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, repositoryModules]);
        Assert.Contains(set.Diagnostics, problem => problem.Rule == "event.effect" && problem.JsonPath == "$.operations[0].rounds");
        Assert.Contains(set.Diagnostics, problem => problem.Rule == "event.effect" && problem.JsonPath == "$.operations[0].values");
        Assert.Contains(set.Diagnostics, problem => problem.Rule == "event.effect" && problem.JsonPath == "$.operations[0].condition");
        Assert.Contains(set.Diagnostics, problem => problem.Rule == "event.effect" && problem.JsonPath == "$.operations[0].condition" && problem.Message.Contains("Turn hooks", StringComparison.Ordinal));
        Assert.Contains(set.Diagnostics, problem => problem.Rule == "event.effect" && problem.JsonPath == "$.operations[0].condition" && problem.Message.Contains("on_apply", StringComparison.Ordinal));
    }

    private static Character CreateFifth(ModuleSet set, IRandomService random, string name, int constitution = 14)
    {
        using Rng stream = random.CreateScoped(new ScopedRngCreateRequest(401, $"scene.fifth.{name}"));
        List<ModuleDiagnostic> problems = [];
        Character? character = CharacterRules.Create(
            set.Rules!,
            Character.StampsOf(set),
            new CreationRequest(
                name,
                "fighter",
                "human",
                Attributes: new Dictionary<string, decimal> { ["str"] = 12, ["dex"] = 12, ["con"] = constitution, ["int"] = 10, ["wis"] = 10, ["cha"] = 10 },
                Features: ["soldier", "savage_attacker", "defense"]),
            new DiceRoller(random, stream),
            problems);
        Assert.True(character is not null, string.Join("; ", problems.Select(problem => problem.Message)));
        return character!;
    }

    private static Character CreateOriginal(ModuleSet set, IRandomService random, string name)
    {
        using Rng stream = random.CreateScoped(new ScopedRngCreateRequest(402, $"scene.original.{name}"));
        List<ModuleDiagnostic> problems = [];
        Character? character = CharacterRules.Create(
            set.Rules!,
            Character.StampsOf(set),
            new CreationRequest(
                name,
                "vanguard",
                "hillfolk",
                Attributes: new Dictionary<string, decimal> { ["brawn"] = 12, ["finesse"] = 12, ["stamina"] = 12, ["intellect"] = 12, ["insight"] = 12, ["presence"] = 12 },
                Features: ["stonehide", "sentry", "hill_toughness", "shield_ward"],
                Boosts: ["insight", "intellect", "finesse", "stamina", "brawn", "presence", "insight", "finesse"]),
            new DiceRoller(random, stream),
            problems);
        Assert.True(character is not null, string.Join("; ", problems.Select(problem => problem.Message)));
        return character!;
    }

    private static string FifthSrdFixture(TempModules modules)
    {
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("fifth-srd", "*")}, {Require("placeholder-art", "*")}");
        WriteArea(modules);
        modules.Write("tale/campaign.json", """
            { "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "safe_landing" }
            """);
        modules.Write("tale/reflex.json", """
            { "type": "check", "id": "reflex", "name": "Safe landing", "roll": "1d1", "bonus": "0", "target": "1", "succeeds": "at-least", "tiers": [] }
            """);
        modules.Write("tale/safe_landing.json", """
            { "type": "event", "id": "safe_landing", "kind": "check", "check": "reflex", "member": 1, "on_success": "fall", "on_failure": "safe" }
            """);
        modules.Write("tale/fall.json", """
            { "type": "event", "id": "fall", "kind": "effect", "member": 1, "operations": [
              { "op": "damage", "track": "fifth-srd:hit_points", "amount": "3" },
              { "op": "apply_condition", "condition": "fifth-srd:prone" }
            ], "next": "continue" }
            """);
        modules.Write("tale/safe.json", """
            { "type": "event", "id": "safe", "kind": "text", "text": "The ledge is safe." }
            """);
        modules.Write("tale/continue.json", """
            { "type": "event", "id": "continue", "kind": "menu", "text": "Continue?", "options": [
              { "label": "Continue", "next": "done" },
              { "label": "Try an invalid member", "next": "bad_member" },
              { "label": "Avoid the fall", "next": "failed_check" },
              { "label": "Test the track cap", "next": "overrun" }
            ] }
            """);
        modules.Write("tale/bad_member.json", """
            { "type": "event", "id": "bad_member", "kind": "check", "check": "reflex", "member": 2, "on_success": "done", "on_failure": "safe" }
            """);
        modules.Write("tale/failure.json", """
            { "type": "check", "id": "failure", "name": "Unsteady footing", "roll": "1d1", "bonus": "0", "target": "2", "succeeds": "at-least", "tiers": [] }
            """);
        modules.Write("tale/failed_check.json", """
            { "type": "event", "id": "failed_check", "kind": "check", "check": "failure", "member": 1, "on_success": "done", "on_failure": "safe" }
            """);
        modules.Write("tale/overrun.json", """
            { "type": "event", "id": "overrun", "kind": "effect", "member": 1, "operations": [
              { "op": "damage", "track": "fifth-srd:hit_points", "amount": "99" },
              { "op": "heal", "track": "fifth-srd:hit_points", "amount": "99" }
            ], "next": "safe" }
            """);
        modules.Write("tale/done.json", """
            { "type": "event", "id": "done", "kind": "text", "text": "The ledge is safe." }
            """);
        return campaign;
    }

    private static string OriginalFixture(TempModules modules)
    {
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("degrees", "*")}, {Require("placeholder-art", "*")}");
        WriteArea(modules);
        modules.Write("tale/campaign.json", """
            { "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 3 } }
            """);
        modules.Write("tale/steady.json", """
            { "type": "check", "id": "steady", "name": "Steady footing", "roll": "1d1", "bonus": "0", "target": "1", "succeeds": "at-least", "tiers": [] }
            """);
        return campaign;
    }

    private static void WriteOriginalPartyEvents(TempModules modules)
    {
        modules.Write("tale/campaign.json", """
            { "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 3 }, "intro": "recruit" }
            """);
        modules.Write("tale/recruit.json", """
            { "type": "event", "id": "recruit", "kind": "join", "npc": "guide", "next": "after_join" }
            """);
        modules.Write("tale/after_join.json", """
            { "type": "event", "id": "after_join", "kind": "branch", "branches": [
              { "when": "party_size() == 2", "next": "dismiss" }
            ], "otherwise": "safe" }
            """);
        modules.Write("tale/dismiss.json", """
            { "type": "event", "id": "dismiss", "kind": "dismiss", "npc": "guide", "next": "after_dismiss" }
            """);
        modules.Write("tale/after_dismiss.json", """
            { "type": "event", "id": "after_dismiss", "kind": "branch", "branches": [
              { "when": "party_size() == 1", "next": "scene_check" }
            ], "otherwise": "safe" }
            """);
        modules.Write("tale/scene_check.json", """
            { "type": "event", "id": "scene_check", "kind": "check", "check": "steady", "member": 1, "on_success": "scene_effect", "on_failure": "safe" }
            """);
        modules.Write("tale/scene_effect.json", """
            { "type": "event", "id": "scene_effect", "kind": "effect", "member": 1, "operations": [
              { "op": "damage", "track": "degrees:hit_points", "amount": "2" },
              { "op": "apply_condition", "condition": "degrees:braced" }
            ], "next": "continue" }
            """);
        modules.Write("tale/continue.json", """
            { "type": "event", "id": "continue", "kind": "menu", "text": "Continue?", "options": [ { "label": "Continue", "next": "safe" } ] }
            """);
        modules.Write("tale/safe.json", """
            { "type": "event", "id": "safe", "kind": "text", "text": "The road is clear." }
            """);
    }

    private static void WriteArea(TempModules modules)
    {
        modules.Write("tale/hall.json", """
            { "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|     |", "+--+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }
            """);
    }
}
