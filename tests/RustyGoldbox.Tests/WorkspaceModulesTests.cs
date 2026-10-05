using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Game;

namespace RustyGoldbox.Tests;

/// <summary>The Game plays a workspace's modules from source in place of installed copies, and sees edits on Refresh.</summary>
public sealed class WorkspaceModulesTests
{
    [Fact]
    public void AWorkspaceCampaignReplacesItsInstalledCopyAndRefreshShowsEdits()
    {
        using TempModules scratch = new();
        string repository = Path.Combine(Rules.RepositoryRoot, "modules");
        List<string> installed = new[] { "classic", "placeholder-art", "sample-crypt" }
            .Select(id => EngineContentTests.Pack(Path.Combine(repository, id), scratch))
            .ToList();

        // A workspace authoring its own copy of the sample campaign.
        string workspace = Path.Combine(scratch.Root, "workspace");
        string campaign = Path.Combine(workspace, "modules", "sample-crypt");
        CopyDirectory(Path.Combine(repository, "sample-crypt"), campaign);
        File.WriteAllText(Path.Combine(workspace, "goldbox.json"), $$"""
            {
              "modules": [{{System.Text.Json.JsonSerializer.Serialize(repository)}}],
              "authoring": { "modules": ["modules/sample-crypt"], "staging": ".goldbox/staged", "exports": "exports" }
            }
            """);
        Retitle(campaign, "The Edited Crypt");

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            WorkspaceModules workspaces = new([workspace]);
            ModuleLibrary library = new(
                problems =>
                {
                    List<ProductContentBundle> modules = installed.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList();
                    workspaces.Open(engine.Content, modules, problems);
                    return modules;
                },
                problems => workspaces.Prepare(engine.Content, problems));

            List<string> problems = [];
            CampaignChoice edited = Assert.Single(library.Campaigns(problems));
            Assert.Empty(problems);
            Assert.Equal("The Edited Crypt", edited.Title);
            Assert.True(library.Load(edited.Bundle, []).IsValid);

            // Refresh without an edit keeps the packed container; an edit repacks it.
            string packed = Path.Combine(workspace, ".goldbox", "game", "sample-crypt-0.1.0.rpak");
            DateTime written = File.GetLastWriteTimeUtc(packed);
            Assert.Equal("The Edited Crypt", Assert.Single(library.Campaigns(problems)).Title);
            Assert.Equal(written, File.GetLastWriteTimeUtc(packed));

            Retitle(campaign, "The Edited Crypt, Again");
            Assert.Equal("The Edited Crypt, Again", Assert.Single(library.Campaigns(problems)).Title);
            Assert.Empty(problems);
        });
    }

    private static void Retitle(string module, string title)
    {
        string manifest = Path.Combine(module, "module.json");
        JsonNode node = JsonNode.Parse(File.ReadAllText(manifest))!;
        node["title"] = title;
        File.WriteAllText(manifest, node.ToJsonString());
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
