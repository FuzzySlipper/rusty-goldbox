using System.Text.Json;
using RustyGoldbox.Cli;

namespace RustyGoldbox.Tests;

public sealed class AuthoringKitTests
{
    [Fact]
    public void EmbeddedKitIsDiscoverableWithoutARepositoryCheckout()
    {
        using TempModules scratch = new();

        (int code, string text) = Run(scratch.Root, "authoring", "list", "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(text);
        Assert.Equal("draft-0.1", json.RootElement.GetProperty("revision").GetString());
        Assert.Equal(
            ["kit", "brief", "canon", "chapter", "encounter", "art", "handoff", "workflow", "revision"],
            json.RootElement.GetProperty("resources").EnumerateArray().Select(resource => resource.GetProperty("id").GetString()));
    }

    [Fact]
    public void ShowReturnsAnEmbeddedTemplateAndItsDraftStatus()
    {
        using TempModules scratch = new();

        (int code, string text) = Run(scratch.Root, "authoring", "show", "brief", "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(text);
        Assert.Equal("brief", json.RootElement.GetProperty("resource").GetProperty("id").GetString());
        Assert.Equal("draft", json.RootElement.GetProperty("resource").GetProperty("status").GetString());
        string content = json.RootElement.GetProperty("content").GetString()!;
        Assert.Contains("campaign_id:", content, StringComparison.Ordinal);
        Assert.Contains("```yaml", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\\`", content, StringComparison.Ordinal);
        Assert.DoesNotContain(scratch.Root, content, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyAllRefusesAccidentalOverwriteAndSupportsExplicitReplacement()
    {
        using TempModules scratch = new();
        string destination = Path.Combine(scratch.Root, "authoring");

        (int firstCode, string firstText) = Run(scratch.Root, "authoring", "copy", "--all", "--out", "authoring", "--json");
        Assert.Equal(GoldboxCli.Ok, firstCode);
        using (JsonDocument first = JsonDocument.Parse(firstText))
        {
            Assert.Equal(9, first.RootElement.GetProperty("files").GetArrayLength());
        }

        string briefPath = Path.Combine(destination, "brief.md");
        string copiedBrief = File.ReadAllText(briefPath);
        Assert.Contains("```yaml", copiedBrief, StringComparison.Ordinal);
        Assert.DoesNotContain("\\`", copiedBrief, StringComparison.Ordinal);

        string workflowPath = Path.Combine(destination, "workflow.md");
        string original = File.ReadAllText(workflowPath);
        File.AppendAllText(workflowPath, "\nlocal note\n");

        (int refusedCode, string refusedText) = Run(scratch.Root, "authoring", "copy", "--all", "--out", "authoring", "--json");
        Assert.Equal(GoldboxCli.Invalid, refusedCode);
        using (JsonDocument refused = JsonDocument.Parse(refusedText))
        {
            Assert.Equal("authoring.copy.exists", refused.RootElement.GetProperty("diagnostics")[0].GetProperty("rule").GetString());
        }

        Assert.Contains("local note", File.ReadAllText(workflowPath), StringComparison.Ordinal);

        (int overwriteCode, string overwriteText) = Run(scratch.Root, "authoring", "copy", "workflow", "--out", "authoring", "--overwrite", "--json");
        Assert.Equal(GoldboxCli.Ok, overwriteCode);
        Assert.DoesNotContain("local note", File.ReadAllText(workflowPath), StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(workflowPath));
        Assert.Contains("workflow.md", overwriteText, StringComparison.Ordinal);
    }

    private static (int Code, string Output) Run(string workingDirectory, params string[] args)
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(args, output, workingDirectory);
        return (code, output.ToString());
    }
}
