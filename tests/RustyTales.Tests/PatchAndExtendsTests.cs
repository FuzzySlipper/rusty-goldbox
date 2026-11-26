using System.Text.Json;
using RustyTales.Cli;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;
using RustyTales.Core.Rules;
using static RustyTales.Tests.TempModules;

namespace RustyTales.Tests;

public sealed class PatchAndExtendsTests
{
    [Fact]
    public void ExtensionPatchReplacesFieldsAndResolvesBothOriginalAndPatchedModuleReferences()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        modules.Write("rules/economy.json", """{ "type": "economy", "id": "standard", "sell_fraction": 0.5 }""");
        modules.Write("rules/combat.json", """
            { "type": "combat", "id": "standard", "name": "Standard", "initiative": "1d6", "initiative_by": "side",
              "initiative_order": "highest-first", "initiative_each": "round", "round_seconds": 6,
              "budget": [ { "id": "action", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/slash.json", """
            { "type": "action", "id": "slash", "name": "Slash", "cost": { "action": 1 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1d4" } ] }
            """);
        modules.Write("rules/goblin.json", """
            { "type": "monster", "id": "goblin", "name": "Goblin", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "1d6" }, "stats": { "hit": "self.str + 1" },
              "actions": [ { "action": "slash" } ], "xp": 10 }
            """);

        string house = modules.Module("house", "extension", requires: Require("rules", "*"));
        modules.Write("house/additions.json", """
            [
              { "type": "table", "id": "house_bonus", "keys": [ { "name": "s", "type": "number" } ], "value": "number",
                "rows": [ ["1-10", 5], ["11+", 9] ] },
              { "type": "action", "id": "house_bite", "name": "House bite", "cost": { "action": 1 }, "target": "enemy",
                "always": [ { "op": "damage", "amount": "2d4" } ] },
              { "type": "patch", "id": "generous_shops", "target_type": "economy", "target": "rules:standard",
                "set": { "sell_fraction": 0.8 } },
              { "type": "patch", "id": "fierce_goblin", "target_type": "monster", "target": "rules:goblin",
                "set": { "actions": [ { "action": "house_bite" } ], "stats": null, "xp": 25 } },
              { "type": "patch", "id": "house_hit", "target_type": "derived", "target": "rules:hit",
                "set": { "value": "table(house_bonus, self.str)" } }
            ]
            """);

        ModuleSet set = ModuleLoader.Load(house, []);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(3, set.Patches.Count);
        Assert.Equal(["house:generous_shops", "house:fierce_goblin", "house:house_hit"], set.Patches.Select(patch => patch.PatchId));

        Definition economy = set.Rules!.Find(DefinitionTypes.Economy, "rules:standard", out _)!;
        Assert.Equal(0.8m, economy.Json.GetProperty("sell_fraction").GetDecimal());

        Definition goblin = set.Rules.Find(DefinitionTypes.Monster, "rules:goblin", out _)!;
        Assert.Equal("rules:warrior", set.Rules.Reference(goblin, "$.class").QualifiedId);
        Assert.Equal("house:house_bite", set.Rules.Reference(goblin, "$.actions[0].action").QualifiedId);
        Assert.False(goblin.Json.TryGetProperty("stats", out _));
        Assert.Equal(25, goblin.Json.GetProperty("xp").GetInt32());

        Evaluator evaluator = new(set.Rules, null);
        Creature hero = new("self");
        hero.Values["str"] = 14;
        Assert.Equal(9m, evaluator.Stat(hero, "hit").Number);
    }

    [Fact]
    public void StackedPatchesApplyInDependencyLoadOrder()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        modules.Write("rules/economy.json", """{ "type": "economy", "id": "standard", "sell_fraction": 0.5 }""");
        modules.Module("art", "assets");

        modules.Module("house", "extension", requires: Require("rules", "*"));
        modules.Write("house/patch.json", """
            { "type": "patch", "id": "mid_shops", "target_type": "economy", "target": "rules:standard",
              "set": { "sell_fraction": 0.7 } }
            """);

        string campaign = modules.Module("camp", "campaign", requires: string.Join(", ", Require("rules", "*"), Require("art", "*"), Require("house", "*")));
        modules.Write("camp/world.json", """
            [
              { "type": "area", "id": "room", "name": "Room", "map": [ "+--+", "|  |", "+--+" ],
                "entries": { "start": { "at": [0, 0], "facing": "north" } } },
              { "type": "campaign", "id": "camp", "name": "Camp", "start": { "area": "room", "entry": "start" }, "party": { "min": 1, "max": 4 } },
              { "type": "patch", "id": "final_shops", "target_type": "economy", "target": "rules:standard",
                "set": { "sell_fraction": 0.9 } }
            ]
            """);

        ModuleSet set = ModuleLoader.Load(campaign, []);

        Assert.Empty(set.Diagnostics);
        Assert.Equal(["house:mid_shops", "camp:final_shops"], set.Patches.Select(patch => patch.PatchId));
        Assert.Equal(0.9m, set.Rules!.Economy!.Json.GetProperty("sell_fraction").GetDecimal());
    }

    [Fact]
    public void ExtendsInheritsAcrossModulesAndChainsAndRemovesNullFields()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        modules.Write("rules/goblin.json", """
            { "type": "monster", "id": "goblin", "name": "Goblin", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "1d6" }, "stats": { "hit": "self.str + 1" }, "actions": [], "xp": 10 }
            """);

        string house = modules.Module("house", "extension", requires: Require("rules", "*"));
        modules.Write("house/goblins.json", """
            [
              { "type": "monster", "id": "goblin_archer", "extends": "rules:goblin",
                "name": "Goblin Archer", "stats": null, "xp": 20 },
              { "type": "monster", "id": "goblin_sniper", "extends": "goblin_archer",
                "name": "Goblin Sniper", "level": 3, "xp": 45 }
            ]
            """);

        ModuleSet set = ModuleLoader.Load(house, []);

        Assert.Empty(set.Diagnostics);
        Definition archer = set.Rules!.Find(DefinitionTypes.Monster, "house:goblin_archer", out _)!;
        Definition sniper = set.Rules.Find(DefinitionTypes.Monster, "house:goblin_sniper", out _)!;

        Assert.Equal("Goblin Archer", archer.Name);
        Assert.Equal("rules:warrior", set.Rules.Reference(archer, "$.class").QualifiedId);
        Assert.Equal(1, archer.Json.GetProperty("level").GetInt32());
        Assert.False(archer.Json.TryGetProperty("stats", out _));
        Assert.False(archer.Json.TryGetProperty("extends", out _));

        Assert.Equal("Goblin Sniper", sniper.Name);
        Assert.Equal("rules:warrior", set.Rules.Reference(sniper, "$.class").QualifiedId);
        Assert.Equal(3, sniper.Json.GetProperty("level").GetInt32());
        Assert.Equal(45, sniper.Json.GetProperty("xp").GetInt32());
        Assert.False(sniper.Json.TryGetProperty("stats", out _));
    }

    [Fact]
    public void PatchedParentFlowsIntoExtendedChildAndPatchedChildOverridesParent()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        modules.Write("rules/goblin.json", """
            { "type": "monster", "id": "goblin", "name": "Goblin", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "1d6" }, "actions": [], "xp": 10 }
            """);
        modules.Module("art", "assets");

        modules.Module("house", "extension", requires: Require("rules", "*"));
        modules.Write("house/goblins.json", """
            [
              { "type": "patch", "id": "buff_goblin", "target_type": "monster", "target": "rules:goblin",
                "set": { "xp": 15, "level": 2 } },
              { "type": "monster", "id": "goblin_chief", "extends": "rules:goblin", "name": "Goblin Chief" }
            ]
            """);

        string campaign = modules.Module("camp", "campaign", requires: string.Join(", ", Require("rules", "*"), Require("art", "*"), Require("house", "*")));
        modules.Write("camp/world.json", """
            [
              { "type": "area", "id": "room", "name": "Room", "map": [ "+--+", "|  |", "+--+" ],
                "entries": { "start": { "at": [0, 0], "facing": "north" } } },
              { "type": "campaign", "id": "camp", "name": "Camp", "start": { "area": "room", "entry": "start" }, "party": { "min": 1, "max": 4 } },
              { "type": "patch", "id": "boss_chief", "target_type": "monster", "target": "house:goblin_chief",
                "set": { "xp": 50 } }
            ]
            """);

        ModuleSet set = ModuleLoader.Load(campaign, []);

        Assert.Empty(set.Diagnostics);
        Definition chief = set.Rules!.Find(DefinitionTypes.Monster, "house:goblin_chief", out _)!;
        Assert.Equal(2, chief.Json.GetProperty("level").GetInt32());
        Assert.Equal(50, chief.Json.GetProperty("xp").GetInt32());
    }

    [Fact]
    public void PatchDiagnosticsPointBackToThePatchFileAndSetPath()
    {
        using TempModules modules = new();
        string ruleset = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/economy.json", """{ "type": "economy", "id": "standard", "sell_fraction": 0.5 }""");
        modules.Write("rules/goblin.json", """
            { "type": "monster", "id": "goblin", "name": "Goblin", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "1d6" }, "actions": [], "xp": 10 }
            """);
        modules.Write("rules/self_patch.json", """
            { "type": "patch", "id": "illegal", "target_type": "economy", "target": "rules:standard", "set": { "sell_fraction": 0.6 } }
            """);

        ModuleSet rulesetSet = ModuleLoader.Load(ruleset, []);
        Assert.Equal("patch.module", Assert.Single(rulesetSet.Diagnostics).Rule);

        File.Delete(Path.Combine(ruleset, "self_patch.json"));
        string house = modules.Module("house", "extension", requires: Require("rules", "*"));
        modules.Write("house/bad_patches.json", """
            [
              { "type": "patch", "id": "empty_set", "target_type": "economy", "target": "rules:standard", "set": {} },
              { "type": "patch", "id": "bad_id_change", "target_type": "economy", "target": "rules:standard", "set": { "id": "other" } },
              { "type": "patch", "id": "unqualified", "target_type": "economy", "target": "standard", "set": { "sell_fraction": 0.6 } },
              { "type": "patch", "id": "missing_target", "target_type": "economy", "target": "rules:nope", "set": { "sell_fraction": 0.6 } },
              { "type": "patch", "id": "bad_field", "target_type": "economy", "target": "rules:standard", "set": { "sell_fracton": 0.6, "sell_fraction": null } }
            ]
            """);

        ModuleSet readerSet = ModuleLoader.Load(house, []);

        Assert.All(readerSet.Diagnostics, diagnostic =>
        {
            Assert.Equal("house", diagnostic.Module);
            Assert.Equal("bad_patches.json", Path.GetFileName(diagnostic.File!));
        });
        Assert.Equal(
            [
                ("definition.field-required", "$[4].set.sell_fraction"),
                ("definition.unknown-field", "$[4].set.sell_fracton"),
                ("patch.field", "$[1].set.id"),
                ("patch.set", "$[0].set"),
                ("patch.target", "$[2].target"),
                ("reference.not-found", "$[3].target"),
            ],
            readerSet.Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)).Order());

        // Cross-module RuleSetBuilder errors on a patched field also attribute to the patch file.
        modules.Write("house/bad_patches.json", """
            [
              { "type": "patch", "id": "bad_class", "target_type": "monster", "target": "rules:goblin",
                "set": { "class": "nonexistent_class" } }
            ]
            """);

        ModuleSet builderSet = ModuleLoader.Load(house, []);
        ModuleDiagnostic refError = Assert.Single(builderSet.Diagnostics);
        Assert.Equal("reference.not-found", refError.Rule);
        Assert.Equal("house", refError.Module);
        Assert.Equal("bad_patches.json", Path.GetFileName(refError.File!));
        Assert.Equal("$[0].set.class", refError.JsonPath);
    }

    [Fact]
    public void ExtendsDiagnosticsCatchCyclesMissingTargetsAndUnrequiredModules()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        modules.Module("other", "assets");
        string house = modules.Module("house", "extension", requires: Require("rules", "*"));
        modules.Write("house/bad_extends.json", """
            [
              { "type": "monster", "id": "a", "extends": "b", "name": "A" },
              { "type": "monster", "id": "b", "extends": "a", "name": "B" },
              { "type": "monster", "id": "missing_parent", "extends": "rules:nope", "name": "Nope" },
              { "type": "monster", "id": "unrequired_parent", "extends": "other:nope", "name": "Unrequired" }
            ]
            """);

        ModuleSet set = ModuleLoader.Load(house, []);

        Assert.Equal(
            [
                ("definition.extends-cycle", "$[1].extends"),
                ("reference.module", "$[3].extends"),
                ("reference.not-found", "$[2].extends"),
            ],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)).Order());
    }

    [Fact]
    public void CliValidateAndSchemaReportPatchesAndExtends()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        modules.Write("rules/economy.json", """{ "type": "economy", "id": "standard", "sell_fraction": 0.5 }""");
        modules.Module("house", "extension", requires: Require("rules", "*"));
        modules.Write("house/patch.json", """
            { "type": "patch", "id": "generous_shops", "target_type": "economy", "target": "rules:standard",
              "set": { "sell_fraction": 0.75 } }
            """);

        (int textCode, string textOut) = Run(modules, "module", "validate", "house");
        Assert.Equal(TalesCli.Ok, textCode);
        Assert.Contains("patches applied (1):", textOut, StringComparison.Ordinal);
        Assert.Contains("house:generous_shops -> economy rules:standard (sell_fraction)", textOut, StringComparison.Ordinal);

        (int jsonCode, string jsonOut) = Run(modules, "module", "validate", "house", "--json");
        Assert.Equal(TalesCli.Ok, jsonCode);
        using (JsonDocument doc = JsonDocument.Parse(jsonOut))
        {
            JsonElement patch = Assert.Single(doc.RootElement.GetProperty("patches").EnumerateArray());
            Assert.Equal("house:generous_shops", patch.GetProperty("id").GetString());
            Assert.Equal("economy", patch.GetProperty("targetType").GetString());
            Assert.Equal("rules:standard", patch.GetProperty("target").GetString());
            Assert.Equal(["sell_fraction"], patch.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!));
        }

        (int patchSchemaCode, string patchSchema) = Run(modules, "schema", "patch", "--json");
        Assert.Equal(TalesCli.Ok, patchSchemaCode);
        using (JsonDocument doc = JsonDocument.Parse(patchSchema))
        {
            List<string> fieldNames = doc.RootElement.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()!).ToList();
            Assert.Contains("target_type", fieldNames);
            Assert.Contains("target", fieldNames);
            Assert.Contains("set", fieldNames);
            Assert.DoesNotContain("extends", fieldNames);
        }

        (int monsterSchemaCode, string monsterSchema) = Run(modules, "schema", "monster", "--json");
        Assert.Equal(TalesCli.Ok, monsterSchemaCode);
        using (JsonDocument doc = JsonDocument.Parse(monsterSchema))
        {
            List<string> fieldNames = doc.RootElement.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()!).ToList();
            Assert.Contains("extends", fieldNames);
        }
    }

    private static (int Code, string Output) Run(TempModules modules, params string[] args)
    {
        using StringWriter writer = new();
        int code = TalesCli.Run(args, writer, modules.Root);
        return (code, writer.ToString());
    }
}
