using System.Text;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

public sealed class EquipmentTests
{
    [Fact]
    public void OriginalRulesetTransfersOneCopyAndPreservesDerivedModifiersAcrossSaveLoad()
    {
        using TempModules modules = new();
        string campaign = ShopTests.Fixture(modules);
        modules.Write("rules/blade.json", """{ "type": "item", "id": "blade", "name": "Blade", "kind": "weapon", "cost": 10, "currency": "gold", "weight": 2, "modifiers": [{ "stat": "hit", "value": "3" }] }""");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);

        List<Character> party = ShopTests.Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        Definition blade = set.Rules.Find(DefinitionTypes.Item, "blade", out _)!
            ?? throw new InvalidOperationException("fixture blade was not loaded");
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 7);
        state.Inventory.Add(blade);
        state.Inventory.Add(blade);
        CampaignRunner runner = new(set.Rules, state);
        Evaluator evaluator = new(set.Rules, null);

        Assert.Equal(0, evaluator.Stat(party[0].ToCreature(), "hit").Number);
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            List<PlayFact> equipped = runner.Execute("equip 1 rules:blade", engine.Random);
            EquipmentFact change = Assert.Single(equipped.OfType<EquipmentFact>());
            Assert.True(change.Equipped);
            Assert.Equal(1, change.Member);
            Assert.Equal("blade", change.Item.Id);
            Assert.Equal(3, evaluator.Stat(party[0].ToCreature(), "hit").Number);
            Assert.Single(state.Party[0].Equipment, item => item == blade);
            Assert.Single(state.Inventory, item => item == blade);
            Assert.All(equipped, fact => Assert.Empty(fact.Rolls));

            List<PlayFact> unequipped = runner.Execute("unequip 1 blade", engine.Random);
            Assert.True(Assert.Single(unequipped.OfType<EquipmentFact>()).Equipped is false);
            Assert.Equal(0, evaluator.Stat(party[0].ToCreature(), "hit").Number);
            Assert.Equal(2, state.Inventory.Count(item => item == blade));
            Assert.DoesNotContain(state.Party[0].Equipment, item => item == blade);
        });

        List<ModuleDiagnostic> problems = [];
        CampaignState? restored = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(state, set)), "equipment-save", set, problems);
        Assert.Empty(problems);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Inventory.Count(item => item.QualifiedId == blade.QualifiedId));
        Assert.DoesNotContain(restored.Party[0].Equipment, item => item.QualifiedId == blade.QualifiedId);
    }

    [Fact]
    public void RestrictedBadAndMisindexedRequestsRefuseWithChoicesBeforeMutation()
    {
        using TempModules modules = new();
        string campaign = ShopTests.Fixture(modules);
        modules.Write("rules/warrior.json", """{ "type": "class", "id": "warrior", "name": "Warrior", "levels": [{ "xp": 0, "hp": "1d10" }], "equipment": "item.kind != 'weapon'" }""");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);

        List<Character> party = ShopTests.Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        Definition blade = set.Rules.Find(DefinitionTypes.Item, "blade", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 11);
        state.Inventory.Add(blade);
        CampaignRunner runner = new(set.Rules, state);
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            RefusedFact restricted = Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("equip 1 blade", engine.Random)));
            Assert.Contains("can't equip Blade", restricted.Reason, StringComparison.Ordinal);
            Assert.Contains("rules:blade", restricted.Reason, StringComparison.Ordinal);
            Assert.Single(state.Inventory);
            Assert.DoesNotContain(party[0].Equipment, item => item == blade);
            Assert.Empty(restricted.Rolls);

            RefusedFact unknown = Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("equip 1 rules:missing", engine.Random)));
            Assert.Contains("There is no item 'rules:missing'", unknown.Reason, StringComparison.Ordinal);
            Assert.Contains("Carried: rules:blade", unknown.Reason, StringComparison.Ordinal);
            Assert.Single(state.Inventory);

            RefusedFact member = Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("equip 0 blade", engine.Random)));
            Assert.Contains("Members: 1=A, 2=B", member.Reason, StringComparison.Ordinal);
            Assert.Contains("equip <member> <item-id>", member.Reason, StringComparison.Ordinal);
            Assert.Single(state.Inventory);

            RefusedFact unequip = Assert.IsType<RefusedFact>(Assert.Single(runner.Execute("unequip 1 blade", engine.Random)));
            Assert.Contains("does not have Blade equipped", unequip.Reason, StringComparison.Ordinal);
            Assert.Contains("equipped items: none", unequip.Reason, StringComparison.Ordinal);
            Assert.Single(state.Inventory);
        });
    }

    [Fact]
    public void ClassicEquipmentChangesTheExistingArmorDerivedValue()
    {
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        string modulesRoot = Path.Combine(Rules.RepositoryRoot, "modules");
        ModuleSet set = ModuleLoader.Load(CampaignTests.SampleCrypt, [modulesRoot]);
        Assert.Empty(set.Diagnostics);
        List<ModuleDiagnostic> problems = [];
        Character ada = CharacterFile.Read(Path.Combine(scratch.Root, "ada.json"), set, problems)!;
        Assert.Empty(problems);
        Definition campaign = set.Rules!.Find(DefinitionTypes.Campaign, "crypt", out _)!;
        Definition chainMail = set.Rules.Find(DefinitionTypes.Item, "classic:chain_mail", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaign, [ada], 13);
        state.Inventory.Add(chainMail);
        CampaignRunner runner = new(set.Rules, state);
        Evaluator evaluator = new(set.Rules, null);
        decimal before = evaluator.Stat(ada.ToCreature(), "ac").Number;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Assert.IsType<EquipmentFact>(Assert.Single(runner.Execute("equip 1 classic:chain_mail", engine.Random)));
            decimal after = evaluator.Stat(ada.ToCreature(), "ac").Number;
            Assert.Equal(before - 5, after);
            Assert.Equal(2, ada.Equipment.Count(item => item == chainMail));
            Assert.Empty(state.Inventory);
        });
    }

    [Fact]
    public void FifthRulesetEquipmentChangesArmorAndAcceptsQualifiedOrLocalIds()
    {
        using TempModules modules = new();
        string campaignPath = FifthCampaign(modules);
        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");
        ModuleSet set = ModuleLoader.Load(campaignPath, [modules.Root, repositoryModules]);
        Assert.Empty(set.Diagnostics);
        List<ModuleDiagnostic> problems = [];
        Character character = CharacterFile.Read(Path.Combine(modules.Root, "hero.json"), set, problems)!;
        Assert.Empty(problems);
        Definition campaign = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        Definition chainMail = set.Rules.Find(DefinitionTypes.Item, "fifth-srd:chain_mail", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaign, [character], 17);
        state.Inventory.Add(chainMail);
        CampaignRunner runner = new(set.Rules, state);
        Evaluator evaluator = new(set.Rules, null);
        decimal before = evaluator.Stat(character.ToCreature(), "ac").Number;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Assert.IsType<EquipmentFact>(Assert.Single(runner.Execute("equip 1 chain_mail", engine.Random)));
            Assert.Equal(before + 5, evaluator.Stat(character.ToCreature(), "ac").Number);
            Assert.Single(character.Equipment, item => item == chainMail);
            Assert.Empty(state.Inventory);
        });
    }

    [Fact]
    public void CliPlayTransfersPurchasedGearAndReportsEquipmentFactsAsJson()
    {
        using TempModules modules = new();
        string campaign = ShopTests.Fixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root]);
        Assert.Empty(set.Diagnostics);
        ShopTests.Party(modules, campaign, set);
        modules.Write("gear.script", "buy 1\nleave\nequip 1 tool\nunequip 1 tool\n");

        (int code, string output) = CampaignTests.Run(modules, "play", "--campaign", campaign, "--modules", ".", "--party", "a.json,b.json", "--seed", "19", "--script", "gear.script", "--save", "gear.json", "--json");
        Assert.True(code == 0, output);
        using System.Text.Json.JsonDocument transcript = System.Text.Json.JsonDocument.Parse(output);
        List<string> kinds = transcript.RootElement.GetProperty("transcript").EnumerateArray()
            .SelectMany(step => step.GetProperty("facts").EnumerateArray())
            .Select(fact => fact.GetProperty("kind").GetString()!)
            .ToList();
        Assert.Contains("equipped", kinds);
        Assert.Contains("unequipped", kinds);
        Assert.Contains("A equips Tool.", output, StringComparison.Ordinal);
        Assert.Contains("A unequips Tool.", output, StringComparison.Ordinal);
        Assert.Contains("rules:tool", File.ReadAllText(Path.Combine(modules.Root, "gear.json")), StringComparison.Ordinal);
    }

    private static string FifthCampaign(TempModules modules)
    {
        string campaign = modules.Module("tale", "campaign", requires: $"{TempModules.Require("fifth-srd", "*")}, {TempModules.Require("placeholder-art", "*")}");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }""");
        (int code, string output) = CampaignTests.Run(modules, "character", "new", "--module", campaign, "--modules", Path.Combine(Rules.RepositoryRoot, "modules"), "--class", "fighter", "--race", "human", "--name", "Hero", "--attributes", "str=16,dex=12,con=14,int=10,wis=10,cha=10", "--feature", "soldier,savage_attacker,defense", "--seed", "1", "--out", "hero.json");
        Assert.True(code == 0, output);
        return campaign;
    }
}
