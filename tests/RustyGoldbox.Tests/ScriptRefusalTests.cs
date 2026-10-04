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
    public void CombatRefusalFlagUsesResultAndPreservesPendingSave()
    {
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        string fixturePath = Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "scripts", "live-combat-inspect.script");
        string[] fixtureLines = File.ReadAllLines(fixturePath);
        string fixture = File.ReadAllText(fixturePath);
        string baselineScript = Path.Combine(scratch.Root, "combat-baseline.script");
        File.WriteAllText(baselineScript, fixture);

        (int baselineCode, string baselineOutput) = CampaignTests.Run(scratch,
            "play", "--campaign", CampaignTests.SampleCrypt, "--party", "ada.json,brom.json", "--seed", "1",
            "--combat-control", "manual", "--script", baselineScript, "--save", "baseline.json", "--json");
        Assert.Equal(0, baselineCode);
        using JsonDocument baseline = JsonDocument.Parse(baselineOutput);
        JsonElement inspection = baseline.RootElement.GetProperty("transcript").EnumerateArray()
            .Single(step => step.GetProperty("command").GetString() == "combat inspect");
        JsonElement decision = inspection.GetProperty("combat").GetProperty("pendingDecision");
        string actor = decision.GetProperty("actorId").GetString()!;
        JsonElement action = decision.GetProperty("actions")[0];
        string actionId = action.GetProperty("actionId").GetString()!;
        string legalTarget = action.GetProperty("targets")[0].GetProperty("id").GetString()!;

        string refusedScript = Path.Combine(scratch.Root, "combat-refused.script");
        File.WriteAllText(refusedScript,
            string.Join(Environment.NewLine, fixtureLines[..^1])
            + Environment.NewLine
            + $"combat action {actor} {actionId} --target side-99{Environment.NewLine}");

        (int relaxedCode, string relaxedOutput) = CampaignTests.Run(scratch,
            "play", "--campaign", CampaignTests.SampleCrypt, "--party", "ada.json,brom.json", "--seed", "1",
            "--combat-control", "manual", "--script", refusedScript, "--save", "relaxed.json", "--json");
        Assert.Equal(0, relaxedCode);
        using JsonDocument relaxed = JsonDocument.Parse(relaxedOutput);
        JsonElement relaxedRoot = relaxed.RootElement;
        Assert.True(relaxedRoot.GetProperty("ok").GetBoolean());
        JsonElement relaxedCombat = relaxedRoot.GetProperty("transcript").EnumerateArray()
            .Single(step => step.TryGetProperty("command", out JsonElement command)
                && command.ValueKind == JsonValueKind.String
                && command.GetString()!.StartsWith("combat action", StringComparison.Ordinal))
            .GetProperty("combat");
        Assert.False(relaxedCombat.GetProperty("accepted").GetBoolean());
        Assert.Contains("side-99", relaxedCombat.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.False(relaxedRoot.TryGetProperty("diagnostics", out _));
        Assert.Equal(File.ReadAllText(Path.Combine(scratch.Root, "baseline.json")), File.ReadAllText(Path.Combine(scratch.Root, "relaxed.json")));

        (int strictCode, string strictOutput) = CampaignTests.Run(scratch,
            "play", "--campaign", CampaignTests.SampleCrypt, "--party", "ada.json,brom.json", "--seed", "1",
            "--combat-control", "manual", "--script", refusedScript, "--save", "strict.json", "--fail-on-refusal", "--json");
        Assert.Equal(1, strictCode);
        using JsonDocument strict = JsonDocument.Parse(strictOutput);
        JsonElement strictRoot = strict.RootElement;
        Assert.False(strictRoot.GetProperty("ok").GetBoolean());
        JsonElement strictCombat = strictRoot.GetProperty("transcript").EnumerateArray()
            .Single(step => step.TryGetProperty("command", out JsonElement command)
                && command.ValueKind == JsonValueKind.String
                && command.GetString()!.StartsWith("combat action", StringComparison.Ordinal))
            .GetProperty("combat");
        Assert.False(strictCombat.GetProperty("accepted").GetBoolean());
        JsonElement diagnostic = strictRoot.GetProperty("diagnostics").EnumerateArray()
            .Single(entry => entry.GetProperty("message").GetString()!.Contains("combat action", StringComparison.Ordinal));
        Assert.Equal("play.refusal", diagnostic.GetProperty("rule").GetString());
        Assert.Equal("sample-crypt", diagnostic.GetProperty("module").GetString());
        Assert.EndsWith("combat-refused.script", diagnostic.GetProperty("file").GetString(), StringComparison.Ordinal);
        Assert.Equal($"line {fixtureLines.Length}", diagnostic.GetProperty("jsonPath").GetString());
        string message = diagnostic.GetProperty("message").GetString()!;
        Assert.Contains("side-99", message, StringComparison.Ordinal);
        Assert.Contains("combat inspect", message, StringComparison.Ordinal);
        Assert.Contains("valid combat choice", message, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllText(Path.Combine(scratch.Root, "baseline.json")), File.ReadAllText(Path.Combine(scratch.Root, "strict.json")));

        string repairScript = Path.Combine(scratch.Root, "combat-repair.script");
        File.WriteAllText(repairScript, $"combat inspect{Environment.NewLine}combat action {actor} {actionId} --target {legalTarget}{Environment.NewLine}");
        (int repairCode, string repairOutput) = CampaignTests.Run(scratch,
            "play", "--campaign", CampaignTests.SampleCrypt, "--load", "strict.json", "--script", repairScript,
            "--save", "repaired.json", "--fail-on-refusal", "--json");
        Assert.Equal(0, repairCode);
        using JsonDocument repair = JsonDocument.Parse(repairOutput);
        Assert.True(repair.RootElement.GetProperty("ok").GetBoolean());
        JsonElement[] repairCombats = repair.RootElement.GetProperty("transcript").EnumerateArray()
            .Where(step => step.TryGetProperty("combat", out _))
            .Select(step => step.GetProperty("combat"))
            .ToArray();
        Assert.Equal(2, repairCombats.Length);
        Assert.All(repairCombats, combat => Assert.True(combat.GetProperty("accepted").GetBoolean(), combat.GetProperty("reason").GetString()));
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
