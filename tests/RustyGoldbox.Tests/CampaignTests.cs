using System.Text.Json;
using System.Text.RegularExpressions;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class CampaignTests
{
    internal static string SampleCrypt => Path.Combine(Rules.RepositoryRoot, "modules", "sample-crypt");

    internal static string Script(string name) => Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "scripts", name);

    [Fact]
    public void SampleCryptPlaysFromStartToFinish()
    {
        using TempModules scratch = new();
        WriteParty(scratch, Rules.ClassicPath);

        Golden.Verify("sample-crypt-play.txt", CliTranscript.Run(scratch.Root,
            ["map", "render", "entrance", "--module", SampleCrypt],
            ["play", "--campaign", SampleCrypt, "--party", "ada.json,brom.json", "--seed", "1", "--script", Script("crypt.script")],
            ["play", "--campaign", SampleCrypt, "--party", "ada.json,brom.json", "--seed", "1", "--script", Script("crypt-early-stairs.script")]));
    }

    [Fact]
    public void ASavedGameResumesExactlyAsAnUnbrokenRun()
    {
        using TempModules scratch = new();
        WriteParty(scratch, Rules.ClassicPath);
        // Split just before the door menu's choice: the save holds a waiting menu, and the
        // guard fight and treasure roll after the resume.
        string[] lines = File.ReadAllLines(Script("crypt.script"));
        int split = Array.FindIndex(lines, line => line.StartsWith("choose 1", StringComparison.Ordinal));
        File.WriteAllLines(Path.Combine(scratch.Root, "first.script"), lines[..split]);
        File.WriteAllLines(Path.Combine(scratch.Root, "second.script"), lines[split..]);

        JsonElement whole = Play(scratch, "--party", "ada.json,brom.json", "--seed", "5", "--script", Script("crypt.script"), "--save", "whole.json");
        Play(scratch, "--party", "ada.json,brom.json", "--seed", "5", "--script", "first.script", "--save", "half.json");
        Assert.Contains("barred_door", File.ReadAllText(Path.Combine(scratch.Root, "half.json")), StringComparison.Ordinal);
        JsonElement resumed = Play(scratch, "--load", "half.json", "--script", "second.script", "--save", "resumed.json");

        List<string> wholeTail = Commands(whole).TakeLast(Commands(resumed).Count).ToList();
        Assert.Equal(wholeTail, Commands(resumed));
        Assert.Contains("Combat with", string.Join("\n", Commands(resumed)), StringComparison.Ordinal);
        Assert.Equal(File.ReadAllText(Path.Combine(scratch.Root, "whole.json")), File.ReadAllText(Path.Combine(scratch.Root, "resumed.json")));
        Assert.True(resumed.GetProperty("ended").GetBoolean());
    }

    [Fact]
    public void ASaveRecordsItsAddedExtensionsAndNeedsThemToLoad()
    {
        using TempModules modules = new();
        modules.Module("house", "extension", requires: Require("classic", "^0.1.0"));
        using TempModules scratch = new();
        WriteParty(scratch, Rules.ClassicPath);
        File.WriteAllText(Path.Combine(scratch.Root, "look.script"), "status\n");
        string library = Path.Combine(Rules.RepositoryRoot, "modules");

        (int played, _) = Run(scratch, "play", "--campaign", SampleCrypt, "--party", "ada.json,brom.json", "--script", "look.script", "--save", "s.json",
            "--modules", library, "--modules", modules.Root, "--extension", "house");
        using JsonDocument save = JsonDocument.Parse(File.ReadAllText(Path.Combine(scratch.Root, "s.json")));
        (int without, string refused) = Run(scratch, "play", "--campaign", SampleCrypt, "--load", "s.json", "--script", "look.script", "--modules", library);
        (int with, _) = Run(scratch, "play", "--campaign", SampleCrypt, "--load", "s.json", "--script", "look.script",
            "--modules", library, "--modules", modules.Root, "--extension", "house");

        Assert.Equal(0, played);
        Assert.Equal(["house"], save.RootElement.GetProperty("extensions").EnumerateArray().Select(entry => entry.GetString()));
        Assert.NotEqual(0, without);
        Assert.Contains("added to the saved set as an extension but isn't loaded (add it with --extension house)", refused, StringComparison.Ordinal);
        Assert.Equal(0, with);
    }

    [Fact]
    public void SaveSlotsInEnginePersistenceCarryGamesBothWays()
    {
        using TempModules scratch = new();
        WriteParty(scratch, Rules.ClassicPath);
        string store = Path.Combine(scratch.Root, "persistence");
        string[] lines = File.ReadAllLines(Script("crypt.script"));
        int split = Array.FindIndex(lines, line => line.StartsWith("choose 1", StringComparison.Ordinal));
        File.WriteAllLines(Path.Combine(scratch.Root, "first.script"), lines[..split]);
        File.WriteAllLines(Path.Combine(scratch.Root, "second.script"), lines[split..]);
        Play(scratch, "--party", "ada.json,brom.json", "--seed", "5", "--script", Script("crypt.script"), "--save", "whole.json");
        Play(scratch, "--party", "ada.json,brom.json", "--seed", "5", "--script", "first.script", "--save", "half.json");

        // The CLI writes a slot; the store holds the same save JSON a file does.
        Play(scratch, "--party", "ada.json,brom.json", "--seed", "5", "--script", "first.script", "--store", store, "--save", "from-cli");
        string half = File.ReadAllText(Path.Combine(scratch.Root, "half.json"));
        using (EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = store }))
        {
            host.Call(engine =>
            {
                using SaveSlots slots = new(engine);
                Assert.Equal(half, System.Text.Encoding.UTF8.GetString(slots.Read("from-cli")!));

                // The Game writes slots through the same SaveSlots.
                slots.Write("from-game", half);
            });
        }

        // The CLI resumes the slot the Game wrote, exactly as from the file.
        Play(scratch, "--load", "from-game", "--store", store, "--script", "second.script", "--save", "resumed");
        using (EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = store }))
        {
            string resumed = host.Call(engine =>
            {
                using SaveSlots slots = new(engine);
                return System.Text.Encoding.UTF8.GetString(slots.Read("resumed")!);
            });
            Assert.Equal(File.ReadAllText(Path.Combine(scratch.Root, "whole.json")), resumed);
        }

        (int code, string output) = Run(scratch, "play", "--campaign", SampleCrypt, "--store", store, "--load", "empty-slot");
        Assert.Equal(1, code);
        Assert.Contains("save.store", output, StringComparison.Ordinal);
        Assert.Contains("The slot is empty", output, StringComparison.Ordinal);
        Assert.Equal(2, Run(scratch, "play", "--campaign", SampleCrypt, "--store", store, "--load", "../escape").Code);
    }

    [Fact]
    public void SavesRefuseADifferentModuleSet()
    {
        using TempModules scratch = new();
        foreach (string module in new[] { "classic", "placeholder-art", "sample-crypt" })
        {
            Copy(Path.Combine(Rules.RepositoryRoot, "modules", module), Path.Combine(scratch.Root, "modules", module));
        }

        scratch.Write("goldbox.json", """{ "modules": ["modules"] }""");
        string campaign = Path.Combine(scratch.Root, "modules", "sample-crypt");
        WriteParty(scratch, Path.Combine(scratch.Root, "modules", "classic"));
        Run(scratch, "play", "--campaign", campaign, "--party", "ada.json,brom.json", "--script", Script("crypt-early-stairs.script"), "--save", "game.json");

        string goblinFile = Path.Combine(scratch.Root, "modules", "classic", "monsters", "giant_rat.json");
        File.WriteAllText(goblinFile, File.ReadAllText(goblinFile).Replace("\"xp\": 7", "\"xp\": 8", StringComparison.Ordinal));
        string manifest = Path.Combine(scratch.Root, "modules", "placeholder-art", "module.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"0.1.0\"", "\"0.1.1\"", StringComparison.Ordinal));

        (int code, string output) = Run(scratch, "play", "--campaign", campaign, "--load", "game.json", "--script", Script("crypt-early-stairs.script"));

        Assert.Equal(1, code);
        Assert.Contains("save.file", output, StringComparison.Ordinal);
        Assert.Contains("classic 0.1.0 has different content from when the save was made", output, StringComparison.Ordinal);
        Assert.Contains("placeholder-art is 0.1.1, but the save was made with 0.1.0", output, StringComparison.Ordinal);
    }

    [Fact]
    public void UndecidedFightsAndTreasureFollowTheirOwnRules()
    {
        using TempModules modules = new();
        Rules.WriteSmallRuleset(modules);
        modules.Module("art", "assets");
        modules.Write("rules/stalemate.json", """
            { "type": "combat", "id": "stalemate", "name": "Stalemate", "initiative": "1", "initiative_by": "side", "initiative_order": "highest-first",
              "initiative_each": "combat", "round_seconds": 6, "round_limit": 3, "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points",
              "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/wait.json", """{ "type": "action", "id": "wait", "name": "Wait", "cost": { "turn": 1 }, "target": "self", "always": [ { "op": "heal", "amount": "0" } ] }""");
        modules.Write("rules/statue.json", """{ "type": "monster", "id": "statue", "name": "Statue", "tracks": { "hit_points": "5" }, "actions": [ { "action": "wait" } ], "xp": 0 }""");
        modules.Write("rules/statues.json", """{ "type": "encounter", "id": "statues", "name": "Statues", "monsters": [ { "monster": "statue", "count": "1" } ] }""");
        modules.Write("rules/creation.json", """{ "type": "character-creation", "id": "c", "name": "C", "attributes": ["str"], "attribute_roll": "10", "assignment": "in-order", "starting_gold": { "warrior": "5" } }""");
        modules.Write("rules/folk.json", """{ "type": "race", "id": "folk", "name": "Folk", "classes": ["warrior"] }""");
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("rules", "*")}, {Require("art", "*")}");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "standoff" }""");
        modules.Write("tale/standoff.json", """{ "type": "event", "id": "standoff", "kind": "combat", "encounter": "rules:statues", "on_draw": "coins" }""");
        modules.Write("tale/coins.json", """{ "type": "event", "id": "coins", "kind": "treasure", "gold": "7" }""");
        using TempModules scratch = new();
        Run(scratch, "character", "new", "--module", Path.Combine(modules.Root, "rules"), "--class", "warrior", "--race", "folk", "--name", "A", "--out", "a.json");
        Run(scratch, "character", "new", "--module", Path.Combine(modules.Root, "rules"), "--class", "warrior", "--race", "folk", "--name", "B", "--out", "b.json");
        File.WriteAllText(Path.Combine(scratch.Root, "status.script"), "status\n");

        (int code, string output) = Run(scratch, "play", "--campaign", campaign, "--party", "a.json,b.json", "--script", "status.script");

        Assert.Equal(0, code);
        Assert.Contains("neither side wins before the round limit", output, StringComparison.Ordinal);
        Assert.Contains("No side won after 3 rounds.", output, StringComparison.Ordinal);
        Assert.Contains("gold 9 + 8", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public void ARestThatPreparesSpellsLetsACasterCastAgain(bool prepare, int flares)
    {
        string fixtures = Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures");
        string ascend = Path.Combine(fixtures, "ascend");
        using TempModules modules = new();
        string campaign = modules.Module("camp", "campaign", requires: $"{Require("ascend", "*")}, {Require("placeholder-art", "*")}");
        modules.Write("camp/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("camp/campaign.json", """{ "type": "campaign", "id": "camp", "name": "Camp", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "first" }""");
        modules.Write("camp/first.json", """{ "type": "event", "id": "first", "kind": "combat", "encounter": "ascend:imps", "on_win": "camp" }""");
        modules.Write("camp/camp.json", $$"""{ "type": "event", "id": "camp", "kind": "rest", "text": "You rest.", "tracks": ["ascend:arcana_1", "ascend:hit_points"], "prepare": {{(prepare ? "true" : "false")}}, "next": "second" }""");
        modules.Write("camp/second.json", """{ "type": "event", "id": "second", "kind": "combat", "encounter": "ascend:imps" }""");
        using TempModules scratch = new();
        Run(scratch, "character", "new", "--module", ascend, "--class", "warrior", "--race", "folk", "--name", "Kara", "--attributes", "might=16,grace=12,grit=14,wit=12", "--feature", "iron_will,improved_initiative", "--out", "kara.json");
        Run(scratch, "character", "new", "--module", ascend, "--class", "adept", "--race", "folk", "--name", "Ilse", "--attributes", "might=9,grace=12,grit=16,wit=16", "--feature", "lightning_reflexes", "--spells", "flare", "--out", "ilse.json");
        Equip(scratch, "kara.json", "ascend:longsword");
        File.WriteAllText(Path.Combine(scratch.Root, "status.script"), "status\n");

        (int code, string output) = Run(scratch, "play", "--campaign", campaign, "--party", "kara.json,ilse.json", "--script", "status.script", "--seed", "5",
            "--modules", fixtures, "--modules", Path.Combine(Rules.RepositoryRoot, "modules"));

        Assert.Equal(0, code);
        // Ilse prepares one flare a day: a rest restores the slot either way, but only one that prepares spells gives her the copy back.
        Assert.Equal(2, Regex.Matches(output, "Combat with Cinder imps: the party wins").Count);
        Assert.Equal(flares, Regex.Matches(output, "Ilse uses Flare").Count);
    }

    [Fact]
    public void ExperienceEarnedInPlayGainsLevelsOrWaitsForChoices()
    {
        string fixtures = Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures");
        string ascend = Path.Combine(fixtures, "ascend");
        using TempModules modules = new();
        string campaign = modules.Module("deeds", "campaign", requires: $"{Require("ascend", "*")}, {Require("placeholder-art", "*")}");
        modules.Write("deeds/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|     |", "+--+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } }, "cells": [ { "at": [1, 0], "event": "praise" } ] }""");
        modules.Write("deeds/campaign.json", """{ "type": "campaign", "id": "deeds", "name": "Deeds", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "thanks" }""");
        modules.Write("deeds/thanks.json", """{ "type": "event", "id": "thanks", "kind": "experience", "amount": "2000", "text": "The town thanks you." }""");
        modules.Write("deeds/praise.json", """{ "type": "event", "id": "praise", "kind": "experience", "amount": "2000", "each": true }""");
        using TempModules scratch = new();
        Run(scratch, "character", "new", "--module", ascend, "--class", "warrior", "--race", "folk", "--name", "Kara", "--attributes", "might=16,grace=12,grit=14,wit=12", "--feature", "iron_will,improved_initiative", "--out", "kara.json");
        Run(scratch, "character", "new", "--module", ascend, "--class", "adept", "--race", "folk", "--name", "Ilse", "--attributes", "might=9,grace=12,grit=16,wit=16", "--feature", "lightning_reflexes", "--out", "ilse.json");
        File.WriteAllText(Path.Combine(scratch.Root, "deeds.script"), string.Join("\n",
            "status",
            "level 1",            // refused: a warrior level grants a combat feat
            "level 1 --feature weapon_focus",
            "level 1",            // nothing more yet
            "level 2 --class warrior --feature improved_initiative",
            "forward",            // 2000 each: level 3, where the advancement grants a feat too
            "level 1",
            "level 1 --feature great_fortitude",
            "status") + "\n");

        (int code, string output) = Run(scratch, "play", "--campaign", campaign, "--party", "kara.json,ilse.json", "--script", "deeds.script",
            "--modules", fixtures, "--modules", Path.Combine(Rules.RepositoryRoot, "modules"));

        Assert.Equal(0, code);
        Golden.Verify("ascend-experience.txt", output.Replace(scratch.Root, "<scratch>", StringComparison.Ordinal).Replace(modules.Root, "<modules>", StringComparison.Ordinal));
    }

    [Fact]
    public void ExperienceToEachGivesEveryCharacterTheWholeAward()
    {
        using TempModules modules = new();
        string campaign = modules.Module("hunt", "campaign", requires: $"{Require("three-action", "*")}, {Require("placeholder-art", "*")}");
        modules.Write("hunt/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|     |", "+--+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } }, "cells": [ { "at": [1, 0], "event": "wolves" } ] }""");
        modules.Write("hunt/campaign.json", """{ "type": "campaign", "id": "hunt", "name": "Hunt", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }""");
        modules.Write("hunt/wolves.json", """{ "type": "event", "id": "wolves", "kind": "combat", "encounter": "three-action:wolf_pack" }""");
        using TempModules scratch = new();
        string three = Path.Combine(Rules.RepositoryRoot, "modules", "three-action");
        Run(scratch, "character", "new", "--module", three, "--class", "fighter", "--race", "human", "--name", "Bram", "--feature", "skilled_human,warrior,sudden_charge",
            "--boosts", "str,con,str,dex,str,str,con,dex,wis", "--out", "bram.json");
        Run(scratch, "character", "new", "--module", three, "--class", "fighter", "--race", "dwarf", "--name", "Dagna", "--feature", "rock_dwarf,guard,vicious_swing",
            "--boosts", "str,str,con,str,str,dex,con,wis", "--out", "dagna.json");
        Equip(scratch, "bram.json", "three-action:longsword", "three-action:chain_mail", "three-action:steel_shield");
        Equip(scratch, "dagna.json", "three-action:greatsword", "three-action:full_plate");
        File.WriteAllText(Path.Combine(scratch.Root, "hunt.script"), "forward\nstatus\n");

        (int code, string output) = Run(scratch, "play", "--campaign", campaign, "--party", "bram.json,dagna.json", "--script", "hunt.script", "--seed", "1",
            "--modules", Path.Combine(Rules.RepositoryRoot, "modules"));

        // Two wolves at 40 each: 80 to each character, not 80 shared.
        Assert.Equal(0, code);
        Assert.Contains("Experience: Bram 80, Dagna 80.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CallingOnAFormerClassForfeitsTheAdventuresExperience()
    {
        using TempModules modules = new();
        string campaign = modules.Module("trial", "campaign", requires: $"{Require("classic", "*")}, {Require("placeholder-art", "*")}");
        modules.Write("trial/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+--+", "|        |", "+--+--+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } }, "cells": [ { "at": [1, 0], "event": "guards" }, { "at": [2, 0], "event": "praise" } ] }""");
        modules.Write("trial/campaign.json", """{ "type": "campaign", "id": "trial", "name": "Trial", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }""");
        modules.Write("trial/guards.json", """{ "type": "event", "id": "guards", "kind": "combat", "encounter": "classic:crypt_guard" }""");
        modules.Write("trial/praise.json", """{ "type": "event", "id": "praise", "kind": "experience", "amount": "100", "each": true }""");
        using TempModules scratch = new();
        string classic = Rules.ClassicPath;
        Run(scratch, "character", "new", "--module", classic, "--class", "fighter", "--race", "human", "--name", "Aldo", "--attributes", "str=16,dex=12,con=13,int=17,wis=10,cha=10", "--out", "aldo.json");
        Run(scratch, "character", "level", "aldo.json", "--module", classic, "--xp", "4500", "--seed", "2");
        Run(scratch, "character", "level", "aldo.json", "--module", classic, "--xp", "0", "--class", "magic_user", "--seed", "5");
        Run(scratch, "character", "new", "--module", classic, "--class", "fighter", "--race", "human", "--name", "Ada", "--attributes", "str=16,dex=13,con=15,int=10,wis=9,cha=11", "--seed", "2", "--out", "ada.json");
        Equip(scratch, "aldo.json", "classic:dagger");
        Equip(scratch, "ada.json", "classic:long_sword", "classic:chain_mail", "classic:shield");
        File.WriteAllText(Path.Combine(scratch.Root, "trial.script"), string.Join("\n",
            "status",
            "former 2 on",      // Ada has no former class
            "former 1 on",      // the fighter's sword arm, at a price
            "forward",          // the guards: Aldo fights as a fighter and forfeits his share
            "former 1 off",
            "forward",          // still forfeited: the rest of the adventure
            "status") + "\n");

        (int code, string output) = Run(scratch, "play", "--campaign", campaign, "--party", "aldo.json,ada.json", "--script", "trial.script", "--seed", "3",
            "--modules", Path.Combine(Rules.RepositoryRoot, "modules"));

        Assert.Equal(0, code);
        Golden.Verify("classic-dual-class-play.txt", output.Replace(scratch.Root, "<scratch>", StringComparison.Ordinal).Replace(modules.Root, "<modules>", StringComparison.Ordinal));
    }

    [Fact]
    public void HandEditedSavesAreRefusedWithTheirPath()
    {
        using TempModules scratch = new();
        WriteParty(scratch, Rules.ClassicPath);
        Play(scratch, "--party", "ada.json,brom.json", "--script", Script("crypt-early-stairs.script"), "--save", "game.json");
        string save = File.ReadAllText(Path.Combine(scratch.Root, "game.json"));
        System.Text.Json.Nodes.JsonNode edited = System.Text.Json.Nodes.JsonNode.Parse(save)!;
        edited["x"] = 50;
        edited["commands"] = -1;
        edited["pending_menu"] = 5;
        edited["fired"] = new System.Text.Json.Nodes.JsonArray(3);
        edited["party"] = new System.Text.Json.Nodes.JsonArray();
        File.WriteAllText(Path.Combine(scratch.Root, "bad.json"), edited.ToJsonString());

        File.WriteAllText(Path.Combine(scratch.Root, "empty.script"), "");
        (int code, string output) = Run(scratch, "play", "--campaign", SampleCrypt, "--load", "bad.json", "--script", "empty.script", "--json");

        Assert.Equal(1, code);
        JsonElement diagnostics = JsonDocument.Parse(output).RootElement.GetProperty("diagnostics");
        Assert.Equal(["$.commands", "$.x"], diagnostics.EnumerateArray().Select(diagnostic => diagnostic.GetProperty("jsonPath").GetString()).Order());

        edited["x"] = 0;
        edited["commands"] = 3;
        File.WriteAllText(Path.Combine(scratch.Root, "bad.json"), edited.ToJsonString());
        (_, string again) = Run(scratch, "play", "--campaign", SampleCrypt, "--load", "bad.json", "--script", "empty.script", "--json");
        Assert.Equal(
            ["$.fired", "$.party", "$.pending_menu"],
            JsonDocument.Parse(again).RootElement.GetProperty("diagnostics").EnumerateArray().Select(diagnostic => diagnostic.GetProperty("jsonPath").GetString()).Order());
    }

    [Fact]
    public void CampaignDataIsCheckedAtLoad()
    {
        using TempModules modules = new();
        modules.Module("art", "assets");
        modules.Write("art/missing.json", """{ "type": "asset", "id": "missing", "kind": "backdrop", "file": "nowhere.svg" }""");
        Rules.WriteSmallRuleset(modules);
        string campaign = modules.Module("tale", "campaign", requires: $"{Require("rules", "*")}, {Require("art", "*")}");
        modules.Write("tale/hall.json", """
            { "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|  X  |", "+--+--+"],
              "entries": { "in": { "at": [0, 0], "facing": "east" } } }
            """);
        modules.Write("tale/yard.json", """
            { "type": "area", "id": "yard", "name": "Yard", "map": ["+--+", "|  |", "+--+"],
              "cells": [ { "at": [3, 0], "event": "go" } ], "entries": { "in": { "at": [0, 0], "facing": "east" } } }
            """);
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "yard", "entry": "gate" }, "party": { "min": 2, "max": 1 } }""");
        modules.Write("tale/flag.json", """{ "type": "variable", "id": "flag", "value_type": "boolean", "initial": "0" }""");
        modules.Write("tale/go.json", """{ "type": "event", "id": "go", "kind": "teleport", "area": "yard", "entry": "nowhere", "next": "say" }""");
        modules.Write("tale/say.json", """{ "type": "event", "id": "say", "kind": "set", "variable": "flag", "value": "campaign.var.count + 1" }""");
        modules.Write("tale/stuck.json", """{ "type": "event", "id": "stuck", "kind": "menu", "text": "?", "options": [ { "label": "x", "when": "false" } ] }""");

        ModuleSet set = ModuleLoader.Load(campaign, []);

        Assert.Equal(
            [("area.cell", "yard.json", "$.cells[0].at"), ("area.map", "hall.json", "$.map[1]"), ("event.menu", "stuck.json", "$.options")],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, Path.GetFileName(diagnostic.File!), diagnostic.JsonPath!)).Order());
        Assert.Contains("Column 4", set.Diagnostics.Single(diagnostic => diagnostic.Rule == "area.map").Message, StringComparison.Ordinal);

        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|  D  |", "+--+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/yard.json", """{ "type": "area", "id": "yard", "name": "Yard", "map": ["+--+", "|  |", "+--+"], "cells": [ { "at": [0, 0], "event": "go" } ], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        modules.Write("tale/stuck.json", """{ "type": "event", "id": "stuck", "kind": "menu", "text": "?", "options": [ { "label": "x" } ] }""");
        ModuleSet again = ModuleLoader.Load(campaign, []);

        Assert.Equal(
            [
                ("area.entry", "campaign.json", "$.start.entry"),
                ("area.entry", "go.json", "$.entry"),
                ("asset.file", "missing.json", "$.file"),
                ("campaign.party", "campaign.json", "$.party"),
                ("expression.type", "flag.json", "$.initial"),
                ("expression.type", "say.json", "$.value"),
            ],
            again.Diagnostics.Select(diagnostic => (diagnostic.Rule, Path.GetFileName(diagnostic.File!), diagnostic.JsonPath!)).Order());
        Assert.Contains("'count' is not a declared variable", again.Diagnostics.Single(diagnostic => diagnostic.File!.EndsWith("say.json", StringComparison.Ordinal)).Message, StringComparison.Ordinal);
    }

    private static List<string> Commands(JsonElement transcript)
    {
        return transcript.GetProperty("transcript").EnumerateArray()
            .Where(step => step.GetProperty("command").ValueKind == JsonValueKind.String)
            .Select(step => step.GetRawText())
            .ToList();
    }

    private static JsonElement Play(TempModules scratch, params string[] args)
    {
        (int code, string output) = Run(scratch, ["play", "--campaign", SampleCrypt, "--json", .. args]);
        Assert.Equal(0, code);
        return JsonDocument.Parse(output).RootElement.Clone();
    }

    internal static (int Code, string Output) Run(TempModules scratch, params string[] args)
    {
        using StringWriter output = new();
        int code = Cli.GoldboxCli.Run(args, output, scratch.Root);
        return (code, output.ToString());
    }

    internal static void WriteParty(TempModules scratch, string classic)
    {
        Run(scratch, "character", "new", "--module", classic, "--class", "fighter", "--race", "human", "--name", "Ada", "--attributes", "str=16,dex=13,con=15,int=10,wis=9,cha=11", "--seed", "2", "--out", "ada.json");
        Run(scratch, "character", "new", "--module", classic, "--class", "cleric", "--race", "dwarf", "--name", "Brom", "--attributes", "str=13,dex=10,con=14,int=9,wis=15,cha=10", "--spells", "cure_light_wounds", "--seed", "4", "--out", "brom.json");
        Equip(scratch, "ada.json", "classic:long_sword", "classic:chain_mail", "classic:shield");
        Equip(scratch, "brom.json", "classic:heavy_mace", "classic:chain_mail");
    }

    private static void Equip(TempModules scratch, string file, params string[] items)
    {
        string path = Path.Combine(scratch.Root, file);
        System.Text.Json.Nodes.JsonNode node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        node["equipment"] = new System.Text.Json.Nodes.JsonArray(items.Select(item => (System.Text.Json.Nodes.JsonNode)item!).ToArray());
        File.WriteAllText(path, node.ToJsonString());
    }

    private static void Copy(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
