using System.Text.Json.Nodes;
using RustyGoldbox.Cli;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

public sealed class LifepathFileTests
{
    private static string Fixture => Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "lifepath");

    [Fact]
    public void RejectsAgeThatDoesNotMatchTheLastCareerTerm()
    {
        using TempModules scratch = new();
        string path = CreateCharacter(scratch, terms: 1);
        JsonObject edited = ReadJson(path);
        edited["age"] = 99;
        WriteJson(path, edited);

        List<ModuleDiagnostic> problems = ReadProblems(path);

        Assert.Contains(problems, problem => problem.JsonPath == "$.age" && problem.Message.Contains("last career term", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsNonSequentialAndDisconnectedCareerTerms()
    {
        using TempModules scratch = new();
        string path = CreateCharacter(scratch, terms: 2);
        JsonObject edited = ReadJson(path);
        JsonObject second = edited["career_terms"]!.AsArray()[1]!.AsObject();
        second["number"] = 3;
        second["age_before"] = 99;
        WriteJson(path, edited);

        List<ModuleDiagnostic> problems = ReadProblems(path);

        Assert.Contains(problems, problem => problem.JsonPath == "$.career_terms[1].number" && problem.Message.Contains("sequential", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.JsonPath == "$.career_terms[1].age_before" && problem.Message.Contains("continuous", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsFirstCareerTermThatDoesNotStartAtTheLifepathAge()
    {
        using TempModules scratch = new();
        string path = CreateCharacter(scratch, terms: 1);
        JsonObject edited = ReadJson(path);
        edited["career_terms"]![0]!["age_before"] = 17;
        WriteJson(path, edited);

        List<ModuleDiagnostic> problems = ReadProblems(path);

        Assert.Contains(problems, problem => problem.JsonPath == "$.career_terms[0].age_before" && problem.Message.Contains("start age 18", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsARecordedLifepathRollWhoseTotalIsNotRollPlusModifier()
    {
        using TempModules scratch = new();
        string path = CreateCharacter(scratch, terms: 1);
        JsonObject edited = ReadJson(path);
        JsonObject qualification = edited["career_terms"]![0]!["qualification"]!.AsObject();
        qualification["total"] = qualification["total"]!.GetValue<decimal>() + 1;
        WriteJson(path, edited);

        List<ModuleDiagnostic> problems = ReadProblems(path);

        Assert.Contains(problems, problem => problem.JsonPath == "$.career_terms[0].qualification.total" && problem.Message.Contains("roll + modifier", StringComparison.Ordinal));
    }

    private static string CreateCharacter(TempModules scratch, int terms)
    {
        string path = Path.Combine(scratch.Root, $"lifepath-{terms}.json");
        string[] tables = Enumerable.Repeat("professional", terms * 2).ToArray();
        string[] benefits = Enumerable.Repeat("cash", terms).ToArray();
        string transcript = CliTranscript.Run(scratch.Root,
            [
                "character", "new", "--module", Fixture, "--name", "Saved", "--lifepath", "careers", "--career", "maker",
                "--terms", terms.ToString(System.Globalization.CultureInfo.InvariantCulture), "--skill-table", string.Join(',', tables),
                "--benefit", string.Join(',', benefits), "--seed", "2", "--out", Path.GetFileName(path), "--json",
            ]);
        Assert.Contains("[exit 0]", transcript, StringComparison.Ordinal);
        return path;
    }

    private static JsonObject ReadJson(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    private static void WriteJson(string path, JsonObject value)
    {
        File.WriteAllText(path, value.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<ModuleDiagnostic> ReadProblems(string path)
    {
        ModuleSet set = ModuleLoader.Load(Fixture, []);
        List<ModuleDiagnostic> problems = [];
        Assert.Null(CharacterFile.Read(path, set, problems));
        return problems;
    }
}
