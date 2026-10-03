using System.Text;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class CampaignSkillMarkTests
{
    [Fact]
    public void SuccessfulSearchAndDoorChecksMarkTheCharacterAndImproveAfterSave()
    {
        using TempModules modules = new();
        string campaign = OriginalCampaign(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            List<Character> party = OriginalParty(set, engine.Random);
            Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 7);
            CampaignRunner runner = new(set.Rules, state);

            SearchFact search = Assert.Single(runner.Execute("search", engine.Random).OfType<SearchFact>());
            Assert.True(search.Found);
            Assert.Empty(party[0].SkillMarks);
            Assert.Equal(1, party[1].SkillMarks["craft"]);

            Assert.Contains(runner.Execute("forward", engine.Random), fact => fact is MovedFact { X: 1, Y: 0 });
            DoorFact pick = Assert.Single(runner.Execute("pick east", engine.Random).OfType<DoorFact>());
            Assert.True(pick.Opened);
            Assert.Empty(party[0].SkillMarks);
            Assert.Equal(1, party[1].SkillMarks["craft"]);

            string json = SaveFile.ToJson(state, set);
            List<ModuleDiagnostic> saveProblems = [];
            CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(json), "save.json", set, saveProblems)!;
            Assert.Empty(saveProblems);
            Assert.Equal(1, restored.Party[1].SkillMarks["craft"]);

            List<ModuleDiagnostic> improvementProblems = [];
            using Rng improvementStream = engine.Random.CreateScoped(new ScopedRngCreateRequest(7, "campaign-skill-improvement"));
            List<SkillImprovement> improvements = CharacterRules.ImproveMarkedSkills(
                set.Rules, restored.Party[1],
                new DiceRoller(engine.Random, improvementStream),
                improvementProblems);
            Assert.Empty(improvementProblems);
            Assert.Equal(new SkillImprovement("craft", 1, 1, true), Assert.Single(improvements));
            Assert.Equal(1, restored.Party[1].StatBonuses["craft"]);
            Assert.Empty(restored.Party[1].SkillMarks);
        });
    }

    [Fact]
    public void FailedSearchAndDoorChecksDoNotMarkACharacter()
    {
        using TempModules modules = new();
        string campaign = OriginalCampaign(modules);
        modules.Write("tale/search.json", """{ "type": "check", "id": "search", "name": "Search", "roll": "self.str", "skill": "craft", "target": "10", "succeeds": "at-least" }""");
        modules.Write("tale/pick.json", """{ "type": "check", "id": "pick", "name": "Pick", "roll": "self.str", "skill": "craft", "target": "10", "succeeds": "at-least" }""");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            List<Character> party = OriginalParty(set, engine.Random)[..1];
            Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 11);
            CampaignRunner runner = new(set.Rules, state);

            SearchFact search = Assert.Single(runner.Execute("search", engine.Random).OfType<SearchFact>());
            Assert.False(search.Found);
            Assert.Empty(party[0].SkillMarks);

            state.X = 1;
            DoorFact pick = Assert.Single(runner.Execute("pick east", engine.Random).OfType<DoorFact>());
            Assert.False(pick.Opened);
            Assert.Empty(party[0].SkillMarks);
        });
    }

    [Fact]
    public void UniversalD100CampaignSearchMarksItsDeclaredSkill()
    {
        using TempModules modules = new();
        modules.Module("art", "assets");
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("universal-d100", "*")}, {Require("art", "*")}");
        modules.Write("tale/search.json", """{ "type": "check", "id": "search", "name": "Search", "roll": "1", "skill": "sword", "target": "1", "succeeds": "at-least" }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|  S  |", "+--+--+"], "search": "search", "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 1 } }""");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(13, "universal-campaign-skill"));
            List<ModuleDiagnostic> creationProblems = [];
            Character character = CharacterRules.Create(set.Rules!, Character.StampsOf(set), new CreationRequest(
                "Ada", null, null,
                Attributes: new Dictionary<string, decimal>
                {
                    ["str"] = 10, ["con"] = 10, ["siz"] = 10, ["int"] = 10,
                    ["pow"] = 10, ["dex"] = 10, ["cha"] = 10,
                },
                Features: ["soldier"]), new DiceRoller(engine.Random, stream), creationProblems)!;
            Assert.Empty(creationProblems);

            Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, [character], 13);
            CampaignRunner runner = new(set.Rules, state);
            SearchFact search = Assert.Single(runner.Execute("search", engine.Random).OfType<SearchFact>());

            Assert.True(search.Found);
            Assert.Equal(1, character.SkillMarks["sword"]);
        });
    }

    private static string OriginalCampaign(TempModules modules)
    {
        Rules.WriteSmallRuleset(modules);
        modules.Write("rules/craft.json", """{ "type": "derived", "id": "craft", "name": "Craft", "value": "self.str" }""");
        modules.Write("rules/creation.json", """{ "type": "character-creation", "id": "standard", "name": "Standard", "attributes": ["str"], "attribute_roll": "10", "default": true }""");
        modules.Write("rules/advancement.json", """
            { "type": "advancement", "id": "improving", "name": "Improving skills", "kind": "improvement",
              "improvement": { "checks": [ { "skill": "craft", "when": "true", "amount": "1" } ] } }
            """);
        modules.Module("art", "assets");
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("rules", "*")}, {Require("art", "*")}");
        modules.Write("tale/search.json", """{ "type": "check", "id": "search", "name": "Search", "roll": "self.str", "skill": "craft", "target": "10", "succeeds": "at-least" }""");
        modules.Write("tale/pick.json", """{ "type": "check", "id": "pick", "name": "Pick", "roll": "self.str", "skill": "craft", "target": "10", "succeeds": "at-least" }""");
        modules.Write("tale/hall.json", """
            {
              "type": "area", "id": "hall", "name": "Hall",
              "map": ["+--+--+--+", "|  S  D  |", "+--+--+--+"],
              "search": "search",
              "doors": [{ "id": "gate", "at": [1, 0], "facing": "east", "pick": "pick" }],
              "entries": { "in": { "at": [0, 0], "facing": "east" } }
            }
            """);
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 2 } }""");
        return campaign;
    }

    private static List<Character> OriginalParty(ModuleSet set, IRandomService random)
    {
        using Rng stream = random.CreateScoped(new ScopedRngCreateRequest(5, "campaign-skill-characters"));
        DiceRoller dice = new(random, stream);
        List<ModuleDiagnostic> problems = [];
        Character first = CharacterRules.Create(set.Rules!, Character.StampsOf(set), new CreationRequest(
            "Failing", "warrior", null, Attributes: new Dictionary<string, decimal> { ["str"] = 9 }), dice, problems)!;
        Character second = CharacterRules.Create(set.Rules!, Character.StampsOf(set), new CreationRequest(
            "Successful", "warrior", null, Attributes: new Dictionary<string, decimal> { ["str"] = 10 }), dice, problems)!;
        Assert.Empty(problems);
        return [first, second];
    }
}
