using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyTales.Core.Characters;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;
using RustyTales.Game;

namespace RustyTales.Tests;

/// <summary>
/// Best-effort ruleset-bound character import, campaign level policies,
/// persistent Game character stable, and <c>tales character import</c>.
/// </summary>
public sealed class CharacterImportTests
{
    [Fact]
    public void ImportReconcilesLevelPolicyMissingItemsTweakedRulesAndArtSwapWithLog()
    {
        using TempModules scratch = new();
        // Campaign A: classic + custom quest item + placeholder-art.
        string campaignA = scratch.Module("campaign-a", "campaign",
            requires: $"{TempModules.Require("classic", "^0.1.0")}, {TempModules.Require("placeholder-art", "^0.1.0")}",
            directory: "campaign-a");
        scratch.Write("campaign-a/campaign.json", """
            {
              "type": "campaign", "id": "a", "name": "Campaign A",
              "start": { "area": "hall", "entry": "door" },
              "party": { "min": 1, "max": 4 }
            }
            """);
        scratch.Write("campaign-a/areas/hall.json", """
            { "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "door": { "at": [0, 0], "facing": "east" } } }
            """);
        scratch.Write("campaign-a/items/relic.json", """
            { "type": "item", "id": "crypt_relic", "name": "Crypt Relic", "kind": "gear", "cost": 0, "currency": "classic:gold", "weight": 1 }
            """);

        // Campaign B: same baseline ruleset (classic), no crypt_relic, patches fighter equipment rule to disallow longsword, and allows higher-level import up to level 2.
        string campaignB = scratch.Module("campaign-b", "campaign",
            requires: $"{TempModules.Require("classic", "^0.1.0")}, {TempModules.Require("placeholder-art", "^0.1.0")}",
            directory: "campaign-b");
        scratch.Write("campaign-b/campaign.json", """
            {
              "type": "campaign", "id": "b", "name": "Campaign B",
              "start": { "area": "hall", "entry": "door" },
              "party": { "min": 1, "max": 4 },
              "import": { "allow_higher_level": true, "max_level": 2 }
            }
            """);
        scratch.Write("campaign-b/areas/hall.json", """
            { "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "door": { "at": [0, 0], "facing": "east" } } }
            """);
        scratch.Write("campaign-b/patches/no_sword.json", """
            {
              "type": "patch", "id": "no_long_sword",
              "target_type": "class", "target": "classic:fighter",
              "set": { "equipment": "item.id != 'long_sword'" }
            }
            """);

        // Campaign C: same baseline ruleset, default import policy (forces Level 1).
        string campaignC = scratch.Module("campaign-c", "campaign",
            requires: $"{TempModules.Require("classic", "^0.1.0")}, {TempModules.Require("placeholder-art", "^0.1.0")}",
            directory: "campaign-c");
        scratch.Write("campaign-c/campaign.json", """
            {
              "type": "campaign", "id": "c", "name": "Campaign C",
              "start": { "area": "hall", "entry": "door" },
              "party": { "min": 1, "max": 4 }
            }
            """);
        scratch.Write("campaign-c/areas/hall.json", """
            { "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "door": { "at": [0, 0], "facing": "east" } } }
            """);

        List<string> searchDirs = [Path.Combine(Rules.RepositoryRoot, "modules"), scratch.Root];
        ModuleSet setA = ModuleLoader.Load(campaignA, searchDirs);
        ModuleSet setB = ModuleLoader.Load(campaignB, searchDirs);
        ModuleSet setC = ModuleLoader.Load(campaignC, searchDirs);
        Assert.True(setA.IsValid && setB.IsValid && setC.IsValid);

        // Create a level-3 fighter in Campaign A with both a shared item, a campaign-specific relic, and a tweaked-disallowed longsword.
        string charFile = Path.Combine(scratch.Root, "veteran.json");
        (int newCode, string newOut) = CampaignTests.Run(scratch,
            "character", "new",
            "--module", campaignA,
            "--modules", Path.Combine(Rules.RepositoryRoot, "modules"),
            "--modules", scratch.Root,
            "--name", "Valen",
            "--race", "human",
            "--class", "fighter",
            "--equipment", "long_sword,dagger,campaign-a:crypt_relic",
            "--portrait", "placeholder-art:fighter_portrait",
            "--seed", "2",
            "--out", charFile);
        Assert.True(newCode == 0, newOut);

        (int lvlCode, string lvlOut) = CampaignTests.Run(scratch,
            "character", "level", charFile,
            "--module", campaignA,
            "--modules", Path.Combine(Rules.RepositoryRoot, "modules"),
            "--modules", scratch.Root,
            "--xp", "5000",
            "--trained",
            "--seed", "42");
        Assert.True(lvlCode == 0, lvlOut);

        // 1. Import into Campaign B (allow_higher_level: true, max_level: 2, longsword disallowed by patch, crypt_relic missing).
        Definition defB = setB.Rules!.OfType(DefinitionTypes.Campaign).Single();
        CharacterImportResult resultB = CharacterImport.ImportFile(charFile, setB, defB);
        Assert.True(resultB.Succeeded, string.Join("; ", resultB.Problems.Select(p => p.Message)));
        Character inB = resultB.Character!;
        Assert.Equal(2, inB.Level);
        Assert.Contains(inB.Equipment, item => item.Id == "dagger");
        Assert.DoesNotContain(inB.Equipment, item => item.Id == "long_sword");
        Assert.DoesNotContain(inB.Equipment, item => item.Id == "crypt_relic");
        Assert.Contains(resultB.Log, line => line.Contains("level 2", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resultB.Log, line => line.Contains("crypt_relic", StringComparison.Ordinal));
        Assert.Contains(resultB.Log, line => line.Contains("Long sword", StringComparison.OrdinalIgnoreCase));

        // Verify the imported character round-trips strictly through CharacterFile.Read under Campaign B!
        string importedBPath = Path.Combine(scratch.Root, "imported-b.json");
        File.WriteAllText(importedBPath, CharacterFile.ToJson(inB));
        List<ModuleDiagnostic> readProblems = [];
        Character? strictRead = CharacterFile.Read(importedBPath, setB, readProblems);
        Assert.NotNull(strictRead);
        Assert.Empty(readProblems);

        // 2. Import into Campaign C (default policy forces level 1).
        Definition defC = setC.Rules!.OfType(DefinitionTypes.Campaign).Single();
        CharacterImportResult resultC = CharacterImport.ImportFile(charFile, setC, defC);
        Assert.True(resultC.Succeeded);
        Assert.Equal(1, resultC.Character!.Level);
        Assert.Equal(0, resultC.Character.Experience);
        Assert.Contains(resultC.Log, line => line.Contains("level 1", StringComparison.OrdinalIgnoreCase));

        // 3. CLI `tales character import` works with --json and writes a valid output character.
        string cliOutPath = Path.Combine(scratch.Root, "cli-imported.json");
        (int importCode, string importOut) = CampaignTests.Run(scratch,
            "character", "import", charFile,
            "--module", campaignB,
            "--modules", Path.Combine(Rules.RepositoryRoot, "modules"),
            "--modules", scratch.Root,
            "--out", cliOutPath,
            "--json");
        Assert.True(importCode == 0, importOut);
        using JsonDocument cliJson = JsonDocument.Parse(importOut);
        Assert.True(cliJson.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(2, cliJson.RootElement.GetProperty("level").GetInt32());
        Assert.True(cliJson.RootElement.GetProperty("import_log").GetArrayLength() >= 2);
        Assert.True(File.Exists(cliOutPath));
    }

    [Fact]
    public void ImportRefusesCharacterFromDifferentBaselineRuleset()
    {
        using TempModules scratch = new();
        string classicChar = Path.Combine(scratch.Root, "classic.json");
        (int code, string output) = CampaignTests.Run(scratch,
            "character", "new",
            "--module", Path.Combine(Rules.RepositoryRoot, "modules", "sample-crypt"),
            "--modules", Path.Combine(Rules.RepositoryRoot, "modules"),
            "--name", "Roland",
            "--race", "human",
            "--class", "fighter",
            "--seed", "2",
            "--out", classicChar);
        Assert.True(code == 0, output);

        ModuleSet jrpgSet = ModuleLoader.Load(
            Path.Combine(Rules.RepositoryRoot, "modules", "crystal-keep"),
            [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.True(jrpgSet.IsValid);

        CharacterImportResult result = CharacterImport.ImportFile(classicChar, jrpgSet);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Problems, problem => problem.Rule == "character.import.ruleset");
    }

    [Fact]
    public void GameStableSharesRolledAndExportedCharactersAcrossCampaignsOfSameRuleset()
    {
        using TempModules scratch = new();
        string persistence = Path.Combine(scratch.Root, "persist");
        Directory.CreateDirectory(persistence);
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = persistence });
        host.Call(engine =>
        {
            string rules = EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", "classic"), scratch);
            string art = EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", "placeholder-art"), scratch);
            string ext = EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", "tactical-bestiaire"), scratch);
            string crypt = Path.Combine(scratch.Root, "sample-crypt.rpak");
            string gallery = Path.Combine(scratch.Root, "tactical-expedition.rpak");
            Assert.Equal(0, CampaignTests.Run(scratch, "module", "pack", Path.Combine(Rules.RepositoryRoot, "modules", "sample-crypt"), "--modules", Path.Combine(Rules.RepositoryRoot, "modules"), "--output", crypt).Code);
            Assert.Equal(0, CampaignTests.Run(scratch, "module", "pack", Path.Combine(Rules.RepositoryRoot, "modules", "tactical-expedition"), "--modules", Path.Combine(Rules.RepositoryRoot, "modules"), "--output", gallery).Code);

            string[] bundles = [rules, art, ext, crypt, gallery];
            ModuleLibrary Library() => new(_ => bundles.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList());

            // Session 1: open Sample Crypt, roll a hero (auto-saved to stable), equip a weapon, save back to stable.
            GameSession session1 = new(Library());
            session1.Refresh();
            CharacterStable.Load(engine, session1);
            string cryptBundle = session1.Campaigns.Single(c => c.Id == "sample-crypt").Bundle;
            Run(session1, engine, JsonSerializer.Serialize(new { action = "open", campaign = cryptBundle, seed = "7" }));
            Run(session1, engine, """{ "action": "roll", "name": "Dara", "race": "classic:human", "class": "classic:fighter", "portrait": "placeholder-art:fighter_portrait" }""");
            Assert.Single(session1.Party);
            Assert.Single(session1.Stable);

            Run(session1, engine, """{ "action": "equip", "member": 0, "item": "classic:long_sword" }""");
            Run(session1, engine, """{ "action": "stable-save", "member": 0 }""");
            Assert.Single(session1.Stable);

            // Session 2 (fresh restart loading from Engine persistence!): open Tactical Gallery and import Dara from stable!
            GameSession session2 = new(Library());
            session2.Refresh();
            CharacterStable.Load(engine, session2);
            StableEntry persisted = Assert.Single(session2.Stable);
            Assert.Equal("Dara", persisted.Name);
            Assert.Equal("classic", persisted.RulesetId);

            string galleryBundle = session2.Campaigns.Single(c => c.Id == "tactical-expedition").Bundle;
            Run(session2, engine, JsonSerializer.Serialize(new { action = "open", campaign = galleryBundle, seed = "11" }));
            JsonObject partyProj = SessionProjection.Build(session2);
            Assert.True(partyProj["importPolicy"]!["allowHigherLevel"]!.GetValue<bool>());
            Assert.Single(partyProj["stable"]!.AsArray());

            Run(session2, engine, JsonSerializer.Serialize(new { action = "stable-import", id = persisted.Id }));
            Character imported = Assert.Single(session2.Party);
            Assert.Equal("Dara", imported.Name);
            Assert.Contains(imported.Equipment, item => item.Id == "long_sword");
            Assert.Equal("placeholder-art:fighter_portrait", imported.Portrait?.QualifiedId);

            // Begin play and export party back to stable cleanly.
            Run(session2, engine, """{ "action": "begin" }""");
            Assert.Equal(Screen.Play, session2.Screen);
            Run(session2, engine, """{ "action": "stable-export-party" }""");
            Assert.Single(session2.Stable);
            Assert.Equal("The Tactical Gallery", session2.Stable[0].OriginCampaign);
        });
    }

    private static void Run(GameSession session, IEngineContext engine, string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        GameCommands.Run(session, engine, document.RootElement);
    }
}
