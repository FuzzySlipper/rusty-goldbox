using System.Text.Json;
using RustyGoldbox.Cli;

namespace RustyGoldbox.Tests;

public sealed class CliTests
{
    [Fact]
    public void NewModulesValidateAndResolve()
    {
        using TempModules modules = new();
        Assert.Equal(GoldboxCli.Ok, Run(modules, "module", "new", "ruleset", "classic").Code);
        Assert.Equal(GoldboxCli.Ok, Run(modules, "module", "new", "assets", "crypt-art").Code);
        Assert.Equal(GoldboxCli.Ok, Run(modules, "module", "new", "campaign", "sample", "--require", "classic@^0.1.0", "--require", "crypt-art@*").Code);

        (int code, string output) = Run(modules, "module", "deps", "sample", "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(output);
        Assert.Equal(
            ["classic", "crypt-art", "sample"],
            json.RootElement.GetProperty("loadOrder").EnumerateArray().Select(module => module.GetProperty("id").GetString()));
    }

    [Fact]
    public void NewModuleGoesIntoTheWorkspaceModulesDirectory()
    {
        using TempModules modules = new();
        modules.Write("goldbox.json", """{ "modules": ["mods"] }""");
        Directory.CreateDirectory(Path.Combine(modules.Root, "mods"));

        Assert.Equal(GoldboxCli.Ok, Run(modules, "module", "new", "ruleset", "classic").Code);

        Assert.True(File.Exists(Path.Combine(modules.Root, "mods", "classic", "module.json")));
    }

    [Fact]
    public void InvalidModuleReportsJsonDiagnostics()
    {
        using TempModules modules = new();
        modules.Manifest("bad", """{ "format": 1 }""");

        (int code, string output) = Run(modules, "module", "validate", "bad", "--json");

        Assert.Equal(GoldboxCli.Invalid, code);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement first = json.RootElement.GetProperty("diagnostics")[0];
        Assert.Equal("manifest.field-required", first.GetProperty("rule").GetString());
        Assert.Equal("bad/module.json", first.GetProperty("file").GetString());
    }

    [Fact]
    public void NewModuleReportsTargetsItCannotUse()
    {
        using TempModules modules = new();
        modules.Write("taken", "");
        modules.Write("afile", "");

        Assert.Contains("module.exists", Run(modules, "module", "new", "ruleset", "taken", "--dir", ".").Output, StringComparison.Ordinal);
        Assert.Contains("module.create", Run(modules, "module", "new", "ruleset", "x", "--dir", "afile").Output, StringComparison.Ordinal);
    }

    [Fact]
    public void BadArgumentsAreUsageErrors()
    {
        using TempModules modules = new();

        Assert.Equal(GoldboxCli.Usage, Run(modules, "module", "new", "spellbook", "x").Code);
        Assert.Equal(GoldboxCli.Usage, Run(modules, "module", "validate").Code);
        Assert.Equal(GoldboxCli.Usage, Run(modules, "module", "deps", "x", "--bogus").Code);
        Assert.Equal(GoldboxCli.Usage, Run(modules, "module", "new", "ruleset", "x", "--title", " ").Code);
    }

    [Fact]
    public void EvalAnswersAFightersThac0()
    {
        (int code, string output) = RunInRepository("eval", "self.thac0", "--module", "modules/classic", "--context", """{"self": {"class": "fighter", "level": 5}}""", "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(output);
        Assert.Equal(16, json.RootElement.GetProperty("value").GetDecimal());
        Assert.Equal("number", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void EvalRunsChecksWithSeededRolls()
    {
        string[] args = ["eval", "--check", "attack", "--module", "modules/classic", "--seed", "7", "--json",
            "--context", """{"self": {"class": "fighter", "level": 5, "str": 17}, "target": {"monster": "ogre"}}"""];

        (int code, string output) = RunInRepository(args);
        (_, string again) = RunInRepository(args);

        Assert.Equal(GoldboxCli.Ok, code);
        Assert.Equal(output, again);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement result = json.RootElement;
        Assert.Equal(11, result.GetProperty("target").GetDecimal());
        Assert.Equal(result.GetProperty("rolls")[0].GetProperty("total").GetDecimal() + 1, result.GetProperty("roll").GetDecimal());
    }

    [Fact]
    public void EvalPointsAtTheBrokenPartOfAnExpression()
    {
        (int code, string output) = RunInRepository("eval", "3d6 + self.lvl", "--module", "modules/classic");

        Assert.Equal(GoldboxCli.Usage, code);
        Assert.Contains("column 7", output, StringComparison.Ordinal);
        Assert.Contains("'lvl' is not a stat", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EvalRejectsContextValuesThatDontFit()
    {
        (int code, string output) = RunInRepository("eval", "self.str", "--module", "modules/classic", "--context", """{"self": {"str": 1e400}}""");

        Assert.Equal(GoldboxCli.Usage, code);
        Assert.Contains("self.str must be a number no larger than", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaDescribesEveryDefinitionType()
    {
        (int code, string output) = RunInRepository("schema", "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(output);
        Assert.Equal(
            Core.Definitions.DefinitionTypes.All.Select(type => type.Name),
            json.RootElement.GetProperty("types").EnumerateArray().Select(type => type.GetProperty("name").GetString()));
        Assert.Contains("levels (required)", RunInRepository("schema", "class").Output, StringComparison.Ordinal);
        Assert.Contains("table(id, key, ...)", RunInRepository("schema", "expressions").Output, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectShowsADefinitionWithItsExpressionTypes()
    {
        (int code, string output) = RunInRepository("module", "inspect", "modules/classic", "classic:ac", "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement definition = json.RootElement.GetProperty("definitions")[0];
        Assert.Equal("derived", definition.GetProperty("type").GetString());
        Assert.Equal("number", definition.GetProperty("expressions")[0].GetProperty("type").GetString());
    }

    private static (int Code, string Output) RunInRepository(params string[] args)
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(args, output, Rules.RepositoryRoot);
        return (code, output.ToString());
    }

    private static (int Code, string Output) Run(TempModules modules, params string[] args)
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(args, output, modules.Root);
        return (code, output.ToString());
    }
}
