using System.Text.Json;
using System.Text.Json.Nodes;
using RustyGoldbox.Cli;

namespace RustyGoldbox.Tests;

/// <summary>Regression coverage for the CLI's temporary one-turn combat assist.</summary>
public sealed class CombatAutoStepTests
{
    [Fact]
    public void AutoStepAdvancesOneManualTurnAndKeepsTheSavedActorManual()
    {
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        JsonObject ada = JsonNode.Parse(File.ReadAllText(Path.Combine(scratch.Root, "ada.json")))!.AsObject();
        ada["combat_control"] = "manual";
        File.WriteAllText(Path.Combine(scratch.Root, "ada.json"), ada.ToJsonString());
        string fixture = File.ReadAllText(Path.Combine(
            Rules.RepositoryRoot,
            "tests",
            "RustyGoldbox.Tests",
            "Fixtures",
            "scripts",
            "live-combat-inspect.script"));
        string script = Path.Combine(scratch.Root, "auto-step.script");
        File.WriteAllText(script, fixture + "combat auto-step\ncombat inspect\n");

        (int code, string output) = CampaignTests.Run(
            scratch,
            "play",
            "--campaign",
            CampaignTests.SampleCrypt,
            "--party",
            "ada.json",
            "--seed",
            "1",
            "--combat-control",
            "manual",
            "--script",
            script,
            "--save",
            "after-auto-step.json",
            "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument transcript = JsonDocument.Parse(output);
        JsonElement autoStep = transcript.RootElement.GetProperty("transcript").EnumerateArray()
            .Single(step => step.GetProperty("command").GetString() == "combat auto-step")
            .GetProperty("combat");
        Assert.True(autoStep.GetProperty("accepted").GetBoolean(), autoStep.GetProperty("reason").GetString());
        Assert.Equal("AwaitingAction", autoStep.GetProperty("phase").GetString());

        JsonElement decision = autoStep.GetProperty("pendingDecision");
        string actorId = decision.GetProperty("actorId").GetString()!;
        JsonElement actor = autoStep.GetProperty("combatants").EnumerateArray()
            .Single(combatant => combatant.GetProperty("id").GetString() == actorId);
        Assert.Equal("Manual", actor.GetProperty("controller").GetString());
        Assert.Equal(actorId, autoStep.GetProperty("activeActorId").GetString());

        string save = File.ReadAllText(Path.Combine(scratch.Root, "after-auto-step.json"));
        Assert.Contains("\"combat_control\": \"manual\"", save, StringComparison.Ordinal);
        Assert.DoesNotContain("\"combat_control\": \"automatic\"", save, StringComparison.Ordinal);

        string resumeScript = Path.Combine(scratch.Root, "resume-inspect.script");
        File.WriteAllText(resumeScript, "combat inspect\n");
        (int resumeCode, string resumeOutput) = CampaignTests.Run(
            scratch,
            "play",
            "--campaign",
            CampaignTests.SampleCrypt,
            "--load",
            "after-auto-step.json",
            "--script",
            resumeScript,
            "--json");

        Assert.Equal(GoldboxCli.Ok, resumeCode);
        using JsonDocument resumed = JsonDocument.Parse(resumeOutput);
        JsonElement resumedCombat = resumed.RootElement.GetProperty("transcript")[0].GetProperty("combat");
        Assert.Equal("AwaitingAction", resumedCombat.GetProperty("phase").GetString());
        string resumedActorId = resumedCombat.GetProperty("pendingDecision").GetProperty("actorId").GetString()!;
        Assert.Equal("Manual", resumedCombat.GetProperty("combatants").EnumerateArray()
            .Single(combatant => combatant.GetProperty("id").GetString() == resumedActorId)
            .GetProperty("controller")
            .GetString());
    }
}
