using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Cli;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

public sealed class CharacterTests
{
    private static string Ascend => Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "ascend");

    private static string Degrees => Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "degrees");

    [Fact]
    public void ClassicFighterIsCreatedAndLevelled()
    {
        Golden.Verify("classic-character.txt", CliTranscript.Run(
            ["character", "new", "--module", Rules.ClassicPath, "--class", "fighter", "--race", "dwarf", "--name", "Brom", "--seed", "11", "--out", "brom.json"],
            ["character", "level", "brom.json", "--module", Rules.ClassicPath, "--xp", "5000", "--seed", "3", "--trained"],
            ["character", "new", "--module", Rules.ClassicPath, "--class", "magic_user", "--race", "dwarf", "--seed", "1"]));
    }

    [Fact]
    public void ClassicMultiClassedAndDualClassedCharacters()
    {
        Golden.Verify("classic-multiclass.txt", CliTranscript.Run(
            ["character", "new", "--module", Rules.ClassicPath, "--class", "fighter,magic_user", "--race", "dwarf", "--name", "Nain", "--attributes", "str=15,dex=14,con=15,int=10,wis=10,cha=9"],
            ["character", "new", "--module", Rules.ClassicPath, "--class", "fighter,thief", "--race", "dwarf", "--name", "Thror", "--attributes", "str=15,dex=14,con=15,int=10,wis=10,cha=9", "--seed", "4", "--out", "thror.json"],
            // Experience divides evenly: 2500 each takes the thief to level 3 but the fighter only to 2.
            ["character", "level", "thror.json", "--module", Rules.ClassicPath, "--xp", "5000", "--seed", "3", "--trained"],
            ["character", "new", "--module", Rules.ClassicPath, "--class", "fighter", "--race", "human", "--name", "Aldo", "--attributes", "str=16,dex=12,con=13,int=17,wis=10,cha=10", "--out", "aldo.json"],
            ["character", "level", "aldo.json", "--module", Rules.ClassicPath, "--xp", "4500", "--class", "thief", "--seed", "2", "--trained"],
            ["character", "level", "aldo.json", "--module", Rules.ClassicPath, "--xp", "4500", "--seed", "2", "--trained"],
            // A dual-classed fighter starts magic user at level 1, gains no hit points until passing fighter 3,
            // and fights with the magic user's table until then.
            ["character", "level", "aldo.json", "--module", Rules.ClassicPath, "--xp", "0", "--class", "magic_user", "--seed", "5", "--trained"],
            // Calling on the fighter brings back its table (and, in play, costs the adventure's experience).
            ["character", "former", "aldo.json", "--module", Rules.ClassicPath, "on"],
            ["character", "former", "aldo.json", "--module", Rules.ClassicPath, "off"],
            ["character", "level", "aldo.json", "--module", Rules.ClassicPath, "--xp", "20000", "--seed", "6", "--trained"],
            ["character", "former", "aldo.json", "--module", Rules.ClassicPath, "on"],
            ["character", "show", "aldo.json", "--module", Rules.ClassicPath, "--json"]));
    }

    [Fact]
    public void AscendingArmourClassRulesetUsesTheSameCommands()
    {
        Golden.Verify("ascend-character.txt", CliTranscript.Run(
            ["character", "new", "--module", Ascend, "--class", "warrior", "--race", "stoneborn", "--name", "Kara", "--priority", "might,grit,grace,wit", "--feature", "iron_will,weapon_focus", "--seed", "5", "--out", "kara.json"],
            ["character", "level", "kara.json", "--module", Ascend, "--xp", "3500", "--seed", "8"],
            ["character", "level", "kara.json", "--module", Ascend, "--xp", "3500", "--feature", "great_fortitude,iron_will", "--seed", "8"],
            ["character", "level", "kara.json", "--module", Ascend, "--xp", "3500", "--feature", "improved_initiative,lightning_reflexes", "--seed", "8"],
            ["character", "level", "kara.json", "--module", Ascend, "--xp", "3000", "--class", "adept", "--seed", "9"],
            ["character", "new", "--module", Ascend, "--creation", "point_buy", "--class", "warrior", "--race", "folk", "--name", "Pell", "--attributes", "might=16,grace=14,grit=14,wit=10", "--feature", "iron_will,improved_initiative"],
            ["character", "new", "--module", Ascend, "--creation", "point_buy", "--class", "warrior", "--race", "folk", "--name", "Pell", "--attributes", "might=16,grace=14,grit=12,wit=10", "--feature", "iron_will,improved_initiative"],
            ["character", "new", "--module", Ascend, "--creation", "array", "--class", "warrior", "--race", "folk", "--name", "Quill", "--priority", "might,grit,grace,wit", "--feature", "iron_will,improved_initiative"],
            ["character", "new", "--module", Ascend, "--class", "adept", "--race", "folk", "--name", "Ilse", "--attributes", "might=9,grace=12,grit=10,wit=16", "--feature", "iron_will", "--out", "ilse.json"],
            ["character", "level", "ilse.json", "--module", Ascend, "--xp", "1000", "--class", "warrior", "--feature", "improved_initiative", "--seed", "4"],
            ["character", "show", "ilse.json", "--module", Ascend, "--json"]));
    }

    [Fact]
    public void AncestryHeritageAndBackgroundAreChosenFeatures()
    {
        Golden.Verify("degrees-character.txt", CliTranscript.Run(
            ["character", "new", "--module", Degrees, "--class", "mystic", "--race", "sylvan", "--name", "Wren", "--feature", "stonehide,scribe,sylvan_step,spark", "--boosts", "insight,intellect,finesse,insight,finesse,stamina,presence"],
            ["character", "new", "--module", Degrees, "--class", "vanguard", "--race", "hillfolk", "--name", "Tor", "--feature", "stonehide,sentry", "--boosts", "brawn,insight,stamina,brawn,brawn,stamina,finesse,insight", "--out", "tor.json"],
            ["character", "new", "--module", Degrees, "--class", "vanguard", "--race", "hillfolk", "--name", "Tor", "--feature", "stonehide,sentry,hill_toughness,shield_ward", "--out", "tor.json"],
            ["character", "new", "--module", Degrees, "--class", "vanguard", "--race", "hillfolk", "--name", "Tor", "--feature", "stonehide,sentry,hill_toughness,shield_ward", "--boosts", "stamina,insight,stamina,brawn,brawn,stamina,finesse,insight", "--out", "tor.json"],
            ["character", "new", "--module", Degrees, "--class", "vanguard", "--race", "hillfolk", "--name", "Tor", "--feature", "stonehide,sentry,hill_toughness,shield_ward", "--boosts", "brawn,insight,stamina,brawn,brawn,stamina,finesse,insight", "--out", "tor.json"],
            ["character", "level", "tor.json", "--module", Degrees, "--xp", "1000", "--feature", "battle_cry", "--seed", "2"],
            ["character", "level", "tor.json", "--module", Degrees, "--xp", "3000", "--feature", "steady_stance,hill_lore", "--seed", "2"],
            ["character", "level", "tor.json", "--module", Degrees, "--xp", "3000", "--feature", "steady_stance,hill_lore", "--boosts", "brawn,stamina,insight,finesse", "--seed", "2"],
            ["character", "show", "tor.json", "--module", Degrees, "--json"]));
    }

    [Fact]
    public void TheFirstLevelsChoicesAreKnownBeforeARoll()
    {
        ModuleSet set = ModuleLoader.Load(Degrees, []);
        RuleSet rules = set.Rules!;
        Core.Definitions.Definition creation = CharacterRules.DefaultCreation(rules)!;
        Core.Definitions.Definition vanguard = rules.Find(Core.Definitions.DefinitionTypes.Class, "vanguard", out _)!;

        Assert.Equal([["heritage"], ["background"]], CharacterRules.CreationChoices(creation).Select(grant => grant.Kinds));
        Assert.Equal([["ancestry feat"], ["vanguard feat"]], CharacterRules.FirstLevelChoices(rules, vanguard).Select(grant => grant.Kinds));
    }

    [Fact]
    public void CreationRejectsWhatTheRulesetForbids()
    {
        Assert.Equal(["character.priority"], Problems(Rules.ClassicPath, new CreationRequest("x", "fighter", "human", Priority: ["str", "dex", "con", "int", "wis", "cha"])));
        Assert.Equal(["character.priority"], Problems(Ascend, new CreationRequest("x", "warrior", "folk", Priority: ["might", "might"])));
        Assert.Equal(["character.attributes", "character.attributes"], Problems(Ascend, new CreationRequest("x", "warrior", "folk", Attributes: new Dictionary<string, decimal> { ["might"] = 25, ["grace"] = 10, ["grit"] = 10, ["wit"] = 10, ["luck"] = 3 })));
        Assert.Equal(["character.class"], Problems(Ascend, new CreationRequest("x", "adept", "folk", Attributes: Scores(10, 10, 10, 9))));
        Assert.Equal(["character.race", "character.race"], Problems(Ascend, new CreationRequest("x", "adept", "stoneborn", Attributes: Scores(10, 10, 4, 12))));
        Assert.Equal(["character.reference"], Problems(Ascend, new CreationRequest("x", "bard", "folk", Attributes: Scores(10, 10, 10, 10))));
        Assert.Equal(["character.attributes"], Problems(Ascend, new CreationRequest("x", "warrior", "folk", Creation: "point_buy")));
        Assert.Equal(["character.feature"], Problems(Ascend, new CreationRequest("x", "warrior", "folk", Attributes: Scores(12, 10, 10, 10), Features: ["iron_will", "great_fortitude"])));
        Assert.Equal(["character.point-buy"], Problems(Ascend, new CreationRequest("x", "warrior", "folk", Attributes: Scores(19, 8, 8, 8), Creation: "point_buy")));
        Assert.Equal(["character.boosts"], Problems(Ascend, new CreationRequest("x", "warrior", "folk", Boosts: ["might"])));
        Assert.Equal(["character.priority"], Problems(Degrees, new CreationRequest("x", "vanguard", "hillfolk", Priority: ["brawn"])));
        Assert.Equal(["character.boosts"], Problems(Degrees, new CreationRequest("x", "vanguard", "hillfolk", Features: ["stonehide", "sentry", "hill_lore", "battle_cry"], Boosts: ["brawn", "insight", "stamina", "brawn", "brawn", "stamina", "finesse", "insight", "presence"])));
    }

    [Fact]
    public void LevellingStopsAtTheLastLevel()
    {
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        Character character = Create(set, new CreationRequest("x", "warrior", "folk", Attributes: Scores(12, 13, 10, 10), Features: ["iron_will", "improved_initiative"]))!;

        List<ModuleDiagnostic> problems = [];
        List<LevelGain>? gains = WithDice(dice => CharacterRules.AddExperience(
            set.Rules!, character, 1_000_000, dice, problems, features: ["lightning_reflexes", "weapon_focus", "might_increase", "dodge"]));

        Assert.Empty(problems);
        Assert.Equal([2, 3, 4, 5], gains!.Select(gain => gain.Level));
        Assert.Equal(5, character.Level);
        Assert.True(character.LevelWaiting(set.Rules!));
        Assert.Equal(character.Levels.Sum(level => level.Gain), character.Tracks["hit_points"].Max);

        // The waiting levels go to another class, up to the last character level.
        gains = WithDice(dice => CharacterRules.AddExperience(set.Rules!, character, 0, dice, problems, "adept", ["great_fortitude", "might_increase"]));
        Assert.Empty(problems);
        Assert.Equal([6, 7, 8], gains!.Select(gain => gain.Level));
        Assert.Equal(new Dictionary<string, int> { ["warrior"] = 5, ["adept"] = 3 }, character.ClassLevels().ToDictionary(entry => entry.Key.Id, entry => entry.Value));
        Assert.Null(character.NextLevelExperience(set.Rules!));
    }

    [Fact]
    public void ExperienceAwardedInPlayLevelsWithoutChoicesAndOtherwiseWaits()
    {
        ModuleSet classic = ModuleLoader.Load(Rules.ClassicPath, []);
        Character fighter = Create(classic, new CreationRequest("Ada", "fighter", "human", Attributes: new Dictionary<string, decimal> { ["str"] = 16, ["dex"] = 13, ["con"] = 15, ["int"] = 10, ["wis"] = 9, ["cha"] = 11 }))!;

        // Classic experience waits for paid training even when no choices are needed.
        List<LevelGain> gains = WithDice(dice => CharacterRules.Award(classic.Rules!, fighter, 2001, dice));
        Assert.Empty(gains);
        Assert.Equal(1, fighter.Level);
        Assert.True(CharacterRules.ReadyToLevel(classic.Rules!, fighter));

        // Ascend's levels take a class (and here a feat): the experience is kept, the level waits, and a failed try changes nothing.
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        Character warrior = Create(set, new CreationRequest("x", "warrior", "folk", Attributes: Scores(12, 13, 10, 10), Features: ["iron_will", "improved_initiative"]))!;
        Assert.Empty(WithDice(dice => CharacterRules.Award(set.Rules!, warrior, 1000, dice)));
        Assert.True(CharacterRules.ReadyToLevel(set.Rules!, warrior));
        string before = CharacterFile.ToJson(warrior);
        List<ModuleDiagnostic> problems = [];
        Assert.Null(WithDice(dice => CharacterRules.AddExperience(set.Rules!, warrior, 0, dice, problems)));
        Assert.Equal(before, CharacterFile.ToJson(warrior));
        Assert.NotNull(WithDice(dice => CharacterRules.AddExperience(set.Rules!, warrior, 0, dice, [], features: ["weapon_focus"])));
        Assert.Equal(2, warrior.Level);
    }

    [Fact]
    public void HitPointBonusesFollowTheStatsTheyRead()
    {
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        Character character = Create(set, new CreationRequest("x", "warrior", "folk", Attributes: Scores(14, 10, 14, 10), Features: ["iron_will", "improved_initiative"]))!;
        List<ModuleDiagnostic> problems = [];
        WithDice(dice => CharacterRules.AddExperience(set.Rules!, character, 3000, dice, problems, features: ["lightning_reflexes", "weapon_focus"]));
        Assert.Empty(problems);
        decimal before = MaxHitPoints(set, character);
        Assert.Equal(character.Levels.Sum(level => level.Gain) + (3 * 2), before);

        // The charm raises grit from 14 to 16, so every one of the three levels gains one more.
        character.Equipment.Add(set.Rules!.Find(Core.Definitions.DefinitionTypes.Item, "grit_charm", out _)!);

        Assert.Equal(before + 3, MaxHitPoints(set, character));
    }

    [Fact]
    public void MultiClassedRacesTakeTheMoreOrLessRestrictiveEquipmentRule()
    {
        ModuleSet set = ModuleLoader.Load(Rules.ClassicPath, []);
        RuleSet rules = set.Rules!;
        Dictionary<string, decimal> scores = new() { ["str"] = 15, ["dex"] = 14, ["con"] = 15, ["int"] = 12, ["wis"] = 10, ["cha"] = 9 };
        Character dwarf = Create(set, new CreationRequest("Thror", "fighter", "dwarf", Attributes: scores, AlsoClasses: ["thief"]))!;
        Character elf = Create(set, new CreationRequest("Lir", "fighter", "elf", Attributes: scores, AlsoClasses: ["thief"]))!;
        Core.Definitions.Definition chain = rules.Find(Core.Definitions.DefinitionTypes.Item, "chain_mail", out _)!;
        Core.Definitions.Definition sword = rules.Find(Core.Definitions.DefinitionTypes.Item, "long_sword", out _)!;

        // Dwarves take the more restrictive rule (the thief's), elves the less (the fighter's).
        Assert.Equal("Thror can't equip Chain mail: Thief doesn't allow it.", CharacterRules.EquipmentProblem(rules, dwarf, chain));
        Assert.Null(CharacterRules.EquipmentProblem(rules, dwarf, sword));
        Assert.Null(CharacterRules.EquipmentProblem(rules, elf, chain));
    }

    [Fact]
    public void RequirementsAreCheckedWithTheScoresOfTheirLevel()
    {
        using TempModules modules = new();
        ModuleSet set = ModuleLoader.Load(Degrees, []);
        Character tor = Create(set, new CreationRequest("Tor", "vanguard", "hillfolk",
            Features: ["stonehide", "sentry", "hill_toughness", "shield_ward"], Boosts: ["brawn", "insight", "stamina", "brawn", "brawn", "stamina", "finesse", "insight"]))!;
        List<ModuleDiagnostic> problems = [];
        WithDice(dice => CharacterRules.AddExperience(set.Rules!, tor, 4000, dice, problems, features: ["battle_cry", "steady_stance", "hill_lore"], boosts: ["brawn", "stamina", "insight", "finesse"]));
        Assert.Empty(problems);
        Assert.Equal(18m, tor.Attributes["brawn"]);
        string file = Path.Combine(modules.Root, "tor.json");
        File.WriteAllText(file, CharacterFile.ToJson(tor));
        Assert.NotNull(CharacterFile.Read(file, set, problems));

        // Stone fist needs brawn 18, which only level 5's boost gives, after that level's feat is chosen.
        System.Text.Json.Nodes.JsonNode edited = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        edited["levels"]![4]!["features"]![0] = "degrees:stone_fist";
        File.WriteAllText(file, edited.ToJsonString());

        Assert.Null(CharacterFile.Read(file, set, problems));
        Assert.Contains("Level 5 (Vanguard): Tor doesn't meet degrees:stone_fist's requirements", Assert.Single(problems).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWithEquipmentTheClassForbidsIsRefused()
    {
        using TempModules modules = new();
        ModuleSet set = ModuleLoader.Load(Rules.ClassicPath, []);
        Character mage = Create(set, new CreationRequest("Mira", "magic_user", "human", Attributes: new Dictionary<string, decimal> { ["str"] = 9, ["dex"] = 12, ["con"] = 12, ["int"] = 16, ["wis"] = 10, ["cha"] = 10 }))!;
        mage.Equipment.Add(set.Rules!.Find(Core.Definitions.DefinitionTypes.Item, "chain_mail", out _)!);
        string file = Path.Combine(modules.Root, "mira.json");
        File.WriteAllText(file, CharacterFile.ToJson(mage));
        List<ModuleDiagnostic> problems = [];

        Assert.Null(CharacterFile.Read(file, set, problems));
        Assert.Equal(("character.file", "$.equipment[0]"), (Assert.Single(problems).Rule, problems[0].JsonPath));
    }

    [Fact]
    public void ImprovementAdvancementMarksSuccessfulSkillsAndPersistsThem()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/craft.json", """{ "type": "derived", "id": "craft", "name": "Craft", "value": "self.str" }""");
        modules.Write("rules/creation.json", """{ "type": "character-creation", "id": "standard", "name": "Standard", "attributes": ["str"], "attribute_roll": "10", "default": true }""");
        modules.Write("rules/advancement.json", """
            { "type": "advancement", "id": "improving", "name": "Improving skills", "kind": "improvement",
              "improvement": { "checks": [ { "skill": "craft", "when": "self.craft >= 10", "amount": "1" } ] } }
            """);
        modules.Write("rules/craft_check.json", """{ "type": "check", "id": "craft_check", "name": "Craft check", "roll": "1", "skill": "craft", "target": "1", "succeeds": "at-least" }""");

        ModuleSet set = ModuleLoader.Load(root, []);
        Assert.Empty(set.Diagnostics);
        Character character = Create(set, new CreationRequest("Ada", "warrior", null, Attributes: new Dictionary<string, decimal> { ["str"] = 10 }))!;
        List<ModuleDiagnostic> problems = [];

        Assert.True(CharacterRules.MarkSkillUse(set.Rules!, character, "craft", problems));
        Assert.Empty(problems);
        Assert.Equal(1, character.SkillMarks["craft"]);

        string file = Path.Combine(modules.Root, "ada.json");
        File.WriteAllText(file, CharacterFile.ToJson(character));
        Character loaded = CharacterFile.Read(file, set, problems)!;
        Assert.Empty(problems);
        Assert.Equal(1, loaded.SkillMarks["craft"]);

        List<SkillImprovement> improvements = WithDice(dice => CharacterRules.ImproveMarkedSkills(set.Rules!, loaded, dice, problems));
        Assert.Empty(problems);
        Assert.Equal(new SkillImprovement("craft", 1, 1, true), Assert.Single(improvements));
        Assert.Equal(1, loaded.StatBonuses["craft"]);
        Assert.Empty(loaded.SkillMarks);
    }

    [Fact]
    public void MilestoneAdvancementRaisesSkillsAndAddsStunts()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/craft.json", """{ "type": "derived", "id": "craft", "name": "Craft", "value": "self.str" }""");
        modules.Write("rules/creation.json", """{ "type": "character-creation", "id": "standard", "name": "Standard", "attributes": ["str"], "attribute_roll": "10", "default": true }""");
        modules.Write("rules/stunt.json", """{ "type": "feature", "id": "stunt", "name": "Stunt", "kind": "stunt" }""");
        modules.Write("rules/advancement.json", """
            { "type": "advancement", "id": "milestones", "name": "Milestones", "kind": "milestone",
              "milestones": { "skill_raise": { "count": 1, "amount": 1 }, "skill_swap": { "count": 0 }, "feature": { "kind": "stunt", "count": 1 } } }
            """);

        ModuleSet set = ModuleLoader.Load(root, []);
        Assert.Empty(set.Diagnostics);
        Character character = Create(set, new CreationRequest("Ada", "warrior", null, Attributes: new Dictionary<string, decimal> { ["str"] = 10 }))!;
        List<ModuleDiagnostic> problems = [];

        Assert.True(CharacterRules.ApplyMilestone(set.Rules!, character,
            new MilestoneChoices(new Dictionary<string, decimal> { ["craft"] = 1 }, Features: ["stunt"]), problems));
        Assert.Empty(problems);
        Assert.Equal(1, character.StatBonuses["craft"]);
        Assert.Equal("stunt", Assert.Single(character.MilestoneFeatures).Id);

        string file = Path.Combine(modules.Root, "ada.json");
        File.WriteAllText(file, CharacterFile.ToJson(character));
        Character loaded = CharacterFile.Read(file, set, problems)!;
        Assert.Empty(problems);
        Assert.Equal(1, loaded.StatBonuses["craft"]);
        Assert.Equal("stunt", Assert.Single(loaded.MilestoneFeatures).Id);
    }

    [Fact]
    public void FateModuleUsesMilestoneChoices()
    {
        string module = Path.Combine(Rules.RepositoryRoot, "modules", "fate-condensed");
        ModuleSet set = ModuleLoader.Load(module, []);
        Assert.Empty(set.Diagnostics);
        Character character = Create(set, new CreationRequest("Ruth", null, null, Features: ["quick_feet", "heavy_hitter", "iron_will"]))!;
        List<ModuleDiagnostic> problems = [];

        decimal before = character.Attributes["fight"];
        Assert.True(CharacterRules.ApplyMilestone(set.Rules!, character,
            new MilestoneChoices(new Dictionary<string, decimal> { ["fight"] = 1 }, Features: ["deadeye"]), problems));
        Assert.Empty(problems);
        Assert.Equal(before + 1, character.Attributes["fight"]);
        Assert.Contains(character.MilestoneFeatures, feature => feature.Id == "deadeye");
    }

    [Fact]
    public void ExperienceAwardDoesNotAssumeEveryAdvancementHasAnExperienceField()
    {
        string module = Path.Combine(Rules.RepositoryRoot, "modules", "fate-condensed");
        ModuleSet set = ModuleLoader.Load(module, []);
        Character character = Create(set, new CreationRequest("Ruth", null, null, Features: ["quick_feet", "heavy_hitter", "iron_will"]))!;

        List<LevelGain> gains = WithDice(dice => CharacterRules.Award(set.Rules!, character, 25, dice));

        Assert.Empty(gains);
        Assert.Equal(25, character.Experience);
    }

    [Fact]
    public void ClassfulNonExperienceAdvancementCanBeCreatedThroughCli()
    {
        using TempModules modules = new();
        string module = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/warrior.json", """{ "type": "class", "id": "warrior", "name": "Warrior", "levels": [ { "hp": "1" } ] }""");
        modules.Write("rules/creation.json", """{ "type": "character-creation", "id": "standard", "name": "Standard", "attributes": ["str"], "attribute_roll": "10", "default": true }""");
        modules.Write("rules/advancement.json", """
            { "type": "advancement", "id": "milestones", "name": "Milestones", "kind": "milestone",
              "milestones": { "skill_raise": { "count": 0, "amount": 1 }, "skill_swap": { "count": 0 }, "feature": { "kind": "stunt", "count": 0 } } }
            """);

        (int code, string output) = CampaignTests.Run(modules, "character", "new", "--module", module,
            "--class", "warrior", "--name", "Ada", "--attributes", "str=10", "--out", "ada.json");

        Assert.True(code == 0, output);
        Assert.True(File.Exists(Path.Combine(modules.Root, "ada.json")), output);
        Assert.Contains("Warrior", output, StringComparison.Ordinal);
    }

    [Fact]
    public void NonExperienceAdvancementRefusesExplicitExperienceLevel()
    {
        using TempModules scratch = new();
        string module = Path.Combine(Rules.RepositoryRoot, "modules", "fate-condensed");
        (int created, string creationOutput) = CampaignTests.Run(scratch, "character", "new", "--module", module,
            "--name", "Ruth", "--priority", "fight,athletics,physique,notice,will,shoot,empathy,lore,provoke,stealth",
            "--feature", "quick_feet,heavy_hitter,iron_will", "--out", "ruth.json");
        Assert.True(created == 0, creationOutput);

        (int code, string output) = CampaignTests.Run(scratch, "character", "level", "ruth.json", "--module", module, "--xp", "100");

        Assert.Equal(1, code);
        Assert.Contains("milestone", output, StringComparison.Ordinal);
        Assert.Contains("no experience levels", output, StringComparison.Ordinal);
        ModuleSet set = ModuleLoader.Load(module, []);
        List<ModuleDiagnostic> problems = [];
        Character saved = CharacterFile.Read(Path.Combine(scratch.Root, "ruth.json"), set, problems)!;
        Assert.Empty(problems);
        Assert.Equal(0, saved.Experience);
    }

    [Fact]
    public void UniversalD100ModuleMarksAndImprovesADeclaredSkill()
    {
        string module = Path.Combine(Rules.RepositoryRoot, "modules", "universal-d100");
        ModuleSet set = ModuleLoader.Load(module, []);
        Assert.Empty(set.Diagnostics);
        Character character = Create(set, new CreationRequest("Rook", null, null,
            Attributes: new Dictionary<string, decimal>
            {
                ["str"] = 12, ["con"] = 12, ["siz"] = 12, ["int"] = 18, ["pow"] = 12, ["dex"] = 12, ["cha"] = 12,
            }, Features: ["warrior"]))!;
        List<ModuleDiagnostic> problems = [];

        Assert.True(CharacterRules.MarkSkillUse(set.Rules!, character, "sword", problems));
        Assert.Empty(problems);
        List<SkillImprovement> results = WithDice(dice => CharacterRules.ImproveMarkedSkills(set.Rules!, character, dice, problems));
        Assert.Empty(problems);
        Assert.Equal("sword", Assert.Single(results).Skill);
        Assert.Empty(character.SkillMarks);
    }

    [Fact]
    public void UniversalD100CanSpendStagedProfessionAndPersonalSkills()
    {
        string module = Path.Combine(Rules.RepositoryRoot, "modules", "universal-d100");
        ModuleSet set = ModuleLoader.Load(module, []);
        Assert.Empty(set.Diagnostics);
        IReadOnlyList<SkillAllocation> allocations =
        [
            new("sword", Profession: 100, Personal: 20),
            new("shield", Profession: 50),
            new("dodge", Profession: 40),
            new("brawl", Profession: 30, Personal: 80),
            new("bow", Profession: 30),
        ];
        Character character = Create(set, new CreationRequest("Rook", null, null,
            Attributes: new Dictionary<string, decimal>
            {
                ["str"] = 12, ["con"] = 12, ["siz"] = 12, ["int"] = 10, ["pow"] = 12, ["dex"] = 12, ["cha"] = 12,
            },
            Creation: "staged", Features: ["staged_soldier"], SkillPoints: allocations))!;

        Assert.Equal(135, new Evaluator(set.Rules!, null).Stat(character.ToCreature(), "sword").Number);
        Assert.Equal(64, new Evaluator(set.Rules!, null).Stat(character.ToCreature(), "dodge").Number);
        Assert.Equal(120, character.StatBonuses["sword"]);
        Assert.Contains("\"stat_bonuses\"", CharacterFile.ToJson(character), StringComparison.Ordinal);

        List<ModuleDiagnostic> problems = [];
        Assert.Null(WithDice(dice => CharacterRules.Create(set.Rules!, Character.StampsOf(set), new CreationRequest("Rook", null, null,
            Attributes: new Dictionary<string, decimal>
            {
                ["str"] = 12, ["con"] = 12, ["siz"] = 12, ["int"] = 10, ["pow"] = 12, ["dex"] = 12, ["cha"] = 12,
            },
            Creation: "staged", Features: ["staged_soldier"], SkillPoints:
            [new SkillAllocation("axe", Profession: 250), new SkillAllocation("sword", Personal: 100)]), dice, problems)));
        Assert.Contains(problems, problem => problem.Rule == "character.skill-points" && problem.Message.Contains("does not list", StringComparison.Ordinal));
    }

    [Fact]
    public void StagedSkillChoicesAreAcceptedByTheCliAndSaved()
    {
        using TempModules scratch = new();
        string module = Path.Combine(Rules.RepositoryRoot, "modules", "universal-d100");
        (int code, string output) = CampaignTests.Run(scratch, "character", "new", "--module", module,
            "--creation", "staged", "--name", "Rook", "--feature", "staged_soldier",
            "--attributes", "str=12,con=12,siz=12,int=10,pow=12,dex=12,cha=12",
            "--skill", "sword=profession:100+personal:20,shield=profession:50,dodge=profession:40,brawl=profession:30+personal:80,bow=profession:30",
            "--out", "rook.json");
        Assert.Equal(0, code);
        Assert.Contains("sword", output, StringComparison.Ordinal);
        Assert.Contains("135", output, StringComparison.Ordinal);
        string path = Path.Combine(scratch.Root, "rook.json");
        Assert.Contains("\"stat_bonuses\"", File.ReadAllText(path), StringComparison.Ordinal);
        ModuleSet set = ModuleLoader.Load(module, []);
        List<ModuleDiagnostic> problems = [];
        Character saved = CharacterFile.Read(path, set, problems)!;
        Assert.Empty(problems);
        Assert.Equal(new SkillAllocation("sword", 100, 20), saved.SkillAllocations["sword"]);
    }

    [Fact]
    public void CharacterCliRejectsOverflowingSkillAllocation()
    {
        using TempModules scratch = new();
        string module = Path.Combine(Rules.RepositoryRoot, "modules", "universal-d100");
        (int code, string output) = CampaignTests.Run(scratch, "character", "new", "--module", module,
            "--creation", "staged", "--feature", "staged_soldier", "--skill",
            "axe=profession:79228162514264337593543950335+profession:79228162514264337593543950335");

        Assert.Equal(GoldboxCli.Usage, code);
        Assert.Contains("too large to calculate safely", output, StringComparison.Ordinal);
    }

    [Fact]
    public void StagedRollValuesPersistAcrossTheChoiceCommit()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/int.json", """{ "type": "attribute", "id": "int", "name": "Intelligence", "min": 3, "max": 18, "default": 10 }""");
        modules.Write("rules/skill.json", """{ "type": "derived", "id": "skill", "name": "Skill", "value": "1d6" }""");
        modules.Write("rules/profession.json", """{ "type": "feature", "id": "profession", "name": "Profession", "kind": "staged-profession", "skills": ["skill"] }""");
        modules.Write("rules/staged.json", """
            { "type": "character-creation", "id": "staged", "name": "Staged", "attributes": ["str", "int"],
              "method": "roll", "attribute_roll": "10",
              "skill_points": { "profession": "1d6", "personal": "1d6", "skills": { "skill": "1d6" } },
              "features": [ { "kind": "staged-profession", "count": 1 } ] }
            """);

        ModuleSet set = ModuleLoader.Load(root, []);
        Assert.Empty(set.Diagnostics);
        Dictionary<string, decimal> attributes = new() { ["str"] = 10, ["int"] = 10 };
        Character character = WithDice(1, dice => CharacterRules.Create(set.Rules!, Character.StampsOf(set),
            new CreationRequest("Rook", "warrior", null, Attributes: attributes, Creation: "staged", Features: ["profession"]), dice, []))!;
        SkillPointOptions before = character.SkillPointData!;
        List<ModuleDiagnostic> problems = [];
        SkillPointOptions shown = CharacterRules.GetSkillPointOptions(set.Rules!, character, problems)!;
        Assert.Empty(problems);
        Assert.Equal(before.Profession, shown.Profession);
        Assert.Equal(before.Personal, shown.Personal);
        Assert.Equal(before.Skills.Select(skill => (skill.Skill, skill.Base, skill.Current, skill.ProfessionAllowed)),
            shown.Skills.Select(skill => (skill.Skill, skill.Base, skill.Current, skill.ProfessionAllowed)));

        bool applied = WithDice(2, dice => CharacterRules.ApplySkillPoints(set.Rules!, character,
            [new SkillAllocation("skill", before.Profession, before.Personal)], dice, problems));
        Assert.True(applied);
        Assert.Empty(problems);
        SkillPointOption option = Assert.Single(character.SkillPointData!.Skills);
        Assert.Equal((option.Base, option.Current), (before.Skills[0].Base, before.Skills[0].Current));
        Assert.Equal(before.Profession, character.SkillPointData.Profession);
        Assert.Equal(before.Personal, character.SkillPointData.Personal);
    }

    [Fact]
    public void CharacterFileRejectsForgedStagedAllocationAndBonus()
    {
        using TempModules scratch = new();
        string module = Path.Combine(Rules.RepositoryRoot, "modules", "universal-d100");
        ModuleSet set = ModuleLoader.Load(module, []);
        Character character = Create(set, new CreationRequest("Rook", null, null,
            Attributes: new Dictionary<string, decimal>
            {
                ["str"] = 12, ["con"] = 12, ["siz"] = 12, ["int"] = 10, ["pow"] = 12, ["dex"] = 12, ["cha"] = 12,
            },
            Creation: "staged", Features: ["staged_warrior"], SkillPoints:
            [new SkillAllocation("axe", Profession: 100), new SkillAllocation("spear", Profession: 150), new SkillAllocation("brawl", Personal: 100)]))!;
        string path = Path.Combine(scratch.Root, "forged.json");
        System.Text.Json.Nodes.JsonNode forged = System.Text.Json.Nodes.JsonNode.Parse(CharacterFile.ToJson(character))!;
        forged["skill_allocations"]!["axe"]!["profession"] = 999;
        forged["stat_bonuses"]!["axe"] = 1234;
        File.WriteAllText(path, forged.ToJsonString());

        List<ModuleDiagnostic> problems = [];
        Assert.Null(CharacterFile.Read(path, set, problems));
        Assert.Contains(problems, problem => problem.JsonPath == "$.skill_allocations" && problem.Message.Contains("budget", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.JsonPath == "$.stat_bonuses.axe" && problem.Message.Contains("require", StringComparison.Ordinal));
    }

    [Fact]
    public void CharacterNewJsonIncludesStagedOptions()
    {
        using TempModules scratch = new();
        string module = Path.Combine(Rules.RepositoryRoot, "modules", "universal-d100");
        (int code, string output) = CampaignTests.Run(scratch, "character", "new", "--module", module, "--creation", "staged",
            "--name", "Rook", "--feature", "staged_soldier", "--attributes", "str=12,con=12,siz=12,int=10,pow=12,dex=12,cha=12", "--json");
        Assert.Equal(0, code);
        System.Text.Json.JsonElement json = System.Text.Json.JsonDocument.Parse(output).RootElement;
        System.Text.Json.JsonElement options = json.GetProperty("skill_points");
        Assert.Equal(250, options.GetProperty("profession").GetDecimal());
        Assert.Equal(100, options.GetProperty("personal").GetDecimal());
        Assert.Contains(options.GetProperty("skills").EnumerateArray(), skill => skill.GetProperty("id").GetString() == "dodge" && skill.GetProperty("base").GetDecimal() == 24 && skill.GetProperty("profession").GetBoolean());
    }

    [Fact]
    public void PendingStagedCharacterCannotStartCampaignThroughCli()
    {
        using TempModules scratch = new();
        string campaign = scratch.Module("staged-campaign", "campaign", requires: $"{TempModules.Require("universal-d100", "0.1.0")}, {TempModules.Require("placeholder-art", "0.1.0")}");
        scratch.Write("staged-campaign/campaign.json", """
            { "type": "campaign", "id": "start", "name": "Staged campaign", "start": { "area": "hall", "entry": "start" }, "party": { "min": 1, "max": 1 } }
            """);
        scratch.Write("staged-campaign/hall.json", """
            { "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "start": { "at": [0, 0], "facing": "east" } } }
            """);
        (int created, string creationOutput) = CampaignTests.Run(scratch, "character", "new", "--module", Path.Combine(Rules.RepositoryRoot, "modules", "universal-d100"),
            "--creation", "staged", "--name", "Rook", "--feature", "staged_soldier", "--attributes", "str=12,con=12,siz=12,int=10,pow=12,dex=12,cha=12", "--out", "rook.json");
        Assert.True(created == 0, creationOutput);

        (int code, string output) = CampaignTests.Run(scratch, "play", "--campaign", campaign, "--party", "rook.json",
            "--modules", Path.Combine(Rules.RepositoryRoot, "modules"));
        Assert.Equal(1, code);
        Assert.Contains("staged skill choices", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ToughnessRaisesTheMaximumEachTimeItIsTaken()
    {
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        Character character = Create(set, new CreationRequest("x", "warrior", "folk", Attributes: Scores(12, 10, 10, 10), Features: ["toughness", "improved_initiative"]))!;
        decimal before = MaxHitPoints(set, character);
        Assert.Equal(character.Levels[0].Gain + 3, before);
        Assert.Equal(before, character.Tracks["hit_points"].Current);

        List<ModuleDiagnostic> problems = [];
        WithDice(dice => CharacterRules.AddExperience(set.Rules!, character, 3000, dice, problems, features: ["weapon_focus", "toughness"]));
        Assert.Empty(problems);
        Assert.Equal(character.Levels.Sum(level => level.Gain) + 6, MaxHitPoints(set, character));
        Assert.Equal(MaxHitPoints(set, character), character.Tracks["hit_points"].Current);
    }

    [Fact]
    public void AFileWithAChoiceNoLevelGrantedIsRefused()
    {
        using TempModules modules = new();
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        Character character = Create(set, new CreationRequest("Ilse", "adept", "folk", Attributes: Scores(9, 12, 10, 16), Features: ["iron_will"]))!;
        System.Text.Json.Nodes.JsonNode edited = System.Text.Json.Nodes.JsonNode.Parse(CharacterFile.ToJson(character))!;
        edited["levels"]![0]!["features"]!.AsArray().Add("ascend:great_fortitude");
        string file = Path.Combine(modules.Root, "ilse.json");
        File.WriteAllText(file, edited.ToJsonString());
        List<ModuleDiagnostic> problems = [];

        Assert.Null(CharacterFile.Read(file, set, problems));
        ModuleDiagnostic problem = Assert.Single(problems);
        Assert.Equal(("character.file", "$.levels"), (problem.Rule, problem.JsonPath));
        Assert.Contains("Level 1 (Adept): Nothing granted a feat for ascend:great_fortitude", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewClassMustAcceptTheCharacter()
    {
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        Character weak = Create(set, new CreationRequest("x", "warrior", "folk", Attributes: Scores(12, 10, 10, 9), Features: ["iron_will", "improved_initiative"]))!;
        List<ModuleDiagnostic> problems = [];

        Assert.Null(WithDice(dice => CharacterRules.AddExperience(set.Rules!, weak, 1000, dice, problems, "adept")));
        Assert.Equal("character.class", Assert.Single(problems).Rule);
        Assert.Equal(1, weak.Level);
    }

    [Fact]
    public void ClassExperienceKeepsOneClass()
    {
        ModuleSet set = ModuleLoader.Load(Rules.ClassicPath, []);
        Character fighter = Create(set, new CreationRequest("x", "fighter", "human", Attributes: new Dictionary<string, decimal> { ["str"] = 15, ["dex"] = 12, ["con"] = 12, ["int"] = 12, ["wis"] = 12, ["cha"] = 12 }))!;
        List<ModuleDiagnostic> problems = [];

        Assert.Null(WithDice(dice => CharacterRules.AddExperience(set.Rules!, fighter, 5000, dice, problems, "thief", trained: true)));
        Assert.Equal("character.multiclass", Assert.Single(problems).Rule);
    }

    [Fact]
    public void CharacterFilesRoundTripAndNeedTheirModules()
    {
        using TempModules modules = new();
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        Character character = Create(set, new CreationRequest("Ilse", "adept", "folk", Attributes: Scores(9, 12, 10, 16), Features: ["iron_will"]))!;
        string file = Path.Combine(modules.Root, "ilse.json");
        File.WriteAllText(file, CharacterFile.ToJson(character));

        List<ModuleDiagnostic> problems = [];
        Character? again = CharacterFile.Read(file, set, problems);
        Assert.Empty(problems);
        Assert.Equal(CharacterFile.ToJson(character), CharacterFile.ToJson(again!));

        // A set that requires the character's ruleset (here an extension) still reads it.
        modules.Module("house", "extension", requires: TempModules.Require("ascend", "^0.1.0"));
        ModuleSet superset = ModuleLoader.Load(Path.Combine(modules.Root, "house"), [modules.Root, Path.GetDirectoryName(Ascend)!]);
        Assert.Empty(superset.Diagnostics);
        Assert.NotNull(CharacterFile.Read(file, superset, problems));
        Assert.Empty(problems);

        ModuleSet classic = ModuleLoader.Load(Rules.ClassicPath, []);
        Assert.Null(CharacterFile.Read(file, classic, problems));
        ModuleDiagnostic mismatch = problems.First(problem => problem.JsonPath == "$.modules");
        Assert.Contains("ascend 0.1.0 is not loaded", mismatch.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddedExtensionsClassBringsItsOwnStartingBalancesAndIsStamped()
    {
        using TempModules modules = new();
        string degrees = Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "degrees");
        modules.Module("blades", "extension", requires: TempModules.Require("degrees", "^0.1.0"));
        string level = """{ "hp": "if self.level == 1 then 8 + self.ancestry_hp else 8", "hp_bonus": "self.stamina_mod" }""";
        string levels = string.Join(", ", Enumerable.Repeat(level, 5));
        modules.Write("blades/classes/duelist.json", $$"""{ "type": "class", "id": "duelist", "name": "Duelist", "levels": [ {{levels}} ], "boosts": [ { "from": ["finesse"] } ], "starting": { "degrees:gold": "20" } }""");
        modules.Write("blades/classes/drifter.json", $$"""{ "type": "class", "id": "drifter", "name": "Drifter", "levels": [ {{levels}} ], "boosts": [ { "from": ["finesse"] } ] }""");
        ModuleSet set = ModuleLoader.Load(degrees, [modules.Root, Path.GetDirectoryName(degrees)!], extensions: ["blades"]);
        Assert.Empty(set.Diagnostics);

        // The creation's starting map names only the fixture's own classes; the extension's class pays its own.
        string[] boosts = ["presence", "insight", "stamina", "finesse", "stamina", "insight", "brawn"];
        Character duelist = Create(set, new CreationRequest("Vex", "duelist", "sylvan", Features: ["duskwood", "scribe", "sylvan_step"], Boosts: boosts))!;
        Assert.Equal(20, duelist.Balances["gold"]);
        Assert.Equal(["degrees", "blades"], duelist.Modules.Select(module => module.Id));

        List<ModuleDiagnostic> problems = [];
        Assert.Null(WithDice(dice => CharacterRules.Create(set.Rules!, Character.StampsOf(set), new CreationRequest("Rook", "drifter", "sylvan", Features: ["duskwood", "scribe", "sylvan_step"], Boosts: boosts), dice, problems)));
        Assert.Contains(problems, problem => problem.Rule == "character.currency" && problem.Message.Contains("Give the class a \"starting\"", StringComparison.Ordinal));

        // Read back without the extension, the character is refused with the ID to add.
        string file = Path.Combine(modules.Root, "vex.json");
        File.WriteAllText(file, CharacterFile.ToJson(duelist));
        problems.Clear();
        Assert.Null(CharacterFile.Read(file, ModuleLoader.Load(degrees, []), problems));
        Assert.Contains(problems, problem => problem.Message.Contains("--extension blades", StringComparison.Ordinal));
    }

    [Fact]
    public void FailingRuleExpressionsNameTheirDefinition()
    {
        using TempModules modules = new();
        string ascend = CopyAscend(modules);
        modules.Write("ascend/creation/standard.json", File.ReadAllText(Path.Combine(Ascend, "creation", "standard.json")).Replace("roll_keep(4, 6, 3)", "roll_keep(4, 6, 5)", StringComparison.Ordinal));
        modules.Write("ascend/classes/adept.json", File.ReadAllText(Path.Combine(Ascend, "classes", "adept.json")).Replace("then 6 else", "then 6 / (self.grit_mod - self.grit_mod) else", StringComparison.Ordinal));
        ModuleSet set = ModuleLoader.Load(ascend, []);
        Assert.Empty(set.Diagnostics);

        ModuleDiagnostic roll = Assert.Single(ProblemDiagnostics(set, new CreationRequest("x", "warrior", "folk", Features: ["iron_will", "improved_initiative"])));
        ModuleDiagnostic hitPoints = Assert.Single(ProblemDiagnostics(set, new CreationRequest("x", "adept", "folk", Attributes: Scores(10, 10, 10, 12), Features: ["iron_will"])));

        Assert.Equal(("character.evaluate", "$.attribute_roll"), (roll.Rule, roll.JsonPath));
        Assert.EndsWith("standard.json", roll.File, StringComparison.Ordinal);
        Assert.Equal(("character.evaluate", "$.levels[0].hp"), (hitPoints.Rule, hitPoints.JsonPath));
        Assert.Contains("Division by zero", hitPoints.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HugeExperienceIsAProblemNotACrash()
    {
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        Character character = Create(set, new CreationRequest("x", "warrior", "folk", Attributes: Scores(12, 10, 10, 10), Features: ["iron_will", "improved_initiative"]))!;
        character.Experience = decimal.MaxValue - 1;
        List<ModuleDiagnostic> problems = [];

        Assert.Null(WithDice(dice => CharacterRules.AddExperience(set.Rules!, character, 10, dice, problems)));
        Assert.Equal("character.number", Assert.Single(problems).Rule);
    }

    [Fact]
    public void MalformedCharacterFilesNameTheBadField()
    {
        using TempModules modules = new();
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        string file = Path.Combine(modules.Root, "bad.json");
        File.WriteAllText(file, """
            { "format": 1, "name": 5, "modules": ["ascend"], "race": "folk", "levels": "x", "experience": -5,
              "attributes": { "might": 10, "grace": 10, "grit": 10, "wit": 10 }, "tracks": { "hit_points": { "max": 5, "current": 5 } }, "balances": { "gold": 0 } }
            """);
        List<ModuleDiagnostic> problems = [];

        Assert.Null(CharacterFile.Read(file, set, problems));

        Assert.Contains(problems, problem => problem.JsonPath == "$.modules[0]");
        Assert.Contains(problems, problem => problem.JsonPath == "$.name");
        Assert.All(problems, problem => Assert.Equal("character.file", problem.Rule));
        File.WriteAllText(file, """
            { "format": 1, "name": "x", "modules": [ { "id": "ascend", "version": "0.1.0" } ], "race": "folk", "creation": "standard", "levels": [ { "class": "warrior", "gain": 5, "features": [ "iron_will", "improved_initiative" ] } ], "experience": -5,
              "attributes": { "might": 10, "grace": 10, "grit": 10, "wit": 10 }, "tracks": { "hit_points": { "max": 5, "current": 5 }, "arcana_1": { "current": 0 } }, "balances": { "gold": 0 } }
            """);
        problems.Clear();
        Assert.Null(CharacterFile.Read(file, set, problems));
        Assert.Equal(["$.experience"], problems.Select(problem => problem.JsonPath));
    }

    private static string CopyAscend(TempModules modules)
    {
        string target = Path.Combine(modules.Root, "ascend");
        foreach (string file in Directory.EnumerateFiles(Ascend, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(Ascend, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return target;
    }

    private static List<ModuleDiagnostic> ProblemDiagnostics(ModuleSet set, CreationRequest request)
    {
        List<ModuleDiagnostic> problems = [];
        Assert.Null(WithDice(dice => CharacterRules.Create(set.Rules!, Character.StampsOf(set), request, dice, problems)));
        return problems;
    }

    private static decimal MaxHitPoints(ModuleSet set, Character character)
    {
        return CharacterSheet.Tracks(set.Rules!, character).Single(track => track.Track.Id == "hit_points").Max!.Value;
    }

    private static Dictionary<string, decimal> Scores(decimal might, decimal grace, decimal grit, decimal wit)
    {
        return new Dictionary<string, decimal> { ["might"] = might, ["grace"] = grace, ["grit"] = grit, ["wit"] = wit };
    }

    private static List<string> Problems(string module, CreationRequest request)
    {
        ModuleSet set = ModuleLoader.Load(module, []);
        List<ModuleDiagnostic> problems = [];
        Character? character = WithDice(dice => CharacterRules.Create(set.Rules!, Character.StampsOf(set), request, dice, problems));
        Assert.Null(character);
        return problems.Select(problem => problem.Rule).ToList();
    }

    private static Character? Create(ModuleSet set, CreationRequest request)
    {
        List<ModuleDiagnostic> problems = [];
        Character? character = WithDice(dice => CharacterRules.Create(set.Rules!, Character.StampsOf(set), request, dice, problems));
        Assert.Empty(problems);
        return character;
    }

    private static T WithDice<T>(Func<DiceRoller, T> work)
    {
        return WithDice(1, work);
    }

    private static T WithDice<T>(ulong seed, Func<DiceRoller, T> work)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(seed, "tests"));
            return work(new DiceRoller(engine.Random, stream));
        });
    }

    [Fact]
    public void ACharacterCanHaveAPortraitFromTheModuleSet()
    {
        using TempModules scratch = new();
        string crypt = Path.Combine(Rules.RepositoryRoot, "modules", "sample-crypt");
        (int code, string output) = CampaignTests.Run(scratch, "character", "new", "--module", crypt, "--class", "fighter", "--race", "human", "--name", "Ada",
            "--attributes", "str=16,dex=13,con=15,int=10,wis=9,cha=11", "--portrait", "placeholder-art:fighter_portrait", "--out", "ada.json");
        Assert.True(code == 0, output);
        Assert.Contains("portrait placeholder-art:fighter_portrait", output, StringComparison.Ordinal);
        Assert.Contains("\"portrait\": \"placeholder-art:fighter_portrait\"", File.ReadAllText(Path.Combine(scratch.Root, "ada.json")), StringComparison.Ordinal);

        // Saves carry it with the party.
        File.WriteAllText(Path.Combine(scratch.Root, "look.script"), "look\n");
        Assert.Equal(0, CampaignTests.Run(scratch, "play", "--campaign", crypt, "--party", "ada.json", "--script", "look.script", "--save", "game.json").Code);
        Assert.Contains("placeholder-art:fighter_portrait", File.ReadAllText(Path.Combine(scratch.Root, "game.json")), StringComparison.Ordinal);

        // A portrait is a picture slot: any visual media fits, an animated sheet as well as an image.
        (int sheet, string drawn) = CampaignTests.Run(scratch, "character", "new", "--module", crypt, "--class", "fighter", "--race", "human",
            "--attributes", "str=16,dex=13,con=15,int=10,wis=9,cha=11", "--portrait", "placeholder-art:skeleton");
        Assert.True(sheet == 0, drawn);
        (int refused, string message) = CampaignTests.Run(scratch, "character", "new", "--module", crypt, "--class", "fighter", "--race", "human",
            "--attributes", "str=16,dex=13,con=15,int=10,wis=9,cha=11", "--portrait", "placeholder-art:nowhere");
        Assert.Equal(1, refused);
        Assert.Contains("placeholder-art:nowhere", message, StringComparison.Ordinal);

        string edited = File.ReadAllText(Path.Combine(scratch.Root, "ada.json")).Replace("fighter_portrait", "nowhere", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(scratch.Root, "ada.json"), edited);
        (int unread, string why) = CampaignTests.Run(scratch, "character", "show", "ada.json", "--module", crypt);
        Assert.Equal(1, unread);
        Assert.Contains("$.portrait", why, StringComparison.Ordinal);
    }
}
