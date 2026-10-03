using System.Text;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

public sealed class InventoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EventsCountAndRemoveIndividualCarriedAndEquippedCopiesAcrossSaves(bool classic)
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, classic);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set, classic);
        Definition item = set.Rules!.Find(DefinitionTypes.Item, classic ? "dagger" : "tool", out _)!;
        party[0].Equipment.Add(item);
        CampaignRunner runner = new(set.Rules, CampaignRunner.NewState(set.Rules, set.Rules.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Assert.Contains(runner.Begin(engine.Random), fact => fact is ItemsFact { Given: true, Count: 2 });
            Assert.Equal("Offer three", runner.MenuOptions()[0].Label);
            Assert.Equal(3, runner.State.CarriedItems.Count());

            List<ModuleDiagnostic> problems = [];
            CampaignState resumed = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(runner.State, set)), "save.json", set, problems)!;
            Assert.Empty(problems);
            runner = new CampaignRunner(set.Rules, resumed);
            Assert.Equal("Offer three", runner.MenuOptions()[0].Label);
            Assert.Contains(runner.Execute("choose 1", engine.Random), fact => fact is ItemsFact { Given: false, Count: 3 });
            Assert.Empty(runner.State.Inventory);
            Assert.Empty(runner.State.Party.SelectMany(member => member.Equipment));
            Assert.Equal("Empty", Assert.Single(runner.MenuOptions()).Label);
        });
    }

    [Fact]
    public void TakingTooManyRefusesWithoutRemovingCopiesAndAbsentMembersDoNotCount()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, false);
        modules.Write("tale/start.json", """{ "type": "event", "id": "start", "kind": "take", "item": "rules:tool", "count": 2, "next": "after", "on_refused": "refused" }""");
        modules.Write("tale/refused.json", """{ "type": "event", "id": "refused", "kind": "menu", "text": "No toll.", "options": [{ "label": "One owned", "when": "carried(item.id == 'tool') == 1" }, { "label": "Leave" }] }""");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = ShopTests.Party(modules, campaign, set);
        Definition item = set.Rules!.Find(DefinitionTypes.Item, "tool", out _)!;
        Character absent = party[1];
        party.RemoveAt(1);
        absent.Equipment.Add(item);
        CampaignState state = CampaignRunner.NewState(set.Rules, set.Rules.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1);
        state.AbsentNpcs.Add(absent);
        state.Inventory.Add(item);
        CampaignRunner runner = new(set.Rules, state);
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Assert.Contains(runner.Begin(engine.Random), fact => fact is RefusedFact);
            Assert.Single(state.Inventory);
            Assert.Single(absent.Equipment);
            Assert.Equal("One owned", runner.MenuOptions()[0].Label);
        });
    }

    [Fact]
    public void CliInventoryTranscriptAndSchemaDescribeTheSameEvents()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, false);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        ShopTests.Party(modules, campaign, set);
        modules.Write("tale/toll.json", """{ "type": "event", "id": "toll", "kind": "take", "item": "rules:tool", "count": 2, "next": "after" }""");
        modules.Write("tale/choice.json", """{ "type": "event", "id": "choice", "kind": "menu", "text": "Offer?", "options": [{ "label": "Offer two", "when": "carried(item.id == 'tool') == 2", "next": "toll" }, { "label": "Leave" }] }""");
        modules.Write("inventory.script", "status\nchoose 1\nstatus\nchoose 1\n");
        Golden.Verify("fixture-inventory-play.txt", CliTranscript.Run(modules.Root,
            ["play", "--campaign", campaign, "--modules", ".", "--party", "a.json,b.json", "--seed", "1", "--script", "inventory.script"]));
        (int code, string schema) = CampaignTests.Run(modules, "schema", "events", "--json");
        Assert.Equal(0, code);
        Assert.Contains("\"give\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"take\"", schema, StringComparison.Ordinal);
        (code, schema) = CampaignTests.Run(modules, "schema", "expressions", "--json");
        Assert.Equal(0, code);
        Assert.Contains("carried(item.id", schema, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "type": "event", "id": "start", "kind": "give", "item": "rules:tool", "count": 0 }""", "event.items", "$.count")]
    [InlineData("""{ "type": "event", "id": "start", "kind": "take", "item": "rules:missing" }""", "reference.not-found", "$.item")]
    [InlineData("""{ "type": "event", "id": "start", "kind": "menu", "text": "Bad?", "options": [{ "label": "Bad", "when": "carried(1) > 0" }, { "label": "Leave" }] }""", "expression.type", "$.options[0].when")]
    public void BadInventoryDataNamesTheModuleFilePathAndRule(string json, string rule, string path)
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, false);
        modules.Write("tale/start.json", json);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.Module == "tale" && diagnostic.File == Path.Combine(modules.Root, "tale/start.json") && diagnostic.JsonPath == path && diagnostic.Rule == rule);
    }

    private static string Fixture(TempModules modules, bool classic)
    {
        string campaign = classic
            ? modules.Module("tale", "campaign", requires: $"{TempModules.Require("classic", "*")}, {TempModules.Require("placeholder-art", "*")}")
            : ShopTests.Fixture(modules);
        string item = classic ? "classic:dagger" : "rules:tool";
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "start" }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "in": { "at": [0, 0], "facing": "north" } } }""");
        modules.Write("tale/start.json", $$"""{ "type": "event", "id": "start", "kind": "give", "item": "{{item}}", "count": 2, "next": "choice" }""");
        modules.Write("tale/choice.json", $$"""{ "type": "event", "id": "choice", "kind": "menu", "text": "Offer?", "options": [{ "label": "Offer three", "when": "carried(item.id == '{{(classic ? "dagger" : "tool")}}') == 3", "next": "toll" }, { "label": "Leave" }] }""");
        modules.Write("tale/toll.json", $$"""{ "type": "event", "id": "toll", "kind": "take", "item": "{{item}}", "count": 3, "next": "after" }""");
        modules.Write("tale/after.json", """{ "type": "event", "id": "after", "kind": "menu", "text": "Paid.", "options": [{ "label": "Still carrying", "when": "carried(item.kind == 'gear' or item.kind == 'weapon') > 0" }, { "label": "Empty" }] }""");
        return campaign;
    }

    private static List<Character> Party(TempModules modules, string campaign, ModuleSet set, bool classic)
    {
        if (!classic)
        {
            return ShopTests.Party(modules, campaign, set);
        }

        CampaignTests.WriteParty(modules, Rules.ClassicPath);
        List<ModuleDiagnostic> problems = [];
        Character character = CharacterFile.Read(Path.Combine(modules.Root, "ada.json"), set, problems)!;
        Assert.Empty(problems);
        character.Equipment.Clear();
        return [character];
    }
}
