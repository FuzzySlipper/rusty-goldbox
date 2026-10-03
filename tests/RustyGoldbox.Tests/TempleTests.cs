using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

public sealed class TempleTests
{
    [Fact]
    public void PaidServicesChooseOneMemberClampHealingRemoveConditionsAndResume()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        var party = ShopTests.Party(modules, campaign, set);
        var condition = set.Rules!.Find(DefinitionTypes.Condition, "ill", out _)!;
        party[1].Conditions.Add(condition);
        party[0].Tracks["hit_points"].Current = 0;
        party[1].Tracks["hit_points"].Current = 1;
        CampaignRunner runner = new(set.Rules, CampaignRunner.NewState(set.Rules, set.Rules.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Assert.IsType<TempleFact>(runner.Begin(engine.Random).Last());
            foreach (string command in new[] { "forward", "serve 0 1", "serve 1 0", "serve 5 1", "buy 1" })
            {
                Assert.IsType<RefusedFact>(Assert.Single(runner.Execute(command, engine.Random)));
            }

            party.ForEach(character => character.Gold = 0);
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("serve 1 2", engine.Random)));
            Assert.Equal(1, party[1].Tracks["hit_points"].Current);
            party[0].Gold = 1;
            party[1].Gold = 1;
            runner.Execute("serve 1 2", engine.Random);
            Assert.Equal(0, party[0].Gold);
            Assert.Equal(0, party[1].Gold);
            Assert.Equal(party[1].Tracks["hit_points"].Max, party[1].Tracks["hit_points"].Current);
            Assert.Equal(0, party[0].Tracks["hit_points"].Current);
            runner.Execute("serve 2 2", engine.Random);
            Assert.Empty(party[1].Conditions);
            List<ModuleDiagnostic> problems = [];
            CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(runner.State, set)), "save", set, problems)!;
            Assert.Empty(problems);
            Assert.Equal("shrine", restored.PendingTemple!.Id);
            Assert.Empty(restored.Party[1].Conditions);
            Assert.Contains(new CampaignRunner(set.Rules, restored).Execute("leave", engine.Random), fact => fact is TextFact { Text: "Safe travels." });
        });
    }

    [Theory]
    [InlineData("{ \"op\": \"heal\", \"amount\": \"1\" }", ".track")]
    [InlineData("{ \"op\": \"damage\", \"amount\": \"1\" }", ".op")]
    [InlineData("{ \"op\": \"heal\", \"amount\": \"1\", \"track\": \"rules:hit_points\", \"to\": \"target\" }", ".to")]
    public void InvalidServiceOperationsHaveAuthoringDiagnostics(string operation, string suffix)
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        modules.Write("tale/shrine.json", "{ \"type\": \"event\", \"id\": \"shrine\", \"kind\": \"temple\", \"text\": \"Hi\", \"services\": [{ \"label\": \"Bad\", \"cost\": \"0\", \"operations\": [" + operation + "] }] }");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Contains(set.Diagnostics, problem => problem.JsonPath == "$.services[0].operations[0]" + suffix && problem.Module == "tale" && problem.File!.EndsWith("shrine.json", StringComparison.Ordinal));
    }

    [Fact]
    public void CliServicesHaveAGoldenTranscript()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        modules.Write("services.script", "serve 1 1\nserve 2 1\nstatus\nleave\n");
        Golden.Verify("fixture-temple-play.txt", CliTranscript.Run(modules.Root,
            ["character", "new", "--module", campaign, "--modules", ".", "--class", "warrior", "--race", "folk", "--name", "A", "--seed", "1", "--out", "a.json"],
            ["play", "--campaign", campaign, "--modules", ".", "--party", "a.json", "--seed", "1", "--script", "services.script"]));
    }

    internal static string Fixture(TempModules modules)
    {
        string campaign = ShopTests.Fixture(modules);
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "shrine" }""");
        modules.Write("rules/ill.json", """{ "type": "condition", "id": "ill", "name": "Ill", "modifiers": [] }""");
        modules.Write("tale/shrine.json", """{ "type": "event", "id": "shrine", "kind": "temple", "text": "Welcome to the shrine.", "services": [{ "label": "Mend", "cost": "self.level * 2", "operations": [{ "op": "heal", "track": "rules:hit_points", "amount": "100" }] }, { "label": "Cure", "cost": "0", "operations": [{ "op": "remove_condition", "condition": "rules:ill" }] }], "next": "farewell" }""");
        return campaign;
    }
}
