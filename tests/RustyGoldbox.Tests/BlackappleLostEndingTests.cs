using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustyGoldbox.Tests;

public sealed class BlackappleLostEndingTests
{
    private static readonly (string Id, string Value)[] Account =
    [
        ("child_arthur_figwort", "freed"),
        ("child_amelia_goodall", "captive"),
        ("child_bernard_goodall", "unknown"),
        ("child_giles_weadley", "returned"),
        ("child_philip_anvil", "freed"),
        ("child_ursula_cooke", "captive"),
        ("child_stevie_leeford", "unknown"),
        ("double_arthur_figwort", "contained"),
        ("double_amelia_goodall", "escaped"),
        ("double_bernard_goodall", "masked"),
        ("double_giles_weadley", "removed"),
        ("double_philip_anvil", "exposed"),
        ("double_ursula_cooke", "unknown"),
        ("double_stevie_leeford", "contained"),
    ];

    private static string CampaignSource => Path.Combine(
        Rules.RepositoryRoot,
        "campaigns",
        "blackapple-brugh",
        "modules",
        "blackapple-brugh");

    private static string CampaignModules => Path.Combine(
        Rules.RepositoryRoot,
        "campaigns",
        "blackapple-brugh",
        "modules");

    private static string RepositoryModules => Path.Combine(Rules.RepositoryRoot, "modules");

    private static string FinaleRoot => Path.Combine(
        Rules.RepositoryRoot,
        "campaigns",
        "blackapple-brugh",
        "scripts",
        "finale");

    [Fact]
    public void AuthoredLostScenarioSetsEndingAndPreservesAccountAcrossRevisit()
    {
        using TempModules scratch = new();
        string campaign = BuildCampaign(scratch);
        string party = CreateParty(scratch);
        string script = Path.Combine(FinaleRoot, "lost-defeat.script");
        string firstSave = Path.Combine(scratch.Root, "lost-defeat.save.json");
        string revisitSave = Path.Combine(scratch.Root, "lost-defeat.revisit.save.json");

        (int firstCode, string firstOutput) = CampaignTests.Run(
            scratch,
            "play",
            "--campaign",
            campaign,
            "--modules",
            RepositoryModules,
            "--modules",
            CampaignModules,
            "--party",
            party,
            "--seed",
            "9304",
            "--script",
            script,
            "--save",
            firstSave,
            "--fail-on-refusal",
            "--json");
        Assert.True(firstCode == 0, firstOutput);
        using JsonDocument firstTranscript = JsonDocument.Parse(firstOutput);
        Assert.True(firstTranscript.RootElement.GetProperty("ok").GetBoolean(), firstOutput);
        Assert.Contains("ending_id is now ending.lost_in_brugh.", firstOutput, StringComparison.Ordinal);
        Assert.Contains("Your expedition is lost.", firstOutput, StringComparison.Ordinal);

        (int revisitCode, string revisitOutput) = CampaignTests.Run(
            scratch,
            "play",
            "--campaign",
            campaign,
            "--modules",
            RepositoryModules,
            "--modules",
            CampaignModules,
            "--load",
            firstSave,
            "--script",
            Path.Combine(FinaleRoot, "lost-revisit.script"),
            "--save",
            revisitSave,
            "--fail-on-refusal",
            "--json");
        Assert.True(revisitCode == 0, revisitOutput);
        using JsonDocument revisitTranscript = JsonDocument.Parse(revisitOutput);
        Assert.True(revisitTranscript.RootElement.GetProperty("ok").GetBoolean(), revisitOutput);

        using JsonDocument first = JsonDocument.Parse(File.ReadAllText(firstSave));
        using JsonDocument revisit = JsonDocument.Parse(File.ReadAllText(revisitSave));
        JsonElement firstState = first.RootElement;
        JsonElement revisitState = revisit.RootElement;
        Assert.True(firstState.GetProperty("ended").GetBoolean());
        Assert.True(revisitState.GetProperty("ended").GetBoolean());
        Assert.Equal(
            "ending.lost_in_brugh",
            firstState.GetProperty("variables").GetProperty("campaign").GetProperty("ending_id").GetString());
        Assert.Equal(
            "ending.lost_in_brugh",
            revisitState.GetProperty("variables").GetProperty("campaign").GetProperty("ending_id").GetString());

        JsonElement firstCampaign = firstState.GetProperty("variables").GetProperty("campaign");
        JsonElement revisitCampaign = revisitState.GetProperty("variables").GetProperty("campaign");
        foreach ((string id, string value) in Account)
        {
            Assert.Equal(value, firstCampaign.GetProperty(id).GetString());
            Assert.Equal(value, revisitCampaign.GetProperty(id).GetString());
        }
    }

    private static string BuildCampaign(TempModules scratch)
    {
        string campaign = Path.Combine(scratch.Root, "campaign");
        CopyDirectory(CampaignSource, campaign);

        string campaignFile = Path.Combine(campaign, "campaign.json");
        JsonObject definition = JsonNode.Parse(File.ReadAllText(campaignFile))!.AsObject();
        definition["intro"] = "evt_fin_setup_lost_defeat";
        File.WriteAllText(campaignFile, definition.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        string fixtureDirectory = Path.Combine(FinaleRoot, "fixtures");
        foreach (string fixture in Directory.EnumerateFiles(fixtureDirectory, "evt_fin_setup_lost_defeat*.json"))
        {
            File.Copy(fixture, Path.Combine(campaign, "events", Path.GetFileName(fixture)));
        }

        return campaign;
    }

    private static string CreateParty(TempModules scratch)
    {
        string ruleset = Path.Combine(RepositoryModules, "fifth-srd");
        CreateCharacter(scratch, ruleset, "fighter", "Mara Venn", "str,con,dex,wis,int,cha", "soldier,savage_attacker,defense", "longsword,chain_mail,shield", "9301", "mara.json");
        CreateCharacter(scratch, ruleset, "rogue", "Orin Reed", "dex,con,int,cha,wis,str", "criminal,alert", "rapier,leather", "9302", "orin.json");
        CreateCharacter(scratch, ruleset, "cleric", "Sela Ash", "wis,con,str,dex,cha,int", "acolyte,tough", "mace,scale_mail,shield", "9303", "sela.json", "cure_wounds,healing_word");
        CreateCharacter(scratch, ruleset, "wizard", "Tamsin Vale", "int,con,dex,wis,cha,str", "sage,alert", "quarterstaff,dagger", "9304", "tamsin.json", "magic_missile,fire_bolt");
        return string.Join(",", new[]
        {
            Path.Combine(scratch.Root, "mara.json"),
            Path.Combine(scratch.Root, "orin.json"),
            Path.Combine(scratch.Root, "sela.json"),
            Path.Combine(scratch.Root, "tamsin.json"),
        });
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
        List<string> command =
        [
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

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
