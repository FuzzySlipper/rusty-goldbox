using System.Text.Json;
using RustyTales.Cli;
using RustyTales.Core.Modules;

namespace RustyTales.Tests;

/// <summary>
/// Validates and headlessly plays both Phase 2 bundled showcase demos
/// (<c>wizardry-delve</c> first-person crawler and <c>crystal-keep</c> side-view JRPG)
/// from opening scene dialogue through locked doors and boss victory.
/// </summary>
public sealed class ShowcaseDemoTests
{
    [Theory]
    [InlineData("wizardry-rules")]
    [InlineData("wizardry-art")]
    [InlineData("wizardry-delve")]
    [InlineData("jrpg-rules")]
    [InlineData("jrpg-art")]
    [InlineData("crystal-keep")]
    public void ShowcaseModulesLoadAndValidateCleanly(string moduleId)
    {
        string moduleDir = Path.Combine(Rules.RepositoryRoot, "modules", moduleId);
        string modulesRoot = Path.Combine(Rules.RepositoryRoot, "modules");
        ModuleSet set = ModuleLoader.Load(moduleDir, [modulesRoot]);
        Assert.Empty(set.Diagnostics);
    }

    [Theory]
    [InlineData("wizardry-delve", "The Dread Knight collapses", "Gate Warden:")]
    [InlineData("crystal-keep", "the Crimson Drake yields", "Keep Archivist:")]
    public void ShowcaseCampaignsPlayThroughDialogueShrineLockedDoorAndBossToVictory(string campaignId, string expectedEnding, string expectedSpeaker)
    {
        using TempModules scratch = new();
        string scriptPath = Path.Combine(scratch.Root, $"{campaignId}-smoke.txt");
        File.WriteAllText(scriptPath, """
            right
            forward
            around
            forward
            right
            forward
            choose 1
            choose 2
            buy 1
            leave
            choose 3
            choose 4
            forward
            right
            forward
            forward
            open east
            forward
            """);

        string campaignDir = Path.Combine(Rules.RepositoryRoot, "modules", campaignId);
        string modulesRoot = Path.Combine(Rules.RepositoryRoot, "modules");
        using StringWriter output = new();
        int code = TalesCli.Run(
            [
                "play",
                "--campaign", campaignDir,
                "--modules", modulesRoot,
                "--script", scriptPath,
                "--seed", "7",
                "--fail-on-refusal",
                "--json",
            ],
            output,
            Rules.RepositoryRoot);

        string json = output.ToString();
        Assert.True(code == TalesCli.Ok, json);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.True(doc.RootElement.GetProperty("ended").GetBoolean(), json);
        Assert.Contains(expectedSpeaker, json, StringComparison.Ordinal);
        Assert.Contains(expectedEnding, json, StringComparison.Ordinal);
    }
}
