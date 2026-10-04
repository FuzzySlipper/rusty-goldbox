using System.Text;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

public sealed class ShopStockTests
{
    [Fact]
    public void AreaStockDecrementsAfterPaymentAndSurvivesExhaustionAndSave()
    {
        using TempModules modules = new();
        string campaign = FiniteFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);

        List<Character> party = ShopTests.Party(modules, campaign, set);
        party[0].Balances["silver"] = 10;
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 41);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            ShopFact opening = runner.Shop()!;
            ShopOffer offer = Assert.Single(opening.Stock);
            Assert.Equal(2m, offer.Remaining);
            Assert.Equal(5m, opening.MaxBuyValue);
            Assert.Equal("rules:silver", opening.BuyingCurrency!.QualifiedId);

            runner.Execute("buy 1", engine.Random);
            Assert.Equal(1m, runner.Shop()!.Stock[0].Remaining);
            Assert.Single(state.Inventory);
            Assert.Equal(7m, party.Sum(member => member.Balances["silver"]));

            runner.Execute("buy 1", engine.Random);
            Assert.Equal(0m, runner.Shop()!.Stock[0].Remaining);
            Assert.Equal(2, state.Inventory.Count);
            decimal silverBeforeSoldOut = party.Sum(member => member.Balances["silver"]);
            Assert.Contains("0 left", runner.Shop()!.Describe(), StringComparison.Ordinal);

            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("buy 1", engine.Random)));
            Assert.Equal(silverBeforeSoldOut, party.Sum(member => member.Balances["silver"]));
            Assert.Equal(2, state.Inventory.Count);

            string saved = SaveFile.ToJson(state, set);
            List<ModuleDiagnostic> problems = [];
            CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(saved), "stock.json", set, problems)!;
            Assert.Empty(problems);
            runner = new CampaignRunner(set.Rules, restored);
            Assert.Equal(0m, runner.Shop()!.Stock[0].Remaining);
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("buy 1", engine.Random)));
            Assert.Equal(2, restored.Inventory.Count);
        });
    }

    [Fact]
    public void RefusedPaymentLeavesFiniteStockAndPartyUntouched()
    {
        using TempModules modules = new();
        string campaign = FiniteFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = ShopTests.Party(modules, campaign, set);
        party[0].Balances["silver"] = 0;
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 42);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("buy 1", engine.Random)));
            Assert.Equal(2m, runner.Shop()!.Stock[0].Remaining);
            Assert.Empty(state.Inventory);
            Assert.Equal(0m, party.Sum(member => member.Balances.GetValueOrDefault("silver")));
        });
    }

    [Fact]
    public void MaximumValueUsesNamedCurrencyWithUnlimitedCash()
    {
        using TempModules modules = new();
        string campaign = FiniteFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = ShopTests.Party(modules, campaign, set);
        party[0].Balances["silver"] = 0;
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        Definition cheap = set.Rules.Find(DefinitionTypes.Item, "silver_tool", out _)!;
        Definition expensive = set.Rules.Find(DefinitionTypes.Item, "silver_expensive", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 43);
        state.Inventory.AddRange([cheap, expensive]);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            ShopFact opening = runner.Shop()!;
            Assert.Equal([0.75m, 25m], opening.Carried.Select(offer => offer.Price));
            Assert.Equal([true, false], opening.Carried.Select(offer => offer.Sellable));
            Assert.Contains("cannot sell", opening.Describe(), StringComparison.Ordinal);
            Assert.Contains("at most", opening.Carried[1].RefusalReason, StringComparison.Ordinal);
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("sell 2", engine.Random)));
            Assert.Equal(2, state.Inventory.Count);
            Assert.Equal(0m, party.Sum(member => member.Balances.GetValueOrDefault("silver")));

            runner.Execute("sell 1", engine.Random);
            Assert.Single(state.Inventory);
            Assert.Equal(expensive, state.Inventory[0]);
            Assert.Equal(0.75m, party.Sum(member => member.Balances["silver"]));
        });
    }

    [Fact]
    public void SchemaAndShopValidationDescribeFiniteStockAndMaximumValue()
    {
        using TempModules modules = new();
        string campaign = FiniteFixture(modules);
        (int code, string schema) = CampaignTests.Run(modules, "schema", "events", "--json");
        Assert.Equal(0, code);
        Assert.Contains("stock", schema, StringComparison.Ordinal);
        Assert.Contains("max_value", schema, StringComparison.Ordinal);
        Assert.Contains("unlimited", schema, StringComparison.OrdinalIgnoreCase);

        modules.Write("tale/stock.json", """{ "type": "variable", "id": "stock", "value_type": "boolean", "initial": "false", "scope": "area" }""");
        ModuleSet wrongType = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Contains(wrongType.Diagnostics, problem => problem.Rule == "event.shop.stock-type" && problem.JsonPath == "$.items[0].stock");

        modules.Write("tale/stock.json", """{ "type": "variable", "id": "stock", "value_type": "number", "initial": "1.5", "scope": "area" }""");
        ModuleSet fractional = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Contains(fractional.Diagnostics, problem => problem.Rule == "event.shop.stock-integral" && problem.JsonPath == "$.items[0].stock");

        modules.Write("tale/stock.json", """{ "type": "variable", "id": "stock", "value_type": "number", "initial": "1 / 2", "scope": "area" }""");
        ModuleSet expressionFractional = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Contains(expressionFractional.Diagnostics, problem => problem.Rule == "event.shop.stock-integral" && problem.JsonPath == "$.items[0].stock");

        modules.Write("tale/store.json", """{ "type": "event", "id": "store", "kind": "shop", "text": "Too large.", "items": [], "buying": { "currency": "rules:silver", "max_value": 1e100 } }""");
        ModuleSet oversized = ModuleLoader.Load(campaign, [modules.Root]);
        ModuleDiagnostic problem = Assert.Single(oversized.Diagnostics, entry => entry.JsonPath == "$.buying.max_value");
        Assert.Equal("event.shop.buying-max-value", problem.Rule);
        Assert.Contains("decimal", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string FiniteFixture(TempModules modules)
    {
        string campaign = ShopTests.Fixture(modules);
        modules.Write("rules/silver.json", """{ "type": "currency", "id": "silver", "name": "Silver" }""");
        modules.Write("rules/silver_tool.json", """{ "type": "item", "id": "silver_tool", "name": "Silver tool", "kind": "gear", "cost": 3, "currency": "silver", "weight": 1 }""");
        modules.Write("rules/silver_expensive.json", """{ "type": "item", "id": "silver_expensive", "name": "Silver relic", "kind": "treasure", "cost": 100, "currency": "silver", "weight": 1 }""");
        modules.Write("tale/stock.json", """{ "type": "variable", "id": "stock", "value_type": "number", "initial": "2", "scope": "area" }""");
        modules.Write("tale/store.json", """{ "type": "event", "id": "store", "kind": "shop", "text": "Silver stock.", "items": [{ "item": "rules:silver_tool", "stock": "stock" }], "buying": { "currency": "rules:silver", "max_value": 5 }, "next": "farewell" }""");
        return campaign;
    }
}
