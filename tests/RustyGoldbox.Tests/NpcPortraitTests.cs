using System.Text;
using System.Text.Json;
using Rusty.Engine.Testing;
using RustyGoldbox.Cli;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

public sealed class NpcPortraitTests
{
    [Fact]
    public void ClassicNpcPortraitUsesThePictureSlotDuringValidation()
    {
        using TempModules modules = new();
        string campaign = WriteCampaignShell(modules, "classic");
        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");

        (int code, string output) = CampaignTests.Run(modules,
            "character", "new", "--module", campaign, "--modules", repositoryModules,
            "--class", "fighter", "--race", "human", "--name", "Guide",
            "--attributes", "str=16,dex=13,con=15,int=10,wis=9,cha=11",
            "--portrait", "placeholder-art:fighter_portrait", "--out", "hero.json");
        Assert.True(code == GoldboxCli.Ok, output);

        (code, output) = CampaignTests.Run(modules,
            "character", "npc", "hero.json", "--module", campaign, "--modules", repositoryModules,
            "--id", "guide", "--out", "tale/guide.json");
        Assert.True(code == GoldboxCli.Ok, output);

        ModuleSet set = ModuleLoader.Load(campaign, [repositoryModules]);
        Assert.True(set.IsValid, string.Join(Environment.NewLine, set.Diagnostics.Select(problem => problem.Message)));
        RuleSet rules = set.Rules!;
        Definition npc = rules.Find(DefinitionTypes.Npc, "tale:guide", out _)
            ?? throw new InvalidOperationException("NPC fixture missing");
        Assert.Equal("placeholder-art:fighter_portrait", rules.Reference(npc, "$.character.portrait").QualifiedId);
    }

    [Fact]
    public void NpcSchemaDescribesPortraitAsAPictureSlot()
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(["schema", "npc", "--json"], output, Rules.RepositoryRoot);

        Assert.Equal(GoldboxCli.Ok, code);
        using JsonDocument schema = JsonDocument.Parse(output.ToString());
        JsonElement character = schema.RootElement.GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("name").GetString() == "character");
        JsonElement portrait = character.GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("name").GetString() == "portrait");

        Assert.Equal("reference to an asset for a picture slot (\"id\" or \"module:id\"; see `goldbox schema media`)", portrait.GetProperty("kind").GetString());
        Assert.Contains("portrait tag", portrait.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FifthSrdPortraitNpcLoadsJoinsAndPersists()
    {
        using TempModules modules = new();
        string campaign = WriteCampaignShell(modules);
        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");

        (int code, string output) = CampaignTests.Run(modules,
            "character", "new", "--module", campaign, "--modules", repositoryModules,
            "--class", "fighter", "--race", "human", "--name", "Hero",
            "--feature", "soldier,savage_attacker,defense", "--portrait", "placeholder-art:fighter_portrait",
            "--seed", "3", "--out", "hero.json");
        Assert.True(code == GoldboxCli.Ok, output);

        (code, output) = CampaignTests.Run(modules,
            "character", "npc", "hero.json", "--module", campaign, "--modules", repositoryModules,
            "--id", "guide", "--out", "tale/guide.json");
        Assert.True(code == GoldboxCli.Ok, output);

        modules.Write("tale/join.json", """{ "type": "event", "id": "join", "kind": "join", "npc": "guide", "next": "welcome" }""");
        modules.Write("tale/welcome.json", """{ "type": "event", "id": "welcome", "kind": "text", "text": "Welcome." }""");
        modules.Write("tale/campaign.json", """
            {
              "type": "campaign",
              "id": "tale",
              "name": "Tale",
              "start": { "area": "hall", "entry": "in" },
              "party": { "min": 1, "max": 2 },
              "intro": "join"
            }
            """);

        ModuleSet set = ModuleLoader.Load(campaign, [repositoryModules]);
        Assert.True(set.IsValid, string.Join(Environment.NewLine, set.Diagnostics.Select(problem => problem.Message)));
        RuleSet rules = set.Rules!;
        Definition campaignDefinition = rules.Find(DefinitionTypes.Campaign, "tale", out _)!
            ?? throw new InvalidOperationException("campaign fixture missing");
        List<ModuleDiagnostic> problems = [];
        Character hero = CharacterFile.Read(Path.Combine(modules.Root, "hero.json"), set, problems)
            ?? throw new InvalidOperationException(string.Join(Environment.NewLine, problems.Select(problem => problem.Message)));
        Assert.Empty(problems);

        CampaignState state = CampaignRunner.NewState(rules, campaignDefinition, [hero], 1);
        CampaignRunner runner = new(rules, state);
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            PartyFact joined = Assert.Single(runner.Begin(engine.Random).OfType<PartyFact>());
            Assert.True(joined.Joined);
            Character npc = Assert.Single(runner.State.Party.Skip(1));
            Assert.Equal("tale:guide", npc.Npc!.QualifiedId);
            Assert.Equal("placeholder-art:fighter_portrait", npc.Portrait!.QualifiedId);

            List<ModuleDiagnostic> saveProblems = [];
            CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(SaveFile.ToJson(runner.State, set)), "save.json", set, saveProblems)
                ?? throw new InvalidOperationException(string.Join(Environment.NewLine, saveProblems.Select(problem => problem.Message)));
            Assert.Empty(saveProblems);
            Assert.Equal("tale:guide", restored.Party[1].Npc!.QualifiedId);
            Assert.Equal("placeholder-art:fighter_portrait", restored.Party[1].Portrait!.QualifiedId);
        });
    }

    [Fact]
    public void InvalidSoundMediaReportsItsReferenceInsteadOfAbortingModuleLoad()
    {
        using TempModules modules = new();
        string campaign = WriteCampaignShell(modules);
        modules.Write("tale/bad_sound.json", """
            {
              "type": "event",
              "id": "bad_sound",
              "kind": "text",
              "text": "A picture is not a sound.",
              "sound": "placeholder-art:fighter_portrait"
            }
            """);

        ModuleSet set = ModuleLoader.Load(campaign, [Path.Combine(Rules.RepositoryRoot, "modules")]);

        ModuleDiagnostic diagnostic = Assert.Single(set.Diagnostics, problem => problem.Rule == "reference.media");
        Assert.Equal(Path.Combine(modules.Root, "tale", "bad_sound.json"), diagnostic.File);
        Assert.Equal("$.sound", diagnostic.JsonPath);
        Assert.Contains("audio", diagnostic.Message, StringComparison.Ordinal);
    }

    private static string WriteCampaignShell(TempModules modules, string ruleset = "fifth-srd")
    {
        string campaign = modules.Module("tale", "campaign",
            requires: $"{TempModules.Require(ruleset, "^0.1.0")}, {TempModules.Require("placeholder-art", "^0.1.0")}");
        modules.Write("tale/campaign.json", """
            {
              "type": "campaign",
              "id": "tale",
              "name": "Tale",
              "start": { "area": "hall", "entry": "in" },
              "party": { "min": 1, "max": 2 }
            }
            """);
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");
        return campaign;
    }
}
