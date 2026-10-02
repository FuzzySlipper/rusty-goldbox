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

    [Fact]
    public void ClassicFighterIsCreatedAndLevelled()
    {
        Golden.Verify("classic-character.txt", CliTranscript.Run(
            ["character", "new", "--module", Rules.ClassicPath, "--class", "fighter", "--race", "dwarf", "--name", "Brom", "--seed", "11", "--out", "brom.json"],
            ["character", "level", "brom.json", "--module", Rules.ClassicPath, "--xp", "5000", "--seed", "3"],
            ["character", "new", "--module", Rules.ClassicPath, "--class", "magic_user", "--race", "dwarf", "--seed", "1"]));
    }

    [Fact]
    public void AscendingArmourClassRulesetUsesTheSameCommands()
    {
        Golden.Verify("ascend-character.txt", CliTranscript.Run(
            ["character", "new", "--module", Ascend, "--class", "warrior", "--race", "stoneborn", "--name", "Kara", "--priority", "might,grit,grace,wit", "--seed", "5", "--out", "kara.json"],
            ["character", "level", "kara.json", "--module", Ascend, "--xp", "3500", "--seed", "8"],
            ["character", "new", "--module", Ascend, "--class", "adept", "--race", "folk", "--name", "Ilse", "--attributes", "might=9,grace=12,grit=10,wit=16", "--out", "ilse.json"],
            ["character", "show", "ilse.json", "--module", Ascend],
            ["character", "show", "ilse.json", "--module", Ascend, "--json"]));
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
    }

    [Fact]
    public void LevellingStopsAtTheLastLevel()
    {
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        Character character = Create(set, new CreationRequest("x", "warrior", "folk", Attributes: Scores(12, 10, 10, 10)))!;

        List<ModuleDiagnostic> problems = [];
        List<LevelGain>? gains = WithDice(dice => CharacterRules.AddExperience(set.Rules!, character, 1_000_000, dice, problems));

        Assert.Empty(problems);
        Assert.Equal([2, 3, 4, 5], gains!.Select(gain => gain.Level));
        Assert.Equal(5, character.Level);
        Assert.Null(character.NextLevelExperience());
        Assert.Equal(character.LevelGains.Sum(), character.Tracks["hit_points"].Max);
    }

    [Fact]
    public void CharacterFilesRoundTripAndNeedTheirModules()
    {
        using TempModules modules = new();
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        Character character = Create(set, new CreationRequest("Ilse", "adept", "folk", Attributes: Scores(9, 12, 10, 16)))!;
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
        modules.Write("ascend/classes/adept.json", File.ReadAllText(Path.Combine(Ascend, "classes", "adept.json")).Replace("6 + self.grit_mod", "6 / (self.grit_mod - self.grit_mod)", StringComparison.Ordinal));
        ModuleSet set = ModuleLoader.Load(ascend, []);
        Assert.Empty(set.Diagnostics);

        ModuleDiagnostic roll = Assert.Single(ProblemDiagnostics(set, new CreationRequest("x", "warrior", "folk")));
        ModuleDiagnostic hitPoints = Assert.Single(ProblemDiagnostics(set, new CreationRequest("x", "adept", "folk", Attributes: Scores(10, 10, 10, 12))));

        Assert.Equal(("character.evaluate", "$.attribute_roll"), (roll.Rule, roll.JsonPath));
        Assert.EndsWith("standard.json", roll.File, StringComparison.Ordinal);
        Assert.Equal(("character.evaluate", "$.levels[0].hp"), (hitPoints.Rule, hitPoints.JsonPath));
        Assert.Contains("Division by zero", hitPoints.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HugeExperienceIsAProblemNotACrash()
    {
        ModuleSet set = ModuleLoader.Load(Ascend, []);
        Character character = Create(set, new CreationRequest("x", "warrior", "folk", Attributes: Scores(12, 10, 10, 10)))!;
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
            { "format": 1, "name": 5, "modules": ["ascend"], "race": "folk", "class": "warrior", "level": 2, "experience": -5,
              "attributes": { "might": 10, "grace": 10, "grit": 10, "wit": 10 }, "tracks": { "hit_points": { "max": 5, "current": 5 } }, "level_gains": "x", "gold": 0 }
            """);
        List<ModuleDiagnostic> problems = [];

        Assert.Null(CharacterFile.Read(file, set, problems));

        Assert.Contains(problems, problem => problem.JsonPath == "$.modules[0]");
        Assert.Contains(problems, problem => problem.JsonPath == "$.name");
        Assert.All(problems, problem => Assert.Equal("character.file", problem.Rule));
        File.WriteAllText(file, """
            { "format": 1, "name": "x", "modules": [ { "id": "ascend", "version": "0.1.0" } ], "race": "folk", "class": "warrior", "level": 2, "experience": -5,
              "attributes": { "might": 10, "grace": 10, "grit": 10, "wit": 10 }, "tracks": { "hit_points": { "max": 5, "current": 5 } }, "level_gains": [5], "gold": 0 }
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
}
