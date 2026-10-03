using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

public sealed class CurrencyTests
{
    [Fact]
    public void ShopTreasureAndCharacterFilesKeepCurrenciesSeparate()
    {
        using TempModules modules = new();
        string campaign = ShopTests.Fixture(modules);
        modules.Write("rules/silver.json", """{ "type": "currency", "id": "silver", "name": "Silver" }""");
        modules.Write("rules/silver_tool.json", """{ "type": "item", "id": "silver_tool", "name": "Silver tool", "kind": "gear", "cost": 3, "currency": "silver", "weight": 1 }""");
        modules.Write("tale/silver_store.json", """{ "type": "event", "id": "silver_store", "kind": "shop", "text": "Silver only.", "items": [{ "item": "rules:silver_tool" }], "next": "silver_treasure" }""");
        modules.Write("tale/silver_treasure.json", """{ "type": "event", "id": "silver_treasure", "kind": "treasure", "currency": "rules:silver", "amount": "3" }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "silver_store" }""");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = ShopTests.Party(modules, campaign, set);
        party[0].Balances["gold"] = 100;
        party[1].Balances["gold"] = 100;
        party[0].Balances["silver"] = 0;
        party[1].Balances["silver"] = 0;
        CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Assert.IsType<ShopFact>(runner.Begin(engine.Random).Last());
            Assert.Equal("silver", Assert.Single(runner.Shop()!.Stock).Currency.Id);
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("buy 1", engine.Random)));
            party[0].Balances["silver"] = 1;
            party[1].Balances["silver"] = 2;
            Assert.DoesNotContain(runner.Execute("buy 1", engine.Random), fact => fact is RefusedFact);
            Assert.Equal(0, party.Sum(member => member.Balances["silver"]));
            Assert.Equal(200, party.Sum(member => member.Balances["gold"]));
            Assert.Contains(runner.Execute("leave", engine.Random), fact => fact is TreasureFact { Amount: 3 });
            Assert.Equal([2m, 1m], party.Select(member => member.Balances["silver"]));

            party[0].Balances["silver"] = 7;
            string file = Path.Combine(modules.Root, "multi.json");
            File.WriteAllText(file, CharacterFile.ToJson(party[0]));
            List<ModuleDiagnostic> problems = [];
            Character? restored = CharacterFile.Read(file, set, problems);
            Assert.Empty(problems);
            Assert.Equal(100, restored!.Balances["gold"]);
            Assert.Equal(7, restored.Balances["silver"]);
        });
    }

    [Fact]
    public void RulesetWithNoCurrenciesCreatesAndSavesAnEmptyBalanceMap()
    {
        using TempModules modules = new();
        string rules = modules.Module("rules", "ruleset");
        modules.Write("rules/hit_points.json", """{ "type": "track", "id": "hit_points", "name": "Hit points", "from_levels": true }""");
        modules.Write("rules/str.json", """{ "type": "attribute", "id": "str", "name": "Strength", "min": 3, "max": 18, "default": 10 }""");
        modules.Write("rules/warrior.json", """{ "type": "class", "id": "warrior", "name": "Warrior", "levels": [{ "xp": 0, "hp": "5" }] }""");
        modules.Write("rules/folk.json", """{ "type": "race", "id": "folk", "name": "Folk", "classes": ["warrior"] }""");
        modules.Write("rules/creation.json", """{ "type": "character-creation", "id": "standard", "name": "Standard", "attributes": ["str"], "attribute_roll": "10", "assignment": "in-order", "starting": { "warrior": {} } }""");
        ModuleSet set = ModuleLoader.Load(rules, []);
        Assert.Empty(set.Diagnostics);
        Assert.Empty(set.Rules!.Currencies);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            List<ModuleDiagnostic> problems = [];
            Character character = CharacterRules.Create(set.Rules, Character.StampsOf(set), new CreationRequest("A", "warrior", "folk"), new DiceRoller(engine.Random, engine.Random.CreateScoped(new ScopedRngCreateRequest(1, "currency-none"))), problems)!;
            Assert.Empty(problems);
            Assert.Empty(character.Balances);
            string file = Path.Combine(modules.Root, "none.json");
            File.WriteAllText(file, CharacterFile.ToJson(character));
            Character? restored = CharacterFile.Read(file, set, problems);
            Assert.Empty(problems);
            Assert.Empty(restored!.Balances);

            JsonObject legacy = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            legacy.Remove("balances");
            legacy["gold"] = 1;
            problems.Clear();
            using JsonDocument legacyDocument = JsonDocument.Parse(Encoding.UTF8.GetBytes(legacy.ToJsonString()));
            Assert.Null(CharacterFile.Read(legacyDocument.RootElement, "legacy.json", "$", set, problems));
            Assert.Contains(problems, problem => problem.JsonPath == "$.balances");
        });
    }
}
