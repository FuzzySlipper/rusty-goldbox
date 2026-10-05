using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Game;

namespace RustyGoldbox.Tests;

/// <summary>Checks that the ordinary Game surface keeps a large live party addressable at a save boundary.</summary>
public sealed class LiveGameCombatTests
{
    [Fact]
    public void ChainedLiveFightResetsFactCursorAndKeepsTheNextFightCoreOwned()
    {
        using TempModules modules = new();
        string campaign = LiveCampaignCombatTests.CampaignFixture(modules);
        using TempModules scratch = new();
        string[] containers =
        [
            EngineContentTests.Pack(Rules.ClassicPath, scratch),
            EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", "placeholder-art"), scratch),
            PackCampaign(campaign, modules, scratch),
        ];

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            GameSession session = new(new ModuleLibrary(_ => containers
                .Select(path => ProductContentBundle.OpenContainer(engine.Content, path))
                .ToList()));
            session.Refresh();
            Run(session, engine, JsonSerializer.Serialize(new
            {
                action = "open",
                campaign = Assert.Single(session.Campaigns).Bundle,
                seed = "41",
            }));
            for (int attempt = 0; session.Party.Count < 2 && attempt < 100; attempt++)
            {
                int member = session.Party.Count + 1;
                Run(session, engine, JsonSerializer.Serialize(new
                {
                    action = "roll",
                    name = $"Member {member}",
                    race = "classic:human",
                    @class = "classic:fighter",
                }));
            }

            Assert.Equal(2, session.Party.Count);
            Run(session, engine, "{ \"action\": \"equip\", \"member\": 0, \"item\": \"classic:long_sword\" }");
            Run(session, engine, "{ \"action\": \"equip\", \"member\": 1, \"item\": \"classic:long_sword\" }");
            Run(session, engine, "{ \"action\": \"begin\" }");
            Assert.Equal(Screen.Combat, session.Screen);
            Assert.Equal("tale:fight", session.Runner!.State.PendingCombat!.Event.QualifiedId);

            bool reachedSecond = false;
            for (int step = 0; session.Runner.State.PendingCombat is not null && step < 300; step++)
            {
                CombatObservation observation = Assert.IsType<CombatObservation>(session.Combat);
                CombatDecision decision = Assert.IsType<CombatDecision>(observation.PendingDecision);
                if (session.Runner.State.CombatSequence == 2)
                {
                    reachedSecond = true;
                    Assert.Equal("tale:fight2", session.Runner.State.PendingCombat.Event.QualifiedId);
                    Assert.All(observation.Facts, fact => Assert.Contains(fact.Describe(), session.Log));
                    break;
                }

                if (decision.Kind is CombatDecisionKind.Interrupt or CombatDecisionKind.PostRoll)
                {
                    Run(session, engine, JsonSerializer.Serialize(new
                    {
                        action = "combat-decide",
                        decision = decision.Id,
                    }));
                    continue;
                }

                if (decision.Kind == CombatDecisionKind.Initiative)
                {
                    Run(session, engine, JsonSerializer.Serialize(new
                    {
                        action = "combat-decide",
                        decision = decision.Id,
                        option = decision.Options?.FirstOrDefault()?.Id,
                    }));
                    continue;
                }

                if (decision.Kind == CombatDecisionKind.Action)
                {
                    CombatantObservation actor = observation.Combatants.Single(member => member.Id == decision.ActorId);
                    CombatActionChoice? action = decision.Actions.FirstOrDefault(choice =>
                        choice.Targets.Any(target => target.Side != actor.Side && !target.Defeated));
                    if (action is CombatActionChoice attack)
                    {
                        CombatTargetChoice target = attack.Targets.First(candidate => candidate.Side != actor.Side && !candidate.Defeated);
                        CombatMoveChoice? move = attack.Moves.FirstOrDefault();
                        Run(session, engine, JsonSerializer.Serialize(new
                        {
                            action = "combat-action",
                            actor = decision.ActorId,
                            choice = attack.Id,
                            targets = new[] { target.Id },
                            path = move?.Path.Select(cell => new { x = cell.X, y = cell.Y }),
                        }));
                        continue;
                    }
                }

                Run(session, engine, JsonSerializer.Serialize(new
                {
                    action = "combat-end-turn",
                    actor = decision.ActorId,
                }));
            }

            Assert.True(reachedSecond, "The first live fight did not chain into its authored second fight.");
        });
    }

    [Fact]
    public void GamePayloadAndCliScriptKeepTheSamePackagedCombatContinuation()
    {
        using TempModules scratch = new();
        string campaign = Path.Combine(Rules.RepositoryRoot, "modules", "tactical-expedition");
        string moduleLibrary = Path.Combine(Rules.RepositoryRoot, "modules");
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        string beforePath = Path.Combine(scratch.Root, "bridge-before.json");
        string setupScript = Path.Combine(scratch.Root, "bridge-setup.script");
        File.WriteAllText(setupScript, "choose 2\nforward\n");

        (int setupCode, string setupOutput) = CampaignTests.Run(scratch,
            "play", "--campaign", campaign, "--modules", moduleLibrary,
            "--party", "ada.json,brom.json", "--seed", "31", "--combat-control", "manual",
            "--script", setupScript, "--save", "bridge-before.json", "--json");
        Assert.Equal(0, setupCode);
        Assert.True(File.Exists(beforePath), setupOutput);

        string[] moduleIds = ["classic", "placeholder-art", "tactical-bestiaire", "tactical-expedition"];
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions
        {
            PersistenceRoot = Path.Combine(scratch.Root, "persistence"),
        });

        (string Payload, CombatCommand.UseAction Command) bridge = host.Call(engine =>
        {
            List<string> containers = moduleIds
                .Select(id => EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
                .ToList();
            using SaveSlots slots = new(engine);
            slots.Write("bridge-before", File.ReadAllText(beforePath));
            GameSession session = new(new ModuleLibrary(_ => containers
                .Select(path => ProductContentBundle.OpenContainer(engine.Content, path))
                .ToList()));
            session.Refresh();
            Run(session, engine, "{ \"action\": \"load\", \"slot\": \"bridge-before\" }");
            Assert.Equal(Screen.Combat, session.Screen);

            CombatObservation observation = Assert.IsType<CombatObservation>(session.Combat);
            CombatDecision decision = Assert.IsType<CombatDecision>(observation.PendingDecision);
            CombatActionChoice action = decision.Actions.First(choice => choice.Targets.Count > 0);
            CombatTargetChoice target = action.Targets.First(candidate => !candidate.Defeated);
            CombatMoveChoice? move = action.Moves.FirstOrDefault();
            CombatCommand.UseAction command = new(
                decision.ActorId,
                action.Id,
                [target.Id],
                move?.Path);
            JsonObject payload = new()
            {
                ["action"] = "combat-action",
                ["actor"] = decision.ActorId,
                ["choice"] = action.Id,
                ["targets"] = new JsonArray(target.Id),
            };
            if (move is not null)
            {
                payload["path"] = new JsonArray(move.Path
                    .Select(cell => (JsonNode)new JsonObject { ["x"] = cell.X, ["y"] = cell.Y })
                    .ToArray());
            }

            return (payload.ToJsonString(), command);
        });

        string actionScript = Path.Combine(scratch.Root, "bridge-action.script");
        string cliAction = $"combat action {CliWord(bridge.Command.ActorId)} {CliWord(bridge.Command.ActionId)}"
            + string.Concat(bridge.Command.TargetIds.Select(target => $" --target {CliWord(target)}"));
        if (bridge.Command.Path is { Count: > 0 } path)
        {
            cliAction += $" --path {string.Join(';', path.Select(cell => $"{cell.X},{cell.Y}"))}";
        }

        File.WriteAllText(actionScript, cliAction + Environment.NewLine);
        string afterPath = Path.Combine(scratch.Root, "bridge-after.json");
        (int actionCode, string actionOutput) = CampaignTests.Run(scratch,
            "play", "--campaign", campaign, "--modules", moduleLibrary,
            "--load", "bridge-before.json", "--script", actionScript,
            "--save", "bridge-after.json", "--json");
        Assert.Equal(0, actionCode);
        Assert.True(File.Exists(afterPath), actionOutput);

        host.Call(engine =>
        {
            List<string> containers = moduleIds
                .Select(id => EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
                .ToList();
            using SaveSlots slots = new(engine);
            slots.Write("bridge-before", File.ReadAllText(beforePath));
            GameSession session = new(new ModuleLibrary(_ => containers
                .Select(path => ProductContentBundle.OpenContainer(engine.Content, path))
                .ToList()));
            session.Refresh();
            Run(session, engine, "{ \"action\": \"load\", \"slot\": \"bridge-before\" }");
            Run(session, engine, bridge.Payload);

            Assert.Empty(session.Notes);
            Assert.Equal(File.ReadAllText(afterPath), SaveFile.ToJson(session.Runner!.State, session.Set!));
            Assert.NotNull(session.Combat);
            using JsonDocument cli = JsonDocument.Parse(actionOutput);
            Assert.Contains(cli.RootElement.GetProperty("transcript").EnumerateArray(), step =>
                step.TryGetProperty("command", out JsonElement command)
                && command.GetString() == cliAction);
        });
    }

    [Fact]
    public void LargePartyLiveProjectionKeepsStableMemberIdsAndControllerOverrideAcrossSave()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions
        {
            PersistenceRoot = Path.Combine(scratch.Root, "persistence"),
        });
        host.Call(engine =>
        {
            string[] moduleIds = ["classic", "placeholder-art", "tactical-bestiaire", "tactical-expedition"];
            List<string> containers = moduleIds
                .Select(id => EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
                .ToList();
            GameSession session = new(new ModuleLibrary(_ => containers
                .Select(path => ProductContentBundle.OpenContainer(engine.Content, path))
                .ToList()));
            session.Refresh();
            Run(session, engine, JsonSerializer.Serialize(new
            {
                action = "open",
                campaign = Assert.Single(session.Campaigns).Bundle,
                seed = "31",
            }));

            for (int attempt = 0; session.Party.Count < 12 && attempt < 100; attempt++)
            {
                int member = session.Party.Count + 1;
                Run(session, engine, JsonSerializer.Serialize(new
                {
                    action = "roll",
                    name = $"Member {member}",
                    race = "classic:human",
                    @class = "classic:fighter",
                }));
            }

            Assert.Equal(12, session.Party.Count);
            Run(session, engine, """{ "action": "begin" }""");
            Run(session, engine, """{ "action": "play", "command": "choose 2" }""");
            Run(session, engine, """{ "action": "play", "command": "forward" }""");
            Assert.Equal(Screen.Combat, session.Screen);

            JsonObject Fight() => SessionProjection.Build(session)["fight"]!.AsObject();
            JsonObject initial = Fight();
            JsonArray members = initial["members"]!.AsArray();
            List<JsonNode?> party = members.Where(member => member!["side"]!.GetValue<int>() == 0).ToList();
            Assert.Equal(12, party.Count);
            Assert.Equal(members.Count, members.Select(member => member!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
            Assert.All(members, member => Assert.NotNull(member!["value"]));
            JsonObject member12 = party.Single(member => member!["id"]!.GetValue<string>() == "side-1-member-12")!.AsObject();
            Assert.Equal("Member 12", member12["name"]!.GetValue<string>());
            Assert.Equal("manual", member12["controller"]!.GetValue<string>());

            JsonObject decision = initial["decision"]!.AsObject();
            string activeActor = decision["actorId"]!.GetValue<string>();
            string overrideActor = activeActor == member12["id"]!.GetValue<string>()
                ? "side-1-member-11"
                : member12["id"]!.GetValue<string>();
            Run(session, engine, JsonSerializer.Serialize(new
            {
                action = "combat-control",
                actor = overrideActor,
                mode = "auto",
            }));
            Assert.Equal("automatic", Fight()["members"]!.AsArray()
                .Single(member => member!["id"]!.GetValue<string>() == overrideActor)!["controller"]!.GetValue<string>());

            Run(session, engine, """{ "action": "save", "slot": "large-live" }""");
            Run(session, engine, """{ "action": "load", "slot": "large-live" }""");
            Assert.Equal(Screen.Combat, session.Screen);
            JsonObject restored = Fight();
            Assert.Equal("automatic", restored["members"]!.AsArray()
                .Single(member => member!["id"]!.GetValue<string>() == overrideActor)!["controller"]!.GetValue<string>());
        });
    }

    private static void Run(GameSession session, IEngineContext engine, string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        GameCommands.Run(session, engine, document.RootElement);
    }

    private static string CliWord(string value)
    {
        return value.Any(char.IsWhiteSpace) ? $"'{value}'" : value;
    }

    private static string PackCampaign(string campaign, TempModules modules, TempModules scratch)
    {
        string output = Path.Combine(scratch.Root, "tale.rpak");
        (int code, string printed) = CampaignTests.Run(scratch,
            "module", "pack", campaign,
            "--modules", Path.Combine(Rules.RepositoryRoot, "modules"),
            "--modules", modules.Root,
            "--output", output);
        Assert.Equal(0, code);
        return output;
    }
}
