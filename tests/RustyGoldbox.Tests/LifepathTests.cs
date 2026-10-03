using System.Text.Json;
using RustyGoldbox.Cli;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

public sealed class LifepathTests
{
    private static string Fixture => Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "lifepath");

    private static string Scifi => Path.Combine(Rules.RepositoryRoot, "modules", "scifi-2d6");

    [Fact]
    public void OriginalFixtureRunsTwoSkillTablesAndPersistsTheTermLedger()
    {
        using TempModules scratch = new();
        string transcript = CliTranscript.Run(scratch.Root,
            ["character", "new", "--module", Fixture, "--name", "Original", "--lifepath", "careers", "--career", "maker", "--terms", "1",
                "--skill-table", "professional", "--benefit", "material", "--seed", "2", "--out", "maker.json", "--json"],
            ["character", "show", "maker.json", "--module", Fixture, "--json"]);

        Assert.Contains("[exit 0]", transcript, StringComparison.Ordinal);
        ModuleSet set = ModuleLoader.Load(Fixture, []);
        List<ModuleDiagnostic> problems = [];
        Character character = CharacterFile.Read(Path.Combine(scratch.Root, "maker.json"), set, problems)!;

        Assert.Empty(problems);
        Assert.Equal(22, character.Age);
        LifepathTerm term = Assert.Single(character.CareerTerms);
        Assert.Equal(["skill:professional", "benefit:material"], term.Choices);
        Assert.Contains(term.Results, result => result == "benefit attribute body +1");
        Assert.Equal(1, character.StatBonuses["craft"]);
        Assert.Empty(character.Equipment);
        Assert.NotNull(term.Qualification);
        Assert.NotNull(term.Survival);
        Assert.NotNull(term.Aging);
        Assert.Equal(0, term.Aging!.Modifier);
    }

    [Fact]
    public void FailedSurvivalEndsLicensedCareerAndLosesBenefits()
    {
        using TempModules scratch = new();
        string transcript = CliTranscript.Run(scratch.Root,
            ["character", "new", "--module", Scifi, "--name", "Retired", "--attributes", "str=10,dex=10,end=10,int=10,edu=10,soc=10",
                "--lifepath", "prior_history", "--career", "scout", "--terms", "1", "--skill-table", "personal", "--benefit", "cash", "--seed", "5", "--out", "retired.json", "--json"],
            ["character", "show", "retired.json", "--module", Scifi, "--json"]);

        Assert.Contains("[exit 0]", transcript, StringComparison.Ordinal);
        Assert.Contains("\"benefits_lost\": true", transcript, StringComparison.Ordinal);
        Assert.Contains("\"success\": false", transcript, StringComparison.Ordinal);
        Assert.Contains("survival failed; career ended", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public void OneSkillAndBenefitChoiceRepeatAcrossPolicyRolls()
    {
        using TempModules scratch = new();
        string transcript = CliTranscript.Run(scratch.Root,
            ["character", "new", "--module", Scifi, "--name", "Vance", "--priority", "end,dex,str,int,edu,soc",
                "--lifepath", "prior_history", "--career", "marine", "--terms", "1", "--skill-table", "personal",
                "--benefit", "cash", "--seed", "3", "--out", "vance.json", "--json"]);

        Assert.Contains("[exit 0]", transcript, StringComparison.Ordinal);
        ModuleSet set = ModuleLoader.Load(Scifi, []);
        List<ModuleDiagnostic> problems = [];
        Character character = CharacterFile.Read(Path.Combine(scratch.Root, "vance.json"), set, problems)!;

        Assert.Empty(problems);
        LifepathTerm term = Assert.Single(character.CareerTerms);
        Assert.Equal(["skill:personal", "skill:personal", "skill:personal", "benefit:cash"], term.Choices);
    }

    [Fact]
    public void LicensedCareerRecordsCommissionAndAdvancementRolls()
    {
        using TempModules scratch = new();
        string transcript = CliTranscript.Run(scratch.Root,
            ["character", "new", "--module", Scifi, "--name", "Vance", "--priority", "end,dex,str,int,edu,soc",
                "--lifepath", "prior_history", "--career", "marine", "--terms", "1", "--skill-table", "service,service,service",
                "--benefit", "cash", "--seed", "3", "--out", "vance.json", "--json"]);

        Assert.Contains("[exit 0]", transcript, StringComparison.Ordinal);
        ModuleSet set = ModuleLoader.Load(Scifi, []);
        List<ModuleDiagnostic> problems = [];
        Character character = CharacterFile.Read(Path.Combine(scratch.Root, "vance.json"), set, problems)!;

        Assert.Empty(problems);
        LifepathTerm term = Assert.Single(character.CareerTerms);
        Assert.Equal(2, term.RankAfter);
        Assert.True(term.Qualification?.Success);
        Assert.True(term.Survival?.Success);
        Assert.True(term.Commission?.Success);
        Assert.True(term.Advancement?.Success);
        Assert.Contains(term.Results, result => result == "skill tactics +1");
        Assert.Equal(1, character.StatBonuses["tactics"]);
    }

    [Fact]
    public void RepeatedCareerOnlyQualifiesOnceAndAgesEachTerm()
    {
        using TempModules scratch = new();
        string transcript = CliTranscript.Run(scratch.Root,
            ["character", "new", "--module", Fixture, "--name", "Multi", "--lifepath", "careers", "--career", "maker",
                "--terms", "2", "--skill-table", "professional", "--benefit", "cash",
                "--seed", "2", "--out", "multi.json", "--json"]);

        Assert.Contains("[exit 0]", transcript, StringComparison.Ordinal);
        ModuleSet set = ModuleLoader.Load(Fixture, []);
        List<ModuleDiagnostic> problems = [];
        Character character = CharacterFile.Read(Path.Combine(scratch.Root, "multi.json"), set, problems)!;

        Assert.Empty(problems);
        Assert.Equal(26, character.Age);
        Assert.Equal(2, character.CareerTerms.Count);
        Assert.NotNull(character.CareerTerms[0].Qualification);
        Assert.Null(character.CareerTerms[1].Qualification);
        Assert.NotNull(character.CareerTerms[0].Aging);
        Assert.NotNull(character.CareerTerms[1].Aging);
        Assert.Equal(100, character.Balances["credits"]);
    }

    [Fact]
    public void LifepathSchemaAndMissingChoiceExplainTheCliSurface()
    {
        (int code, string output) = RunInRepository("schema", "lifepath", "--json");
        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument schema = JsonDocument.Parse(output);
        Assert.Contains(schema.RootElement.GetProperty("fields").EnumerateArray(), field => field.GetProperty("name").GetString() == "careers");
        Assert.Contains("qualification", schema.RootElement.GetProperty("example").GetRawText(), StringComparison.Ordinal);

        (code, output) = RunInRepository("character", "new", "--module", Fixture, "--name", "Missing", "--lifepath", "careers", "--json");
        Assert.Equal(GoldboxCli.Invalid, code);
        Assert.Contains("character.lifepath-career", output, StringComparison.Ordinal);
        Assert.Contains("--career", output, StringComparison.Ordinal);
    }

    private static (int Code, string Output) RunInRepository(params string[] args)
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(args, output, Rules.RepositoryRoot);
        return (code, output.ToString());
    }
}
