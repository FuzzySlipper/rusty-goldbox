using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class DefinitionTests
{
    public static TheoryData<string> TypeNames => [.. DefinitionTypes.All.Select(type => type.Name)];

    [Theory]
    [MemberData(nameof(TypeNames))]
    public void SchemaExamplesAreValidDefinitions(string typeName)
    {
        DefinitionType type = DefinitionTypes.Find(typeName)!;
        using JsonDocument example = JsonDocument.Parse(type.Example);
        List<ModuleDiagnostic> diagnostics = [];

        Definition? definition = DefinitionReader.Read(example.RootElement, "example", "example.json", diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(typeName, definition!.Type.Name);
    }

    [Fact]
    public void FieldProblemsNameTheFieldAndWhatItTakes()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/bad.json", """
            { "type": "class", "id": "Bad-Id", "name": 3, "levels": [ { "xp": 5, "hp": "1d6" }, { "xp": 1 } ], "colour": "red" }
            """);

        ModuleSet set = ModuleLoader.Load(root, []);

        Assert.Equal(
            [
                ("definition.unknown-field", "$.colour"),
                ("definition.field-type", "$.name"),
                ("definition.field-required", "$.levels[1]"),
                ("definition.id", "$.id"),
            ],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)));
        Assert.Contains("Missing required field \"hp\"", Message(set, "definition.field-required"), StringComparison.Ordinal);
        Assert.Null(set.Rules);
    }

    [Fact]
    public void ReferencesMustExist()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/elf.json", """{ "type": "race", "id": "elf", "name": "Elf", "classes": ["wizard"], "ability_limits": { "dex": [7, 19] } }""");

        ModuleSet set = ModuleLoader.Load(root, []);

        Assert.Equal(["reference.stat", "reference.not-found"], set.Diagnostics.Select(diagnostic => diagnostic.Rule));
        Assert.Contains("Attributes: str", set.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("class IDs in 'rules': warrior", set.Diagnostics[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencesNeedTheModuleInRequires()
    {
        using TempModules modules = new();
        modules.Module("art", "assets");
        modules.Write("art/shades.json", """{ "type": "table", "id": "shades", "keys": [ { "name": "n", "type": "number" } ], "value": "number", "rows": [ [1, 1] ] }""");
        modules.Module("rules", "ruleset", requires: Require("art", "*"));
        string extension = modules.Module("house", "extension", requires: Require("rules", "*"));
        modules.Write("house/shade.json", """{ "type": "derived", "id": "shade", "name": "Shade", "value": "table(art:shades, 1)" }""");
        modules.Write("house/elf.json", """{ "type": "race", "id": "elf", "name": "Elf", "classes": ["art:warrior"] }""");

        ModuleSet set = ModuleLoader.Load(extension, []);

        Assert.Equal(["reference.module", "expression.type"], set.Diagnostics.Select(diagnostic => diagnostic.Rule));
        Assert.All(set.Diagnostics, diagnostic => Assert.Contains("which 'house' does not require", diagnostic.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void CrossModuleReferencesResolve()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        string extension = modules.Module("house", "extension", requires: Require("rules", "*"));
        modules.Write("house/elf.json", """{ "type": "race", "id": "elf", "name": "Elf", "classes": ["rules:warrior"], "modifiers": [ { "stat": "hit", "value": "table(rules:bonus, self.str)" } ] }""");

        ModuleSet set = ModuleLoader.Load(extension, []);

        Assert.Empty(set.Diagnostics);
        Definition elf = set.Rules!.Find(DefinitionTypes.Race, "house:elf", out _)!;
        Assert.Equal("rules:warrior", set.Rules.Reference(elf, "$.classes[0]").QualifiedId);
    }

    [Fact]
    public void DerivedValuesMustNotLoop()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/a.json", """{ "type": "derived", "id": "a", "name": "A", "value": "self.b + 1" }""");
        modules.Write("rules/b.json", """{ "type": "derived", "id": "b", "name": "B", "value": "self.a" }""");

        ModuleSet set = ModuleLoader.Load(root, []);

        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.Message.Contains("'a' depends on itself", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpressionsAreCheckedAgainstTheirField()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/hit_check.json", """{ "type": "check", "id": "hit_check", "name": "Hit", "roll": "self.class", "target": "1d20 +", "succeeds": "at-least" }""");
        modules.Write("rules/flag.json", """{ "type": "condition", "id": "flag", "name": "Flag", "modifiers": [ { "stat": "strong", "value": "1" } ] }""");

        ModuleSet set = ModuleLoader.Load(root, []);

        Assert.Equal(
            [("expression.syntax", "$.target"), ("expression.type", "$.modifiers[0].stat"), ("expression.type", "$.roll")],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)).Order());
        Assert.Contains("needs a number, but the expression gives a text", Message(set, "expression.type"), StringComparison.Ordinal);
    }

    [Fact]
    public void StatsAndTableRowsMustBeUnique()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        string extension = modules.Module("house", "extension", requires: Require("rules", "*"));
        modules.Write("house/str.json", """{ "type": "derived", "id": "str", "name": "Str", "value": "1" }""");
        modules.Write("house/level.json", """{ "type": "attribute", "id": "level", "name": "Level", "min": 1, "max": 9 }""");
        modules.Write("house/odd.json", """{ "type": "table", "id": "odd", "keys": [ { "name": "n", "type": "number" } ], "value": "number", "rows": [ ["1-5", 1], ["5+", 2] ] }""");

        ModuleSet set = ModuleLoader.Load(extension, []);

        Assert.Equal(["stat.reserved", "stat.duplicate", "table.overlap"], set.Diagnostics.Select(diagnostic => diagnostic.Rule));
    }

    [Fact]
    public void StatsCantReadThemselvesThroughModifiersOrMonsterStats()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/grow.json", """{ "type": "condition", "id": "grow", "name": "Grow", "modifiers": [ { "stat": "str", "value": "floor(self.str / 2)" } ] }""");
        modules.Write("rules/brute.json", """
            { "type": "monster", "id": "brute", "name": "Brute", "class": "warrior", "level": 1, "hit_points": "1d8",
              "stats": { "str": "self.str + 1", "strong": "1" }, "actions": [], "xp": 5 }
            """);

        ModuleSet set = ModuleLoader.Load(root, []);

        Assert.Equal(
            [("modifier.loop", "$.modifiers[0].value"), ("modifier.loop", "$.stats.str"), ("expression.type", "$.stats.strong")],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)));
    }

    [Fact]
    public void TableNumbersMustFitInADecimal()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/huge.json", """{ "type": "table", "id": "huge", "keys": [ { "name": "n", "type": "number" } ], "value": "number", "rows": [ [1, 1e400] ] }""");
        modules.Write("rules/big.json", """{ "type": "derived", "id": "big", "name": "Big", "value": "99999999999999999999999999999999 + 1" }""");

        ModuleSet set = ModuleLoader.Load(root, []);

        Assert.Equal(["definition.table-row"], set.Diagnostics.Select(diagnostic => diagnostic.Rule));
        modules.Write("rules/huge.json", """{ "type": "table", "id": "huge", "keys": [ { "name": "n", "type": "number" } ], "value": "number", "rows": [ [1, 1] ] }""");
        ModuleSet again = ModuleLoader.Load(root, []);
        Assert.Contains("too large to be a number", Assert.Single(again.Diagnostics).Message, StringComparison.Ordinal);
    }

    private static string Message(ModuleSet set, string rule) => set.Diagnostics.First(diagnostic => diagnostic.Rule == rule).Message;
}
