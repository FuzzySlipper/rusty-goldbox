using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustyGoldbox.Tests;

public sealed class BlackappleWormBehaviorTests
{
    private static string CampaignModules => BlackappleCheckout.Path("modules");

    private static string EncounterModule => Path.Combine(
        CampaignModules,
        "blackapple-fae");

    private static string RepositoryModules => Path.Combine(Rules.RepositoryRoot, "modules");

    [BlackappleFact]
    public void WormMovesThenLashesWithFourMemberPartyWhenCastersAreDepleted()
    {
        using TempModules scratch = new();
        string party = CreateParty(scratch);
        DepleteFirstLevelSlots(Path.Combine(scratch.Root, "sela.json"));
        DepleteFirstLevelSlots(Path.Combine(scratch.Root, "tamsin.json"));

        // Keep the old authored profile in a temporary module so this assertion
        // records the original separated-deployment failure without changing
        // the first-party source under test.
        string beforeModule = Path.Combine(scratch.Root, "blackapple-fae-before");
        CopyDirectory(EncounterModule, beforeModule);
        string beforeBehavior = Path.Combine(beforeModule, "behaviors", "tentacle_worm_tactics.json");
        JsonObject beforeDefinition = JsonNode.Parse(File.ReadAllText(beforeBehavior))!.AsObject();
        beforeDefinition["rules"]![0]!["steps"]![0]!.AsObject().Remove("destination");
        File.WriteAllText(beforeBehavior, beforeDefinition.ToJsonString());

        JsonElement before = RunCombat(scratch, beforeModule, party);
        JsonElement[] beforeWormTrace = WormTraces(before);
        JsonElement beforeOpening = beforeWormTrace[0];
        Assert.Equal("end-turn", beforeOpening.GetProperty("fallback").GetString());
        Assert.Contains("No authored step is currently legal", beforeOpening.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            before.GetProperty("facts").EnumerateArray(),
            fact => fact.GetProperty("kind").GetString() == "move"
                && fact.GetProperty("text").GetString()!.StartsWith("Tentacle worm moves", StringComparison.Ordinal));

        JsonElement after = RunCombat(scratch, EncounterModule, party);
        Assert.Equal("Party", after.GetProperty("winner").GetString());
        Assert.InRange(after.GetProperty("rounds").GetInt32(), 1, 20);

        string[] members = after.GetProperty("combatants").EnumerateArray()
            .Select(combatant => combatant.GetProperty("name").GetString()!)
            .ToArray();
        Assert.Contains("Mara Venn", members);
        Assert.Contains("Orin Reed", members);
        Assert.Contains("Sela Ash", members);
        Assert.Contains("Tamsin Vale", members);

        Assert.Contains(
            after.GetProperty("facts").EnumerateArray(),
            fact => fact.GetProperty("kind").GetString() == "move"
                && fact.GetProperty("text").GetString()!.StartsWith("Tentacle worm moves", StringComparison.Ordinal));
        Assert.Contains(
            after.GetProperty("facts").EnumerateArray(),
            fact => fact.GetProperty("kind").GetString() == "action"
                && fact.GetProperty("text").GetString()!.StartsWith("Tentacle worm uses Six tentacles", StringComparison.Ordinal));

        JsonElement[] afterWormTrace = WormTraces(after);
        JsonElement[] afterSelected = afterWormTrace
            .Where(trace => trace.GetProperty("selected").ValueKind == JsonValueKind.Object)
            .ToArray();
        JsonElement movement = Assert.Single(afterSelected, trace =>
            trace.GetProperty("selected").GetProperty("movementOnly").GetBoolean());
        JsonElement selectedMovement = movement.GetProperty("selected");
        Assert.EndsWith("fifth-srd:move", selectedMovement.GetProperty("actionId").GetString(), StringComparison.Ordinal);
        Assert.Equal("toward", selectedMovement.GetProperty("destinationKind").GetString());
        Assert.Equal(1, selectedMovement.GetProperty("destinationDistance").GetInt32());

        Assert.Contains(afterSelected, trace =>
            !trace.GetProperty("selected").GetProperty("movementOnly").GetBoolean()
            && trace.GetProperty("selected").GetProperty("actionId").GetString()!.EndsWith(
                "blackapple-fae:tentacle_lash",
                StringComparison.Ordinal));
    }

    private static JsonElement RunCombat(TempModules scratch, string module, string party)
    {
        (int code, string output) = CampaignTests.Run(
            scratch,
            "sim",
            "combat",
            "--module",
            module,
            "--modules",
            RepositoryModules,
            "--modules",
            CampaignModules,
            "--party",
            party,
            "--encounter",
            "blackapple-fae:tentacle_worm_pit",
            "--combat",
            "fifth-srd:standard",
            "--seed",
            "9406",
            "--max-rounds",
            "20",
            "--trace",
            "--json");
        Assert.True(code == 0, output);
        return JsonDocument.Parse(output).RootElement.Clone();
    }

    private static JsonElement[] WormTraces(JsonElement simulation)
    {
        return simulation.GetProperty("trace").EnumerateArray()
            .Where(trace => trace.GetProperty("behaviorId").GetString() == "blackapple-fae:tentacle_worm_tactics")
            .ToArray();
    }

    private static string CreateParty(TempModules scratch)
    {
        string ruleset = Path.Combine(RepositoryModules, "fifth-srd");
        CreateCharacter(
            scratch,
            ruleset,
            "fighter",
            "Mara Venn",
            "str,con,dex,wis,int,cha",
            "soldier,savage_attacker,defense",
            "longsword,chain_mail,shield",
            "9301",
            "mara.json");
        CreateCharacter(
            scratch,
            ruleset,
            "rogue",
            "Orin Reed",
            "dex,con,int,cha,wis,str",
            "criminal,alert",
            "rapier,leather",
            "9302",
            "orin.json");
        CreateCharacter(
            scratch,
            ruleset,
            "cleric",
            "Sela Ash",
            "wis,con,str,dex,cha,int",
            "acolyte,tough",
            "mace,scale_mail,shield",
            "9303",
            "sela.json",
            "cure_wounds,healing_word");
        CreateCharacter(
            scratch,
            ruleset,
            "wizard",
            "Tamsin Vale",
            "int,con,dex,wis,cha,str",
            "sage,alert",
            "quarterstaff,dagger",
            "9304",
            "tamsin.json",
            "magic_missile,fire_bolt");
        return string.Join(",", [
            Path.Combine(scratch.Root, "mara.json"),
            Path.Combine(scratch.Root, "orin.json"),
            Path.Combine(scratch.Root, "sela.json"),
            Path.Combine(scratch.Root, "tamsin.json"),
        ]);
    }

    private static void CreateCharacter(
        TempModules scratch,
        string ruleset,
        string classId,
        string name,
        string priority,
        string features,
        string equipment,
        string seed,
        string output,
        string? spells = null)
    {
        List<string> command = [
            "character",
            "new",
            "--module",
            ruleset,
            "--class",
            classId,
            "--race",
            "human",
            "--name",
            name,
            "--priority",
            priority,
            "--feature",
            features,
            "--equipment",
            equipment,
        ];
        if (spells is not null)
        {
            command.AddRange(["--spells", spells]);
        }

        command.AddRange(["--seed", seed, "--out", output, "--json"]);
        (int code, string text) = CampaignTests.Run(scratch, [.. command]);
        Assert.True(code == 0, text);
    }

    private static void DepleteFirstLevelSlots(string path)
    {
        JsonObject character = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        character["tracks"]!["spells_1"]!["current"] = 0;
        File.WriteAllText(path, character.ToJsonString());
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
