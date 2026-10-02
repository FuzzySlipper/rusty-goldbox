using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void ValidManifestReads()
    {
        using TempModules modules = new();
        string directory = modules.Module("classic", "ruleset");

        List<ModuleDiagnostic> diagnostics = [];
        ModuleManifest? manifest = ManifestReader.Read(directory, diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(manifest);
        Assert.Equal("classic", manifest.Id);
        Assert.Equal(ModuleKind.Ruleset, manifest.Kind);
        Assert.Equal(new ModuleVersion(0, 1, 0), manifest.Version);
    }

    [Fact]
    public void BadManifestReportsEveryProblemWithItsPath()
    {
        using TempModules modules = new();
        string directory = modules.Manifest("broken", """
            {
              "format": 2,
              "id": "broken",
              "kind": "rules",
              "version": "1.0",
              "title": "",
              "requires": [ { "id": "classic", "version": "1.x" }, "classic", { "id": "broken", "version": "*" } ],
              "colour": "red"
            }
            """);

        List<ModuleDiagnostic> diagnostics = [];
        ModuleManifest? manifest = ManifestReader.Read(directory, diagnostics);

        Assert.Null(manifest);
        Assert.All(diagnostics, diagnostic => Assert.Equal("broken", diagnostic.Module));
        Assert.All(diagnostics, diagnostic => Assert.EndsWith("module.json", diagnostic.File));
        Assert.Equal(
            [
                ("manifest.unknown-field", "$.colour"),
                ("manifest.format", "$.format"),
                ("manifest.kind", "$.kind"),
                ("manifest.version", "$.version"),
                ("manifest.field-empty", "$.title"),
                ("requires.range", "$.requires[0].version"),
                ("manifest.field-type", "$.requires[1]"),
                ("requires.self", "$.requires[2].id"),
                ("manifest.field-required", "$"),
            ],
            diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)));
        Assert.Contains("provenance", diagnostics[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SyntaxErrorNamesLineAndColumn()
    {
        using TempModules modules = new();
        string directory = modules.Manifest("syntax", "{\n  \"format\": 1,\n  \"id\": \"syntax\",\n}\n");

        List<ModuleDiagnostic> diagnostics = [];
        ManifestReader.Read(directory, diagnostics);

        ModuleDiagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("json.syntax", diagnostic.Rule);
        Assert.Contains("line 4", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingManifestPointsAtModuleNew()
    {
        using TempModules modules = new();
        string directory = modules.Write("empty/readme.txt", "");

        List<ModuleDiagnostic> diagnostics = [];
        ManifestReader.Read(directory, diagnostics);

        ModuleDiagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("manifest.missing", diagnostic.Rule);
        Assert.Contains("goldbox module new", diagnostic.Message, StringComparison.Ordinal);
    }
}
