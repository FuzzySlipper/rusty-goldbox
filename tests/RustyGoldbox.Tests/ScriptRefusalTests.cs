using System.Text.Json;

namespace RustyGoldbox.Tests;

public sealed class ScriptRefusalTests
{
    [Fact]
    public void DefaultScriptRefusalRemainsASuccessfulTranscript()
    {
        using TempModules scratch = CreateScratch("left\nforward\n");

        (int code, string output) = CampaignTests.Run(scratch,
            "play", "--campaign", CampaignTests.SampleCrypt, "--party", "ada.json,brom.json", "--script", "refused.script", "--json");

        Assert.Equal(0, code);
        using JsonDocument json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("transcript").EnumerateArray(), step =>
            step.GetProperty("facts").EnumerateArray().Any(fact => fact.GetProperty("kind").GetString() == "refused"));
    }

    [Fact]
    public void FlaggedRefusalReturnsOneObjectWithScriptLocationAndGuidance()
    {
        using TempModules scratch = CreateScratch("left\nforward\n");

        (int code, string output) = CampaignTests.Run(scratch,
            "play", "--campaign", CampaignTests.SampleCrypt, "--party", "ada.json,brom.json", "--script", "refused.script", "--fail-on-refusal", "--json");

        Assert.Equal(1, code);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement root = json.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Contains(root.GetProperty("transcript").EnumerateArray(), step =>
            step.GetProperty("facts").EnumerateArray().Any(fact => fact.GetProperty("kind").GetString() == "refused"));

        JsonElement diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("play.refusal", diagnostic.GetProperty("rule").GetString());
        Assert.Equal("sample-crypt", diagnostic.GetProperty("module").GetString());
        Assert.EndsWith("refused.script", diagnostic.GetProperty("file").GetString(), StringComparison.Ordinal);
        Assert.Equal("line 2", diagnostic.GetProperty("jsonPath").GetString());
        string message = diagnostic.GetProperty("message").GetString()!;
        Assert.Contains("line 2", message, StringComparison.Ordinal);
        Assert.Contains("forward", message, StringComparison.Ordinal);
        Assert.Contains("wall", message, StringComparison.Ordinal);
        Assert.Contains("status", message, StringComparison.Ordinal);
        Assert.Contains("map render", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedScriptWithFlagStillSucceeds()
    {
        using TempModules scratch = CreateScratch("look\n");

        (int code, string output) = CampaignTests.Run(scratch,
            "play", "--campaign", CampaignTests.SampleCrypt, "--party", "ada.json,brom.json", "--script", "refused.script", "--fail-on-refusal", "--json");

        Assert.Equal(0, code);
        using JsonDocument json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain(json.RootElement.GetProperty("transcript").EnumerateArray(), step =>
            step.GetProperty("facts").EnumerateArray().Any(fact => fact.GetProperty("kind").GetString() == "refused"));
    }

    [Fact]
    public void FlagRequiresAScriptFile()
    {
        using TempModules scratch = CreateScratch(null);

        (int code, string output) = CampaignTests.Run(scratch,
            "play", "--campaign", CampaignTests.SampleCrypt, "--party", "ada.json,brom.json", "--fail-on-refusal", "--json");

        Assert.Equal(2, code);
        using JsonDocument json = JsonDocument.Parse(output);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", json.RootElement.GetProperty("diagnostics")[0].GetProperty("rule").GetString());
        Assert.Contains("requires --script", json.RootElement.GetProperty("diagnostics")[0].GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private static TempModules CreateScratch(string? script)
    {
        TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        if (script is not null)
        {
            File.WriteAllText(Path.Combine(scratch.Root, "refused.script"), script);
        }

        return scratch;
    }
}
