using System.Text.Json;
using RustyGoldbox.Cli;

namespace RustyGoldbox.Tests;

public sealed class CharacterEquipmentCliTests
{
    private static string Fifth => Path.Combine(Rules.RepositoryRoot, "modules", "fifth-srd");

    [Fact]
    public void OriginalClassWithoutAnEquipmentRuleAcceptsAndPersistsItsLoadout()
    {
        string ascend = Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "ascend");
        using TempModules scratch = new();
        (int code, string output) = Run(scratch.Root,
            "character", "new", "--module", ascend, "--class", "warrior", "--race", "folk",
            "--attributes", "might=16,grace=12,grit=14,wit=10", "--feature", "iron_will,weapon_focus",
            "--equipment", "ascend:longsword,banded_mail", "--seed", "7", "--out", "warrior.json", "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(scratch.Root, "warrior.json")));
        Assert.Equal(["ascend:longsword", "ascend:banded_mail"],
            saved.RootElement.GetProperty("equipment").EnumerateArray().Select(item => item.GetString()));

        (code, output) = Run(scratch.Root, "character", "show", "warrior.json", "--module", ascend, "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument loaded = JsonDocument.Parse(output);
        Assert.Equal(["ascend:longsword", "ascend:banded_mail"],
            loaded.RootElement.GetProperty("character").GetProperty("equipment").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void FifthFighterCanStartEquippedAndReadTheSavedLoadout()
    {
        using TempModules scratch = new();
        (int code, string output) = Run(scratch.Root,
            "character", "new", "--module", Fifth, "--class", "fighter", "--race", "human",
            "--feature", "soldier,savage_attacker,defense", "--equipment", "longsword", "--equipment", "chain_mail",
            "--seed", "7", "--out", "fighter.json", "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument made = JsonDocument.Parse(output);
        Assert.Equal(["fifth-srd:longsword", "fifth-srd:chain_mail"],
            made.RootElement.GetProperty("character").GetProperty("equipment").EnumerateArray().Select(item => item.GetString()));

        (code, output) = Run(scratch.Root, "character", "show", "fighter.json", "--module", Fifth, "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument loaded = JsonDocument.Parse(output);
        Assert.Equal(["fifth-srd:longsword", "fifth-srd:chain_mail"],
            loaded.RootElement.GetProperty("character").GetProperty("equipment").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void StartingEquipmentDoesNotChangeSeededCreationResults()
    {
        using TempModules scratch = new();
        string[] common = ["character", "new", "--module", Rules.ClassicPath, "--class", "fighter", "--race", "human", "--seed", "1", "--json"];
        (int withoutCode, string withoutText) = Run(scratch.Root, common);
        (int withCode, string withText) = Run(scratch.Root, [.. common, "--equipment", "long_sword,chain_mail"]);

        Assert.Equal(GoldboxCli.Ok, withoutCode);
        Assert.Equal(GoldboxCli.Ok, withCode);
        using JsonDocument without = JsonDocument.Parse(withoutText);
        using JsonDocument with = JsonDocument.Parse(withText);
        JsonElement withoutCharacter = without.RootElement.GetProperty("character");
        JsonElement withCharacter = with.RootElement.GetProperty("character");
        Assert.Equal(withoutCharacter.GetProperty("attributes").GetRawText(), withCharacter.GetProperty("attributes").GetRawText());
        Assert.Equal(withoutCharacter.GetProperty("tracks").GetRawText(), withCharacter.GetProperty("tracks").GetRawText());
        Assert.Equal(with.RootElement.GetProperty("rolls").GetRawText(), without.RootElement.GetProperty("rolls").GetRawText());
        Assert.Equal(["classic:long_sword", "classic:chain_mail"],
            withCharacter.GetProperty("equipment").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void NewRejectsUnknownOrRestrictedEquipmentWithoutWritingAFile()
    {
        using TempModules scratch = new();
        (int code, string output) = Run(scratch.Root,
            "character", "new", "--module", Fifth, "--class", "wizard", "--race", "human",
            "--feature", "sage,alert", "--equipment", "longsword,chain_mail", "--out", "wizard.json", "--json");

        Assert.Equal(GoldboxCli.Invalid, code);
        Assert.False(File.Exists(Path.Combine(scratch.Root, "wizard.json")));
        using JsonDocument restricted = JsonDocument.Parse(output);
        JsonElement[] restrictedProblems = restricted.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.Equal(2, restrictedProblems.Length);
        Assert.All(restrictedProblems, problem =>
        {
            Assert.Equal("character.equipment", problem.GetProperty("rule").GetString());
            Assert.Equal("$.equipment", problem.GetProperty("jsonPath").GetString());
            Assert.Contains("Wizard doesn't allow it", problem.GetProperty("message").GetString(), StringComparison.Ordinal);
        });

        (code, output) = Run(scratch.Root,
            "character", "new", "--module", Fifth, "--class", "fighter", "--race", "human",
            "--feature", "soldier,savage_attacker,defense", "--equipment", "missing_item", "--out", "missing.json", "--json");

        Assert.Equal(GoldboxCli.Invalid, code);
        Assert.False(File.Exists(Path.Combine(scratch.Root, "missing.json")));
        using JsonDocument missing = JsonDocument.Parse(output);
        JsonElement problem = Assert.Single(missing.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("character.reference", problem.GetProperty("rule").GetString());
        Assert.Contains("There is no item 'missing_item'", problem.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ARestrictedClassCanTakeAnAllowedGenericItemFromAnExtension()
    {
        using TempModules modules = new();
        modules.Module("gear", "extension", requires: TempModules.Require("fifth-srd", "*"));
        modules.Write("gear/toolkit.json", """
            { "type": "item", "id": "toolkit", "name": "Toolkit", "kind": "gear", "cost": 1, "currency": "fifth-srd:gold", "weight": 1 }
            """);
        using TempModules scratch = new();

        (int code, string output) = Run(scratch.Root,
            "character", "new", "--module", Fifth, "--modules", modules.Root, "--extension", "gear",
            "--class", "wizard", "--race", "human", "--feature", "sage,alert", "--equipment", "gear:toolkit",
            "--out", "wizard.json", "--json");

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument made = JsonDocument.Parse(output);
        Assert.Equal(["gear:toolkit"],
            made.RootElement.GetProperty("character").GetProperty("equipment").EnumerateArray().Select(item => item.GetString()));
        Assert.True(File.Exists(Path.Combine(scratch.Root, "wizard.json")), output);
    }

    private static (int Code, string Output) Run(string workingDirectory, params string[] args)
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(args, output, workingDirectory);
        return (code, output.ToString());
    }
}
