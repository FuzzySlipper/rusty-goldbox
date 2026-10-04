using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using RustyGoldbox.Game;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class PerceptionTests
{
    [Fact]
    public void TextViewsRejectEmptyOrDuplicateModesAtModuleLoad()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, "fifth-srd", "int_save");
        modules.Write("tale/mirror.json", """
            { "type": "event", "id": "mirror", "kind": "text", "text": "Room", "views": [
              { "mode": "truth", "text": "one" },
              { "mode": "truth", "text": "two" }
            ] }
            """);

        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Contains(set.Diagnostics, problem => problem.Rule == "event.views" && problem.JsonPath == "$.views[1].mode");
    }

    [Fact]
    public void PerceptionResolvesEachMemberOnceAndPersistsTheSelectedView()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, "fifth-srd", "int_save");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Character first = Create(set, engine.Random, "Ada", 8, intelligence: 8);
            Character second = Create(set, engine.Random, "Brom", 18, intelligence: 18);
            Definition tale = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!
                ?? throw new InvalidOperationException("campaign fixture missing");
            CampaignRunner runner = new(set.Rules, CampaignRunner.NewState(set.Rules, tale, [first, second], 7));

            Assert.Single(runner.Begin(engine.Random));
            List<PlayFact> facts = runner.Execute("forward", engine.Random);
            PerceptionFact[] resolved = facts.OfType<PerceptionFact>().ToArray();
            Assert.Equal(2, resolved.Length);
            Assert.All(resolved, fact => Assert.Single(fact.Rolls));
            Assert.Equal([first.Perception!.Mode, second.Perception!.Mode], resolved.Select(fact => fact.Mode));
            Assert.Equal([-1m, 4m], resolved.Select(fact => fact.Result.Bonus));
            Assert.All(resolved, fact => Assert.Equal(-1m, fact.Result.Modifier));
            Assert.Equal(1, runner.State.X);

            facts = runner.Execute("view 1", engine.Random);
            ViewFact selected = Assert.Single(facts.OfType<ViewFact>());
            Assert.Equal(first.Perception.Mode, selected.Mode);
            Assert.Equal(first.Perception.Mode == "truth" ? "The buried halls are real." : "The palace is a lie.", selected.Text);
            Assert.Equal("placeholder-art:altar", runner.State.Picture!.QualifiedId);
            int x = runner.State.X;
            int commands = runner.State.Commands;
            PerceptionState firstResult = first.Perception;

            facts = runner.Execute("view 1", engine.Random);
            Assert.DoesNotContain(facts, fact => fact is PerceptionFact);
            Assert.Equal(firstResult, first.Perception);
            Assert.Equal(x, runner.State.X);

            // Re-entering the same event scope after a room change keeps the
            // existing member results and spends no new dice.
            runner.Execute("back", engine.Random);
            facts = runner.Execute("forward", engine.Random);
            Assert.DoesNotContain(facts, fact => fact is PerceptionFact);
            Assert.Equal(firstResult, first.Perception);
            Assert.Equal(commands + 3, runner.State.Commands);

            string save = SaveFile.ToJson(runner.State, set);
            using JsonDocument json = JsonDocument.Parse(save);
            Assert.Equal("tale:mirror", json.RootElement.GetProperty("view_event").GetString());
            Assert.Equal("entry-1", json.RootElement.GetProperty("party")[0].GetProperty("perception").GetProperty("scope").GetString());
            List<ModuleDiagnostic> problems = [];
            CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(save), "perception-save", set, problems)!;
            Assert.Empty(problems);
            Assert.Equal(firstResult, restored.Party[0].Perception);
            Assert.Equal(second.Perception, restored.Party[1].Perception);
            Assert.Equal("tale:mirror", restored.ViewEvent!.QualifiedId);

            CampaignRunner resumed = new(set.Rules, restored);
            facts = resumed.Execute("view 2", engine.Random);
            ViewFact resumedView = Assert.Single(facts.OfType<ViewFact>());
            Assert.Equal(restored.Party[1].Perception!.Mode, resumedView.Mode);
            Assert.Equal(restored.Party[1].Perception!.Mode == "truth" ? "The buried halls are real." : "The palace is a lie.", resumedView.Text);
            Assert.Equal("placeholder-art:altar", restored.Picture!.QualifiedId);
        });
    }

    [Fact]
    public void AuthoredResetRerollsSameScopeAndClearsAnAbsentNpcAfterSave()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, "fifth-srd", "int_save", reset: true);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Character npcTemplate = Create(set, engine.Random, "Guide", 18);
            modules.Write("tale/guide.json", NpcFile.ToJson(npcTemplate, "guide"));
            modules.Write("tale/recruit.json", """{ "type": "event", "id": "recruit", "kind": "join", "npc": "guide" }""");
            modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 }, "intro": "recruit" }""");
            set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
            Assert.Empty(set.Diagnostics);

            Character player = Create(set, engine.Random, "Ada", 8);
            Definition tale = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)
                ?? throw new InvalidOperationException("campaign fixture missing");
            CampaignState state = CampaignRunner.NewState(set.Rules, tale, [player], 7);
            CampaignRunner runner = new(set.Rules, state);
            Assert.Contains(runner.Begin(engine.Random), fact => fact is PartyFact { Joined: true });
            Character guide = state.Party[1];
            PerceptionFact[] first = runner.Execute("forward", engine.Random).OfType<PerceptionFact>().ToArray();
            Assert.Equal(2, first.Length);
            Assert.NotNull(guide.Perception);

            string save = SaveFile.ToJson(state, set);
            List<ModuleDiagnostic> problems = [];
            CampaignState restored = SaveFile.Read(Encoding.UTF8.GetBytes(save), "reset-save", set, problems)!;
            Assert.Empty(problems);
            Character absentGuide = restored.Party[1];
            Assert.NotNull(absentGuide.Npc);
            restored.Party.Remove(absentGuide);
            restored.AbsentNpcs.Add(absentGuide);

            CampaignRunner resumed = new(set.Rules, restored);
            resumed.Execute("back", engine.Random);
            PerceptionFact[] reset = resumed.Execute("forward", engine.Random).OfType<PerceptionFact>().ToArray();
            Assert.Single(reset);
            Assert.Equal("entry-1", reset[0].Scope);
            Assert.Single(reset[0].Rolls);
            Assert.Null(absentGuide.Perception);
            Assert.Equal("entry-1", restored.Party[0].Perception!.Scope);
        });
    }

    [Fact]
    public void CliScriptSelectsAViewWithoutChangingTheRouteOrCampaignVariables()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, "fifth-srd", "int_save");
        modules.Write("tale/shared.json", """{ "type": "variable", "id": "shared", "value_type": "boolean", "initial": "false" }""");
        modules.Write("view.script", "forward\nlook\nview 1\nlook\n");
        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");
        string transcript = CliTranscript.Run(modules.Root,
            ["character", "new", "--module", campaign, "--modules", ".", "--modules", repositoryModules, "--class", "fighter", "--race", "human", "--name", "Ada", "--priority", "int,con,dex,wis,cha,str", "--feature", "soldier,savage_attacker,defense", "--seed", "1", "--out", "ada.json"],
            ["play", "--campaign", campaign, "--modules", ".", "--modules", repositoryModules, "--party", "ada.json", "--seed", "7", "--script", "view.script", "--save", "after.json"]);

        Golden.Verify("fixture-perception-view.txt", transcript);
        using JsonDocument save = JsonDocument.Parse(File.ReadAllText(Path.Combine(modules.Root, "after.json")));
        Assert.Equal("tale:hall", save.RootElement.GetProperty("area").GetString());
        Assert.Equal(1, save.RootElement.GetProperty("x").GetInt32());
        Assert.Equal(0, save.RootElement.GetProperty("y").GetInt32());
        Assert.Equal("east", save.RootElement.GetProperty("facing").GetString());
        Assert.False(save.RootElement.GetProperty("variables").GetProperty("campaign").GetProperty("shared").GetBoolean());
        Assert.Equal(2, transcript.Split("Hall [1, 0]", StringSplitOptions.None).Length - 1);
        Assert.Contains("Ada's ", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public void GameProjectionExposesPerMemberViewsAndKeepsSharedStateUntouched()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, "fifth-srd", "int_save");
        modules.Write("tale/shared.json", """{ "type": "variable", "id": "shared", "value_type": "boolean", "initial": "false" }""");
        using TempModules containers = new();
        string repositoryModules = Path.Combine(Rules.RepositoryRoot, "modules");
        string fifthBundle = EngineContentTests.Pack(Path.Combine(repositoryModules, "fifth-srd"), containers);
        string artBundle = EngineContentTests.Pack(Path.Combine(repositoryModules, "placeholder-art"), containers);
        string campaignBundle = Path.Combine(containers.Root, "tale.rpak");
        (int code, string output) = CampaignTests.Run(containers, "module", "pack", campaign, "--modules", repositoryModules, "--output", campaignBundle);
        Assert.True(code == 0, output);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            string[] bundles = [fifthBundle, artBundle, campaignBundle];
            GameSession session = new(new ModuleLibrary(_ => bundles
                .Select(path => ProductContentBundle.OpenContainer(engine.Content, path))
                .ToList()));
            session.Refresh();
            session.Open(Assert.Single(session.Campaigns).Bundle, 7);
            session.Roll(engine, "Ada", "human", "fighter", features: ["soldier", "savage_attacker", "defense"]);
            session.Begin(engine);
            session.Execute(engine, "forward");

            JsonObject projection = SessionProjection.Build(session, (_, asset) => $"url:{asset.QualifiedId}");
            JsonObject member = projection["party"]!.AsArray()[0]!.AsObject();
            string mode = member["perception"]!["mode"]!.GetValue<string>();
            Assert.Equal("entry-1", member["perception"]!["scope"]!.GetValue<string>());
            Assert.Equal(mode, member["view"]!["mode"]!.GetValue<string>());
            Assert.Equal("tale:mirror", projection["viewEvent"]!.GetValue<string>());
            Assert.Equal(2, projection["viewOptions"]!.AsArray().Count);
            Assert.Contains("view <member>", projection["commands"]!.GetValue<string>(), StringComparison.Ordinal);

            string map = projection["map"]!.GetValue<string>();
            Definition area = session.Runner!.State.Area;
            int x = session.Runner.State.X;
            int y = session.Runner.State.Y;
            Facing facing = session.Runner.State.Facing;
            var shared = session.Runner.State.Variables["shared"];
            HashSet<string> fired = session.Runner.State.Fired.ToHashSet(StringComparer.Ordinal);
            session.Execute(engine, "view 1");

            projection = SessionProjection.Build(session, (_, asset) => $"url:{asset.QualifiedId}");
            JsonObject selected = projection["view"]!.AsObject();
            Assert.Equal(1, selected["member"]!.GetValue<int>());
            Assert.Equal(mode, selected["mode"]!.GetValue<string>());
            Assert.Equal("The buried halls are real.", selected["text"]!.GetValue<string>());
            Assert.NotNull(selected["picture"]);
            Assert.Equal(mode, projection["party"]![0]!["view"]!["mode"]!.GetValue<string>());
            Assert.Equal(map, projection["map"]!.GetValue<string>());
            Assert.Same(area, session.Runner.State.Area);
            Assert.Equal(x, session.Runner.State.X);
            Assert.Equal(y, session.Runner.State.Y);
            Assert.Equal(facing, session.Runner.State.Facing);
            Assert.Equal(shared, session.Runner.State.Variables["shared"]);
            Assert.Equal(fired, session.Runner.State.Fired);
        });
    }

    [Fact]
    public void PerceptionUsesAnOriginalRulesetCheckShapeAndTextModesStayGeneric()
    {
        using TempModules modules = new();
        string campaign = Fixture(modules, "degrees", "mind_save");
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures"), Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            Character member = Create(set, engine.Random, "Wren", 16, "vanguard", "hillfolk", ["stonehide", "sentry", "hill_toughness", "shield_ward"]);
            Definition tale = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!
                ?? throw new InvalidOperationException("campaign fixture missing");
            CampaignRunner runner = new(set.Rules, CampaignRunner.NewState(set.Rules, tale, [member], 12));
            runner.Begin(engine.Random);
            List<PlayFact> facts = runner.Execute("forward", engine.Random);
            PerceptionFact perception = Assert.Single(facts.OfType<PerceptionFact>());
            Assert.Single(perception.Rolls);
            Assert.Contains(perception.Mode, new[] { "truth", "glamour" });
            Assert.Equal("entry-1", member.Perception!.Scope);
            Assert.Equal(2, runner.CurrentViews().Count);
            Assert.Contains(runner.CurrentViews(), view => view.Mode == "truth");
        });
    }

    private static Character Create(ModuleSet set, IRandomService random, string name, int constitution, string characterClass = "fighter", string race = "human", IReadOnlyList<string>? features = null, int intelligence = 10)
    {
        using Rng stream = random.CreateScoped(new ScopedRngCreateRequest(991, $"perception.character.{name}"));
        List<ModuleDiagnostic> problems = [];
        IReadOnlyList<string> choices = features ?? ["soldier", "savage_attacker", "defense"];
        Character? character = CharacterRules.Create(
            set.Rules!,
            Character.StampsOf(set),
            new CreationRequest(
                name,
                characterClass,
                race,
                Attributes: characterClass == "fighter"
                    ? new Dictionary<string, decimal> { ["str"] = 12, ["dex"] = 12, ["con"] = constitution, ["int"] = intelligence, ["wis"] = 10, ["cha"] = 10 }
                    : new Dictionary<string, decimal> { ["brawn"] = 12, ["finesse"] = 12, ["stamina"] = 12, ["intellect"] = constitution, ["insight"] = 12, ["presence"] = 12 },
                Features: choices,
                Boosts: characterClass == "fighter" ? null : ["insight", "intellect", "finesse", "stamina", "brawn", "presence", "insight", "finesse"]),
            new DiceRoller(random, stream),
            problems);
        Assert.True(character is not null, string.Join("; ", problems.Select(problem => problem.Message)));
        return character!;
    }

    private static string Fixture(TempModules modules, string ruleset, string check, bool reset = false)
    {
        string art = Path.Combine(Rules.RepositoryRoot, "modules", "placeholder-art");
        string campaign = modules.Module("tale", "campaign", requires: $"{Require(ruleset, "*")}, {Require("placeholder-art", "*")}");
        modules.Write("tale/hall.json", """
            {
              "type": "area", "id": "hall", "name": "Hall",
              "map": ["+--+--+--+", "|        |", "+--+--+--+"],
              "entries": { "in": { "at": [0, 0], "facing": "east" } },
              "cells": [ { "at": [1, 0], "event": "sense" } ]
            }
            """);
        modules.Write("tale/campaign.json", """
            { "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 4 } }
            """);
        string resetField = reset ? ", \"reset\": true" : string.Empty;
        modules.Write("tale/sense.json", $$"""
            { "type": "event", "id": "sense", "kind": "perception", "scope": "entry-1", "check": "{{check}}"{{resetField}}, "modifier": "-1", "success_mode": "truth", "failure_mode": "glamour", "next": "mirror" }
            """);
        modules.Write("tale/mirror.json", """
            {
              "type": "event", "id": "mirror", "kind": "text", "text": "The room waits.",
              "views": [
                { "mode": "truth", "text": "The buried halls are real.", "picture": "placeholder-art:altar" },
                { "mode": "glamour", "text": "The palace is a lie.", "picture": "placeholder-art:altar" }
              ]
            }
            """);
        if (ruleset == "degrees")
        {
            modules.Write("tale/mind_save.json", """
                { "type": "check", "id": "mind_save", "name": "Mind save", "roll": "1d20", "bonus": "self.intellect_mod", "target": "10", "succeeds": "at-least", "tiers": [] }
                """);
        }
        else if (ruleset == "fifth-srd")
        {
            modules.Write("tale/int_save.json", """
                { "type": "check", "id": "int_save", "name": "Intelligence extension check", "roll": "1d20", "bonus": "self.int_mod", "target": "10", "succeeds": "at-least", "tiers": [] }
                """);
        }

        // Keep this local variable as a reminder that the fixture's art is a
        // real installed module resolved beside the temporary campaign.
        Assert.True(Directory.Exists(art));
        return campaign;
    }
}
