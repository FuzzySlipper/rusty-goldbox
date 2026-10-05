using System.Text.Json;

namespace RustyGoldbox.Tests;

public sealed class BlackappleCampaignTests
{
    private static string Campaign => BlackappleCheckout.Path("modules",
        "blackapple-brugh");

    private static string CampaignModules => BlackappleCheckout.Path("modules");

    private static string RepositoryModules => Path.Combine(Rules.RepositoryRoot, "modules");

    private static string FullSuccessLevelScript => BlackappleCheckout.Path("scripts",
        "routes",
        "full-success-level.script");

    private static string FullPartialScript => BlackappleCheckout.Path("scripts",
        "routes",
        "full-partial.script");

    private static string ReturnHubRegressionScript => BlackappleCheckout.Path("scripts",
        "routes",
        "return-hub-regression.script");

    [BlackappleFact]
    public void FullCampaignGoldenReturnsEveryChildLevelsThePartyAndPaysGoodallOnce()
    {
        using TempModules scratch = new();
        string party = CreateParty(scratch);
        string revisitScript = Path.Combine(scratch.Root, "revisit.script");
        File.WriteAllText(revisitScript, "status\n");

        string freshSave = Path.Combine(scratch.Root, "full-success-level.json");
        string revisitSave = Path.Combine(scratch.Root, "full-success-level.revisit.json");
        string transcript = CliTranscript.Run(
            scratch.Root,
            Play(
                party,
                FullSuccessLevelScript,
                freshSave,
                seed: "9431"),
            Load(
                freshSave,
                revisitScript,
                revisitSave));

        Assert.Equal(2, transcript.Split("[exit 0]", StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            transcript.Split("Goodall opens a quiet letter", StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            transcript.Split("one hundred gold pieces into each traveler", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Goodall opens a quiet letter", RevisitOutput(transcript), StringComparison.Ordinal);

        using JsonDocument save = ReadSave(freshSave);
        AssertFullReturn(save.RootElement);
        AssertGoodallReward(save.RootElement);

        using JsonDocument revisited = ReadSave(revisitSave);
        Assert.Equal(File.ReadAllText(freshSave), File.ReadAllText(revisitSave));
        AssertFullReturn(revisited.RootElement);
        AssertGoodallReward(revisited.RootElement);

        Golden.Verify("blackapple-full-campaign.txt", transcript.TrimEnd('\r', '\n') + Environment.NewLine);
    }

    [BlackappleFact]
    public void MidChapterWaitingMenuReloadMatchesTheUninterruptedSeededRoute()
    {
        using TempModules scratch = new();
        string party = CreateParty(scratch);
        (string prefix, string suffix) = SplitAtBallroomMenu(scratch);

        string prefixSave = Path.Combine(scratch.Root, "mid-prefix.json");
        string resumedSave = Path.Combine(scratch.Root, "mid-resumed.json");
        string uninterruptedSave = Path.Combine(scratch.Root, "mid-uninterrupted.json");

        JsonElement prefixResult = PlayJson(
            scratch,
            Play(
                party,
                prefix,
                prefixSave,
                seed: "9431"));
        using (JsonDocument prefixSaveDocument = ReadSave(prefixSave))
        {
            JsonElement pending = prefixSaveDocument.RootElement.GetProperty("pending_menu");
            Assert.Equal(JsonValueKind.String, pending.ValueKind);
            Assert.Equal("blackapple-brugh:evt_l2_c12_menu", pending.GetString());
        }

        JsonElement resumedResult = PlayJson(
            scratch,
            Load(
                prefixSave,
                suffix,
                resumedSave,
                failOnRefusal: true));
        JsonElement uninterruptedResult = PlayJson(
            scratch,
            Play(
                party,
                FullSuccessLevelScript,
                uninterruptedSave,
                seed: "9431"));

        Assert.True(prefixResult.GetProperty("ok").GetBoolean());
        Assert.True(resumedResult.GetProperty("ok").GetBoolean());
        Assert.True(uninterruptedResult.GetProperty("ok").GetBoolean());
        Assert.Equal(
            File.ReadAllText(uninterruptedSave),
            File.ReadAllText(resumedSave));
        Assert.True(ReadSave(uninterruptedSave).RootElement.GetProperty("ended").GetBoolean());
        Assert.True(ReadSave(resumedSave).RootElement.GetProperty("ended").GetBoolean());
    }

    [BlackappleFact]
    public void CoatroomViewsRemainSelectableAtItsWaitingMenuAndAfterReloadAndRevisit()
    {
        using TempModules scratch = new();
        string party = CreateParty(scratch);
        string[] route = File.ReadAllLines(FullSuccessLevelScript);
        int stop = Array.FindIndex(route, line => line.StartsWith("# Prepared-entry checkpoint", StringComparison.Ordinal));
        Assert.True(stop > 0);

        string entryScript = Path.Combine(scratch.Root, "coatroom-entry.script");
        string viewScript = Path.Combine(scratch.Root, "coatroom-view.script");
        string revisitScript = Path.Combine(scratch.Root, "coatroom-revisit.script");
        // The checkpoint comment precedes the midnight wait, mirror entry,
        // and ticket choice. Stop after entry, while C1's menu is waiting.
        File.WriteAllLines(entryScript, route[..(stop + 3)]);
        File.WriteAllText(viewScript, "view 1\nview 4\n");
        File.WriteAllText(revisitScript, "choose 2\nforward\nchoose 3\naround\nforward\nview 1\nview 4\n");

        string entrySave = Path.Combine(scratch.Root, "coatroom-entry.json");
        string viewedSave = Path.Combine(scratch.Root, "coatroom-viewed.json");
        string revisitSave = Path.Combine(scratch.Root, "coatroom-revisited.json");
        PlayJson(scratch, Play(party, entryScript, entrySave, seed: "9431"));
        JsonElement selected = PlayJson(scratch, Load(entrySave, viewScript, viewedSave, failOnRefusal: true));
        JsonElement revisited = PlayJson(scratch, Load(viewedSave, revisitScript, revisitSave, failOnRefusal: true));

        using JsonDocument entered = ReadSave(entrySave);
        using JsonDocument viewed = ReadSave(viewedSave);
        using JsonDocument returned = ReadSave(revisitSave);
        foreach (JsonElement state in new[] { entered.RootElement, viewed.RootElement, returned.RootElement })
        {
            Assert.Equal("blackapple-brugh:evt_l1_c1_menu", state.GetProperty("pending_menu").GetString());
            Assert.Equal("blackapple-brugh:brugh_l1", state.GetProperty("area").GetString());
            Assert.Equal(0, state.GetProperty("x").GetInt32());
            Assert.Equal(0, state.GetProperty("y").GetInt32());
            Assert.Equal(entered.RootElement.GetProperty("party").GetRawText(), state.GetProperty("party").GetRawText());
        }

        Assert.Equal(entered.RootElement.GetProperty("variables").GetRawText(), viewed.RootElement.GetProperty("variables").GetRawText());
        Assert.Equal("blackapple-brugh:evt_l1_c1_attendant", viewed.RootElement.GetProperty("view_event").GetString());
        Assert.Equal("blackapple-brugh:evt_l1_c1_revisit", returned.RootElement.GetProperty("view_event").GetString());
        string fourthMode = entered.RootElement.GetProperty("party")[3].GetProperty("perception").GetProperty("mode").GetString()!;
        Assert.Equal($"blackapple-art:brugh_coatroom_{fourthMode}", viewed.RootElement.GetProperty("picture").GetString());
        Assert.Equal(viewed.RootElement.GetProperty("picture").GetString(), returned.RootElement.GetProperty("picture").GetString());
        foreach (JsonElement result in new[] { selected, revisited })
        {
            JsonElement[] facts = result.GetProperty("transcript").EnumerateArray()
                .SelectMany(step => step.GetProperty("facts").EnumerateArray()).ToArray();
            Assert.Equal(2, facts.Count(fact => fact.GetProperty("kind").GetString() == "view"));
            Assert.DoesNotContain(facts, fact => fact.GetProperty("kind").GetString() == "perception");
        }
    }

    [BlackappleFact]
    public void ReturnHubVisitNewAmeliaHandoffAndResumeKeepOneTimeProgressStable()
    {
        using TempModules scratch = new();
        string party = CreateParty(scratch);
        Dictionary<string, string> route = WriteReturnHubRouteParts(scratch);

        string firstSave = Path.Combine(scratch.Root, "return-hub-first.json");
        string accountSave = Path.Combine(scratch.Root, "return-hub-account.json");
        string villageSave = Path.Combine(scratch.Root, "return-hub-village.json");
        string resumedSave = Path.Combine(scratch.Root, "return-hub-resumed.json");
        string preparedSave = Path.Combine(scratch.Root, "return-hub-prepared.json");
        string c1Save = Path.Combine(scratch.Root, "return-hub-c1.json");
        string c2Save = Path.Combine(scratch.Root, "return-hub-c2.json");
        string c12Save = Path.Combine(scratch.Root, "return-hub-c12.json");
        string ameliaSave = Path.Combine(scratch.Root, "return-hub-amelia.json");
        string handoffSave = Path.Combine(scratch.Root, "return-hub-handoff.json");
        string repeatSave = Path.Combine(scratch.Root, "return-hub-repeat.json");

        PlayJson(
            scratch,
            Play(
                party,
                FullPartialScript,
                firstSave,
                seed: "9419",
                failOnRefusal: false));
        using JsonDocument first = ReadSave(firstSave);
        AssertPending(first.RootElement, "blackapple-brugh:evt_fin_court_offer");
        Assert.Equal("captive", CampaignVariable(first.RootElement, "child_amelia_goodall").GetString());
        Assert.False(CampaignVariable(first.RootElement, "goodall_reward_given").GetBoolean());

        PlayJson(scratch, Load(firstSave, route["account-decline"], accountSave, failOnRefusal: true));
        using JsonDocument account = ReadSave(accountSave);
        AssertPending(account.RootElement, "blackapple-brugh:evt_fin_account_choice");
        string accountParty = PartyProgress(account.RootElement);

        JsonElement villageResult = PlayJson(
            scratch,
            Load(accountSave, route["village-visit"], villageSave, failOnRefusal: true));
        using JsonDocument village = ReadSave(villageSave);
        AssertPending(village.RootElement, "blackapple-brugh:evt_vil_arrival_objective");
        Assert.Contains("Resume the incomplete return account", TranscriptText(villageResult), StringComparison.Ordinal);

        JsonElement resumedResult = PlayJson(
            scratch,
            Load(villageSave, route["village-resume"], resumedSave, failOnRefusal: true));
        using JsonDocument resumed = ReadSave(resumedSave);
        AssertPending(resumed.RootElement, "blackapple-brugh:evt_fin_account_choice");
        Assert.Equal(accountParty, PartyProgress(resumed.RootElement));
        Assert.False(CampaignVariable(resumed.RootElement, "goodall_reward_given").GetBoolean());
        Assert.Contains("Resume the incomplete return account", TranscriptText(resumedResult), StringComparison.Ordinal);

        PlayJson(scratch, Load(resumedSave, route["prepare-reentry"], preparedSave, failOnRefusal: true));
        using (JsonDocument prepared = ReadSave(preparedSave))
        {
            AssertPending(prepared.RootElement, "blackapple-brugh:evt_entry_choose");
        }

        PlayJson(scratch, Load(preparedSave, route["enter-c1"], c1Save, failOnRefusal: true));
        PlayJson(scratch, Load(c1Save, route["c1-to-c2"], c2Save, failOnRefusal: true));
        PlayJson(scratch, Load(c2Save, route["c2-to-c12"], c12Save, failOnRefusal: true));
        PlayJson(scratch, Load(c12Save, route["rescue-amelia-c27"], ameliaSave, failOnRefusal: true));
        using JsonDocument amelia = ReadSave(ameliaSave);
        AssertPending(amelia.RootElement, "blackapple-brugh:evt_fin_child_amelia_goodall_freed");
        Assert.Equal("freed", CampaignVariable(amelia.RootElement, "child_amelia_goodall").GetString());
        Assert.Equal(accountParty, PartyProgress(amelia.RootElement));
        Assert.Equal(new[] { 900, 900, 900, 900 }, Experience(amelia.RootElement));

        JsonElement handoffResult = PlayJson(
            scratch,
            Load(ameliaSave, route["amelia-handoff"], handoffSave, failOnRefusal: true));
        using JsonDocument handoff = ReadSave(handoffSave);
        AssertPending(handoff.RootElement, "blackapple-brugh:evt_fin_account_choice");
        Assert.Equal("returned", CampaignVariable(handoff.RootElement, "child_amelia_goodall").GetString());
        Assert.True(CampaignVariable(handoff.RootElement, "goodall_reward_given").GetBoolean());
        Assert.Equal(new[] { 900, 900, 900, 900 }, Experience(handoff.RootElement));
        Assert.Equal(
            new[] { 416m, 410m, 410m, 410m },
            Gold(handoff.RootElement));
        Assert.Equal(1, TranscriptText(handoffResult).Split("Goodall opens a quiet letter", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, TranscriptText(handoffResult).Split("one hundred gold pieces into each traveler", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, SpellCount(handoff.RootElement, "fifth-srd:guiding_bolt"));

        JsonElement repeatedResult = PlayJson(
            scratch,
            Load(handoffSave, route["village-resume-repeat"], repeatSave, failOnRefusal: true));
        using JsonDocument repeated = ReadSave(repeatSave);
        AssertPending(repeated.RootElement, "blackapple-brugh:evt_fin_account_choice");
        Assert.Equal("returned", CampaignVariable(repeated.RootElement, "child_amelia_goodall").GetString());
        Assert.True(CampaignVariable(repeated.RootElement, "goodall_reward_given").GetBoolean());
        Assert.Equal(PartyProgress(handoff.RootElement), PartyProgress(repeated.RootElement));
        Assert.Equal(new[] { 900, 900, 900, 900 }, Experience(repeated.RootElement));
        Assert.Equal(new[] { 416m, 410m, 410m, 410m }, Gold(repeated.RootElement));
        Assert.Equal(0, TranscriptText(repeatedResult).Split("Goodall opens a quiet letter", StringSplitOptions.None).Length - 1);
        Assert.Equal(0, TranscriptText(repeatedResult).Split("one hundred gold pieces into each traveler", StringSplitOptions.None).Length - 1);
        Assert.Contains("Resume the incomplete return account", TranscriptText(repeatedResult), StringComparison.Ordinal);
    }

    private static string CreateParty(TempModules scratch)
    {
        // These four in-process CLI calls mirror scripts/create-party.sh exactly;
        // using GoldboxCli directly keeps the golden test independent of a shell.
        string ruleset = Path.Combine(RepositoryModules, "fifth-srd");
        CreateCharacter(
            scratch,
            ruleset,
            "fighter",
            "Mara Venn",
            "str,con,dex,wis,int,cha",
            "soldier,savage_attacker,defense",
            "longsword,chain_mail,shield",
            seed: "9301",
            output: "mara.json");
        CreateCharacter(
            scratch,
            ruleset,
            "rogue",
            "Orin Reed",
            "dex,con,int,cha,wis,str",
            "criminal,alert",
            "rapier,leather",
            seed: "9302",
            output: "orin.json");
        CreateCharacter(
            scratch,
            ruleset,
            "cleric",
            "Sela Ash",
            "wis,con,str,dex,cha,int",
            "acolyte,tough",
            "mace,scale_mail,shield",
            spells: "cure_wounds,healing_word",
            seed: "9303",
            output: "sela.json");
        CreateCharacter(
            scratch,
            ruleset,
            "wizard",
            "Tamsin Vale",
            "int,con,dex,wis,cha,str",
            "sage,alert",
            "quarterstaff,dagger",
            spells: "magic_missile,fire_bolt",
            seed: "9304",
            output: "tamsin.json");

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
        string? spells = null,
        string? seed = null,
        string? output = null)
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

        command.AddRange(["--seed", seed!, "--out", output!, "--json"]);
        (int code, string text) = CampaignTests.Run(scratch, [.. command]);
        Assert.Equal(0, code);
        Assert.Contains("saved_to", text, StringComparison.Ordinal);
    }

    private static string[] Play(
        string party,
        string script,
        string save,
        string seed,
        bool failOnRefusal = true)
    {
        List<string> command =
        [
            "play",
            "--campaign",
            Campaign,
            "--modules",
            RepositoryModules,
            "--modules",
            CampaignModules,
            "--party",
            party,
            "--seed",
            seed,
            "--script",
            script,
            "--save",
            save,
        ];
        if (failOnRefusal)
        {
            command.Add("--fail-on-refusal");
        }

        return [.. command];
    }

    private static string[] Load(
        string save,
        string script,
        string output,
        bool failOnRefusal = false)
    {
        List<string> command =
        [
            "play",
            "--campaign",
            Campaign,
            "--modules",
            RepositoryModules,
            "--modules",
            CampaignModules,
            "--load",
            save,
            "--script",
            script,
            "--save",
            output,
        ];
        if (failOnRefusal)
        {
            command.Add("--fail-on-refusal");
        }

        return [.. command];
    }

    private static (string Prefix, string Suffix) SplitAtBallroomMenu(TempModules scratch)
    {
        string[] lines = File.ReadAllLines(FullSuccessLevelScript);
        int split = Array.FindIndex(
            lines,
            line => line.StartsWith("# The named rescue award is 600 XP", StringComparison.Ordinal));
        Assert.True(split > 0, "The level route must retain its mid-chapter split marker.");

        string prefix = Path.Combine(scratch.Root, "mid-prefix.script");
        string suffix = Path.Combine(scratch.Root, "mid-suffix.script");
        File.WriteAllLines(prefix, lines[..split]);
        File.WriteAllLines(suffix, lines[split..]);
        return (prefix, suffix);
    }

    private static Dictionary<string, string> WriteReturnHubRouteParts(TempModules scratch)
    {
        string[] lines = File.ReadAllLines(ReturnHubRegressionScript);
        string[] names =
        [
            "account-decline",
            "village-visit",
            "village-resume",
            "prepare-reentry",
            "enter-c1",
            "c1-to-c2",
            "c2-to-c12",
            "rescue-amelia-c27",
            "amelia-handoff",
            "village-resume-repeat",
        ];
        Dictionary<string, string> scripts = new(StringComparer.Ordinal);
        for (int index = 0; index < names.Length; index++)
        {
            string marker = $"## {names[index]}";
            int start = Array.FindIndex(lines, line => line == marker);
            Assert.True(start >= 0, $"Missing regression route marker {marker}.");
            int end = Array.FindIndex(lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
            if (end < 0)
            {
                end = lines.Length;
            }

            string path = Path.Combine(scratch.Root, $"return-hub-{names[index]}.script");
            File.WriteAllLines(path, lines[(start + 1)..end]);
            scripts[names[index]] = path;
        }

        return scripts;
    }

    private static JsonElement PlayJson(TempModules scratch, string[] command)
    {
        string[] jsonCommand = [command[0], "--json", .. command.Skip(1)];
        (int code, string output) = CampaignTests.Run(scratch, jsonCommand);
        Assert.True(code == 0, output);
        return JsonDocument.Parse(output).RootElement.Clone();
    }

    private static JsonDocument ReadSave(string path)
    {
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static void AssertPending(JsonElement save, string expected)
    {
        Assert.False(save.GetProperty("ended").GetBoolean());
        Assert.Equal(expected, save.GetProperty("pending_menu").GetString());
    }

    private static JsonElement CampaignVariable(JsonElement save, string name)
    {
        return save.GetProperty("variables").GetProperty("campaign").GetProperty(name);
    }

    private static string PartyProgress(JsonElement save)
    {
        string[] fields =
        [
            "experience",
            "levels",
            "attributes",
            "tracks",
            "balances",
            "equipment",
            "conditions",
            "spells",
        ];
        return string.Join(
            "\n",
            save.GetProperty("party").EnumerateArray().Select(member => string.Join(
                "|",
                fields.Select(field => member.TryGetProperty(field, out JsonElement value)
                    ? value.GetRawText()
                    : "null"))));
    }

    private static int[] Experience(JsonElement save)
    {
        return save.GetProperty("party").EnumerateArray()
            .Select(member => member.GetProperty("experience").GetInt32())
            .ToArray();
    }

    private static decimal[] Gold(JsonElement save)
    {
        return save.GetProperty("party").EnumerateArray()
            .Select(member => member.GetProperty("balances").GetProperty("gold").GetDecimal())
            .ToArray();
    }

    private static int SpellCount(JsonElement save, string spell)
    {
        int count = 0;
        foreach (JsonElement member in save.GetProperty("party").EnumerateArray())
        {
            if (!member.TryGetProperty("spells", out JsonElement spells) || spells.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            count += spells.EnumerateArray().Count(value => value.GetString() == spell);
        }

        return count;
    }

    private static string TranscriptText(JsonElement result)
    {
        return string.Join(
            "\n",
            result.GetProperty("transcript").EnumerateArray()
                .SelectMany(step => step.GetProperty("facts").EnumerateArray())
                .Select(fact => fact.GetProperty("text").GetString() ?? string.Empty));
    }

    private static void AssertFullReturn(JsonElement save)
    {
        Assert.True(save.GetProperty("ended").GetBoolean());
        Assert.Equal(JsonValueKind.Null, save.GetProperty("pending_menu").ValueKind);
        Assert.Equal("blackapple-brugh:environs", save.GetProperty("area").GetString());
        Assert.Equal(4, save.GetProperty("x").GetInt32());
        Assert.Equal(1, save.GetProperty("y").GetInt32());
        Assert.Equal("east", save.GetProperty("facing").GetString());

        JsonElement campaign = save.GetProperty("variables").GetProperty("campaign");
        foreach (string child in new[]
        {
            "child_amelia_goodall",
            "child_arthur_figwort",
            "child_bernard_goodall",
            "child_giles_weadley",
            "child_philip_anvil",
            "child_stevie_leeford",
            "child_ursula_cooke",
        })
        {
            Assert.Equal("returned", campaign.GetProperty(child).GetString());
        }

        foreach (JsonElement member in save.GetProperty("party").EnumerateArray())
        {
            Assert.Equal(900, member.GetProperty("experience").GetInt32());
            Assert.Equal(3, member.GetProperty("levels").GetArrayLength());
        }
    }

    private static void AssertGoodallReward(JsonElement save)
    {
        JsonElement campaign = save.GetProperty("variables").GetProperty("campaign");
        Assert.True(campaign.GetProperty("goodall_reward_given").GetBoolean());

        JsonElement[] party = save.GetProperty("party").EnumerateArray().ToArray();
        Assert.Equal(4, party.Length);
        Assert.Equal(320m, party[0].GetProperty("balances").GetProperty("gold").GetDecimal());
        Assert.Equal(317m, party[1].GetProperty("balances").GetProperty("gold").GetDecimal());
        Assert.Equal(317m, party[2].GetProperty("balances").GetProperty("gold").GetDecimal());
        Assert.Equal(317m, party[3].GetProperty("balances").GetProperty("gold").GetDecimal());
        Assert.Equal(
            1,
            party[2].GetProperty("spells").EnumerateArray().Count(spell => spell.GetString() == "fifth-srd:guiding_bolt"));
        Assert.Equal(
            1,
            party[3].GetProperty("spells").EnumerateArray().Count(spell => spell.GetString() == "fifth-srd:scorching_ray"));
    }

    private static string RevisitOutput(string transcript)
    {
        int marker = transcript.LastIndexOf("$ goldbox play", StringComparison.Ordinal);
        return marker >= 0 ? transcript[marker..] : transcript;
    }
}
