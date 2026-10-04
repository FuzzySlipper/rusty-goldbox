using System.Text;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

public sealed class MerchantCashTests
{
    [Fact]
    public void NonDndAreaCashPaysRepeatedSalesAndRoundTripsThroughSave()
    {
        using TempModules modules = new();
        string campaign = ShopTests.Fixture(modules);
        modules.Write("tale/cash.json", """{ "type": "variable", "id": "cash", "value_type": "number", "scope": "area", "initial": "7" }""");
        modules.Write("tale/store.json", """{ "type": "event", "id": "store", "kind": "shop", "text": "A credit counter.", "items": [], "buying": { "fraction": 0.9, "currency": "rules:gold", "balance": "cash" } }""");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);

        List<RustyGoldbox.Core.Characters.Character> party = ShopTests.Party(modules, campaign, set);
        foreach (RustyGoldbox.Core.Characters.Character member in party)
        {
            member.Balances["gold"] = 0;
        }

        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        Definition item = set.Rules.Find(DefinitionTypes.Item, "tool", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 11);
        state.Inventory.AddRange([item, item, item]);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Assert.Contains("Buying cash: 7 gold", runner.Begin(engine.Random).OfType<ShopFact>().Single().Text, StringComparison.Ordinal);
            Assert.Equal([3.15m, 3.15m, 3.15m], runner.Shop()!.Carried.Select(offer => offer.Price));

            runner.Execute("sell 1", engine.Random);
            Assert.Equal(3.15m, party.Sum(member => member.Balances["gold"]));
            Assert.Equal(3.85m, state.ValuesFor(state.Area)["cash"].Number);
            Assert.Equal(2, state.Inventory.Count);

            string save = SaveFile.ToJson(state, set);
            List<ModuleDiagnostic> problems = [];
            CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(save), "merchant.json", set, problems)!;
            Assert.Empty(problems);
            Assert.Equal(3.85m, restored.ValuesFor(restored.Area)["cash"].Number);
            runner = new CampaignRunner(set.Rules, restored);

            runner.Execute("sell 1", engine.Random);
            Assert.Equal(0.7m, restored.ValuesFor(restored.Area)["cash"].Number);
            Assert.Single(restored.Inventory);
            decimal beforeCash = restored.ValuesFor(restored.Area)["cash"].Number;
            decimal beforeGold = restored.Party.Sum(member => member.Balances["gold"]);
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("sell 1", engine.Random)));
            Assert.Equal(beforeCash, restored.ValuesFor(restored.Area)["cash"].Number);
            Assert.Equal(beforeGold, restored.Party.Sum(member => member.Balances["gold"]));
            Assert.Single(restored.Inventory);
        });
    }

    [Fact]
    public void OtherCurrencyIsShownWithItsQuoteButCannotConsumeMerchantCash()
    {
        using TempModules modules = new();
        string campaign = ShopTests.Fixture(modules);
        modules.Write("rules/silver.json", """{ "type": "currency", "id": "silver", "name": "Silver" }""");
        modules.Write("rules/silver_tool.json", """{ "type": "item", "id": "silver_tool", "name": "Silver tool", "kind": "gear", "cost": 3, "currency": "silver", "weight": 1 }""");
        modules.Write("tale/cash.json", """{ "type": "variable", "id": "cash", "value_type": "number", "scope": "area", "initial": "7" }""");
        modules.Write("tale/store.json", """{ "type": "event", "id": "store", "kind": "shop", "text": "A gold counter.", "items": [], "buying": { "fraction": 0.9, "currency": "rules:gold", "balance": "cash" } }""");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);

        List<RustyGoldbox.Core.Characters.Character> party = ShopTests.Party(modules, campaign, set);
        foreach (RustyGoldbox.Core.Characters.Character member in party)
        {
            member.Balances["gold"] = 0;
            member.Balances["silver"] = 0;
        }

        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        Definition item = set.Rules.Find(DefinitionTypes.Item, "silver_tool", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 12);
        state.Inventory.Add(item);
        CampaignRunner runner = new(set.Rules, state);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            ShopOffer offer = Assert.Single(runner.Shop()!.Carried);
            Assert.Equal(2.7m, offer.Price);
            Assert.Equal("silver", offer.Currency.Id);
            Assert.Contains("7 gold", runner.Shop()!.Text, StringComparison.Ordinal);

            Assert.Contains("only gold", Assert.Single(runner.Execute("sell 1", engine.Random)).Describe(), StringComparison.Ordinal);
            Assert.Single(state.Inventory);
            Assert.Equal(7m, state.ValuesFor(state.Area)["cash"].Number);
            Assert.Equal(0m, party.Sum(member => member.Balances["gold"]));
            Assert.Equal(0m, party.Sum(member => member.Balances["silver"]));
        });
    }

    [Fact]
    public void FifthSrdShopUsesGlobalMerchantVariableShape()
    {
        using TempModules modules = new();
        string campaign = modules.Module("tale", "campaign", requires: $"{TempModules.Require("fifth-srd", "*")}, {TempModules.Require("placeholder-art", "*")}");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "store" }""");
        modules.Write("tale/merchant_cash.json", """{ "type": "variable", "id": "merchant_cash", "value_type": "number", "initial": "1400" }""");
        modules.Write("tale/gem.json", """{ "type": "item", "id": "gem", "name": "Gem", "kind": "treasure", "cost": 100, "currency": "fifth-srd:gold", "weight": 1 }""");
        modules.Write("tale/store.json", """{ "type": "event", "id": "store", "kind": "shop", "text": "Goodall.", "items": [], "buying": { "fraction": 0.9, "currency": "fifth-srd:gold", "balance": "merchant_cash" } }""");

        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        Definition store = set.Rules!.Find(DefinitionTypes.Event, "tale:store", out _)!;
        Assert.Equal("fifth-srd:gold", set.Rules.Reference(store, "$.buying.currency").QualifiedId);
        Assert.Equal("tale:merchant_cash", set.Rules.Reference(store, "$.buying.balance").QualifiedId);

        string library = Path.Combine(Rules.RepositoryRoot, "modules");
        (int code, string output) = CampaignTests.Run(modules, "character", "new", "--module", campaign,
            "--modules", library, "--class", "fighter", "--race", "human",
            "--attributes", "str=16,dex=12,con=14,int=10,wis=10,cha=10", "--feature", "soldier,savage_attacker,defense",
            "--out", "hero.json", "--json");
        Assert.True(code == 0, output);
        List<ModuleDiagnostic> problems = [];
        Character hero = CharacterFile.Read(Path.Combine(modules.Root, "hero.json"), set, problems)!;
        Assert.Empty(problems);
        Definition definition = set.Rules.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, definition, [hero], 17);
        state.Inventory.Add(set.Rules.Find(DefinitionTypes.Item, "tale:gem", out _)!);
        CampaignRunner runner = new(set.Rules, state);
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            Assert.Equal(90, Assert.Single(runner.Shop()!.Carried).Price);
            Assert.Contains(runner.Execute("sell 1", engine.Random), fact => fact is TradeFact);
            Assert.Equal(90, hero.Balances["gold"]);
            Assert.Equal(1310, state.Variables["merchant_cash"].Number);
            Assert.Empty(state.Inventory);
        });
        CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(state, set)), "fifth-merchant-save", set, problems)!;
        Assert.Empty(problems);
        Assert.Equal(1310, restored.Variables["merchant_cash"].Number);
        Assert.Equal(90, restored.Party[0].Balances["gold"]);
        Assert.Empty(restored.Inventory);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.01")]
    [InlineData("1e100")]
    [InlineData("-1e100")]
    public void UnrepresentableOrOutOfRangeBuyingFractionReturnsALocatedDiagnostic(string rate)
    {
        using TempModules modules = new();
        string campaign = ShopTests.Fixture(modules);
        modules.Write("tale/cash.json", """{ "type": "variable", "id": "cash", "value_type": "number", "initial": "7" }""");
        modules.Write("tale/store.json", $$"""{ "type": "event", "id": "store", "kind": "shop", "text": "A counter.", "items": [], "buying": { "fraction": {{rate}}, "currency": "rules:gold", "balance": "cash" } }""");

        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);

        Assert.False(set.IsValid);
        ModuleDiagnostic problem = Assert.Single(set.Diagnostics, entry => entry.Rule == "event.shop.buying-fraction");
        Assert.Equal("tale", problem.Module);
        Assert.Equal(Path.Combine(modules.Root, "tale", "store.json"), problem.File);
        Assert.Equal("$.buying.fraction", problem.JsonPath);
    }

    [Fact]
    public void BuyingPolicySchemaAndLoadValidationExposeUsableErrors()
    {
        using TempModules modules = new();
        string campaign = ShopTests.Fixture(modules);
        (int code, string schema) = CampaignTests.Run(modules, "schema", "events", "--json");
        Assert.Equal(0, code);
        Assert.Contains("buying", schema, StringComparison.Ordinal);
        Assert.Contains("merchant_cash", schema, StringComparison.Ordinal);

        modules.Write("tale/store.json", """{ "type": "event", "id": "store", "kind": "shop", "text": "Bad rate.", "items": [], "buying": { "fraction": 1.1, "currency": "rules:gold", "balance": "licensed" } }""");
        ModuleSet badRate = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Contains(badRate.Diagnostics, problem => problem.Rule == "event.shop.buying-fraction" && problem.JsonPath == "$.buying.fraction");

        modules.Write("tale/store.json", """{ "type": "event", "id": "store", "kind": "shop", "text": "Bad balance.", "items": [], "buying": { "fraction": 0.9, "currency": "rules:missing", "balance": "licensed" } }""");
        ModuleSet badReferences = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Contains(badReferences.Diagnostics, problem => problem.Rule == "reference.not-found" && problem.JsonPath == "$.buying.currency");
        Assert.Contains(badReferences.Diagnostics, problem => problem.Rule == "event.shop.buying-balance" && problem.JsonPath == "$.buying.balance");
    }
}
