using System.Text.Json;
using RustyGoldbox.Cli;
using RustyGoldbox.Core.Authoring;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

/// <summary>Tests that point the module library somewhere else through its environment variable.</summary>
[CollectionDefinition(nameof(ModuleLibraryVariable), DisableParallelization = true)]
public sealed class ModuleLibraryVariable;

[Collection(nameof(ModuleLibraryVariable))]
public sealed class WorkspaceInstallTests
{
    [Fact]
    public void InstallPacksEveryAuthoredModuleIntoTheLibraryAndLeavesExportsAlone()
    {
        using TempModules scratch = new();
        string root = Path.Combine(scratch.Root, "workspace");
        List<ModuleDiagnostic> diagnostics = [];
        Assert.NotNull(WorkspaceScaffold.Create(root, diagnostics));
        string modules = Path.Combine(root, "modules");
        Directory.CreateDirectory(modules);
        ModuleScaffold.Create(modules, ModuleKind.Ruleset, "rules", "Rules", "Test fixture.", [], diagnostics);
        ModuleScaffold.Create(modules, ModuleKind.Assets, "art", "Art", "Test fixture.", [], diagnostics);
        Assert.Empty(diagnostics);
        File.WriteAllText(Path.Combine(root, Workspace.FileName), """
            {
              "modules": ["modules"],
              "authoring": { "modules": ["modules/rules", "modules/art"], "staging": ".goldbox/staged", "exports": "exports" }
            }
            """);
        string library = Path.Combine(scratch.Root, "library");

        string? previous = Environment.GetEnvironmentVariable(InstalledModules.DirectoryVariable);
        Environment.SetEnvironmentVariable(InstalledModules.DirectoryVariable, library);
        try
        {
            using StringWriter output = new();
            int code = GoldboxCli.Run(["workspace", "install", root, "--json"], output, scratch.Root);

            Assert.Equal(GoldboxCli.Ok, code);
            using JsonDocument json = JsonDocument.Parse(output.ToString());
            Assert.Equal(2, json.RootElement.GetProperty("exports").GetArrayLength());
            Assert.True(File.Exists(Path.Combine(library, "rules-0.1.0.rpak")));
            Assert.True(File.Exists(Path.Combine(library, "art-0.1.0.rpak")));
            string exports = Path.Combine(root, "exports");
            Assert.False(Directory.Exists(exports) && Directory.EnumerateFiles(exports, "*.rpak").Any());
        }
        finally
        {
            Environment.SetEnvironmentVariable(InstalledModules.DirectoryVariable, previous);
        }
    }
}
