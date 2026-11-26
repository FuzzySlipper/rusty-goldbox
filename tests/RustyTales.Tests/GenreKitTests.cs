using System.Text.Json;
using RustyTales.Cli;

namespace RustyTales.Tests;

public sealed class GenreKitTests
{
    [Theory]
    [InlineData("tactical-dungeon", "tactical")]
    [InlineData("first-person-crawler", "first_person")]
    [InlineData("classic-jrpg", "side_view")]
    public void GenreKitScaffoldsValidatesBuildsScopesAndPlaysToVictory(string kitId, string expectedPresentation)
    {
        using TempModules scratch = new();
        string workspaceDir = Path.Combine(scratch.Root, "demo-work");

        (int newCode, string newOut) = Run(scratch.Root, "workspace", "new", "demo-work", "--kit", kitId, "--id", "demo", "--title", "Demo Adventure", "--json");
        Assert.Equal(TalesCli.Ok, newCode);
        using (JsonDocument created = JsonDocument.Parse(newOut))
        {
            Assert.True(created.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(kitId, created.RootElement.GetProperty("kit").GetString());
            Assert.Equal(3, created.RootElement.GetProperty("workspace").GetProperty("authoring").GetProperty("modules").GetArrayLength());
        }

        Assert.True(File.Exists(Path.Combine(workspaceDir, "canon", "KIT.md")));
        Assert.True(File.Exists(Path.Combine(workspaceDir, "scripts", "smoke.txt")));

        (int buildCode, string buildOut) = Run(scratch.Root, "workspace", "build", "demo-work", "--json");
        Assert.True(buildCode == TalesCli.Ok, buildOut);
        using (JsonDocument built = JsonDocument.Parse(buildOut))
        {
            Assert.True(built.RootElement.GetProperty("ok").GetBoolean(), buildOut);
            Assert.Equal(3, built.RootElement.GetProperty("modules").GetArrayLength());
            Assert.Empty(built.RootElement.GetProperty("diagnostics").EnumerateArray());
        }

        string campaignDir = Path.Combine(workspaceDir, "modules", "demo");
        string modulesDir = Path.Combine(workspaceDir, "modules");
        (int schemaCode, string schemaOut) = Run(scratch.Root, "schema", "--module", campaignDir, "--modules", modulesDir, "--json");
        Assert.True(schemaCode == TalesCli.Ok, schemaOut);
        using (JsonDocument scoped = JsonDocument.Parse(schemaOut))
        {
            JsonElement root = scoped.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal("demo-rules", root.GetProperty("ruleset").GetString());
            Assert.Equal(expectedPresentation, root.GetProperty("combatPresentation").GetString());
            Assert.True(root.GetProperty("hasStartingParty").GetBoolean());
            Assert.True(root.GetProperty("attributes").GetArrayLength() >= 4);
            Assert.True(root.GetProperty("actions").GetArrayLength() >= 3);
            Assert.True(root.GetProperty("monsters").GetArrayLength() >= 2);
            Assert.True(root.GetProperty("assets").GetArrayLength() >= 5);
            Assert.Single(root.GetProperty("skins").EnumerateArray());
        }

        string smokeScript = Path.Combine(workspaceDir, "scripts", "smoke.txt");
        (int playCode, string playOut) = Run(
            scratch.Root,
            "play",
            "--campaign", campaignDir,
            "--modules", modulesDir,
            "--script", smokeScript,
            "--seed", "1",
            "--fail-on-refusal",
            "--json");
        Assert.True(playCode == TalesCli.Ok, playOut);
        using (JsonDocument played = JsonDocument.Parse(playOut))
        {
            Assert.True(played.RootElement.GetProperty("ok").GetBoolean(), playOut);
            Assert.True(played.RootElement.GetProperty("ended").GetBoolean(), playOut);
            Assert.DoesNotContain("falls", playOut, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Darkness claims", playOut, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SchemaKitsListsAllGenreStarterKits()
    {
        using TempModules scratch = new();

        (int jsonCode, string jsonOut) = Run(scratch.Root, "schema", "kits", "--json");
        Assert.Equal(TalesCli.Ok, jsonCode);
        using (JsonDocument doc = JsonDocument.Parse(jsonOut))
        {
            List<string> ids = doc.RootElement.GetProperty("kits").EnumerateArray().Select(k => k.GetProperty("id").GetString()!).ToList();
            Assert.Equal(["tactical-dungeon", "first-person-crawler", "classic-jrpg"], ids);
        }

        (int textCode, string textOut) = Run(scratch.Root, "schema", "kits");
        Assert.Equal(TalesCli.Ok, textCode);
        Assert.Contains("tactical-dungeon", textOut, StringComparison.Ordinal);
        Assert.Contains("first-person-crawler", textOut, StringComparison.Ordinal);
        Assert.Contains("classic-jrpg", textOut, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceNewRejectsUnknownKitAndIdWithoutKit()
    {
        using TempModules scratch = new();

        (int unknownCode, string unknownOut) = Run(scratch.Root, "workspace", "new", "bad", "--kit", "bogus", "--json");
        Assert.Equal(TalesCli.Invalid, unknownCode);
        using (JsonDocument doc = JsonDocument.Parse(unknownOut))
        {
            Assert.Equal("workspace.kit", doc.RootElement.GetProperty("diagnostics")[0].GetProperty("rule").GetString());
        }

        (int usageCode, string usageOut) = Run(scratch.Root, "workspace", "new", "bad", "--id", "demo", "--json");
        Assert.Equal(TalesCli.Usage, usageCode);
        Assert.Contains("--kit", usageOut, StringComparison.Ordinal);
    }

    private static (int Code, string Output) Run(string workingDirectory, params string[] args)
    {
        using StringWriter output = new();
        int code = TalesCli.Run(args, output, workingDirectory);
        return (code, output.ToString());
    }
}
