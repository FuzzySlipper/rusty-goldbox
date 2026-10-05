using System.Text.Json;
using RustyGoldbox.Cli;
using RustyGoldbox.Core.Authoring;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public void ModuleNewRejectsAMissingConfiguredWorkspaceDirectory()
    {
        using TempModules scratch = new();
        scratch.Write("goldbox.json", """
            { "modules": ["missing-modules"] }
            """);
        using StringWriter output = new();

        int code = GoldboxCli.Run(["module", "new", "ruleset", "oops", "--json"], output, scratch.Root);

        Assert.Equal(GoldboxCli.Invalid, code);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("search.not-found", diagnostic.GetProperty("rule").GetString());
        Assert.Equal("goldbox.json", diagnostic.GetProperty("file").GetString());
        Assert.Equal("$.modules[0]", diagnostic.GetProperty("jsonPath").GetString());
        Assert.False(Directory.Exists(Path.Combine(scratch.Root, "oops")));
        Assert.False(Directory.Exists(Path.Combine(scratch.Root, "missing-modules", "oops")));
    }

    [Fact]
    public void CliWorkspaceInspectMissingManifestNamesTheFix()
    {
        using TempModules scratch = new();
        using StringWriter output = new();

        int code = GoldboxCli.Run(["workspace", "inspect", "--json"], output, scratch.Root);

        Assert.Equal(GoldboxCli.Invalid, code);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        Assert.Equal("workspace.not-found", json.RootElement.GetProperty("diagnostics")[0].GetProperty("rule").GetString());
        Assert.Contains("goldbox workspace new", json.RootElement.GetProperty("diagnostics")[0].GetProperty("message").GetString());
    }

    [Fact]
    public void WorkspaceSchemaDescribesAuthoringFieldsAndEditableRoots()
    {
        using TempModules scratch = new();
        using StringWriter output = new();

        int code = GoldboxCli.Run(["schema", "workspace", "--json"], output, scratch.Root);

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        Assert.Equal("goldbox.json", json.RootElement.GetProperty("file").GetString());
        Assert.Contains(json.RootElement.GetProperty("fields").EnumerateArray(), field => field.GetProperty("name").GetString() == "authoring");
        Assert.Contains("canon", json.RootElement.GetProperty("editableDirectories").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(".goldbox/staged", json.RootElement.GetProperty("generatedDirectories")[0].GetString());
    }

    [Fact]
    public void ScaffoldCreatesTheEditableAndGeneratedWorkspaceLayout()
    {
        using TempModules scratch = new();
        List<ModuleDiagnostic> diagnostics = [];

        Workspace? workspace = WorkspaceScaffold.Create(Path.Combine(scratch.Root, "campaign"), diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(workspace);
        Assert.Equal(["modules"], workspace!.ModulePaths.Select(path => path.Entry));
        Assert.Empty(workspace.Authoring!.ModulePaths);
        Assert.Equal(".goldbox/staged", workspace.Authoring.Staging.Entry);
        Assert.Equal("exports", workspace.Authoring.Exports.Entry);
        foreach (string directory in Workspace.EditableDirectoryNames.Append("modules").Append(".goldbox/staged").Append("exports"))
        {
            Assert.True(Directory.Exists(Path.Combine(workspace.RootDirectory, directory)), directory);
        }
    }

    [Fact]
    public void CliWorkspaceInspectListsAuthoredModulesAndEditableRoots()
    {
        using TempModules scratch = new();
        string workspaceRoot = Path.Combine(scratch.Root, "campaign");
        List<ModuleDiagnostic> createdDiagnostics = [];
        Workspace workspace = WorkspaceScaffold.Create(workspaceRoot, createdDiagnostics)!;
        ModuleScaffold.Create(Path.Combine(workspaceRoot, "modules"), ModuleKind.Campaign, "brugh", "Brugh", "Original content.", [], []);
        File.WriteAllText(workspace.ManifestPath, """
            {
              "modules": ["modules"],
              "authoring": {
                "modules": ["modules/brugh"],
                "staging": ".goldbox/staged",
                "exports": "exports"
              }
            }
            """);

        using StringWriter output = new();
        int code = GoldboxCli.Run(["workspace", "inspect", workspaceRoot, "--json"], output, scratch.Root);

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement inspection = json.RootElement.GetProperty("workspace");
        Assert.Equal("campaign/modules/brugh", inspection.GetProperty("authoring").GetProperty("modules")[0].GetProperty("path").GetString());
        Assert.Equal("brugh", inspection.GetProperty("authoring").GetProperty("modules")[0].GetProperty("id").GetString());
        Assert.Contains(inspection.GetProperty("editable").EnumerateArray(), directory => directory.GetProperty("name").GetString() == "canon" && directory.GetProperty("exists").GetBoolean());
        Assert.Equal("campaign/.goldbox/staged", inspection.GetProperty("authoring").GetProperty("staging").GetString());
    }

    [Fact]
    public void ModulesOnlyWorkspaceRemainsValidAndAuthoringJsonStaysOutsideRuntimeModules()
    {
        using TempModules scratch = new();
        string module = scratch.Module("rules", "ruleset", directory: "modules/rules");
        scratch.Write("goldbox.json", """
            { "modules": ["modules"] }
            """);
        scratch.Write("canon/story.json", """{ "title": "Editable story" }""");

        List<ModuleDiagnostic> diagnostics = [];
        Workspace? workspace = Workspace.Find(Path.Combine(scratch.Root, "modules", "rules"), diagnostics);
        ModuleSet set = ModuleLoader.Load(module, []);

        Assert.Empty(diagnostics);
        Assert.NotNull(workspace);
        Assert.Null(workspace!.Authoring);
        Assert.True(set.IsValid, string.Join("\n", set.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Fact]
    public void AuthoringContractDiagnosticsNameTheBrokenJsonPath()
    {
        using TempModules scratch = new();
        scratch.Write("goldbox.json", """
            {
              "modules": ["modules"],
              "authoring": { "modules": ["modules/source"], "staging": ".goldbox/staged" }
            }
            """);
        List<ModuleDiagnostic> diagnostics = [];

        Workspace? workspace = Workspace.Read(Path.Combine(scratch.Root, "goldbox.json"), diagnostics);

        Assert.NotNull(workspace);
        ModuleDiagnostic missing = Assert.Single(diagnostics, diagnostic => diagnostic.Rule == "workspace.authoring.exports");
        Assert.Equal("$.authoring.exports", missing.JsonPath);
        Assert.Contains("exports", missing.Message, StringComparison.Ordinal);
    }
}
