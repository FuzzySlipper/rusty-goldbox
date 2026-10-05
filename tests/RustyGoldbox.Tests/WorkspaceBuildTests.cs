using System.Text.Json;
using RustyGoldbox.Cli;
using RustyGoldbox.Core.Authoring;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

public sealed class WorkspaceBuildTests
{
    [Fact]
    public void BuildStagesOnlyExplicitRuntimeModulesAndReportsFiles()
    {
        using TempModules scratch = new();
        string workspaceRoot = CreateWorkspace(scratch, "modules/rules");
        CreateModule(workspaceRoot, "rules", ModuleKind.Ruleset);
        File.WriteAllText(Path.Combine(workspaceRoot, "modules", "rules", "runtime.txt"), "runtime");
        File.WriteAllText(Path.Combine(workspaceRoot, "canon", "story.txt"), "story note");
        File.WriteAllText(Path.Combine(workspaceRoot, "prompts", "trial.txt"), "prompt trial");
        File.WriteAllText(Path.Combine(workspaceRoot, "art", "references", "source.txt"), "source image note");

        (int code, string printed) = Run(scratch, "workspace", "build", workspaceRoot, "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(printed);
        JsonElement module = Assert.Single(json.RootElement.GetProperty("modules").EnumerateArray());
        Assert.Equal("rules", module.GetProperty("id").GetString());
        Assert.Contains("runtime.txt", module.GetProperty("includedFiles").EnumerateArray().Select(value => value.GetString()));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, ".goldbox", "staged", "rules", "module.json")));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, ".goldbox", "staged", "rules", "runtime.txt")));
        Assert.False(File.Exists(Path.Combine(workspaceRoot, ".goldbox", "staged", "canon", "story.txt")));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, "canon", "story.txt")));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, "prompts", "trial.txt")));
        Assert.True(File.Exists(Path.Combine(workspaceRoot, "art", "references", "source.txt")));
    }

    [Fact]
    public void FailedBuildKeepsPreviousStagingAndEditableSources()
    {
        using TempModules scratch = new();
        string workspaceRoot = CreateWorkspace(scratch, "modules/rules");
        string source = CreateModule(workspaceRoot, "rules", ModuleKind.Ruleset);
        File.WriteAllText(Path.Combine(workspaceRoot, "canon", "story.txt"), "story note");
        Assert.Equal(GoldboxCli.Ok, Run(scratch, "workspace", "build", workspaceRoot).Code);
        string marker = Path.Combine(workspaceRoot, ".goldbox", "staged", "marker.txt");
        File.WriteAllText(marker, "keep old staging on a failed build");
        File.WriteAllText(Path.Combine(workspaceRoot, "canon", "story.txt"), "edited story note");
        File.WriteAllText(Path.Combine(source, "broken.json"), "{ \"type\": \"not-a-definition\", \"id\": \"broken\" }");

        (int code, string printed) = Run(scratch, "workspace", "build", workspaceRoot, "--json");

        Assert.Equal(GoldboxCli.Invalid, code);
        Assert.True(File.Exists(marker));
        Assert.Equal("edited story note", File.ReadAllText(Path.Combine(workspaceRoot, "canon", "story.txt")));
        Assert.Contains("definition.type-unknown", printed, StringComparison.Ordinal);
        Assert.Contains("\"modules\": []", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedRootOverlapIsRejectedBeforeClean()
    {
        using TempModules scratch = new();
        string workspaceRoot = CreateWorkspace(scratch, "modules/rules");
        string source = CreateModule(workspaceRoot, "rules", ModuleKind.Ruleset);
        string manifest = Path.Combine(workspaceRoot, Workspace.FileName);
        File.WriteAllText(manifest, """
            {
              "modules": ["modules"],
              "authoring": {
                "modules": ["modules/rules"],
                "staging": "modules",
                "exports": "exports"
              }
            }
            """);
        File.WriteAllText(Path.Combine(source, "keep.txt"), "module source");

        (int code, string printed) = Run(scratch, "workspace", "build", workspaceRoot, "--json");

        Assert.Equal(GoldboxCli.Invalid, code);
        using JsonDocument json = JsonDocument.Parse(printed);
        JsonElement diagnostic = json.RootElement.GetProperty("diagnostics")[0];
        Assert.Equal("workspace.build.path-overlap", diagnostic.GetProperty("rule").GetString());
        Assert.Equal("$.authoring.staging", diagnostic.GetProperty("jsonPath").GetString());
        Assert.True(File.Exists(Path.Combine(source, "keep.txt")));
        Assert.True(File.Exists(Path.Combine(source, "module.json")));
    }

    [Fact]
    public void UnresolvedDependencyIsReportedWithManifestPath()
    {
        using TempModules scratch = new();
        string workspaceRoot = CreateWorkspace(scratch, "modules");
        CreateModule(workspaceRoot, "book", ModuleKind.Extension, TempModules.Require("missing-rules", "^1.0.0"));
        SetAuthoringModules(workspaceRoot, "modules/book");
        (int code, string printed) = Run(scratch, "workspace", "build", workspaceRoot, "--json");

        Assert.Equal(GoldboxCli.Invalid, code);
        using JsonDocument json = JsonDocument.Parse(printed);
        JsonElement dependency = Assert.Single(json.RootElement.GetProperty("unresolvedDependencies").EnumerateArray());
        Assert.Equal("book", dependency.GetProperty("module").GetString());
        Assert.Equal("missing-rules", dependency.GetProperty("id").GetString());
        Assert.Equal("$.requires[0].id", dependency.GetProperty("jsonPath").GetString());
        Assert.EndsWith("book/module.json", json.RootElement.GetProperty("diagnostics")[0].GetProperty("file").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(workspaceRoot, ".goldbox", "staged", "book")));
    }

    [Fact]
    public void TransitiveUnresolvedDependencyNamesTheManifestThatRequiresIt()
    {
        using TempModules scratch = new();
        string workspaceRoot = CreateWorkspace(scratch, "modules/root");
        CreateModule(workspaceRoot, "classic", ModuleKind.Ruleset);
        CreateModule(
            workspaceRoot,
            "helper",
            ModuleKind.Extension,
            $"{TempModules.Require("classic", "*")}, {TempModules.Require("missing-rules", "^2.0.0")}");
        CreateModule(
            workspaceRoot,
            "root",
            ModuleKind.Extension,
            $"{TempModules.Require("classic", "*")}, {TempModules.Require("helper", "*")}" );
        SetAuthoringModules(workspaceRoot, "modules/root");

        (int code, string printed) = Run(scratch, "workspace", "build", workspaceRoot, "--json");

        Assert.Equal(GoldboxCli.Invalid, code);
        using JsonDocument json = JsonDocument.Parse(printed);
        JsonElement dependency = Assert.Single(json.RootElement.GetProperty("unresolvedDependencies").EnumerateArray());
        Assert.Equal("helper", dependency.GetProperty("module").GetString());
        Assert.Equal("missing-rules", dependency.GetProperty("id").GetString());
        Assert.Equal("^2.0.0", dependency.GetProperty("range").GetString());
        Assert.Equal("$.requires[1].id", dependency.GetProperty("jsonPath").GetString());
        JsonElement diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("helper", diagnostic.GetProperty("module").GetString());
        Assert.EndsWith("helper/module.json", diagnostic.GetProperty("file").GetString(), StringComparison.Ordinal);
        Assert.Equal("$.requires[1].id", diagnostic.GetProperty("jsonPath").GetString());
    }

    [Fact]
    public void DuplicateAuthoredModuleUsesAuthoringModulesJsonPath()
    {
        using TempModules scratch = new();
        string workspaceRoot = CreateWorkspace(scratch, "modules/a", "modules/b");
        string first = CreateModule(workspaceRoot, "a", ModuleKind.Ruleset);
        string second = CreateModule(workspaceRoot, "b", ModuleKind.Ruleset);
        File.WriteAllText(Path.Combine(first, "module.json"), File.ReadAllText(Path.Combine(first, "module.json")).Replace("\"id\": \"a\"", "\"id\": \"duplicate\"", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(second, "module.json"), File.ReadAllText(Path.Combine(second, "module.json")).Replace("\"id\": \"b\"", "\"id\": \"duplicate\"", StringComparison.Ordinal));

        (int code, string printed) = Run(scratch, "workspace", "build", workspaceRoot, "--json");

        Assert.Equal(GoldboxCli.Invalid, code);
        using JsonDocument json = JsonDocument.Parse(printed);
        JsonElement diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("workspace.build.duplicate-module", diagnostic.GetProperty("rule").GetString());
        Assert.Equal("$.authoring.modules[1]", diagnostic.GetProperty("jsonPath").GetString());
        Assert.Contains("modules/a", diagnostic.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("modules/b", diagnostic.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCleansRemovedModulesAndStoryNotesDoNotChangeRuntimeIdentity()
    {
        using TempModules scratch = new();
        string workspaceRoot = CreateWorkspace(scratch, "modules");
        string rules = CreateModule(workspaceRoot, "rules", ModuleKind.Ruleset);
        CreateModule(workspaceRoot, "art", ModuleKind.Assets);
        SetAuthoringModules(workspaceRoot, "modules/rules", "modules/art");

        (int firstCode, string firstOutput) = Run(scratch, "workspace", "export", workspaceRoot, "--json");
        Assert.Equal(GoldboxCli.Ok, firstCode);
        Assert.Contains("rules-0.1.0.rpak", firstOutput, StringComparison.Ordinal);
        Assert.Contains("art-0.1.0.rpak", firstOutput, StringComparison.Ordinal);
        string firstIdentity = new DirectoryModuleSource(Path.Combine(workspaceRoot, ".goldbox", "staged", "rules")).Identity;

        File.WriteAllText(Path.Combine(workspaceRoot, "canon", "story.txt"), "a changed authoring note");
        (int secondCode, _) = Run(scratch, "workspace", "build", workspaceRoot, "--json");
        Assert.Equal(GoldboxCli.Ok, secondCode);
        string secondIdentity = new DirectoryModuleSource(Path.Combine(workspaceRoot, ".goldbox", "staged", "rules")).Identity;
        Assert.Equal(firstIdentity, secondIdentity);

        File.WriteAllText(Path.Combine(rules, "runtime.txt"), "runtime edit");
        (int thirdCode, _) = Run(scratch, "workspace", "build", workspaceRoot, "--json");
        Assert.Equal(GoldboxCli.Ok, thirdCode);
        string thirdIdentity = new DirectoryModuleSource(Path.Combine(workspaceRoot, ".goldbox", "staged", "rules")).Identity;
        Assert.NotEqual(secondIdentity, thirdIdentity);

        SetAuthoringModules(workspaceRoot, "modules/rules");
        (int finalCode, string finalOutput) = Run(scratch, "workspace", "export", workspaceRoot, "--json");
        Assert.Equal(GoldboxCli.Ok, finalCode);
        Assert.Contains("rules-0.1.0.rpak", finalOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("art-0.1.0.rpak", finalOutput, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(workspaceRoot, "exports", "art-0.1.0.rpak")));
    }

    private static string CreateWorkspace(TempModules scratch, params string[] authored)
    {
        string root = Path.Combine(scratch.Root, "workspace");
        List<ModuleDiagnostic> diagnostics = [];
        Assert.NotNull(WorkspaceScaffold.Create(root, diagnostics));
        Assert.Empty(diagnostics);
        SetAuthoringModules(root, authored);
        return root;
    }

    private static string CreateModule(string workspaceRoot, string id, ModuleKind kind, string? requires = null)
    {
        string parent = Path.Combine(workspaceRoot, "modules");
        Directory.CreateDirectory(parent);
        List<(string Id, VersionRange Range)> dependencies = [];
        if (requires is not null)
        {
            using JsonDocument json = JsonDocument.Parse($"[{requires}]");
            foreach (JsonElement entry in json.RootElement.EnumerateArray())
            {
                Assert.True(VersionRange.TryParse(entry.GetProperty("version").GetString()!, out VersionRange? range));
                dependencies.Add((entry.GetProperty("id").GetString()!, range!));
            }
        }

        List<ModuleDiagnostic> diagnostics = [];
        string? path = ModuleScaffold.Create(parent, kind, id, id, "Test fixture.", dependencies, diagnostics);
        Assert.Empty(diagnostics);
        return Assert.IsType<string>(path);
    }

    private static void SetAuthoringModules(string workspaceRoot, params string[] modules)
    {
        string manifest = Path.Combine(workspaceRoot, Workspace.FileName);
        File.WriteAllText(manifest, $$"""
            {
              "modules": ["modules"],
              "authoring": {
                "modules": [{{string.Join(", ", modules.Select(module => JsonSerializer.Serialize(module)))}}],
                "staging": ".goldbox/staged",
                "exports": "exports"
              }
            }
            """);
    }

    private static (int Code, string Output) Run(TempModules scratch, params string[] args)
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(args, output, scratch.Root);
        return (code, output.ToString());
    }
}
