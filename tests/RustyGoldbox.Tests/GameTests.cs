using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Game;
using RustyGoldbox.Game.Presentation;

namespace RustyGoldbox.Tests;

/// <summary>
/// The Game's session and its input boundary, run against the sample modules
/// packed as Engine containers (the bundle surface the product opens).
/// </summary>
public sealed class GameTests
{
    private static readonly string[] SampleSet = ["classic", "placeholder-art", "sample-crypt"];

    [Theory]
    [InlineData("""{ "action": "equip", "member": 0, "item": null }""", "\"item\" must be non-empty text")]
    [InlineData("""{ "action": "play", "command": null }""", "\"command\" must be non-empty text")]
    [InlineData("""{ "action": "roll", "name": " ", "race": "classic:human", "class": "classic:fighter" }""", "\"name\" must be non-empty text")]
    [InlineData("""{ "action": "roll", "name": "A", "race": "classic:human", "class": "classic:fighter", "features": [1] }""", "\"features\" must be an array of non-empty text")]
    [InlineData("""{ "action": "roll", "name": "A", "skills": [1] }""", "Each \"skills\" entry must have a non-empty text \"skill\".")]
    [InlineData("""{ "action": "drop", "member": "0" }""", "\"member\" must be a whole number")]
    [InlineData("""{ "action": "open", "campaign": "x", "seed": 7 }""", "\"seed\" must be non-empty text")]
    [InlineData("""{ "action": "save", "slot": "../escape" }""", "isn't a save slot name")]
    [InlineData("""{ "action": "dance" }""", "'dance' is not an action")]
    [InlineData("""[1, 2]""", "must be an object")]
    public void BadPayloadsBecomeNotes(string payload, string note)
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            Run(session, engine, payload);
            Assert.Contains(note, Assert.Single(session.Notes), StringComparison.Ordinal);
            Assert.Empty(session.Party);
        });
    }

    [Fact]
    public void GameRollPassesStagedCreationChoicesToCore()
    {
        using TempModules scratch = new();
        string campaign = scratch.Module("staged-campaign", "campaign", requires: $"{TempModules.Require("universal-d100", "0.1.0")}, {TempModules.Require("placeholder-art", "0.1.0")}", directory: "staged-campaign");
        scratch.Write("staged-campaign/campaign.json", """
            {
              "type": "campaign",
              "id": "start",
              "name": "Staged campaign",
              "start": { "area": "hall", "entry": "start" },
              "party": { "min": 1, "max": 4 }
            }
            """);
        scratch.Write("staged-campaign/hall.json", """
            {
              "type": "area",
              "id": "hall",
              "name": "Hall",
              "map": ["+--+", "|  |", "+--+"],
              "entries": { "start": { "at": [0, 0], "facing": "east" } }
            }
            """);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            string ruleset = EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", "universal-d100"), scratch);
            string art = EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", "placeholder-art"), scratch);
            string campaignBundle = EngineContentTests.Pack(campaign, scratch);
            GameSession session = new(new ModuleLibrary(_ =>
            [
                ProductContentBundle.OpenContainer(engine.Content, ruleset),
                ProductContentBundle.OpenContainer(engine.Content, art),
                ProductContentBundle.OpenContainer(engine.Content, campaignBundle),
            ]));
            session.Refresh();
            Run(session, engine, JsonSerializer.Serialize(new { action = "open", campaign = Assert.Single(session.Campaigns).Bundle, seed = "4" }));
            Run(session, engine, """
                {
                  "action": "roll",
                  "name": "Rook",
                  "creation": "staged",
                  "features": ["staged_soldier"]
                }
                """);

            Assert.Contains("Rolled Rook", Assert.Single(session.Notes), StringComparison.Ordinal);
            JsonObject draft = SessionProjection.Build(session)["party"]![0]!.AsObject();
            Assert.Equal(90, draft["skillPoints"]!["personal"]!.GetValue<double>());
            Assert.Equal(24, draft["skillPoints"]!["skills"]!.AsArray().Single(skill => skill!["id"]!.GetValue<string>() == "dodge")!["current"]!.GetValue<double>());

            Run(session, engine, """
                {
                  "action": "skills",
                  "member": 0,
                  "skills": [
                    { "skill": "sword", "profession": 100, "personal": 90 },
                    { "skill": "shield", "profession": 50 },
                    { "skill": "dodge", "profession": 40 },
                    { "skill": "brawl", "profession": 30 },
                    { "skill": "bow", "profession": 30 }
                  ]
                }
                """);

            Assert.Contains("Committed staged skill points", Assert.Single(session.Notes), StringComparison.Ordinal);
            Character character = Assert.Single(session.Party);
            Assert.Equal(205, new Core.Rules.Evaluator(session.Set!.Rules!, null).Stat(character.ToCreature(), "sword").Number);
        });
    }

    [Fact]
    public void TheLibraryListsInstalledCampaignsAndNamesBrokenContainers()
    {
        using TempModules scratch = new();
        string library = Path.Combine(scratch.Root, "library");
        foreach (string id in SampleSet)
        {
            EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch, Path.Combine(library, $"{id}-0.1.0.rpak"));
        }

        // A second copy of the same campaign (as when the product ships it and it is also installed).
        File.Copy(Path.Combine(library, "sample-crypt-0.1.0.rpak"), Path.Combine(library, "sample-crypt-copy.rpak"));
        scratch.Write("library/zzz-broken-0.1.0.rpak", "not a container");
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = new(new ModuleLibrary(problems =>
            {
                List<ProductContentBundle> opened = [];
                InstalledModules.Open(engine.Content, library, opened, problems);
                return opened;
            }));
            session.Refresh();

            Assert.Equal("sample-crypt", Assert.Single(session.Campaigns).Id);
            Assert.Contains("zzz-broken-0.1.0.rpak isn't a usable module container", Assert.Single(session.Notes), StringComparison.Ordinal);
            Run(session, engine, JsonSerializer.Serialize(new { action = "open", campaign = session.Campaigns[0].Bundle, seed = "1" }));
            Assert.Equal(Screen.Party, session.Screen);
        });
    }

    [Fact]
    public void AClassRefusesEquipmentItMayNotUse()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            for (int attempt = 0; attempt < 50 && session.Party.Count == 0; attempt++)
            {
                Run(session, engine, """{ "action": "roll", "name": "Mira", "race": "classic:human", "class": "classic:magic_user" }""");
            }

            Run(session, engine, """{ "action": "equip", "member": 0, "item": "classic:chain_mail" }""");
            Assert.Equal("Mira can't equip Chain mail: Magic user doesn't allow it.", Assert.Single(session.Notes));
            Assert.Empty(session.Party[0].Equipment);

            Run(session, engine, """{ "action": "equip", "member": 0, "item": "classic:dagger" }""");
            Assert.Empty(session.Notes);
            Assert.Equal("dagger", Assert.Single(session.Party[0].Equipment).Id);
        });
    }

    [Fact]
    public void APartyMemberChoosesSpellsItCanCast()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            for (int attempt = 0; attempt < 50 && session.Party.Count == 0; attempt++)
            {
                Run(session, engine, """{ "action": "roll", "name": "Mira", "race": "classic:human", "class": "classic:magic_user" }""");
            }

            JsonNode member = SessionProjection.Build(session)["party"]![0]!;
            Assert.Equal(["Magic missile", "Sleep"], member["castable"]!.AsArray().Select(spell => spell!["name"]!.GetValue<string>()));

            Run(session, engine, """{ "action": "spells", "member": 0, "spells": ["classic:sleep", "classic:magic_missile"] }""");
            Assert.Empty(session.Notes);
            Assert.Equal(["sleep", "magic_missile"], session.Party[0].Spells.Select(spell => spell.Id));

            // A cleric's spell is refused and the list stays as it was.
            Run(session, engine, """{ "action": "spells", "member": 0, "spells": ["classic:cure_light_wounds"] }""");
            Assert.Equal("Cure light wounds isn't on the spell list of any of Mira's classes.", Assert.Single(session.Notes));
            Assert.Equal(2, session.Party[0].Spells.Count);

            Run(session, engine, """{ "action": "spells", "member": 0, "spells": [] }""");
            Assert.Empty(session.Party[0].Spells);
        });
    }

    [Fact]
    public void PortraitsAndIconsReachThePanelsAsEngineImages()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            List<string> containers = Containers(scratch);
            using UiImages images = new(engine, new ModuleLibrary(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList()));
            for (int attempt = 0; attempt < 50 && session.Party.Count == 0; attempt++)
            {
                Run(session, engine, """{ "action": "roll", "name": "Brom", "race": "classic:human", "class": "classic:cleric", "portrait": "placeholder-art:cleric_portrait" }""");
            }

            JsonObject projection = SessionProjection.Build(session, (set, asset) => images.Url(set, asset));
            string url = projection["party"]![0]!["portraitPicture"]!["url"]!.GetValue<string>();
            Assert.StartsWith("/__rusty/product/runtime/ui-images/", url, StringComparison.Ordinal);
            // One image per asset: the chooser and the member share it.
            Assert.Contains(projection["portraits"]!.AsArray(), entry => entry!["picture"]?["url"]?.GetValue<string>() == url);
        });
    }

    [Fact]
    public void APartyMemberMemorisesSpellsPreparedNowOrAtTheNextRest()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            for (int attempt = 0; attempt < 50 && session.Party.Count == 0; attempt++)
            {
                Run(session, engine, """{ "action": "roll", "name": "Mira", "race": "classic:human", "class": "classic:magic_user" }""");
            }

            Run(session, engine, """{ "action": "spells", "member": 0, "spells": ["classic:sleep", "classic:magic_missile"] }""");
            Assert.Equal("Sleep", SessionProjection.Build(session)["party"]![0]!["memorised"]![0]!["name"]!.GetValue<string>());

            // One 1st level slot: two copies are refused; one is prepared at once while making the party.
            Run(session, engine, """{ "action": "memorise", "member": 0, "spells": ["classic:sleep", "classic:magic_missile"] }""");
            Assert.Contains("can't memorise another Magic missile", Assert.Single(session.Notes), StringComparison.Ordinal);
            Run(session, engine, """{ "action": "memorise", "member": 0, "spells": ["classic:magic_missile"] }""");
            Assert.Empty(session.Notes);
            Assert.Equal(["magic_missile"], CharacterRules.PreparedLeft(session.Set!.Rules!, session.Party[0]).Select(spell => spell.Id));

            // In play the new list waits for a rest: magic missile is still the copy prepared.
            Run(session, engine, """{ "action": "roll", "name": "Ada", "race": "classic:human", "class": "classic:fighter" }""");
            Run(session, engine, """{ "action": "begin" }""");
            Assert.Equal(Screen.Play, session.Screen);
            Run(session, engine, """{ "action": "memorise", "member": 0, "spells": ["classic:sleep"] }""");
            Character mira = session.Runner!.State.Party[0];
            Assert.Equal(["sleep"], mira.Memorised.Select(spell => spell.Id));
            Assert.Equal(["magic_missile"], CharacterRules.PreparedLeft(session.Set!.Rules!, mira).Select(spell => spell.Id));
        });
    }

    [Fact]
    public void ARollCanChooseAPortrait()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            Assert.Contains("placeholder-art:cleric_portrait", SessionProjection.Build(session)["portraits"]!.ToJsonString(), StringComparison.Ordinal);
            for (int attempt = 0; attempt < 50 && session.Party.Count == 0; attempt++)
            {
                Run(session, engine, """{ "action": "roll", "name": "Brom", "race": "classic:human", "class": "classic:cleric", "portrait": "placeholder-art:cleric_portrait" }""");
            }

            Assert.Equal("placeholder-art:cleric_portrait", session.Party.Single().Portrait!.QualifiedId);
            Assert.Equal("placeholder-art:cleric_portrait", SessionProjection.Build(session)["party"]![0]!["portrait"]!.GetValue<string>());

            Run(session, engine, """{ "action": "roll", "name": "Cid", "race": "classic:human", "class": "classic:fighter", "portrait": "placeholder-art:nowhere" }""");
            Assert.Single(session.Party);
            Assert.Contains(session.Notes, note => note.Contains("placeholder-art:nowhere", StringComparison.Ordinal));

            // Any picture media is a portrait: a sheet's projected picture names the frame the panels crop.
            for (int attempt = 0; attempt < 50 && session.Party.Count == 1; attempt++)
            {
                Run(session, engine, """{ "action": "roll", "name": "Bones", "race": "classic:human", "class": "classic:cleric", "portrait": "placeholder-art:skeleton" }""");
            }

            JsonNode party = SessionProjection.Build(session, (_, _) => "url")["party"]!;
            Assert.Null(party[0]!["portraitPicture"]!["frame"]);
            Assert.Equal("[32,48]", party[1]!["portraitPicture"]!["frame"]!.ToJsonString());
            Assert.Equal("""{"frames":[0,1,2,3],"fps":4,"loop":true}""", party[1]!["portraitPicture"]!["animation"]!.ToJsonString());
        });
    }

    [Fact]
    public void EventMusicLoopsAndSoundsPlayOnceThroughEngineAudio()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            List<string> containers = Containers(scratch);
            ModuleLibrary library = new(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList());
            GameSession session = OpenSession(scratch, engine);
            using GameAudio audio = new(engine, library);
            for (int attempt = 0; attempt < 50 && session.Party.Count == 0; attempt++)
            {
                Run(session, engine, """{ "action": "roll", "name": "Ada", "race": "classic:human", "class": "classic:fighter" }""");
            }

            // The crypt's intro starts its music; walking onto the bones plays their crunch once.
            Run(session, engine, """{ "action": "begin" }""");
            audio.Update(session);
            AudioResult started = engine.Audio.Read();
            Run(session, engine, """{ "action": "play", "command": "forward" }""");
            Run(session, engine, """{ "action": "play", "command": "forward" }""");
            Run(session, engine, """{ "action": "volume", "bus": "music", "volume": 0.25 }""");
            Run(session, engine, """{ "action": "volume", "bus": "drums", "volume": 0.5 }""");
            Assert.Contains("'drums' is not a volume", Assert.Single(session.Notes), StringComparison.Ordinal);
            audio.Update(session);

            Assert.Equal(0.25f, engine.Audio.ReadBus(new AudioBusReadRequest(AudioBus.Music)).Volume);
            Assert.Empty(engine.Audio.Read().Diagnostics.ToArray());
            Assert.Equal(1u, started.ActiveVoices);
            Assert.Equal(1ul, engine.Audio.Read().EmittedSignals);
            Assert.Empty(session.TakeSounds());
            session.Quit();
            audio.Update(session);
            Assert.Equal(0u, engine.Audio.Read().ActiveVoices);
        });
    }

    [Fact]
    public void TheCampaignsSkinDressesThePanelsUntilThePlayerPicksAnother()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            JsonObject Projected() => SessionProjection.Build(session, (_, asset) => $"url:{asset.QualifiedId}");

            Assert.Equal(["placeholder-art:parchment", "placeholder-art:stone"], session.Skins.Select(skin => skin.Id).Order());
            JsonNode stone = Projected()["skin"]!;
            Assert.Equal("placeholder-art:stone", stone["id"]!.GetValue<string>());
            Assert.Equal("#e8b04a", stone["colors"]!["accent"]!.GetValue<string>());
            Assert.Equal("url:placeholder-art:stone_frame", stone["frame"]!["picture"]!["url"]!.GetValue<string>());
            Assert.Equal(8, stone["frame"]!["slice"]!.GetValue<int>());

            Run(session, engine, """{ "action": "skin", "skin": "placeholder-art:parchment" }""");
            Assert.Equal("placeholder-art:parchment", Projected()["skin"]!["id"]!.GetValue<string>());
            Assert.Equal("placeholder-art:parchment", Projected()["skinPicked"]!.GetValue<string>());
            Run(session, engine, """{ "action": "skin", "skin": "placeholder-art:velvet" }""");
            Assert.Contains("'placeholder-art:velvet' is not an installed skin", Assert.Single(session.Notes), StringComparison.Ordinal);
            Run(session, engine, """{ "action": "skin", "skin": null }""");
            Assert.Equal("placeholder-art:stone", Projected()["skin"]!["id"]!.GetValue<string>());

            // The title screen has no campaign: no skin unless the player picks one.
            session.Quit();
            Assert.Null(Projected()["skin"]);
        });
    }

    [Fact]
    public void KeyIntentsActOnPressesOnly()
    {
        Assert.True(GameCommands.IsPress(Digital(InputProvenance.DirectUi, InputEdge.None, 1)));
        Assert.False(GameCommands.IsPress(Digital(InputProvenance.DirectUi, InputEdge.None, 0)));
        Assert.True(GameCommands.IsPress(Digital(default, InputEdge.Pressed, 0)));
        Assert.False(GameCommands.IsPress(Digital(default, InputEdge.Released, 0)));
    }

    [Fact]
    public void ACampaignOffersInstalledExtensionsAndASaveLoadsThemAgain()
    {
        using TempModules scratch = new();
        using TempModules modules = new();
        string house = modules.Module("house", "extension", requires: TempModules.Require("classic", "^0.1.0"));
        modules.Module("elsewhere", "extension", requires: TempModules.Require("three-action", "^0.1.0"));
        string Pack(string directory)
        {
            string output = Path.Combine(scratch.Root, Path.GetFileName(directory) + ".rpak");
            (int code, string printed) = CampaignTests.Run(scratch, "module", "pack", directory, "--output", output, "--modules", Path.Combine(Rules.RepositoryRoot, "modules"));
            Assert.True(code == 0, printed);
            return output;
        }

        List<string> containers = [.. Containers(scratch), Pack(house), Pack(Path.Combine(modules.Root, "elsewhere")),
            Pack(Path.Combine(Rules.RepositoryRoot, "modules", "three-action"))];
        string store = Path.Combine(scratch.Root, "persistence");
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = store });
        host.Call(engine =>
        {
            GameSession session = new(new ModuleLibrary(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList()));
            session.Refresh();

            // Only the extension built on the campaign's ruleset is offered.
            JsonNode offered = SessionProjection.Build(session)["campaigns"]![0]!["extensions"]!;
            Assert.Equal("house", Assert.Single(offered.AsArray())!["id"]!.GetValue<string>());
            string campaign = Assert.Single(session.Campaigns).Bundle;
            Run(session, engine, JsonSerializer.Serialize(new { action = "open", campaign, seed = "11", extensions = new[] { "elsewhere" } }));
            Assert.Contains("'elsewhere' is not an extension", Assert.Single(session.Notes), StringComparison.Ordinal);

            Run(session, engine, JsonSerializer.Serialize(new { action = "open", campaign, seed = "11", extensions = new[] { "house" } }));
            Assert.Equal(["house"], session.Set!.Extensions);
            Assert.Equal("house", SessionProjection.Build(session)["extensions"]![0]!.GetValue<string>());
            for (int attempt = 0; attempt < 50 && session.Party.Count == 0; attempt++)
            {
                Run(session, engine, """{ "action": "roll", "name": "Ada", "race": "classic:human", "class": "classic:fighter" }""");
            }

            Run(session, engine, """{ "action": "begin" }""");
            Run(session, engine, """{ "action": "save", "slot": "with-house" }""");
            session.Quit();
            Run(session, engine, """{ "action": "load", "slot": "with-house" }""");
            Assert.Equal(Screen.Play, session.Screen);
            Assert.Equal(["house"], session.Set!.Extensions);
        });
    }

    [Fact]
    public void ThePartyPlaysSavesAndLoadsThroughCommands()
    {
        using TempModules scratch = new();
        string store = Path.Combine(scratch.Root, "persistence");
        List<string> script = File.ReadAllLines(CampaignTests.Script("crypt.script"))
            .Select(line => line.Split('#')[0].Trim())
            .Where(line => line.Length > 0)
            .ToList();
        int split = script.IndexOf("choose 1");

        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = store });
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            foreach ((string name, string characterClass, string[] items) in new[]
            {
                ("Ada", "classic:fighter", new[] { "classic:long_sword", "classic:chain_mail" }),
                ("Brom", "classic:cleric", new[] { "classic:heavy_mace" }),
            })
            {
                // Rolls fail class requirements now and then; each try rolls on its own scope.
                for (int attempt = 0; attempt < 50 && session.Party.All(member => member.Name != name); attempt++)
                {
                    Run(session, engine, $$"""{ "action": "roll", "name": "{{name}}", "race": "classic:human", "class": "{{characterClass}}" }""");
                }

                int member = session.Party.FindIndex(character => character.Name == name);
                Assert.True(member >= 0, string.Join(" ", session.Notes));
                foreach (string item in items)
                {
                    Run(session, engine, $$"""{ "action": "equip", "member": {{member}}, "item": "{{item}}" }""");
                }
            }

            Run(session, engine, """{ "action": "begin" }""");
            Assert.Equal(Screen.Play, session.Screen);
            foreach (string command in script[..split])
            {
                Run(session, engine, JsonSerializer.Serialize(new { action = "play", command }));
            }

            Assert.Equal([(1, "Lift the bar"), (2, "Leave it")], session.Runner!.MenuOptions());
            Run(session, engine, """{ "action": "save", "slot": "game" }""");
            Assert.Equal("Saved to save slot 'game'.", Assert.Single(session.Notes));
            string saved = SaveFile.ToJson(session.Runner.State, session.Set!);

            Run(session, engine, """{ "action": "quit" }""");
            Run(session, engine, """{ "action": "load", "slot": "game" }""");
            Assert.Equal(Screen.Play, session.Screen);
            Assert.Equal(saved, SaveFile.ToJson(session.Runner!.State, session.Set!));
            Assert.Equal(2, session.Runner.MenuOptions().Count);
        });

        // The CLI picks the Game's slot up from the same persistence root.
        File.WriteAllLines(Path.Combine(scratch.Root, "rest.script"), script[split..]);
        (int code, string output) = CampaignTests.Run(scratch, "play", "--campaign", CampaignTests.SampleCrypt, "--store", store, "--load", "game", "--script", "rest.script");
        Assert.True(code == 0, output);
        Assert.Contains("The adventure ends.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AFightPlaysBackOnTheCombatScreenAndShowsThroughTheEngine()
    {
        using TempModules scratch = new();
        List<string> script = File.ReadAllLines(CampaignTests.Script("crypt.script"))
            .Select(line => line.Split('#')[0].Trim())
            .Where(line => line.Length > 0)
            .ToList();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            for (int attempt = 0; attempt < 50 && session.Party.Count == 0; attempt++)
            {
                Run(session, engine, """{ "action": "roll", "name": "Ada", "race": "classic:human", "class": "classic:fighter" }""");
            }

            Run(session, engine, """{ "action": "equip", "member": 0, "item": "classic:long_sword" }""");
            Run(session, engine, """{ "action": "begin" }""");

            // Through the barred door to the guards.
            foreach (string command in script[..(script.IndexOf("choose 1") + 2)])
            {
                Run(session, engine, JsonSerializer.Serialize(new { action = "play", command }));
            }

            Assert.Equal(Screen.Combat, session.Screen);
            FightReplay fight = session.Fight!;
            Assert.Equal(0, fight.Shown);
            Assert.Contains(fight.Fight.Members, member => member.Side == 1 && member.Monster?.Id == "skeleton");

            // Play waits; the scene shows the fight through the Engine.
            Run(session, engine, """{ "action": "play", "command": "forward" }""");
            Assert.Equal("Continue past the fight first.", Assert.Single(session.Notes));
            using SceneView view = new(engine, new ModuleLibrary(_ => Containers(scratch).Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList()));
            view.Show(session);

            Assert.True(session.Tick(1.0));
            Assert.InRange(fight.Shown, 1, fight.Fight.Facts.Count - 1);

            // Skipping shows the rest; the values end where the fight left them.
            Run(session, engine, """{ "action": "continue" }""");
            Assert.True(fight.Done);
            List<Core.Combat.DamageFact> lasts = fight.Fight.Facts.OfType<Core.Combat.DamageFact>()
                .Where(fact => fact.Track == fight.Fight.Track)
                .GroupBy(fact => fact.Who)
                .Select(group => group.Last())
                .ToList();
            Assert.NotEmpty(lasts);
            foreach (Core.Combat.DamageFact damage in lasts)
            {
                Assert.Equal(damage.Left, fight.Values[damage.Who]);
            }

            view.Show(session);
            Run(session, engine, """{ "action": "continue" }""");
            Assert.Equal(Screen.Play, session.Screen);
            Assert.Null(session.Fight);
            view.Show(session);
        });
    }

    [Fact]
    public void TheWholeCryptShowsThroughTheEngineWithItsProps()
    {
        using TempModules scratch = new();
        List<string> script = File.ReadAllLines(CampaignTests.Script("crypt.script"))
            .Select(line => line.Split('#')[0].Trim())
            .Where(line => line.Length > 0)
            .ToList();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            foreach ((string name, string characterClass, string[] items) in new[]
            {
                ("Ada", "classic:fighter", new[] { "classic:long_sword", "classic:chain_mail", "classic:shield" }),
                ("Brom", "classic:cleric", new[] { "classic:heavy_mace", "classic:chain_mail" }),
            })
            {
                for (int attempt = 0; attempt < 50 && session.Party.All(member => member.Name != name); attempt++)
                {
                    Run(session, engine, $$"""{ "action": "roll", "name": "{{name}}", "race": "classic:human", "class": "{{characterClass}}" }""");
                }

                int member = session.Party.FindIndex(character => character.Name == name);
                foreach (string item in items)
                {
                    Run(session, engine, $$"""{ "action": "equip", "member": {{member}}, "item": "{{item}}" }""");
                }
            }

            Run(session, engine, """{ "action": "begin" }""");
            using SceneView view = new(engine, new ModuleLibrary(_ => Containers(scratch).Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList()));
            Core.Definitions.Definition entrance = session.Runner!.State.Area;
            string guardProp = "$.cells[2].prop.hidden";
            Assert.False(session.Runner.IsTrue(entrance, guardProp));

            // Every step shows: corridors with props, the fight, the stairs to the second area.
            view.Show(session);
            foreach (string command in script)
            {
                Run(session, engine, JsonSerializer.Serialize(new { action = "play", command }));
                view.Show(session);
                while (session.Screen == Screen.Combat)
                {
                    Run(session, engine, """{ "action": "continue" }""");
                    view.Show(session);
                }
            }

            // The party won (the seed is fixed), so the guard prop is gone and the party went down the stairs.
            Assert.True(session.Runner.State.Ended);
            Assert.True(session.Runner.IsTrue(entrance, guardProp));
            Assert.NotEqual(entrance, session.Runner.State.Area);
        });
    }

    /// <summary>A session over the packed sample modules, with the sample campaign open on seed 11.</summary>
    [Fact]
    public void ShopCommandsPricesAndSavedTradesReachTheGameProjection()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = Path.Combine(scratch.Root, "persistence") });
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            for (int attempt = 0; attempt < 50 && session.Party.Count == 0; attempt++)
            {
                Run(session, engine, """{ "action": "roll", "name": "Ada", "race": "classic:human", "class": "classic:fighter" }""");
            }

            Assert.Single(session.Party).Balances["gold"] = 2;
            Run(session, engine, """{ "action": "begin" }""");
            Run(session, engine, """{ "action": "play", "command": "right" }""");
            Run(session, engine, """{ "action": "play", "command": "forward" }""");
            JsonObject Projected() => SessionProjection.Build(session);
            JsonNode shop = Projected()["shop"]!;
            Assert.Equal(2m, shop["balances"]!["classic:gold"]!.GetValue<decimal>());
            Assert.Equal("classic:dagger", Assert.Single(shop["stock"]!.AsArray())!["id"]!.GetValue<string>());
            Assert.Equal(2m, shop["stock"]![0]!["price"]!.GetValue<decimal>());

            Run(session, engine, """{ "action": "play", "command": "buy 1" }""");
            shop = Projected()["shop"]!;
            Assert.Equal(0m, shop["balances"]!["classic:gold"]!.GetValue<decimal>());
            Assert.Equal(1m, Assert.Single(shop["carried"]!.AsArray())!["price"]!.GetValue<decimal>());
            Run(session, engine, """{ "action": "save", "slot": "shop" }""");
            session.Quit();
            Run(session, engine, """{ "action": "load", "slot": "shop" }""");
            Assert.NotNull(session.Runner!.State.PendingShop);
            Assert.Single(Projected()["shop"]!["carried"]!.AsArray());
            Run(session, engine, """{ "action": "play", "command": "sell 1" }""");
            Assert.Empty(Projected()["shop"]!["carried"]!.AsArray());
            Assert.Equal(1m, Projected()["shop"]!["balances"]!["classic:gold"]!.GetValue<decimal>());
            Run(session, engine, """{ "action": "play", "command": "leave" }""");
            Assert.Null(Projected()["shop"]);
            Assert.NotNull(Projected()["temple"]);
            session.Runner.State.Party[0].Balances["gold"] = 5;
            session.Runner.State.Party[0].Tracks["hit_points"].Current = 0;
            Run(session, engine, """{ "action": "play", "command": "serve 1 1" }""");
            Assert.True(session.Runner.State.Party[0].Tracks["hit_points"].Current > 0);
            Assert.Equal(0, session.Runner.State.Party[0].Balances["gold"]);
            Run(session, engine, """{ "action": "play", "command": "leave" }""");
            Assert.Null(Projected()["temple"]);
            Run(session, engine, """{ "action": "play", "command": "choose 1" }""");
            Assert.Contains("The outfitter wishes you safe travels.", session.Log);
            var trainee = session.Runner.State.Party[0];
            trainee.Experience = 2001;
            trainee.ClassExperience[trainee.Class!] = 2001;
            trainee.Balances["gold"] = 1500;
            session.Runner.State.X = 3;
            session.Runner.State.Y = 2;
            session.Runner.State.Facing = RustyGoldbox.Core.Campaigns.Facing.West;
            Run(session, engine, """{ "action": "play", "command": "forward" }""");
            Assert.NotNull(Projected()["training"]);
            Run(session, engine, """{ "action": "play", "command": "train 1" }""");
            Assert.Equal(2, trainee.Level);
            Assert.Equal(0, trainee.Balances["gold"]);
            Assert.InRange(session.Runner.State.ElapsedDays, 7, 28);
            Run(session, engine, """{ "action": "save", "slot": "training" }""");
            session.Quit();
            Run(session, engine, """{ "action": "load", "slot": "training" }""");
            Assert.NotNull(Projected()["training"]);
            Assert.Equal(2, session.Runner!.State.Party[0].Level);
            Assert.InRange(Projected()["elapsedDays"]!.GetValue<decimal>(), 7, 28);
        });
    }

    [Fact]
    public void NpcRecruitmentAndDismissalProjectTheSavedParty()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = Path.Combine(scratch.Root, "persistence") });
        host.Call(engine =>
        {
            GameSession session = OpenSession(scratch, engine);
            for (int attempt = 0; attempt < 50 && session.Party.Count == 0; attempt++)
            {
                Run(session, engine, """{ "action": "roll", "name": "Ada", "race": "classic:human", "class": "classic:fighter" }""");
            }

            Run(session, engine, """{ "action": "begin" }""");
            foreach (string command in new[] { "right", "forward", "leave", "leave", "choose 3" })
            {
                Run(session, engine, JsonSerializer.Serialize(new { action = "play", command }));
            }

            Assert.Equal(2, session.Runner!.State.Party.Count);
            JsonObject projection = SessionProjection.Build(session);
            Assert.Equal("sample-crypt:guide", projection["party"]![1]!["npc"]!.GetValue<string>());
            var guide = session.Runner.State.Party[1];
            guide.Tracks["hit_points"].Current = 1;
            guide.Equipment.Clear();
            Run(session, engine, """{ "action": "save", "slot": "npc-party" }""");
            session.Quit();
            Run(session, engine, """{ "action": "load", "slot": "npc-party" }""");
            Assert.Equal(2, session.Runner!.State.Party.Count);
            Run(session, engine, """{ "action": "play", "command": "choose 3" }""");
            Assert.Single(session.Runner.State.Party);
            Assert.Single(session.Runner.State.AbsentNpcs);
            Run(session, engine, """{ "action": "save", "slot": "npc-absent" }""");
            session.Quit();
            Run(session, engine, """{ "action": "load", "slot": "npc-absent" }""");
            Run(session, engine, """{ "action": "play", "command": "choose 3" }""");
            Assert.Equal(2, session.Runner!.State.Party.Count);
            Assert.Equal(1, session.Runner.State.Party[1].Tracks["hit_points"].Current);
            Assert.Empty(session.Runner.State.Party[1].Equipment);
        });
    }

    private static GameSession OpenSession(TempModules scratch, IEngineContext engine)
    {
        List<string> containers = Containers(scratch);
        GameSession session = new(new ModuleLibrary(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList()));
        session.Refresh();
        string campaign = Assert.Single(session.Campaigns).Bundle;
        Run(session, engine, JsonSerializer.Serialize(new { action = "open", campaign, seed = "11" }));
        Assert.Equal(Screen.Party, session.Screen);
        return session;
    }

    /// <summary>The sample modules packed into <paramref name="scratch"/>, packing them the first time.</summary>
    private static List<string> Containers(TempModules scratch)
    {
        return SampleSet
            .Select(id => File.Exists(Path.Combine(scratch.Root, id + ".rpak"))
                ? Path.Combine(scratch.Root, id + ".rpak")
                : EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
            .ToList();
    }

    private static void Run(GameSession session, IEngineContext engine, string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        GameCommands.Run(session, engine, document.RootElement);
    }

    private static ProductInputEvent Digital(InputProvenance provenance, InputEdge edge, float x)
    {
        return new ProductInputEvent(
            InputEventKind.DirectDigital, edge, default, default, default, default, default, default, default, default,
            InputValueKind.Digital, default, provenance, default, default, default, x, 0,
            default, default, Encoding.UTF8.GetBytes("party.forward"), default, default);
    }
}
