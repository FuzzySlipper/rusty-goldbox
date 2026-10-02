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
            ["character", "level", "brom.json", "--module", Rules.ClassicPath, "--xp", "5000", "--seed", "3"],
            ["character", "new", "--module", Rules.ClassicPath, "--class", "magic_user", "--race", "dwarf", "--seed", "1"]));
    }

    [Fact]
    public void ClassicMultiClassedAndDualClassedCharacters()
    {
        Golden.Verify("classic-multiclass.txt", CliTranscript.Run(
            ["character", "new", "--module", Rules.ClassicPath, "--class", "fighter,magic_user", "--race", "dwarf", "--name", "Nain", "--attributes", "str=15,dex=14,con=15,int=10,wis=10,cha=9"],
            ["character", "new", "--module", Rules.ClassicPath, "--class", "fighter,thief", "--race", "dwarf", "--name", "Thror", "--attributes", "str=15,dex=14,con=15,int=10,wis=10,cha=9", "--seed", "4", "--out", "thror.json"],
            // Experience divides evenly: 2500 each takes the thief to level 3 but the fighter only to 2.
            ["character", "level", "thror.json", "--module", Rules.ClassicPath, "--xp", "5000", "--seed", "3"],
            ["character", "new", "--module", Rules.ClassicPath, "--class", "fighter", "--race", "human", "--name", "Aldo", "--attributes", "str=16,dex=12,con=13,int=17,wis=10,cha=10", "--out", "aldo.json"],
            ["character", "level", "aldo.json", "--module", Rules.ClassicPath, "--xp", "4500", "--class", "thief", "--seed", "2"],
            ["character", "level", "aldo.json", "--module", Rules.ClassicPath, "--xp", "4500", "--seed", "2"],
            // A dual-classed fighter starts magic user at level 1, gains no hit points until passing fighter 3,
            // and fights with the magic user's table until then.
            ["character", "level", "aldo.json", "--module", Rules.ClassicPath, "--xp", "0", "--class", "magic_user", "--seed", "5"],
            ["character", "level", "aldo.json", "--module", Rules.ClassicPath, "--xp", "20000", "--seed", "6"],
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
        Assert.Equal(["character.priority"], Problems(Ascend, new CreationRequest("x", "warrior", "folk", Priority: ["might", "grace"])));
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

        Assert.Null(WithDice(dice => CharacterRules.AddExperience(set.Rules!, fighter, 5000, dice, problems, "thief")));
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
              "attributes": { "might": 10, "grace": 10, "grit": 10, "wit": 10 }, "tracks": { "hit_points": { "max": 5, "current": 5 } }, "gold": 0 }
            """);
        List<ModuleDiagnostic> problems = [];

        Assert.Null(CharacterFile.Read(file, set, problems));

        Assert.Contains(problems, problem => problem.JsonPath == "$.modules[0]");
        Assert.Contains(problems, problem => problem.JsonPath == "$.name");
        Assert.All(problems, problem => Assert.Equal("character.file", problem.Rule));
        File.WriteAllText(file, """
            { "format": 1, "name": "x", "modules": [ { "id": "ascend", "version": "0.1.0" } ], "race": "folk", "creation": "standard", "levels": [ { "class": "warrior", "gain": 5, "features": [ "iron_will", "improved_initiative" ] } ], "experience": -5,
              "attributes": { "might": 10, "grace": 10, "grit": 10, "wit": 10 }, "tracks": { "hit_points": { "max": 5, "current": 5 } }, "gold": 0 }
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
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(1, "tests"));
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

        (int refused, string message) = CampaignTests.Run(scratch, "character", "new", "--module", crypt, "--class", "fighter", "--race", "human",
            "--attributes", "str=16,dex=13,con=15,int=10,wis=9,cha=11", "--portrait", "placeholder-art:hall");
        Assert.Equal(1, refused);
        Assert.Contains("placeholder-art:hall is a backdrop asset, but a character's portrait must be a portrait.", message, StringComparison.Ordinal);

        string edited = File.ReadAllText(Path.Combine(scratch.Root, "ada.json")).Replace("fighter_portrait", "skull", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(scratch.Root, "ada.json"), edited);
        (int unread, string why) = CampaignTests.Run(scratch, "character", "show", "ada.json", "--module", crypt);
        Assert.Equal(1, unread);
        Assert.Contains("$.portrait", why, StringComparison.Ordinal);
    }
}
