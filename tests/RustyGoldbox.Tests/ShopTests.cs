using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

public sealed class ShopTests
{
    [Fact]
    public void TradingUsesGuardsPooledGoldAndIndividualCarriedCopies()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        CampaignRunner runner = new(set.Rules!, CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1));
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            ShopFact shop = runner.Shop()!;
            Assert.Equal("tool", Assert.Single(shop.Stock).Item.Id);
            Assert.Equal(3.5m, shop.Stock[0].Price);
            foreach (string command in new[] { "forward", "choose 1", "buy 0", "buy 2", "sell 1", "buy nope" })
            {
                Assert.IsType<RefusedFact>(Assert.Single(runner.Execute(command, engine.Random)));
            }

            runner.State.Variables["licensed"] = Value.Of(true);
            Assert.Equal(2, runner.Shop()!.Stock.Count);
            party[0].Gold = 1;
            party[1].Gold = 2.5m;
            Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("buy 2", engine.Random)));
            Assert.Empty(runner.State.Inventory);
            Assert.Equal([1m, 2.5m], party.Select(member => member.Gold));

            runner.Execute("buy 1", engine.Random);
            Assert.Equal([0m, 0m], party.Select(member => member.Gold));
            Definition tool = Assert.Single(runner.State.Inventory);
            // Duplicate items are separate copies; selling one leaves the other and the worn one.
            runner.State.Inventory.Add(tool);
            party[1].Equipment.Add(tool);
            Assert.Equal([0.875m, 0.875m, 0.875m], runner.Shop()!.Carried.Select(offer => offer.Price));
            runner.Execute("sell 2", engine.Random);
            Assert.Single(runner.State.Inventory);
            Assert.Single(party[1].Equipment);
            Assert.Equal([0.875m, 0m], party.Select(member => member.Gold));
            Assert.Equal("B", runner.Shop()!.Carried[1].Holder);
            runner.Execute("sell 2", engine.Random);
            Assert.Empty(party[1].Equipment);
            Assert.Single(runner.State.Inventory);

            Assert.Contains(runner.Execute("leave", engine.Random), fact => fact is TextFact { Text: "Safe travels." });
            Assert.Null(runner.State.PendingShop);
            Assert.IsType<MovedFact>(Assert.Single(runner.Execute("forward", engine.Random)));
            foreach (string command in new[] { "buy 1", "sell 1", "leave" })
            {
                Assert.IsType<RefusedFact>(Assert.Single(runner.Execute(command, engine.Random)));
            }
        });
    }

    [Fact]
    public void CliShopTranscriptAndSaveResumeAgree()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Party(modules, campaign, set);
        modules.Write("trade.script", "status\nforward\nbuy 1\nbuy 1\nsell 2\nstatus\nleave\nforward\n");
        modules.Write("first.script", "status\nforward\nbuy 1\n");
        modules.Write("rest.script", "buy 1\nsell 2\nstatus\nleave\nforward\n");
        string[] Play(params string[] args) => ["play", "--campaign", campaign, "--modules", ".", .. args];
        Golden.Verify("fixture-shop-play.txt", CliTranscript.Run(modules.Root,
            Play("--party", "a.json,b.json", "--seed", "1", "--script", "trade.script", "--save", "whole.json")));
        (int code, string output) = CampaignTests.Run(modules, Play("--party", "a.json,b.json", "--seed", "1", "--script", "first.script", "--save", "half.json", "--json"));
        Assert.True(code == 0, output);
        using JsonDocument transcript = JsonDocument.Parse(output);
        JsonElement shop = transcript.RootElement.GetProperty("transcript")[0].GetProperty("facts").EnumerateArray().Single(fact => fact.GetProperty("kind").GetString() == "shop").GetProperty("shop");
        Assert.Equal(3.5m, shop.GetProperty("stock")[0].GetProperty("price").GetDecimal());
        Assert.Contains("\"pending_shop\": \"tale:store\"", File.ReadAllText(Path.Combine(modules.Root, "half.json")), StringComparison.Ordinal);
        (code, output) = CampaignTests.Run(modules, Play("--load", "half.json", "--script", "rest.script", "--save", "resumed.json"));
        Assert.True(code == 0, output);
        Assert.Equal(File.ReadAllText(Path.Combine(modules.Root, "whole.json")), File.ReadAllText(Path.Combine(modules.Root, "resumed.json")));
    }

    [Fact]
    public void SavesValidateThePendingShopAtTheirBoundary()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        modules.Write("tale/menu.json", """{ "type": "event", "id": "menu", "kind": "menu", "text": "Wait?", "options": [{ "label": "Leave" }] }""");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        CampaignState state = CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, Party(modules, campaign, set), 1);
        state.PendingShop = set.Rules.Find(DefinitionTypes.Event, "store", out _);
        JsonObject saved = JsonNode.Parse(SaveFile.ToJson(state, set))!.AsObject();
        foreach (string wrong in new[] { "tale:menu", "tale:farewell", "tale:missing" })
        {
            saved["pending_shop"] = wrong;
            List<ModuleDiagnostic> problems = [];
            Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(saved.ToJsonString()), "bad.json", set, problems));
            Assert.Contains(problems, problem => problem.JsonPath == "$.pending_shop");
        }

        saved["pending_shop"] = "tale:store";
        saved["pending_menu"] = "tale:menu";
        List<ModuleDiagnostic> simultaneous = [];
        Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(saved.ToJsonString()), "bad.json", set, simultaneous));
        Assert.Contains(simultaneous, problem => problem.Message.Contains("at the same time", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("rules/economy.json", """{ "type": "economy", "id": "standard", "sell_fraction": -0.1 }""", "economy.sell-fraction", "$.sell_fraction")]
    [InlineData("rules/economy.json", """{ "type": "economy", "id": "standard", "sell_fraction": 1.1 }""", "economy.sell-fraction", "$.sell_fraction")]
    [InlineData("rules/tool.json", """{ "type": "item", "id": "tool", "name": "Tool", "kind": "gear", "cost": -1, "weight": 1 }""", "item.cost", "$.cost")]
    [InlineData("rules/other.json", """{ "type": "economy", "id": "other", "sell_fraction": 0.5 }""", "economy.duplicate", "$.id")]
    [InlineData("tale/store.json", """{ "type": "event", "id": "store", "kind": "shop", "text": "Hi", "items": [{ "item": "rules:missing" }] }""", "reference.not-found", "$.items[0].item")]
    public void BadShopDataNamesItsFileAndPath(string file, string json, string rule, string path)
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        modules.Write(file, json);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Contains(set.Diagnostics, problem => problem.Rule == rule && problem.JsonPath == path && problem.File == Path.Combine(modules.Root, file));
    }

    [Fact]
    public void AShopNeedsRulesetResaleDataAndCanHaveNoStock()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        File.Delete(Path.Combine(modules.Root, "rules", "economy.json"));
        ModuleSet missing = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Contains(missing.Diagnostics, problem => problem.Rule == "event.shop" && problem.JsonPath == "$" && problem.Module == "tale");

        modules.Write("rules/economy.json", """{ "type": "economy", "id": "standard", "sell_fraction": 0 }""");
        modules.Write("tale/store.json", """{ "type": "event", "id": "store", "kind": "shop", "text": "Donations only.", "items": [] }""");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        CampaignState state = CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, Party(modules, campaign, set), 1);
        state.Inventory.Add(set.Rules.Find(DefinitionTypes.Item, "tool", out _)!);
        CampaignRunner runner = new(set.Rules, state);
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            Assert.Empty(runner.Shop()!.Stock);
            Assert.Equal(0, Assert.Single(runner.Shop()!.Carried).Price);
            runner.Execute("sell 1", engine.Random);
            Assert.Empty(state.Inventory);
            runner.Execute("leave", engine.Random);
            Assert.Null(state.PendingShop);
        });
    }

    [Fact]
    public void AResaleOverflowLeavesTheItemAndBalancesIntact()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        List<Character> party = Party(modules, campaign, set);
        party[0].Gold = decimal.MaxValue;
        party[1].Gold = 0;
        CampaignState state = CampaignRunner.NewState(set.Rules!, set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!, party, 1);
        state.Inventory.Add(set.Rules.Find(DefinitionTypes.Item, "blade", out _)!);
        CampaignRunner runner = new(set.Rules, state);
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            RuleFailure failure = Assert.Throws<RuleFailure>(() => runner.Execute("sell 1", engine.Random));
            Assert.Equal("$.sell_fraction", failure.Diagnostic.JsonPath);
            Assert.Single(state.Inventory);
            Assert.Equal([decimal.MaxValue, 0m], party.Select(member => member.Gold));
        });
    }

    internal static string Fixture(TempModules modules)
    {
        Rules.WriteSmallRuleset(modules);
        modules.Write("rules/creation.json", """{ "type": "character-creation", "id": "c", "name": "C", "attributes": ["str"], "attribute_roll": "10", "assignment": "in-order", "starting_gold": { "warrior": "5" } }""");
        modules.Write("rules/folk.json", """{ "type": "race", "id": "folk", "name": "Folk", "classes": ["warrior"] }""");
        modules.Write("rules/economy.json", """{ "type": "economy", "id": "standard", "sell_fraction": 0.25 }""");
        modules.Write("rules/tool.json", """{ "type": "item", "id": "tool", "name": "Tool", "kind": "gear", "cost": 3.5, "weight": 1 }""");
        modules.Write("rules/blade.json", """{ "type": "item", "id": "blade", "name": "Blade", "kind": "weapon", "cost": 10, "weight": 2 }""");
        modules.Module("art", "assets");
        string campaign = modules.Module("tale", "campaign", requires: $"{TempModules.Require("rules", "*")}, {TempModules.Require("art", "*")}");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "store" }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|     |", "+--+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/licensed.json", """{ "type": "variable", "id": "licensed", "value_type": "boolean", "initial": "false" }""");
        modules.Write("tale/store.json", """{ "type": "event", "id": "store", "kind": "shop", "text": "Welcome to the store.", "items": [{ "item": "rules:tool" }, { "item": "rules:blade", "when": "campaign.var.licensed" }], "next": "farewell" }""");
        modules.Write("tale/farewell.json", """{ "type": "event", "id": "farewell", "kind": "text", "text": "Safe travels." }""");
        return campaign;
    }

    internal static List<Character> Party(TempModules scratch, string campaign, ModuleSet set)
    {
        List<Character> party = [];
        foreach (string name in new[] { "A", "B" })
        {
            string file = name.ToLowerInvariant() + ".json";
            (int code, string output) = CampaignTests.Run(scratch, "character", "new", "--module", campaign, "--modules", scratch.Root,
                "--class", "warrior", "--race", "folk", "--name", name, "--seed", "1", "--out", file);
            Assert.True(code == 0, output);
            List<ModuleDiagnostic> problems = [];
            party.Add(CharacterFile.Read(Path.Combine(scratch.Root, file), set, problems)!);
            Assert.Empty(problems);
        }

        return party;
    }
}
