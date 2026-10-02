using System.Text.Json;
using RustyGoldbox.Cli;

namespace RustyGoldbox.Tests;

public sealed class CliTests
{
    [Fact]
    public void NewModulesValidateAndResolve()
    {
        using TempModules modules = new();
        Assert.Equal(GoldboxCli.Ok, Run(modules, "module", "new", "ruleset", "osric").Code);
        Assert.Equal(GoldboxCli.Ok, Run(modules, "module", "new", "assets", "crypt-art").Code);
        Assert.Equal(GoldboxCli.Ok, Run(modules, "module", "new", "campaign", "sample", "--require", "osric@^0.1.0", "--require", "crypt-art@*").Code);

        (int code, string output) = Run(modules, "module", "deps", "sample", "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(output);
        Assert.Equal(
            ["osric", "crypt-art", "sample"],
            json.RootElement.GetProperty("loadOrder").EnumerateArray().Select(module => module.GetProperty("id").GetString()));
    }

    [Fact]
    public void NewModuleGoesIntoTheWorkspaceModulesDirectory()
    {
        using TempModules modules = new();
        modules.Write("goldbox.json", """{ "modules": ["mods"] }""");
        Directory.CreateDirectory(Path.Combine(modules.Root, "mods"));

        Assert.Equal(GoldboxCli.Ok, Run(modules, "module", "new", "ruleset", "osric").Code);

        Assert.True(File.Exists(Path.Combine(modules.Root, "mods", "osric", "module.json")));
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

    private static (int Code, string Output) Run(TempModules modules, params string[] args)
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(args, output, modules.Root);
        return (code, output.ToString());
    }
}
