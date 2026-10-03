using System.Text;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Tests;

public sealed class RestTests
{
    [Theory]
    [InlineData("days", "", 3)]
    [InlineData("hours", "", 0.125)]
    [InlineData("rounds", ", \"combat\": \"stalemate\"", 0.0020833333333333333)]
    public void RestAdvancesAuthoredPeriodsAndRecoversUpToTheCap(string unit, string seconds, double expected)
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, unit, seconds, false);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        var party = ShopTests.Party(modules, campaign, set);
        party[0].Tracks["hit_points"].Current = 1;
        CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            List<PlayFact> facts = runner.Execute("choose 1", engine.Random);
            Assert.Equal(6, facts.Count(fact => fact.Describe().Contains("recovers", StringComparison.Ordinal)));
            Assert.Equal(party[0].Tracks["hit_points"].Max, party[0].Tracks["hit_points"].Current);
            Assert.Contains(facts, fact => fact is TextFact { Text: "Safe travels." });
        });
        Assert.InRange((double)runner.State.ElapsedDays, expected - 1e-12, expected + 1e-12);
    }

    [Fact]
    public void ASeededWandererInterruptsRecoveryAndResumesLikeAnUnbrokenRun()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, "days", "", true);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        var party = ShopTests.Party(modules, campaign, set);
        party[0].Tracks["hit_points"].Current = 1;
        party[0].Prepared = [];
        CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            List<ModuleDiagnostic> problems = [];
            CampaignState saved = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(runner.State, set)), "save", set, problems)!;
            Assert.Empty(problems);
            List<PlayFact> first = runner.Execute("choose 1", engine.Random);
            CampaignRunner resumed = new(set.Rules, saved);
            List<PlayFact> second = resumed.Execute("choose 1", engine.Random);
            Assert.Equal(first.Select(fact => fact.Describe()), second.Select(fact => fact.Describe()));
            Assert.Equal(first.SelectMany(fact => fact.Rolls).Select(roll => roll.ToString()), second.SelectMany(fact => fact.Rolls).Select(roll => roll.ToString()));
            Assert.Single(first.OfType<FightFact>());
            Assert.Contains(first, fact => fact is TextFact { Text: "The wanderer departs." });
            Assert.DoesNotContain(first, fact => fact.Describe().Contains("recovers", StringComparison.Ordinal) || fact is TextFact { Text: "Safe travels." });
            Assert.Equal(1, runner.State.ElapsedDays);
            Assert.Equal(1, party[0].Tracks["hit_points"].Current);
            Assert.NotNull(party[0].Prepared);
            Assert.False(runner.State.Ended);
        });
    }

    [Fact]
    public void CliTimedRestHasAGoldenTranscript()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, "hours", "", false);
        modules.Write("rest.script", "choose 1\nstatus\n");
        Golden.Verify("fixture-rest-play.txt", CliTranscript.Run(modules.Root,
            ["character", "new", "--module", campaign, "--modules", ".", "--class", "warrior", "--race", "folk", "--name", "A", "--seed", "1", "--out", "a.json"],
            ["play", "--campaign", campaign, "--modules", ".", "--party", "a.json", "--seed", "1", "--script", "rest.script"]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidPeriodsNameTheEventAndPath(int count)
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, "days", "", false);
        modules.Write("tale/camp.json", "{ \"type\": \"event\", \"id\": \"camp\", \"kind\": \"rest\", \"text\": \"Rest\", \"tracks\": [], \"resting\": \"rules:slow\", \"periods\": " + count + " }");
        Assert.Contains(ModuleLoader.Load(campaign, [modules.Root]).Diagnostics, problem => problem.Module == "tale" && problem.JsonPath == "$.periods" && problem.Rule == "event.rest");
    }

    private static string Fixture(TempModules modules, string unit, string seconds, bool wandering)
    {
        string campaign = ShopTests.Fixture(modules);
        modules.Write("rules/slow.json", "{ \"type\": \"resting\", \"id\": \"slow\", \"unit\": \"" + unit + "\"" + seconds + ", \"restore\": [{ \"track\": \"hit_points\", \"amount\": \"2\" }] }");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "rest_choice" }""");
        modules.Write("tale/choice.json", """{ "type": "event", "id": "rest_choice", "kind": "menu", "text": "Rest?", "options": [{ "label": "Rest", "next": "camp" }] }""");
        modules.Write("tale/camp.json", "{ \"type\": \"event\", \"id\": \"camp\", \"kind\": \"rest\", \"text\": \"You rest.\", \"tracks\": " + (wandering ? "[\"rules:hit_points\"]" : "[]") + ", \"prepare\": true, \"resting\": \"rules:slow\", \"periods\": 3, \"next\": \"farewell\"" + (wandering ? ", \"wandering\": { \"when\": \"1d6 > 0\", \"event\": \"wanderer\" }" : "") + " }");
        modules.Write("rules/wait.json", """{ "type": "action", "id": "wait", "name": "Wait", "target": "self", "cost": { "turn": 1 }, "always": [{ "op": "heal", "amount": "0" }] }""");
        modules.Write("rules/stalemate.json", """{ "type": "combat", "id": "stalemate", "name": "Stalemate", "initiative": "1", "initiative_by": "side", "initiative_order": "highest-first", "track": "hit_points", "budget": [{ "id": "turn", "per_turn": 1 }], "round_limit": 1, "round_seconds": 60, "initiative_each": "combat", "defeated": "self.hit_points <= 0" }""");
        modules.Write("rules/statue.json", """{ "type": "monster", "id": "statue", "name": "Statue", "tracks": { "hit_points": "5" }, "actions": [{ "action": "wait" }], "xp": 0 }""");
        modules.Write("rules/statues.json", """{ "type": "encounter", "id": "statues", "name": "Statues", "monsters": [{ "monster": "statue", "count": "1" }] }""");
        modules.Write("tale/wanderer.json", """{ "type": "event", "id": "wanderer", "kind": "combat", "encounter": "rules:statues", "on_draw": "departed" }""");
        modules.Write("tale/departed.json", """{ "type": "event", "id": "departed", "kind": "text", "text": "The wanderer departs." }""");
        return campaign;
    }
}
