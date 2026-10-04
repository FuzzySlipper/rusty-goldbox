using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

public sealed class NpcTests
{
    [Fact]
    public void NpcsJoinDismissAndRejoinWithTheirSavedStateAndIdentity()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        var party = ShopTests.Party(modules, campaign, set).Take(1).ToList();
        CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            List<PlayFact> recruited = runner.Execute("choose 1", engine.Random);
            Assert.Contains(recruited, fact => fact is PartyFact { Joined: true });
            Assert.Contains(recruited, fact => fact is PerceptionFact { Scope: "brugh-entry", Mode: "truth" });
            PerceptionFact guidePerception = Assert.Single(recruited.OfType<PerceptionFact>(), fact => fact.Who == "Guide");
            Assert.Equal(1m, guidePerception.Result.Roll);
            Assert.Single(guidePerception.Rolls);
            Character guide = runner.State.Party[1];
            Assert.Equal("tale:guide", guide.Npc!.QualifiedId);
            guide.Balances["gold"] = 0;
            guide.Tracks["hit_points"].Current = 1;
            guide.Equipment.Add(set.Rules.Find(DefinitionTypes.Item, "tool", out _)!);
            Assert.Contains(runner.Execute("choose 1", engine.Random), fact => fact is RefusedFact);
            Assert.Equal(2, runner.State.Party.Count);
            List<ModuleDiagnostic> problems = [];
            CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(runner.State, set)), "save", set, problems)!;
            Assert.Empty(problems);
            Assert.Equal("tale:guide", restored.Party[1].Npc!.QualifiedId);
            runner = new(set.Rules, restored);
            runner.Execute("choose 2", engine.Random);
            Assert.Single(runner.State.Party);
            Assert.Single(runner.State.AbsentNpcs);
            restored = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(runner.State, set)), "save", set, problems)!;
            Assert.Empty(problems);
            runner = new(set.Rules, restored);
            guide = Assert.Single(restored.AbsentNpcs);
            Assert.Equal(new PerceptionState("brugh-entry", "truth"), guide.Perception);
            List<PlayFact> rejoined = runner.Execute("choose 1", engine.Random);
            Assert.DoesNotContain(rejoined, fact => fact is PerceptionFact);
            Assert.Same(guide, restored.Party[1]);
            Assert.Empty(restored.AbsentNpcs);
            Assert.Equal(new PerceptionState("brugh-entry", "truth"), restored.Party[1].Perception);
            Assert.Equal(0, guide.Balances["gold"]);
            Assert.Equal(1, guide.Tracks["hit_points"].Current);
            Assert.Single(guide.Equipment);
        });
    }

    [Fact]
    public void PartyBoundsAndNpcIdentityPreventRemovingPlayerCharacters()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        var party = ShopTests.Party(modules, campaign, set);
        CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            Assert.Contains(runner.Execute("choose 1", engine.Random), fact => fact is RefusedFact);
            Assert.Contains(runner.Execute("choose 2", engine.Random), fact => fact is RefusedFact);
            Assert.Equal(2, runner.State.Party.Count);
            runner.State.Party.RemoveAt(1);
            runner.Execute("choose 1", engine.Random);
            runner.State.Party.RemoveAt(0);
            Assert.Contains(runner.Execute("choose 2", engine.Random), fact => fact is RefusedFact);
            Assert.Single(runner.State.Party);
            Assert.Empty(runner.State.AbsentNpcs);
        });
    }

    [Fact]
    public void CharacterDataAndDuplicateSaveNpcsAreCheckedAtTheirBoundaries()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        JsonObject npc = JsonNode.Parse(File.ReadAllText(Path.Combine(modules.Root, "tale/guide.json")))!.AsObject();
        modules.Write("rules/unearned.json", """{ "type": "feature", "id": "unearned", "name": "Unearned", "kind": "feat", "modifiers": [] }""");
        npc["character"]!["levels"]![0]!["features"] = new JsonArray("rules:unearned");
        modules.Write("tale/guide.json", npc.ToJsonString());
        Assert.Contains(ModuleLoader.Load(campaign, [modules.Root]).Diagnostics, problem => problem.Module == "tale" && problem.File!.EndsWith("guide.json", StringComparison.Ordinal) && problem.JsonPath!.StartsWith("$.character.levels", StringComparison.Ordinal));
        npc["character"]!["levels"]![0]!.AsObject().Remove("features");
        modules.Write("tale/guide.json", npc.ToJsonString());
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        var party = ShopTests.Party(modules, campaign, set).Take(1).ToList();
        CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine => { runner.Begin(engine.Random); runner.Execute("choose 1", engine.Random); });
        JsonObject saved = JsonNode.Parse(SaveFile.ToJson(runner.State, set))!.AsObject();
        saved["absent_npcs"]!.AsArray().Add(saved["party"]![1]!.DeepClone());
        List<ModuleDiagnostic> problems = [];
        Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(saved.ToJsonString()), "bad-save", set, problems));
        Assert.Contains(problems, problem => problem.JsonPath == "$.absent_npcs" && problem.Message.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void CliExportsAndPartyEventsHaveAGoldenTranscript()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        modules.Write("npc.script", "choose 1\nstatus\nchoose 1\nchoose 2\nchoose 1\nstatus\n");
        Golden.Verify("fixture-npc-play.txt", CliTranscript.Run(modules.Root,
            ["character", "new", "--module", campaign, "--modules", ".", "--class", "warrior", "--race", "folk", "--name", "A", "--seed", "1", "--out", "a.json"],
            ["character", "npc", "a.json", "--module", campaign, "--modules", ".", "--id", "exported", "--out", "exported.json", "--json"],
            ["play", "--campaign", campaign, "--modules", ".", "--party", "a.json", "--seed", "1", "--script", "npc.script"]));
    }

    private static string Fixture(TempModules modules)
    {
        string campaign = ShopTests.Fixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Character template = ShopTests.Party(modules, campaign, set)[0];
        template.Name = "Guide";
        template.Perception = new PerceptionState("brugh-entry", "truth");
        modules.Write("tale/guide.json", NpcFile.ToJson(template, "guide"));
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 2 }, "intro": "party_choice" }""");
        modules.Write("tale/choice.json", """{ "type": "event", "id": "party_choice", "kind": "menu", "text": "Company?", "options": [{ "label": "Recruit", "next": "recruit" }, { "label": "Dismiss", "next": "dismiss" }] }""");
        modules.Write("rules/perception.json", """{ "type": "check", "id": "perception", "name": "Perception", "roll": "1d1", "bonus": "self.hit", "target": "1", "succeeds": "at-least", "tiers": [] }""");
        modules.Write("tale/sense.json", """{ "type": "event", "id": "sense", "kind": "perception", "scope": "brugh-entry", "check": "rules:perception", "success_mode": "truth", "failure_mode": "glamour", "next": "party_choice" }""");
        modules.Write("tale/recruit.json", """{ "type": "event", "id": "recruit", "kind": "join", "npc": "guide", "next": "sense", "on_refused": "party_choice" }""");
        modules.Write("tale/dismiss.json", """{ "type": "event", "id": "dismiss", "kind": "dismiss", "npc": "guide", "next": "party_choice", "on_refused": "party_choice" }""");
        return campaign;
    }
}
