using System.Text;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

public sealed class SpellRewardTests
{
    [Fact]
    public void RewardChoosesTheHighestUnknownCastableLevelAndSurvivesSave()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            List<ModuleDiagnostic> problems = [];
            Character character = CharacterRules.Create(
                set.Rules!,
                Character.StampsOf(set),
                new CreationRequest("Mira", "magic_user", "human", new Dictionary<string, decimal>
                {
                    ["str"] = 10, ["dex"] = 10, ["con"] = 10, ["int"] = 16, ["wis"] = 10, ["cha"] = 10,
                }),
                new DiceRoller(engine.Random, 7, "spell-reward-create"),
                problems)!;
            Assert.Empty(problems);
            Assert.NotNull(CharacterRules.AddExperience(set.Rules!, character, 4800, new DiceRoller(engine.Random, 7, "spell-reward-level"), problems, trained: true));
            Assert.Empty(problems);
            Assert.Equal(3, character.Level);
            Assert.True(CharacterRules.SetSpells(set.Rules!, character, ["magic_missile"], problems));
            Assert.Empty(problems);

            Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, campaignDefinition, [character], 23));
            List<PlayFact> facts = runner.Begin(engine.Random);
            SpellRewardFact reward = Assert.Single(facts.OfType<SpellRewardFact>());
            Assert.True(reward.Granted);
            Assert.Equal(2, reward.Level);
            Assert.Equal("gift:arc", reward.Spell!.QualifiedId);
            Assert.Single(reward.Rolls);
            Assert.Equal(1, reward.Rolls[0].Count);
            Assert.Equal(1, reward.Rolls[0].Sides);
            Assert.Contains(character.Spells, spell => spell.QualifiedId == "gift:arc");

            Combatant combatant = Combatant.FromCharacter(set.Rules!, character);
            Assert.Contains(combatant.Uses, use => use.Spell?.QualifiedId == "gift:arc");

            List<ModuleDiagnostic> saveProblems = [];
            CampaignState restored = SaveFile.Read(
                Encoding.UTF8.GetBytes(SaveFile.ToJson(runner.State, set)),
                "spell-reward-save",
                set,
                saveProblems)!;
            Assert.Empty(saveProblems);
            Assert.Contains(restored.Party[0].Spells, spell => spell.QualifiedId == "gift:arc");
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KnownHighestLevelSpellsProduceANoOpWithoutDrawingOrDowngrading(bool knowsLowerLevelSpells)
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            List<ModuleDiagnostic> problems = [];
            Character character = CharacterRules.Create(
                set.Rules!,
                Character.StampsOf(set),
                new CreationRequest("Mira", "magic_user", "human", new Dictionary<string, decimal>
                {
                    ["str"] = 10, ["dex"] = 10, ["con"] = 10, ["int"] = 16, ["wis"] = 10, ["cha"] = 10,
                }),
                new DiceRoller(engine.Random, 8, "spell-reward-create"),
                problems)!;
            Assert.Empty(problems);
            Assert.NotNull(CharacterRules.AddExperience(set.Rules!, character, 4800, new DiceRoller(engine.Random, 8, "spell-reward-level"), problems, trained: true));
            Assert.True(CharacterRules.SetSpells(set.Rules!, character, knowsLowerLevelSpells ? ["magic_missile", "sleep", "arc"] : ["arc"], problems));
            Assert.Empty(problems);

            Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, campaignDefinition, [character], 24));
            SpellRewardFact reward = Assert.Single(runner.Begin(engine.Random).OfType<SpellRewardFact>());
            Assert.False(reward.Granted);
            Assert.Null(reward.Spell);
            Assert.Empty(reward.Rolls);
            Assert.Contains("no unknown castable spell", reward.Reason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RewardMemberMustBePositive()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, member: 0);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        ModuleDiagnostic diagnostic = Assert.Single(set.Diagnostics, problem => problem.Rule == "event.spell-reward");
        Assert.Equal("$.member", diagnostic.JsonPath);
    }

    [Fact]
    public void FifthRulesetSpellListsUseTheSameRewardEvent()
    {
        using TempModules modules = new();
        string campaign = FifthFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            List<ModuleDiagnostic> problems = [];
            Character character = CharacterRules.Create(
                set.Rules!,
                Character.StampsOf(set),
                new CreationRequest(
                    "Iris",
                    "wizard",
                    "human",
                    Priority: ["int", "con", "dex", "wis", "cha", "str"],
                    Creation: "standard_array",
                    Features: ["sage", "alert"]),
                new DiceRoller(engine.Random, 9, "fifth-spell-reward-create"),
                problems)!;
            Assert.Empty(problems);

            Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, campaignDefinition, [character], 25));
            SpellRewardFact reward = Assert.Single(runner.Begin(engine.Random).OfType<SpellRewardFact>());
            Assert.True(reward.Granted);
            Assert.Equal(1, reward.Level);
            Assert.Equal("fifth-srd:magic_missile", reward.Spell!.QualifiedId);
            Assert.Single(reward.Rolls);
            Assert.Contains(Combatant.FromCharacter(set.Rules!, character).Uses, use => use.Spell?.QualifiedId == reward.Spell.QualifiedId);
        });
    }

    [Fact]
    public void RewardUsesActiveListsWhenAFormerClassIsDormantOrCalledOn()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        modules.Write("gift/spells/dual_arc_called.json", """
            {
              "type": "spell",
              "id": "dual_arc_called",
              "name": "Called Arc",
              "lists": { "classic:magic_user": 3, "classic:cleric": 1 },
              "range": "60 feet",
              "duration": "Instantaneous",
              "area": "One creature",
              "casting_time": "1 segment",
              "cost": { "classic:spells_1": "1" },
              "effect": { "action": "classic:spell_damage", "damage": "1d4", "range": "6", "missiles": "1" },
              "description": "A dual-class test spell."
            }
            """);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Definition calledArc = set.Rules!.Find(DefinitionTypes.Spell, "dual_arc_called", out _)!;
            Character MakeDual(string name, string scope)
            {
                List<ModuleDiagnostic> problems = [];
                Character character = CharacterRules.Create(
                    set.Rules!,
                    Character.StampsOf(set),
                    new CreationRequest(name, "magic_user", "human", new Dictionary<string, decimal>
                    {
                        ["str"] = 16, ["dex"] = 12, ["con"] = 13, ["int"] = 17, ["wis"] = 17, ["cha"] = 10,
                    }),
                    new DiceRoller(engine.Random, 12, $"spell-reward-dual-{scope}-create"),
                    problems)!;
                Assert.Empty(problems);
                Assert.NotNull(CharacterRules.AddExperience(set.Rules!, character, 4800, new DiceRoller(engine.Random, 12, $"spell-reward-dual-{scope}-level"), problems, trained: true));
                Assert.Empty(problems);
                Assert.NotNull(CharacterRules.AddExperience(set.Rules!, character, 0, new DiceRoller(engine.Random, 12, $"spell-reward-dual-{scope}-change"), problems, "cleric", trained: true));
                Assert.Empty(problems);
                Assert.Equal(4, character.Level);
                Assert.Equal(3, character.ClassLevels().Single(entry => entry.Key.Id == "magic_user").Value);
                Assert.Equal(1, character.ClassLevels().Single(entry => entry.Key.Id == "cleric").Value);
                Assert.Contains(character.LeftClasses, characterClass => characterClass.Id == "magic_user");
                return character;
            }

            Definition firstCampaign = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            Character dormant = MakeDual("Aldo", "dormant");
            Assert.False(dormant.UsesFormerClasses);
            foreach (Definition spell in CharacterRules.CastableSpells(set.Rules!, dormant).Where(spell => spell != calledArc))
            {
                dormant.Spells.Add(spell);
            }
            Assert.Null(CharacterRules.SpellProblem(set.Rules!, dormant, calledArc));
            CampaignRunner dormantRunner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, firstCampaign, [dormant], 26));
            SpellRewardFact dormantReward = Assert.Single(dormantRunner.Begin(engine.Random).OfType<SpellRewardFact>());
            Assert.True(dormantReward.Granted);
            Assert.Equal("gift:dual_arc_called", dormantReward.Spell!.QualifiedId);
            Assert.Equal(1, dormantReward.Level);

            Character calledOn = MakeDual("Aldo called on", "called");
            List<ModuleDiagnostic> calledOnProblems = [];
            Assert.True(CharacterRules.UseFormerClasses(set.Rules!, calledOn, true, calledOnProblems));
            Assert.Empty(calledOnProblems);
            Assert.True(calledOn.UsesFormerClasses);
            Assert.Contains(calledOn.ToCreature().ClassLevels.Keys, characterClass => characterClass.Id == "magic_user");
            foreach (Definition spell in CharacterRules.CastableSpells(set.Rules!, calledOn).Where(spell => spell != calledArc))
            {
                calledOn.Spells.Add(spell);
            }
            Assert.Null(CharacterRules.SpellProblem(set.Rules!, calledOn, calledArc));

            Assert.True(calledOn.UsesFormerClasses);
            Assert.Contains(calledOn.ToCreature().ClassLevels.Keys, characterClass => characterClass.Id == "magic_user");
            CampaignRunner calledOnRunner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, firstCampaign, [calledOn], 27));
            SpellRewardFact calledOnReward = Assert.Single(calledOnRunner.Begin(engine.Random).OfType<SpellRewardFact>());
            Assert.True(calledOnReward.Granted);
            Assert.Equal("gift:dual_arc_called", calledOnReward.Spell!.QualifiedId);
            Assert.Equal(3, calledOnReward.Level);
        });
    }

    [Fact]
    public void OriginalAscendRewardIsSavedPreparedAndCast()
    {
        using TempModules modules = new();
        string campaign = AscendFixture(modules);
        string fixtures = Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, fixtures]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            List<ModuleDiagnostic> problems = [];
            Character character = CharacterRules.Create(
                set.Rules!,
                Character.StampsOf(set),
                new CreationRequest("Ilse", "adept", "folk", Attributes: new Dictionary<string, decimal>
                {
                    ["might"] = 9, ["grace"] = 12, ["grit"] = 16, ["wit"] = 16,
                }, Features: ["lightning_reflexes"]),
                new DiceRoller(engine.Random, 13, "spell-reward-ascend-create"),
                problems)!;
            Assert.Empty(problems);

            Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
            CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, campaignDefinition, [character], 28));
            SpellRewardFact reward = Assert.Single(runner.Begin(engine.Random).OfType<SpellRewardFact>());
            Assert.True(reward.Granted);
            Assert.Equal("ascend:flare", reward.Spell!.QualifiedId);
            Assert.Equal(1, reward.Level);
            Assert.Contains(character.Spells, spell => spell.QualifiedId == "ascend:flare");

            Assert.True(CharacterRules.SetMemorised(set.Rules!, character, ["flare"], problems, prepareNow: false));
            Assert.Empty(problems);
            Assert.Equal(["ascend:flare"], character.Memorised.Select(spell => spell.QualifiedId));
            Assert.Equal(["ascend:flare"], character.Prepared!.Select(spell => spell.QualifiedId));

            List<ModuleDiagnostic> saveProblems = [];
            CampaignState restored = SaveFile.Read(
                Encoding.UTF8.GetBytes(SaveFile.ToJson(runner.State, set)),
                "ascend-spell-reward-save",
                set,
                saveProblems)!;
            Assert.Empty(saveProblems);
            Character saved = Assert.Single(restored.Party);
            Assert.Contains(saved.Spells, spell => spell.QualifiedId == "ascend:flare");
            Assert.Equal(["ascend:flare"], saved.Memorised.Select(spell => spell.QualifiedId));
            Assert.Equal(["ascend:flare"], saved.Prepared!.Select(spell => spell.QualifiedId));

            Combatant combatant = Combatant.FromCharacter(set.Rules!, saved);
            Definition combat = set.Rules!.Find(DefinitionTypes.Combat, "standard", out _)!;
            Definition encounter = set.Rules!.Find(DefinitionTypes.Encounter, "brutes", out _)!;
            Assert.True(combatant.CanCast(reward.Spell));
            CombatResult result = CombatRunner.Run(
                set.Rules!,
                combat,
                [new CombatSide("Party", [combatant]), new CombatSide("Brutes", Encounters.Spawn(set.Rules!, encounter, new DiceRoller(engine.Random, 14, "spell-reward-ascend-foes")))],
                new DiceRoller(engine.Random, 14, "spell-reward-ascend-combat"),
                6,
                encounter);
            Assert.Contains(result.Facts, fact => fact.Describe().StartsWith("Ilse uses Flare", StringComparison.Ordinal));
            Assert.Contains(result.Facts, fact => fact.Describe().Contains("spends 1 1st circle slots", StringComparison.Ordinal));
            Assert.Empty(combatant.Prepared);
            Assert.Equal(0, combatant.Creature.Track("arcana_1").Current);
        });
    }

    private static string Fixture(TempModules modules, int? member = null)
    {
        string gift = modules.Module("gift", "extension", requires: TempModules.Require("classic", "^0.1.0"));
        modules.Write("gift/spells/arc.json", """
            {
              "type": "spell",
              "id": "arc",
              "name": "Arc",
              "lists": { "classic:magic_user": 2 },
              "range": "60 feet",
              "duration": "Instantaneous",
              "area": "One creature",
              "casting_time": "1 segment",
              "cost": { "classic:spells_2": "1" },
              "effect": { "action": "classic:spell_damage", "damage": "1d4 + 1", "range": "6", "missiles": "1" },
              "description": "A test spell from a different module shape."
            }
            """);
        modules.Module("art", "assets");
        string requires = $"{TempModules.Require("classic", "^0.1.0")}, {TempModules.Require("gift", "*")}, {TempModules.Require("art", "*")}";
        string campaign = modules.Module("tale", "campaign", requires: requires);
        modules.Write("tale/area.json", """
            {
              "type": "area",
              "id": "hall",
              "name": "Hall",
              "map": ["+--+", "|  |", "+--+"],
              "entries": { "in": { "at": [0, 0], "facing": "east" } }
            }
            """);
        modules.Write("tale/campaign.json", """
            { "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "gift" }
            """);
        string memberField = member is int value ? $", \"member\": {value}" : "";
        modules.Write("tale/gift.json", $$"""
            { "type": "event", "id": "gift", "kind": "spell_reward", "text": "The lesson takes hold."{{memberField}} }
            """ );
        return campaign;
    }

    private static string FifthFixture(TempModules modules)
    {
        modules.Module("art", "assets");
        string campaign = modules.Module("tale", "campaign", requires: $"{TempModules.Require("fifth-srd", "^0.1.0")}, {TempModules.Require("art", "*")}");
        modules.Write("tale/area.json", """
            {
              "type": "area",
              "id": "hall",
              "name": "Hall",
              "map": ["+--+", "|  |", "+--+"],
              "entries": { "in": { "at": [0, 0], "facing": "east" } }
            }
            """);
        modules.Write("tale/campaign.json", """
            { "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "gift" }
            """);
        modules.Write("tale/gift.json", """
            { "type": "event", "id": "gift", "kind": "spell_reward", "text": "The lesson takes hold." }
            """);
        return campaign;
    }

    private static string AscendFixture(TempModules modules)
    {
        modules.Module("art", "assets");
        string campaign = modules.Module("tale", "campaign", requires: $"{TempModules.Require("ascend", "^0.1.0")}, {TempModules.Require("art", "*")}");
        modules.Write("tale/area.json", """
            {
              "type": "area",
              "id": "hall",
              "name": "Hall",
              "map": ["+--+", "|  |", "+--+"],
              "entries": { "in": { "at": [0, 0], "facing": "east" } }
            }
            """);
        modules.Write("tale/campaign.json", """
            { "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "gift" }
            """);
        modules.Write("tale/gift.json", """
            { "type": "event", "id": "gift", "kind": "spell_reward", "text": "The spark answers." }
            """);
        return campaign;
    }
}
