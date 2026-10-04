using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

/// <summary>Campaign-level checks for a combat that remains owned by Core between commands.</summary>
public sealed class LiveCampaignCombatTests
{
    [Fact]
    public void ManualCampaignCombatSuspendsAndSaveRestoresTheSameBoundary()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 7);
        CampaignRunner runner = new(set.Rules, state)
        {
            DefaultCombatControl = CombatControlMode.Manual,
        };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            List<PlayFact> facts = runner.Begin(engine.Random);
            Assert.DoesNotContain(facts, fact => fact is FightFact);
            Assert.NotNull(state.PendingCombat);
            Assert.NotNull(state.PendingCombat!.Continuation.RandomScope);
            Assert.True(state.PendingCombat.Continuation.NextRandomKey >= 0);

            CombatObservation before = Assert.IsType<CombatObservation>(runner.ObserveCombat(engine.Random));
            Assert.NotEqual(CombatPhase.Ended, before.Phase);
            Assert.NotNull(before.PendingDecision);
            Assert.Equal(before.Combatants.Select(member => member.Id), state.PendingCombat!.Continuation.Combatants.Select(member => member.Id));

            string json = SaveFile.ToJson(state, set);
            Assert.Contains("\"pending_combat\"", json, StringComparison.Ordinal);
            Assert.Contains("\"continuation\"", json, StringComparison.Ordinal);

            List<ModuleDiagnostic> problems = [];
            CampaignState loaded = SaveFile.Read(Encoding.UTF8.GetBytes(json), "combat-save.json", set, problems)!;
            Assert.Empty(problems);
            Assert.NotNull(loaded.PendingCombat);

            CampaignRunner resumed = new(set.Rules, loaded)
            {
                DefaultCombatControl = CombatControlMode.Manual,
            };
            CombatObservation after = Assert.IsType<CombatObservation>(resumed.ObserveCombat(engine.Random));
            Assert.Equal(before.Phase, after.Phase);
            Assert.Equal(before.Round, after.Round);
            Assert.Equal(before.ActiveActorId, after.ActiveActorId);
            Assert.Equal(before.PendingDecision?.Id, after.PendingDecision?.Id);
            Assert.Equal(before.Combatants.Select(member => member.Id), after.Combatants.Select(member => member.Id));
            Assert.Equal(before.Combatants.Select(member => member.Position), after.Combatants.Select(member => member.Position));

            Assert.Equal(CombatDecisionKind.Action, before.PendingDecision!.Kind);
            CombatCommand.EndTurn endTurn = new(before.ActiveActorId!);
            CampaignCombatCommandResult uninterrupted = runner.SubmitCombat(endTurn, engine.Random);
            CampaignCombatCommandResult resumedResult = resumed.SubmitCombat(endTurn, engine.Random);
            Assert.Equal(uninterrupted.Accepted, resumedResult.Accepted);
            Assert.Equal(uninterrupted.Facts.Select(fact => fact.Describe()), resumedResult.Facts.Select(fact => fact.Describe()));
            Assert.Equal(uninterrupted.Observation.Phase, resumedResult.Observation.Phase);
            Assert.Equal(uninterrupted.Observation.Round, resumedResult.Observation.Round);
        });
    }

    [Fact]
    public void PendingFightMembersUseLiveDeploymentPositions()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 11);
        CampaignRunner runner = new(set.Rules, state)
        {
            DefaultCombatControl = CombatControlMode.Manual,
        };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            PendingCombatState pending = Assert.IsType<PendingCombatState>(state.PendingCombat);
            CombatObservation observation = Assert.IsType<CombatObservation>(runner.Combat);
            Dictionary<string, Cell?> live = observation.Combatants.ToDictionary(member => member.Id, member => member.Position, StringComparer.Ordinal);

            Assert.Equal(pending.Members.Count, live.Count);
            Assert.All(pending.Participants, participant => Assert.Contains(participant.Id, live.Keys));
            Assert.Equal(live.Values.Where(position => position is not null).Count(), live.Values.Where(position => position is not null).Distinct().Count());
            Assert.Equal(live.Values.Where(position => position is not null).OrderBy(position => position!.Value.X).ThenBy(position => position!.Value.Y),
                pending.Members.Where(member => member.Position is not null).Select(member => member.Position).OrderBy(position => position!.Value.X).ThenBy(position => position!.Value.Y));
        });
    }

    [Fact]
    public void ManualCampaignCombatSaveResumesTheOutcomeRewardAndChainedFightOnce()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 19);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            CombatObservation initial = Assert.IsType<CombatObservation>(runner.ObserveCombat(engine.Random));
            CombatCommand opening = CommandFor(initial);
            CampaignCombatCommandResult openingResult = runner.SubmitCombat(opening, engine.Random);
            Assert.True(openingResult.Accepted, openingResult.Reason);
            Assert.NotNull(state.PendingCombat);
            Assert.Equal(1, state.CombatSequence);

            string json = SaveFile.ToJson(state, set);
            List<ModuleDiagnostic> problems = [];
            CampaignState loaded = SaveFile.Read(Encoding.UTF8.GetBytes(json), "outcome-save.json", set, problems)!;
            Assert.Empty(problems);
            Assert.Equal(state.CombatSequence, loaded.CombatSequence);

            CampaignRunner resumed = new(set.Rules, loaded) { DefaultCombatControl = CombatControlMode.Manual };
            CombatObservation waiting = Assert.IsType<CombatObservation>(resumed.ObserveCombat(engine.Random));
            Assert.Equal(openingResult.Observation.PendingDecision?.Id, waiting.PendingDecision?.Id);

            List<PlayFact> uninterrupted = FinishCombatChain(runner, engine.Random);
            List<PlayFact> resumedFacts = FinishCombatChain(resumed, engine.Random);
            Assert.Equal(uninterrupted.Select(fact => fact.Describe()), resumedFacts.Select(fact => fact.Describe()));
            Assert.Equal(2, state.CombatSequence);
            Assert.Equal(2, loaded.CombatSequence);
            Assert.Null(state.PendingCombat);
            Assert.Null(loaded.PendingCombat);
            Assert.Equal(state.Party.Select(member => member.Experience), loaded.Party.Select(member => member.Experience));
            Assert.Equal(2, uninterrupted.OfType<FightFact>().Count());
            Assert.Equal(3, uninterrupted.OfType<ExperienceFact>().Count());
            Assert.Equal(6, uninterrupted.OfType<ExperienceFact>().SelectMany(fact => fact.Shares).Count());
            Assert.Single(uninterrupted.OfType<TextFact>(), fact => fact.Text == "Done.");
        });
    }

    [Fact]
    public void CampaignSaveRestoresPendingInterruptWithoutRerollingOrRepeatingInspection()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules, withGuardReaction: true);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 23);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            CombatObservation initial = Assert.IsType<CombatObservation>(runner.ObserveCombat(engine.Random));
            string reactor = Assert.Single(state.PendingCombat!.Participants, participant => participant.Side == 1).Id;
            CampaignCombatCommandResult controller = runner.SetCombatController(reactor, CombatControlMode.Manual, engine.Random);
            Assert.True(controller.Accepted, controller.Reason);
            CombatObservation action = Assert.IsType<CombatObservation>(runner.ObserveCombat(engine.Random));

            CombatCommand.UseAction attack = Assert.IsType<CombatCommand.UseAction>(CommandFor(action));
            CampaignCombatCommandResult hit = runner.SubmitCombat(attack, engine.Random);
            Assert.True(hit.Accepted, hit.Reason);
            Assert.Equal(CombatDecisionKind.Interrupt, hit.Observation.PendingDecision?.Kind);
            Assert.NotNull(state.PendingCombat!.Continuation.PendingInterrupt);

            // Text commands are inspection/refusal while combat is pending; they do not move the battle cursor.
            long nextKey = state.PendingCombat.Continuation.NextRandomKey;
            List<PlayFact> inspection = runner.Execute("forward", engine.Random);
            Assert.Single(inspection.OfType<RefusedFact>());
            Assert.Equal(nextKey, state.PendingCombat.Continuation.NextRandomKey);
            CombatObservation observed = Assert.IsType<CombatObservation>(runner.ObserveCombat(engine.Random));
            Assert.Equal(hit.Observation.PendingDecision?.Id, observed.PendingDecision?.Id);

            string json = SaveFile.ToJson(state, set);
            List<ModuleDiagnostic> problems = [];
            CampaignState loaded = SaveFile.Read(Encoding.UTF8.GetBytes(json), "interrupt-save.json", set, problems)!;
            Assert.Empty(problems);
            Assert.NotNull(loaded.PendingCombat!.Continuation.PendingInterrupt);

            CampaignRunner resumed = new(set.Rules, loaded) { DefaultCombatControl = CombatControlMode.Manual };
            CombatObservation restored = Assert.IsType<CombatObservation>(resumed.ObserveCombat(engine.Random));
            Assert.Equal(observed.PendingDecision?.Id, restored.PendingDecision?.Id);
            Assert.Equal(observed.PendingDecision?.Kind, restored.PendingDecision?.Kind);
            Assert.Equal(observed.PendingDecision?.Interrupt, restored.PendingDecision?.Interrupt);
            Assert.Equal(observed.PendingDecision?.Options?.Select(option => option.Id), restored.PendingDecision?.Options?.Select(option => option.Id));
            Assert.Equal(observed.Facts.Select(fact => fact.Describe()), restored.Facts.Select(fact => fact.Describe()));

            CombatCommand.Decide decline = new(observed.PendingDecision!.Id);
            CampaignCombatCommandResult left = runner.SubmitCombat(decline, engine.Random);
            CampaignCombatCommandResult right = resumed.SubmitCombat(decline, engine.Random);
            Assert.True(left.Accepted, left.Reason);
            Assert.True(right.Accepted, right.Reason);
            Assert.Equal(left.Facts.Select(fact => fact.Describe()), right.Facts.Select(fact => fact.Describe()));
            Assert.Equal(left.Observation.Facts.Select(fact => fact.Describe()), right.Observation.Facts.Select(fact => fact.Describe()));
        });
    }

    [Fact]
    public void MalformedCampaignSaveRejectsContinuationSideThatDisagreesWithItsSource()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 31);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            JsonObject save = JsonNode.Parse(SaveFile.ToJson(state, set))!.AsObject();
            JsonObject pending = save["pending_combat"]!.AsObject();
            JsonArray participants = pending["participants"]!.AsArray();
            JsonObject partySource = participants
                .Select(node => node!.AsObject())
                .First(node => node["side"]!.GetValue<int>() == 0);
            string id = partySource["id"]!.GetValue<string>();
            JsonArray combatants = pending["continuation"]!["Combatants"]!.AsArray();
            JsonObject continuation = combatants
                .Select(node => node!.AsObject())
                .Single(node => node["Id"]!.GetValue<string>() == id);
            continuation["Side"] = 1;

            List<ModuleDiagnostic> problems = [];
            Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(save.ToJsonString()), "bad-side-save.json", set, problems));
            Assert.Contains(problems, problem => problem.JsonPath is string path
                && path.Contains("$.pending_combat.continuation.Combatants[", StringComparison.Ordinal)
                && path.EndsWith(".Side", StringComparison.Ordinal)
                && problem.Message.Contains("participant source", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void MalformedCampaignSaveRejectsContinuationTurnIndexPastTurnOrder()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 37);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            JsonObject save = JsonNode.Parse(SaveFile.ToJson(state, set))!.AsObject();
            JsonObject continuation = save["pending_combat"]!["continuation"]!.AsObject();
            int orderLength = continuation["TurnOrder"]!.AsArray().Count;
            continuation["TurnIndex"] = orderLength + 1;

            List<ModuleDiagnostic> problems = [];
            Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(save.ToJsonString()), "bad-turn-index-save.json", set, problems));
            Assert.Contains(problems, problem =>
                problem.JsonPath == "$.pending_combat.continuation.TurnIndex"
                && problem.Message.Contains("TurnOrder length", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void MalformedCampaignSaveRejectsUnknownTookTurnId()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 41);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            JsonObject save = JsonNode.Parse(SaveFile.ToJson(state, set))!.AsObject();
            JsonArray tookTurns = save["pending_combat"]!["continuation"]!["TookTurns"]!.AsArray();
            int invalidIndex = tookTurns.Count;
            tookTurns.Add("missing-combatant");

            List<ModuleDiagnostic> problems = [];
            Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(save.ToJsonString()), "bad-took-turn-save.json", set, problems));
            Assert.Contains(problems, problem =>
                problem.JsonPath == $"$.pending_combat.continuation.TookTurns[{invalidIndex}]"
                && problem.Message.Contains("unknown combatant", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void MalformedCampaignSaveRejectsUnknownActiveActorId()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 43);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            JsonObject save = JsonNode.Parse(SaveFile.ToJson(state, set))!.AsObject();
            save["pending_combat"]!["continuation"]!["ActiveActorId"] = "missing-combatant";

            List<ModuleDiagnostic> problems = [];
            Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(save.ToJsonString()), "bad-active-actor-save.json", set, problems));
            Assert.Contains(problems, problem =>
                problem.JsonPath == "$.pending_combat.continuation.ActiveActorId"
                && problem.Message.Contains("unknown combatant", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void MalformedCampaignSaveRejectsNullContinuationCombatantId()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 45);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            JsonObject save = JsonNode.Parse(SaveFile.ToJson(state, set))!.AsObject();
            save["pending_combat"]!["continuation"]!["Combatants"]!.AsArray()[0]!["Id"] = null;

            List<ModuleDiagnostic> problems = [];
            Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(save.ToJsonString()), "bad-null-combatant-save.json", set, problems));
            Assert.Contains(problems, problem =>
                problem.JsonPath == "$.pending_combat.continuation.Combatants[0].Id"
                && problem.Message.Contains("nonempty ID", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void MalformedCampaignSaveRejectsMissingUnpreparedSpellCostQuote()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 49);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            JsonObject save = JsonNode.Parse(SaveFile.ToJson(state, set))!.AsObject();
            JsonObject continuation = save["pending_combat"]!["continuation"]!.AsObject();
            JsonObject action = continuation["PendingDecision"]!["Actions"]!.AsArray()[0]!.AsObject();
            action["SpellId"] = "classic:magic_missile";
            action["SpellCosts"] = null;

            List<ModuleDiagnostic> problems = [];
            Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(save.ToJsonString()), "bad-missing-spell-quote-save.json", set, problems));
            Assert.Contains(problems, problem =>
                problem.JsonPath?.EndsWith(".PendingDecision.Actions[0].SpellCosts", StringComparison.Ordinal) == true
                && problem.Message.Contains("committed cost quote", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void MalformedCampaignSaveRejectsSpellCostQuoteWithWrongKeys()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 53);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            JsonObject save = JsonNode.Parse(SaveFile.ToJson(state, set))!.AsObject();
            JsonObject continuation = save["pending_combat"]!["continuation"]!.AsObject();
            JsonObject action = continuation["PendingDecision"]!["Actions"]!.AsArray()[0]!.AsObject();
            action["SpellId"] = "classic:magic_missile";
            action["SpellCosts"] = new JsonObject { ["wrong_entry"] = 1 };

            List<ModuleDiagnostic> problems = [];
            Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(save.ToJsonString()), "bad-spell-quote-keys-save.json", set, problems));
            Assert.Contains(problems, problem =>
                problem.JsonPath?.EndsWith(".PendingDecision.Actions[0].SpellCosts.spells_1", StringComparison.Ordinal) == true
                && problem.Message.Contains("missing authored cost entry", StringComparison.Ordinal));
            Assert.Contains(problems, problem =>
                problem.JsonPath?.EndsWith(".PendingDecision.Actions[0].SpellCosts.wrong_entry", StringComparison.Ordinal) == true
                && problem.Message.Contains("no authored cost entry", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void MalformedCampaignSaveRejectsNegativeBehaviorStepIndex()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules, withBehavior: true);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 47);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            JsonObject save = JsonNode.Parse(SaveFile.ToJson(state, set))!.AsObject();
            JsonObject continuation = save["pending_combat"]!["continuation"]!.AsObject();
            string actorId = continuation["Combatants"]!.AsArray()[0]!["Id"]!.GetValue<string>();
            JsonObject behaviorState = new()
            {
                ["BehaviorId"] = "behavior-rules:dummy_behavior",
                ["RuleIndex"] = 0,
                ["StepIndex"] = 0,
                ["Committed"] = true,
            };
            continuation["BehaviorStates"] = new JsonObject { [actorId] = behaviorState };

            List<ModuleDiagnostic> validProblems = [];
            Assert.NotNull(SaveFile.Read(Encoding.UTF8.GetBytes(save.ToJsonString()), "valid-behavior-save.json", set, validProblems));
            Assert.Empty(validProblems);

            behaviorState["StepIndex"] = -1;
            continuation["BehaviorPendingActorId"] = actorId;
            continuation["BehaviorPendingId"] = "behavior-rules:dummy_behavior";
            continuation["BehaviorPendingRuleIndex"] = 1;
            continuation["BehaviorPendingStepIndex"] = 0;

            List<ModuleDiagnostic> problems = [];
            Assert.Null(SaveFile.Read(Encoding.UTF8.GetBytes(save.ToJsonString()), "bad-behavior-step-save.json", set, problems));
            Assert.Contains(problems, problem =>
                problem.JsonPath == $"$.pending_combat.continuation.BehaviorStates.{actorId}.StepIndex"
                && problem.Message.Contains("cannot be negative", StringComparison.Ordinal));
            Assert.Contains(problems, problem =>
                problem.JsonPath == "$.pending_combat.continuation.BehaviorPendingRuleIndex"
                && problem.Message.Contains("outside", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void CampaignSaveKeepsExplicitControllerPreferenceForTheNextFight()
    {
        using TempModules modules = new();
        string campaign = CampaignFixture(modules);
        ModuleSet set = ModuleLoader.Load(campaign, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = Party(modules, campaign, set);
        Definition campaignDefinition = set.Rules!.Find(DefinitionTypes.Campaign, "tale", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules, campaignDefinition, party, 29);
        CampaignRunner runner = new(set.Rules, state) { DefaultCombatControl = CombatControlMode.Manual };

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            runner.Begin(engine.Random);
            CombatObservation initial = Assert.IsType<CombatObservation>(runner.ObserveCombat(engine.Random));
            CombatantObservation preferenceActor = initial.Combatants
                .Where(member => member.Side == 0)
                .Single(member => member.Id != initial.ActiveActorId);

            CampaignCombatCommandResult changed = runner.SetCombatController(preferenceActor.Id, CombatControlMode.Automatic, engine.Random);
            Assert.True(changed.Accepted, changed.Reason);
            int partyIndex = state.PendingCombat!.Participants.Single(participant => participant.Id == preferenceActor.Id).PartyIndex!.Value;
            Assert.Equal(CombatControlMode.Automatic, state.Party[partyIndex].CombatControlPreference);

            string json = SaveFile.ToJson(state, set);
            List<ModuleDiagnostic> problems = [];
            CampaignState loaded = SaveFile.Read(Encoding.UTF8.GetBytes(json), "controller-preference-save.json", set, problems)!;
            Assert.Empty(problems);
            Assert.Equal(CombatControlMode.Automatic, loaded.Party[partyIndex].CombatControlPreference);

            CampaignRunner resumed = new(set.Rules, loaded) { DefaultCombatControl = CombatControlMode.Manual };
            CombatObservation restored = Assert.IsType<CombatObservation>(resumed.ObserveCombat(engine.Random));
            Assert.Equal(CombatControlMode.Automatic, restored.Combatants.Single(member => member.Id == preferenceActor.Id).Controller);

            ReachCombatSequence(runner, engine.Random, 2);
            ReachCombatSequence(resumed, engine.Random, 2);
            CombatObservation next = Assert.IsType<CombatObservation>(runner.Combat);
            CombatObservation resumedNext = Assert.IsType<CombatObservation>(resumed.Combat);
            Assert.Equal(2, state.CombatSequence);
            Assert.Equal(2, loaded.CombatSequence);
            Assert.Equal(CombatControlMode.Automatic, next.Combatants.Single(member => member.Id == preferenceActor.Id).Controller);
            Assert.Equal(CombatControlMode.Automatic, resumedNext.Combatants.Single(member => member.Id == preferenceActor.Id).Controller);
            Assert.Equal(next.PendingDecision?.Kind, resumedNext.PendingDecision?.Kind);
        });
    }

    private static List<PlayFact> FinishCombatChain(CampaignRunner runner, Rusty.Engine.IRandomService random)
    {
        List<PlayFact> facts = [];
        for (int steps = 0; runner.State.PendingCombat is not null; steps++)
        {
            Assert.True(steps < 100, "The campaign combat chain did not reach a terminal outcome.");
            CombatObservation observation = Assert.IsType<CombatObservation>(runner.ObserveCombat(random));
            CampaignCombatCommandResult result = runner.SubmitCombat(CommandFor(observation), random);
            Assert.True(result.Accepted, result.Reason);
            facts.AddRange(result.Facts);
        }

        return facts;
    }

    private static void ReachCombatSequence(CampaignRunner runner, Rusty.Engine.IRandomService random, long sequence)
    {
        for (int steps = 0; steps < 100 && runner.State.PendingCombat is not null && runner.State.CombatSequence < sequence; steps++)
        {
            CombatObservation observation = Assert.IsType<CombatObservation>(runner.ObserveCombat(random));
            CampaignCombatCommandResult result = runner.SubmitCombat(CommandFor(observation), random);
            Assert.True(result.Accepted, result.Reason);
        }

        Assert.Equal(sequence, runner.State.CombatSequence);
        Assert.NotNull(runner.State.PendingCombat);
    }

    private static CombatCommand CommandFor(CombatObservation observation)
    {
        CombatDecision decision = Assert.IsType<CombatDecision>(observation.PendingDecision);
        CombatantObservation actor = observation.Combatants.Single(member => member.Id == decision.ActorId);
        if (decision.Kind == CombatDecisionKind.Action)
        {
            CombatActionChoice? action = decision.Actions.FirstOrDefault(choice => choice.Targets.Any(target => target.Side != actor.Side && !target.Defeated));
            if (action is not null)
            {
                CombatTargetChoice target = action.Targets.First(target => target.Side != actor.Side && !target.Defeated);
                return new CombatCommand.UseAction(actor.Id, action.Id, [target.Id]);
            }
        }

        if (decision.Kind == CombatDecisionKind.Targets)
        {
            CombatActionChoice action = Assert.Single(decision.Actions);
            CombatTargetChoice target = Assert.Single(action.Targets, candidate => !candidate.Defeated);
            return new CombatCommand.UseAction(actor.Id, action.Id, [target.Id]);
        }

        return decision.Kind switch
        {
            CombatDecisionKind.Interrupt or CombatDecisionKind.PostRoll or CombatDecisionKind.Initiative => new CombatCommand.Decide(decision.Id),
            _ => new CombatCommand.EndTurn(actor.Id),
        };
    }

    internal static string CampaignFixture(TempModules modules, bool withGuardReaction = false, bool withBehavior = false)
    {
        string requires = $"{TempModules.Require("classic", "*")}, {TempModules.Require("placeholder-art", "*")} ";
        if (withBehavior)
        {
            modules.Module("behavior-rules", "extension", requires: TempModules.Require("classic", "*"));
            requires += $", {TempModules.Require("behavior-rules", "*")}";
        }

        string campaign = modules.Module("tale", "campaign", requires: requires);
        modules.Write("tale/campaign.json", """
            {
              "type": "campaign",
              "id": "tale",
              "name": "Live combat tale",
              "start": { "area": "hall", "entry": "in" },
              "party": { "min": 2, "max": 2 },
              "intro": "fight"
            }
            """);
        modules.Write("tale/hall.json", """
            {
              "type": "area",
              "id": "hall",
              "name": "Hall",
              "map": ["+--+--+--+--+", "|           |", "+--+--+--+--+"],
              "entries": { "in": { "at": [0, 0], "facing": "east" } }
            }
            """);
        string reactions = withGuardReaction ? ",\n              \"reactions\": [ \"guard_reaction\" ]" : "";
        string behavior = withBehavior ? ",\n              \"behavior\": \"behavior-rules:dummy_behavior\"" : "";
        modules.Write("tale/dummy.json", $$"""
            {
              "type": "monster",
              "id": "dummy",
              "name": "Dummy",
              "class": "classic:fighter",
              "level": 1,
              "tracks": { "classic:hit_points": "10" },
              "stats": { "ac": "20", "movement": "120", "str": "1", "size": "'medium'" }{{reactions}}{{behavior}},
              "actions": [],
              "xp": 10
            }
            """);
        if (withBehavior)
        {
            modules.Write("behavior-rules/behaviors/dummy_behavior.json", """
                {
                  "type": "combat-behavior",
                  "name": "Dummy behavior",
                  "id": "dummy_behavior",
                  "rules": [ { "steps": [ { "action": { "action": "classic:close" }, "target": "enemy" } ] } ]
                }
                """);
        }
        modules.Write("tale/guard.json", """
            {
              "type": "action",
              "id": "guard",
              "name": "Guard",
              "cost": { "reaction": 0 },
              "target": "self",
              "always": [ { "op": "reduce_damage", "amount": "2", "to": "self" } ]
            }
            """);
        modules.Write("tale/guard_reaction.json", """
            {
              "type": "reaction",
              "id": "guard_reaction",
              "name": "Guard",
              "trigger": "hit",
              "cost": { "reaction": 1 },
              "use": { "action": "guard" }
            }
            """);
        modules.Write("tale/dummy_encounter.json", """
            {
              "type": "encounter",
              "id": "dummy_encounter",
              "name": "Dummy encounter",
              "monsters": [ { "monster": "dummy", "count": "1" } ]
            }
            """);
        modules.Write("tale/fight.json", """
            {
              "type": "event",
              "id": "fight",
              "kind": "combat",
              "encounter": "tale:dummy_encounter",
              "party_start": [0, 0],
              "monsters_start": [1, 0],
              "on_win": "reward",
              "on_lose": "done",
              "on_draw": "done"
            }
            """);
        modules.Write("tale/reward.json", """
            {
              "type": "event",
              "id": "reward",
              "kind": "experience",
              "amount": "10",
              "each": true,
              "next": "fight2"
            }
            """);
        modules.Write("tale/fight2.json", """
            {
              "type": "event",
              "id": "fight2",
              "kind": "combat",
              "encounter": "tale:dummy_encounter",
              "party_start": [0, 0],
              "monsters_start": [1, 0],
              "on_win": "done",
              "on_lose": "done",
              "on_draw": "done"
            }
            """);
        modules.Write("tale/done.json", """
            { "type": "event", "id": "done", "kind": "text", "text": "Done." }
            """);
        return campaign;
    }

    private static List<Character> Party(TempModules modules, string campaign, ModuleSet set)
    {
        CampaignTests.WriteParty(modules, Rules.ClassicPath);
        List<Character> party = [];
        foreach (string name in new[] { "ada", "brom" })
        {
            List<ModuleDiagnostic> problems = [];
            Character character = CharacterFile.Read(Path.Combine(modules.Root, $"{name}.json"), set, problems)!;
            Assert.Empty(problems);
            party.Add(character);
        }

        return party;
    }
}
