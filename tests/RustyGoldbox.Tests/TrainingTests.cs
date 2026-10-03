using System.Text;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

public sealed class TrainingTests
{
    [Fact]
    public void EligibleClassChangePaysForOneLevelWithoutMoreOldClassExperience()
    {
        ModuleSet set = ModuleLoader.Load(Path.Combine(Rules.RepositoryRoot, "modules", "sample-crypt"), []);
        Assert.Empty(set.Diagnostics);
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(1, "training-class-change"));
            DiceRoller dice = new(engine.Random, stream);
            List<ModuleDiagnostic> problems = [];
            Character character = CharacterRules.Create(set.Rules!, Character.StampsOf(set),
                new CreationRequest("Aldo", "fighter", "human", Attributes: new Dictionary<string, decimal>
                {
                    ["str"] = 16, ["dex"] = 12, ["con"] = 13, ["int"] = 17, ["wis"] = 10, ["cha"] = 10,
                }), dice, problems)!;
            Assert.NotNull(CharacterRules.AddExperience(set.Rules!, character, 4500, dice, problems, trained: true));
            Assert.Empty(problems);
            Assert.Equal(3, character.Level);
            Assert.False(CharacterRules.ReadyToLevel(set.Rules!, character));
            character.Gold = 10000;
            Definition campaign = set.Rules!.Find(DefinitionTypes.Campaign, "crypt", out _)!;
            CampaignState state = CampaignRunner.NewState(set.Rules!, campaign, [character], 1);
            state.PendingTraining = set.Rules!.Find(DefinitionTypes.Event, "trainer", out _)!;
            CampaignRunner runner = new(set.Rules!, state);

            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("train 1", engine.Random)));
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("train 1 --class cleric", engine.Random)));
            Assert.Equal(3, character.Level);
            Assert.Equal(10000, character.Gold);
            Assert.Equal(0, state.ElapsedDays);

            character.Gold = 4499;
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("train 1 --class magic_user", engine.Random)));
            Assert.Equal(3, character.Level);
            Assert.Equal(4499, character.Gold);
            Assert.Equal(0, state.ElapsedDays);
            character.Gold = 10000;

            var facts = runner.Execute("train 1 --class magic_user", engine.Random);
            Assert.DoesNotContain(facts, fact => fact is RefusedFact);
            Assert.Single(facts.OfType<LevelFact>());
            Assert.Equal(4, character.Level);
            Assert.Equal(3, character.ClassLevels().Single(entry => entry.Key.Id == "fighter").Value);
            Assert.Equal(1, character.ClassLevels().Single(entry => entry.Key.Id == "magic_user").Value);
            Assert.Equal(5500, character.Gold);
            TextFact training = Assert.Single(facts.OfType<TextFact>(), fact => fact.Text.Contains("trains for"));
            Assert.Equal(4, training.Rolls[0].Sides);
            Assert.Equal(training.Rolls[0].Total * 7, state.ElapsedDays);
            Assert.InRange(state.ElapsedDays, 7, 28);
        });
    }

    [Theory]
    [InlineData("train 1")]
    [InlineData("train 1 --class warrior")]
    public void OrdinaryTrainingWithoutExperienceRefusesWithoutSpendingGoldOrTime(string command)
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, true);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        var party = ShopTests.Party(modules, campaign, set);
        CampaignState state = CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1);
        state.PendingTraining = set.Rules!.Find(DefinitionTypes.Event, "trainer", out _)!;
        CampaignRunner runner = new(set.Rules!, state);
        decimal gold = party.Sum(character => character.Gold);
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute(command, engine.Random)));
            Assert.Equal(1, party[0].Level);
            Assert.Equal(gold, party.Sum(character => character.Gold));
            Assert.Equal(0, state.ElapsedDays);
        });
    }

    [Fact]
    public void ExperienceWaitsAndEachPaidTrainingBuysOneLevelAndTime()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, true);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        var party = ShopTests.Party(modules, campaign, set);
        CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            Assert.All(party, character => Assert.Equal(1, character.Level));
            Assert.All(party, character => Assert.Equal(30, character.Experience));
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("level 1", engine.Random)));
            party.ForEach(character => character.Gold = 0);
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("train 1", engine.Random)));
            Assert.Equal(0, runner.State.ElapsedDays);
            party[0].Gold = 1;
            party[1].Gold = 5;
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("train 1 --class missing", engine.Random)));
            Assert.Equal(1, party[0].Level);
            Assert.Equal(1, party[0].Gold);
            Assert.Equal(5, party[1].Gold);
            runner.Execute("train 1", engine.Random);
            Assert.Equal(2, party[0].Level);
            Assert.Equal(1, party[1].Level);
            Assert.Equal(2, runner.State.ElapsedDays);
            Assert.Equal(4, party.Sum(character => character.Gold));
            runner.Execute("train 1", engine.Random);
            Assert.Equal(3, party[0].Level);
            Assert.Equal(4, runner.State.ElapsedDays);
            Assert.Equal(0, party.Sum(character => character.Gold));
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("train 1", engine.Random)));
            List<ModuleDiagnostic> problems = [];
            CampaignState saved = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(runner.State, set)), "save", set, problems)!;
            Assert.Empty(problems);
            Assert.Equal(4, saved.ElapsedDays);
            Assert.Equal("trainer", saved.PendingTraining!.Id);
            Assert.Equal(3, saved.Party[0].Level);
        });
    }

    [Fact]
    public void RulesWithoutTrainingStillLevelImmediately()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, false);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        var party = ShopTests.Party(modules, campaign, set);
        CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine => runner.Begin(engine.Random));
        Assert.All(party, character => Assert.Equal(3, character.Level));
    }

    [Fact]
    public void CliTrainingHasAGoldenTranscript()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, true);
        modules.Write("train.script", "level 1\ntrain 1\ntrain 1\nstatus\nleave\n");
        Golden.Verify("fixture-training-play.txt", CliTranscript.Run(modules.Root,
            ["character", "new", "--module", campaign, "--modules", ".", "--class", "warrior", "--race", "folk", "--name", "A", "--seed", "1", "--out", "a.json"],
            ["play", "--campaign", campaign, "--modules", ".", "--party", "a.json", "--seed", "1", "--script", "train.script"]));
    }

    internal static string Fixture(TempModules modules, bool training)
    {
        string campaign = ShopTests.Fixture(modules);
        modules.Write("rules/warrior.json", """{ "type": "class", "id": "warrior", "name": "Warrior", "levels": [{ "xp": 0, "hp": "5" }, { "xp": 10, "hp": "2" }, { "xp": 20, "hp": "2" }] }""");
        if (training)
        {
            modules.Write("rules/advancement.json", """{ "type": "advancement", "id": "standard", "name": "Steps", "experience": "class", "training": { "cost": "self.level * 2", "days": "2" } }""");
            modules.Write("tale/trainer.json", """{ "type": "event", "id": "trainer", "kind": "training", "text": "Train here.", "next": "farewell" }""");
        }

        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "xp" }""");
        modules.Write("tale/xp.json", "{ \"type\": \"event\", \"id\": \"xp\", \"kind\": \"experience\", \"amount\": \"30\", \"each\": true, \"next\": \"" + (training ? "trainer" : "farewell") + "\" }");
        return campaign;
    }
}
