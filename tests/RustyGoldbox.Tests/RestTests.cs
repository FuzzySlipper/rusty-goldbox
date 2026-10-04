using System.Text;
using System.Text.Json;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

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
    public void FullRestUsesRestoreCapWithoutLoweringAnAboveCapTrack()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, "days", "", false);
        modules.Write("rules/ward.json", """{ "type": "track", "id": "ward", "name": "Ward", "max": "10", "restore_cap": "6", "start": "10" }""");
        modules.Write("tale/camp.json", """{ "type": "event", "id": "camp", "kind": "rest", "text": "You rest.", "tracks": ["rules:ward", "rules:hit_points"], "prepare": true, "resting": "rules:slow", "periods": 3, "next": "farewell" }""");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        var party = ShopTests.Party(modules, campaign, set);
        party[0].Tracks["ward"].Current = 2;
        party[1].Tracks["ward"].Current = 9;
        party[0].Tracks["hit_points"].Current = 1;
        party[0].Prepared = [];
        CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            List<PlayFact> facts = runner.Execute("choose 1", engine.Random);
            Assert.Equal(6, facts.Count(fact => fact.Describe().Contains("recovers", StringComparison.Ordinal)));
            Assert.Equal(6, party[0].Tracks["ward"].Current);
            Assert.Equal(9, party[1].Tracks["ward"].Current);
            Assert.Equal(party[0].Tracks["hit_points"].Max, party[0].Tracks["hit_points"].Current);
            Assert.Null(party[0].Prepared);
            Assert.Contains(facts, fact => fact is TextFact { Text: "Safe travels." });
        });

        Assert.Equal(3, runner.State.ElapsedDays);
    }

    [Fact]
    public void FifthDeadCharactersDoNotGainHitPointsFromRestOrPaidHealing()
    {
        using TempModules modules = new();
        string campaign = FifthCampaign(modules);
        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, repositoryModules]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = FifthParty(modules, campaign, set, repositoryModules);
        Definition dead = set.Rules!.Find(DefinitionTypes.Condition, "dead", out _)!;
        Definition hitPoints = set.Rules.Find(DefinitionTypes.Track, "hit_points", out _)!;
        decimal livingMax = new Evaluator(set.Rules, null).TrackMax(party[1].ToCreature(), hitPoints);
        party[0].Conditions.Add(dead);
        party[0].Tracks["hit_points"].Current = 0;
        party[0].Tracks["death_failures"].Current = 3;
        party[0].Prepared = [];
        party[1].Tracks["hit_points"].Current = 1;
        party[0].Balances["gold"] = 1;
        party[1].Balances["gold"] = 1;
        CampaignState state = CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "camp", out _)!, party, 1);
        CampaignRunner runner = new(set.Rules!, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            List<ModuleDiagnostic> problems = [];
            CampaignState saved = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(runner.State, set)), "save", set, problems)!;
            Assert.Empty(problems);

            List<PlayFact> wholeRest = runner.Execute("choose 1", engine.Random);
            Assert.Contains(wholeRest, fact => fact is TextFact { Text: "Safe travels." });
            Assert.Equal(0, party[0].Tracks["hit_points"].Current);
            Assert.Contains(dead, party[0].Conditions);
            Assert.Equal(livingMax, party[1].Tracks["hit_points"].Current);
            Assert.Null(party[0].Prepared);
            Assert.Equal(1, runner.State.ElapsedDays);

            CampaignRunner resumed = new(set.Rules, saved);
            List<PlayFact> resumedFacts = resumed.Execute("choose 1", engine.Random);
            Assert.Equal(wholeRest.Select(fact => fact.Describe()), resumedFacts.Select(fact => fact.Describe()));
            Assert.Equal(0, resumed.State.Party[0].Tracks["hit_points"].Current);
            Assert.Equal(livingMax, resumed.State.Party[1].Tracks["hit_points"].Current);
            Assert.Equal(1, resumed.State.ElapsedDays);

            CampaignRunner temple = new(set.Rules, saved);
            temple.Begin(engine.Random);
            temple.Execute("choose 2", engine.Random);
            temple.Execute("serve 1 1", engine.Random);
            Assert.Equal(0, temple.State.Party[0].Tracks["hit_points"].Current);
            Assert.Contains(dead, temple.State.Party[0].Conditions);
            temple.Execute("serve 1 2", engine.Random);
            Assert.Equal(livingMax, temple.State.Party[1].Tracks["hit_points"].Current);

            List<ModuleDiagnostic> templeProblems = [];
            CampaignState templeSaved = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(temple.State, set)), "temple-save", set, templeProblems)!;
            Assert.Empty(templeProblems);
            Assert.Equal(0, templeSaved.Party[0].Tracks["hit_points"].Current);
            Assert.Contains(dead, templeSaved.Party[0].Conditions);
            Assert.Equal(livingMax, templeSaved.Party[1].Tracks["hit_points"].Current);
        });
    }

    [Fact]
    public void CliFullRestCapFailureReturnsLocatedJson()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, "days", "", false);
        modules.Write("rules/hit_points.json", """{ "type": "track", "id": "hit_points", "name": "Hit points", "from_levels": true, "restore_cap": "1 / (self.str - self.str)" }""");
        modules.Write("rules/slow.json", """{ "type": "resting", "id": "slow", "unit": "days", "restore": [] }""");
        modules.Write("tale/camp.json", """{ "type": "event", "id": "camp", "kind": "rest", "text": "You rest.", "tracks": ["rules:hit_points"], "resting": "rules:slow", "periods": 1, "next": "farewell" }""");
        modules.Write("rest.script", "choose 1\n");

        (int characterCode, string characterOutput) = CampaignTests.Run(modules, "character", "new", "--module", campaign,
            "--modules", ".", "--class", "warrior", "--race", "folk", "--name", "A", "--seed", "1", "--out", "a.json");
        Assert.Equal(0, characterCode);
        Assert.True(File.Exists(Path.Combine(modules.Root, "a.json")), characterOutput);

        (int code, string output) = CampaignTests.Run(modules, "play", "--campaign", campaign, "--modules", ".",
            "--party", "a.json", "--seed", "1", "--script", "rest.script", "--json");
        Assert.Equal(1, code);
        string separator = Environment.NewLine + "{";
        int errorStart = output.LastIndexOf(separator, StringComparison.Ordinal);
        Assert.True(errorStart >= 0, output);
        int errorOffset = errorStart + Environment.NewLine.Length;
        using JsonDocument transcript = JsonDocument.Parse(output[..errorOffset]);
        Assert.True(transcript.RootElement.GetProperty("ok").GetBoolean());
        using JsonDocument error = JsonDocument.Parse(output[errorOffset..]);
        Assert.False(error.RootElement.GetProperty("ok").GetBoolean());
        JsonElement diagnostic = Assert.Single(error.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("event.evaluate", diagnostic.GetProperty("rule").GetString());
        Assert.Equal("rules", diagnostic.GetProperty("module").GetString());
        Assert.Equal("rules/hit_points.json", diagnostic.GetProperty("file").GetString());
        Assert.Equal("$", diagnostic.GetProperty("jsonPath").GetString());
        Assert.Contains("Division by zero", diagnostic.GetProperty("message").GetString(), StringComparison.Ordinal);
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

    private static string FifthCampaign(TempModules modules)
    {
        string campaign = modules.Module("camp", "campaign", requires: $"{TempModules.Require("fifth-srd", "*")}, {TempModules.Require("placeholder-art", "*")}");
        modules.Write("camp/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("camp/campaign.json", """{ "type": "campaign", "id": "camp", "name": "Camp", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "choice" }""");
        modules.Write("camp/choice.json", """{ "type": "event", "id": "choice", "kind": "menu", "text": "Choose care.", "options": [{ "label": "Rest", "next": "rest" }, { "label": "Temple", "next": "temple" }] }""");
        modules.Write("camp/long_rest.json", """{ "type": "resting", "id": "long_rest", "unit": "days", "restore": [{ "track": "fifth-srd:hit_points", "amount": "1" }] }""");
        modules.Write("camp/rest.json", """{ "type": "event", "id": "rest", "kind": "rest", "text": "You rest.", "tracks": ["fifth-srd:hit_points"], "resting": "camp:long_rest", "periods": 1, "prepare": true, "next": "farewell" }""");
        modules.Write("camp/temple.json", """{ "type": "event", "id": "temple", "kind": "temple", "text": "The temple tends the party.", "services": [{ "label": "Care", "cost": "1", "currency": "fifth-srd:gold", "operations": [{ "op": "heal", "track": "fifth-srd:hit_points", "amount": "100" }] }], "next": "farewell" }""");
        modules.Write("camp/farewell.json", """{ "type": "event", "id": "farewell", "kind": "text", "text": "Safe travels." }""");
        return campaign;
    }

    private static List<Character> FifthParty(TempModules scratch, string campaign, ModuleSet set, string repositoryModules)
    {
        List<Character> party = [];
        foreach (string name in new[] { "Dead", "Living" })
        {
            string file = name.ToLowerInvariant() + ".json";
            (int code, string output) = CampaignTests.Run(scratch, "character", "new", "--module", campaign,
                "--modules", repositoryModules, "--class", "fighter", "--race", "human",
                "--name", name, "--attributes", "str=16,dex=12,con=14,int=10,wis=10,cha=10",
                "--feature", "soldier,tough,defense", "--seed", "1", "--out", file);
            Assert.True(code == 0, output);
            List<ModuleDiagnostic> problems = [];
            party.Add(CharacterFile.Read(Path.Combine(scratch.Root, file), set, problems)!);
            Assert.Empty(problems);
        }

        return party;
    }
}
