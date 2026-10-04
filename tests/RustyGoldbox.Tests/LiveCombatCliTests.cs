using System.Text.Json;
using RustyGoldbox.Cli;

namespace RustyGoldbox.Tests;

/// <summary>Focused CLI checks for inspecting and driving a suspended campaign combat.</summary>
public sealed class LiveCombatCliTests
{
    [Fact]
    public void LiveCombatSchemaHasAGoldenCommandReference()
    {
        Golden.Verify("live-combat-schema.txt", CliTranscript.Run(
            Rules.RepositoryRoot,
            ["schema", "live-combat"]));
    }

    [Fact]
    public void LiveCombatSchemaDoesNotShadowTheCombatDefinitionSchema()
    {
        using StringWriter output = new();
        Assert.Equal(GoldboxCli.Ok, GoldboxCli.Run(["schema", "combat", "--json"], output, Rules.RepositoryRoot));
        using JsonDocument definition = JsonDocument.Parse(output.ToString());
        Assert.Equal("combat", definition.RootElement.GetProperty("name").GetString());

        output.GetStringBuilder().Clear();
        Assert.Equal(GoldboxCli.Ok, GoldboxCli.Run(["schema", "live-combat", "--json"], output, Rules.RepositoryRoot));
        using JsonDocument commands = JsonDocument.Parse(output.ToString());
        Assert.Equal("live-combat", commands.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void CombatScriptAcceptsQuotedMachineIds()
    {
        (CombatScriptCommand? parsed, string? error) = CombatScript.Parse("combat action actor 'actor/use/0/Needle volley' --target 'enemy one'");

        Assert.Null(error);
        CombatUseActionCommand command = Assert.IsType<CombatUseActionCommand>(parsed);
        Assert.Equal("actor", command.ActorId);
        Assert.Equal("actor/use/0/Needle volley", command.ActionId);
        Assert.Equal(["enemy one"], command.TargetIds);
    }

    [Fact]
    public void BehaviorInspectionIncludesRepairableSourceAndAuthoredExpressions()
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(
            ["module", "inspect", Path.Combine(Rules.RepositoryRoot, "modules", "tactical-bestiaire"), "combat-behavior", "--trace", "--json"],
            output,
            Rules.RepositoryRoot);

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement first = json.RootElement.GetProperty("definitions")[0];
        Assert.Equal("authored", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal("tactical-bestiaire", first.GetProperty("source").GetProperty("module").GetString());
        Assert.StartsWith("modules/tactical-bestiaire/", first.GetProperty("source").GetProperty("file").GetString(), StringComparison.Ordinal);
        Assert.Equal("$", first.GetProperty("source").GetProperty("jsonPath").GetString());
        Assert.Equal("Pick the weakest exposed foe", first.GetProperty("name").GetString());
        Assert.Equal("end-turn", first.GetProperty("fallback").GetString());
        JsonElement rule = first.GetProperty("rules")[0];
        Assert.True(rule.TryGetProperty("priority", out _));
        Assert.StartsWith("$.rules[", rule.GetProperty("source").GetProperty("jsonPath").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ManualCombatInspectionRefusalAndSaveLoadAreStructured()
    {
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        string fixture = File.ReadAllText(Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "scripts", "live-combat-inspect.script"));
        string baselineScript = Path.Combine(scratch.Root, "live-baseline.script");
        File.WriteAllText(baselineScript, fixture);
        (int baselineCode, _) = CampaignTests.Run(scratch,
            "play", "--campaign", CampaignTests.SampleCrypt, "--party", "ada.json,brom.json", "--seed", "1",
            "--combat-control", "manual", "--script", baselineScript, "--save", "baseline.json", "--json");
        Assert.Equal(GoldboxCli.Ok, baselineCode);

        string firstScript = Path.Combine(scratch.Root, "live-first.script");
        File.WriteAllText(firstScript, fixture
            + "combat end-turn wrong-actor\ncombat inspect\n");

        (int firstCode, string firstOutput) = CampaignTests.Run(scratch,
            "play", "--campaign", CampaignTests.SampleCrypt, "--party", "ada.json,brom.json", "--seed", "1",
            "--combat-control", "manual", "--script", firstScript, "--save", "pending.json", "--trace", "--json");
        Assert.Equal(GoldboxCli.Ok, firstCode);

        using JsonDocument first = JsonDocument.Parse(firstOutput);
        JsonElement inspected = first.RootElement.GetProperty("transcript").EnumerateArray()
            .First(step => step.GetProperty("command").GetString() == "combat inspect");
        JsonElement combat = inspected.GetProperty("combat");
        Assert.True(combat.GetProperty("accepted").GetBoolean());
        Assert.Equal("AwaitingAction", combat.GetProperty("phase").GetString());
        Assert.NotEqual(0, combat.GetProperty("round").GetInt32());
        JsonElement decision = combat.GetProperty("pendingDecision");
        string actor = decision.GetProperty("actorId").GetString()!;
        // The machine choice ID is stable across save/load reconstruction;
        // the qualified Core action ID remains available as a readable alias.
        string action = decision.GetProperty("actions")[0].GetProperty("actionId").GetString()!;
        string target = decision.GetProperty("actions")[0].GetProperty("targets")[0].GetProperty("id").GetString()!;
        Assert.True(combat.GetProperty("combatants")[0].TryGetProperty("budget", out _));

        JsonElement refused = first.RootElement.GetProperty("transcript").EnumerateArray()
            .Single(step => step.GetProperty("command").GetString() == "combat end-turn wrong-actor").GetProperty("combat");
        Assert.False(refused.GetProperty("accepted").GetBoolean());
        Assert.Contains("actor", refused.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        JsonElement trace = inspected.GetProperty("trace");
        Assert.True(trace.GetProperty("sideEffectFree").GetBoolean());
        Assert.True(trace.TryGetProperty("alternatives", out _));
        Assert.True(File.Exists(Path.Combine(scratch.Root, "pending.json")));
        Assert.Equal(
            File.ReadAllText(Path.Combine(scratch.Root, "baseline.json")),
            File.ReadAllText(Path.Combine(scratch.Root, "pending.json")));

        string secondScript = Path.Combine(scratch.Root, "live-second.script");
        File.WriteAllText(secondScript, $"combat action {actor} {action} --target {target}\ncombat inspect\n");
        (int secondCode, string secondOutput) = CampaignTests.Run(scratch,
            "play", "--campaign", CampaignTests.SampleCrypt, "--load", "pending.json", "--script", secondScript, "--save", "continued.json", "--json");
        Assert.Equal(GoldboxCli.Ok, secondCode);
        using JsonDocument second = JsonDocument.Parse(secondOutput);
        JsonElement submitted = second.RootElement.GetProperty("transcript")[0].GetProperty("combat");
        Assert.True(submitted.GetProperty("accepted").GetBoolean(), submitted.GetProperty("reason").GetString());
        Assert.True(File.Exists(Path.Combine(scratch.Root, "continued.json")));
    }
}
