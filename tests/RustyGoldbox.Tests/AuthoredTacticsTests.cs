using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

/// <summary>Original data-only examples for authored combat behavior and its module workflow.</summary>
public sealed class AuthoredTacticsTests
{
    private static string ModulesRoot => Path.Combine(Rules.RepositoryRoot, "modules");

    private static string FixturesRoot => Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures");

    [Fact]
    public void TacticalBestiaireLoadsProfilesFiguresAndACompanionPolicy()
    {
        string module = Path.Combine(ModulesRoot, "tactical-expedition");
        ModuleSet set = ModuleLoader.Load(module, [ModulesRoot]);

        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;

        Assert.Equal(
            ["dusklark", "glasswing", "mossward", "phase_watcher"],
            rules.OfType(DefinitionTypes.Monster)
                .Where(definition => definition.Module == "tactical-bestiaire")
                .Select(definition => definition.Id)
                .Order());

        foreach (Definition monster in rules.OfType(DefinitionTypes.Monster).Where(definition => definition.Module == "tactical-bestiaire"))
        {
            Definition behavior = rules.Reference(monster, "$.behavior");
            Assert.Equal(DefinitionTypes.CombatBehavior, behavior.Type);
            Assert.NotNull(rules.CombatBehaviorOf(behavior));
            Assert.True(rules.Figures.ContainsKey(monster), $"{monster.QualifiedId} needs a logical figure.");
            Assert.True(rules.Icons.ContainsKey(monster), $"{monster.QualifiedId} needs a presentation icon.");
        }

        Definition glasswing = rules.Find(DefinitionTypes.Monster, "tactical-bestiaire:glasswing", out _)!;
        Definition dusklark = rules.Find(DefinitionTypes.Monster, "tactical-bestiaire:dusklark", out _)!;
        Assert.Equal(
            "classic:missile_attack",
            glasswing.Json.GetProperty("actions").EnumerateArray()
                .Single(use => use.GetProperty("action").GetString() == "classic:missile_attack")
                .GetProperty("action").GetString());
        Assert.Equal(
            "classic:missile_attack",
            dusklark.Json.GetProperty("actions").EnumerateArray()
                .Single(use => use.GetProperty("action").GetString() == "classic:missile_attack")
                .GetProperty("action").GetString());

        CombatBehaviorProfile glasswingPolicy = Profile(rules, "tactical-bestiaire:skirmisher");
        CombatBehaviorProfile dusklarkPolicy = Profile(rules, "tactical-bestiaire:duelist");
        Assert.Equal("tactical-bestiaire:withdraw", glasswingPolicy.Rules[0].Steps[0].Action.QualifiedId);
        Assert.Equal("classic:missile_attack", glasswingPolicy.Rules[0].Steps[1].Action.QualifiedId);
        Assert.Equal(CombatBehaviorCommitment.Plan, glasswingPolicy.Rules[0].Commitment);
        Assert.Equal("classic:missile_attack", dusklarkPolicy.Rules[0].Steps[0].Action.QualifiedId);
        Assert.Equal("target.hit_points", glasswingPolicy.Rules[0].Steps[1].TargetScore!.Text);
        Assert.Equal("0 - target.hit_points", dusklarkPolicy.Rules[0].Steps[0].TargetScore!.Text);

        Definition ivy = rules.Find(DefinitionTypes.Npc, "tactical-expedition:ivy", out _)!;
        Assert.Equal("tactical-bestiaire:warder", rules.Reference(ivy, "$.behavior").QualifiedId);
        Assert.Equal("automatic", ivy.Json.GetProperty("control").GetString());

        CombatBehaviorProfile warderPolicy = Profile(rules, "tactical-bestiaire:warder");
        Assert.Equal("tactical-bestiaire:approach_ally", warderPolicy.Rules[0].Steps[0].Action.QualifiedId);
        Assert.Equal("classic:spell_heal", warderPolicy.Rules[0].Steps[1].Action.QualifiedId);
        Assert.Equal("classic:cure_light_wounds", warderPolicy.Rules[0].Steps[1].Spell!.QualifiedId);
        Assert.Equal(CombatBehaviorTarget.HurtAlly, warderPolicy.Rules[0].Steps[0].Target);
    }

    [Fact]
    public void OriginalZonesFixtureUsesASecondFieldAndThreeActionBehaviorShape()
    {
        string module = Path.Combine(FixturesRoot, "tactical-zones");
        ModuleSet set = ModuleLoader.Load(module, [FixturesRoot]);

        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "tactical-zones:zones", out _)!;
        Assert.Equal("zones", combat.Json.GetProperty("field").GetProperty("mode").GetString());
        Assert.Equal(3, combat.Json.GetProperty("budget")[0].GetProperty("per_turn").GetInt32());
        Definition advance = rules.Find(DefinitionTypes.Action, "tactical-zones:advance", out _)!;
        Assert.Equal(1, advance.Json.GetProperty("cost").GetProperty("standard").GetInt32());
        Assert.Contains(
            combat.Json.GetProperty("actions").EnumerateArray(),
            use => use.GetProperty("action").GetString() == "tactical-zones:advance");

        CombatBehaviorProfile profile = Profile(rules, "tactical-zones:three_beats");
        Assert.Equal(3, profile.Rules.Single().Steps.Count);
        Assert.All(profile.Rules.Single().Steps, step =>
        {
            Assert.Equal("ascend:melee_attack", step.Action.QualifiedId);
            Assert.Equal(CombatBehaviorTarget.Enemy, step.Target);
        });

        Definition duelist = rules.Find(DefinitionTypes.Monster, "tactical-zones:zone_duelist", out _)!;
        Assert.Equal("tactical-zones:three_beats", rules.Reference(duelist, "$.behavior").QualifiedId);
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 2)]
    public void OriginalZonesCombatFeatOffersAHitReactionAcrossSaveBoundary(bool accept, decimal expectedDamage)
    {
        string module = Path.Combine(FixturesRoot, "tactical-zones");
        ModuleSet set = ModuleLoader.Load(module, [FixturesRoot]);

        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "tactical-zones:zones", out _)!;
        Definition feature = rules.Find(DefinitionTypes.Feature, "tactical-zones:brace_guard", out _)!;
        Definition striker = rules.Find(DefinitionTypes.Monster, "tactical-zones:zone_striker", out _)!;
        Assert.Equal("combat feat", feature.Json.GetProperty("kind").GetString());
        Assert.Equal(
            1,
            combat.Json.GetProperty("budget").EnumerateArray()
                .Single(entry => entry.GetProperty("id").GetString() == "reaction")
                .GetProperty("per_turn").GetInt32());

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(9346, $"authored-zones-reaction-{accept}"));
            DiceRoller dice = new(engine.Random, stream);
            List<ModuleDiagnostic> problems = [];
            Character guardCharacter = CharacterRules.Create(
                rules,
                Character.StampsOf(set),
                new CreationRequest(
                    "Brace",
                    "ascend:warrior",
                    "ascend:folk",
                    Attributes: new Dictionary<string, decimal>
                    {
                        ["might"] = 12,
                        ["grace"] = 12,
                        ["grit"] = 12,
                        ["wit"] = 10,
                    },
                    Features: ["ascend:iron_will", "tactical-zones:brace_guard"]),
                dice,
                problems)!;
            Assert.Empty(problems);
            Assert.NotNull(guardCharacter);
            Assert.Contains(guardCharacter!.Features, selected => selected == feature);

            Evaluator evaluator = new(rules, dice);
            Combatant guard = Combatant.FromCharacter(rules, guardCharacter);
            Combatant zoneStriker = Combatant.FromMonster(rules, striker, "Zone striker", evaluator);
            Assert.Equal("tactical-zones:brace_on_hit", Assert.Single(guard.Reactions).Reaction.QualifiedId);

            CombatSetup setup = new([new Cell(0, 0), new Cell(1, 0)]);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Party", [guard]),
                new CombatSide("Striker", [zoneStriker]),
            ], dice, setup: setup);
            Assert.True(runner.SetController(guard.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(zoneStriker.Id, CombatControlMode.Manual));

            CombatObservation observation = runner.Start(1);
            if (observation.PendingDecision?.ActorId == guard.Id)
            {
                CombatCommandResult ended = runner.Submit(new CombatCommand.EndTurn(guard.Id));
                Assert.True(ended.Accepted, ended.Reason);
                observation = ended.Observation;
            }

            Assert.Equal(zoneStriker.Id, observation.PendingDecision?.ActorId);
            CombatDecision actionDecision = Assert.IsType<CombatDecision>(observation.PendingDecision);
            CombatActionChoice tap = Assert.Single(actionDecision.Actions, choice => choice.ActionId == "tactical-zones:training_tap");
            CombatCommandResult hit = runner.Submit(new CombatCommand.UseAction(zoneStriker.Id, tap.Id, [guard.Id]));
            Assert.True(hit.Accepted, hit.Reason);
            CombatDecision interrupt = Assert.IsType<CombatDecision>(hit.Observation.PendingDecision);
            Assert.Equal(CombatDecisionKind.Interrupt, interrupt.Kind);
            Assert.Equal(guard.Id, interrupt.ActorId);
            CombatDecisionOption option = Assert.Single(interrupt.Options!);

            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Party", [Combatant.FromCharacter(rules, guardCharacter)]),
                new CombatSide("Striker", [Combatant.FromMonster(rules, striker, "Zone striker", restoredEvaluator)]),
            ], dice, saved, setup: setup);

            CombatCommand.Decide answer = new(interrupt.Id, accept ? option.Id : null);
            CombatCommandResult result = restored.Submit(answer);
            Assert.True(result.Accepted, result.Reason);
            DamageFact damage = Assert.Single(result.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == guard.Name);
            Assert.Equal(expectedDamage, damage.Amount);
            Assert.Equal(accept ? 1 : 0, result.Observation.Facts.OfType<ReactionFact>().Count());
        });
    }

    [Fact]
    public void ClassicGridCombatExecutesDistinctAuthoredTactics()
    {
        ModuleSet set = ModuleLoader.Load(Path.Combine(ModulesRoot, "tactical-expedition"), [ModulesRoot]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "tactical-bestiaire:tactical_grid", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "tactical-expedition:gallery", out _)!;
        Definition glasswingDefinition = rules.Find(DefinitionTypes.Monster, "tactical-bestiaire:glasswing", out _)!;
        Definition dusklarkDefinition = rules.Find(DefinitionTypes.Monster, "tactical-bestiaire:dusklark", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(9344, "authored-grid-combat"));
            DiceRoller dice = new(engine.Random, stream);
            Evaluator evaluator = new(rules, dice);
            Combatant glasswing = Combatant.FromMonster(rules, glasswingDefinition, "Glasswing", evaluator);
            Combatant dusklark = Combatant.FromMonster(rules, dusklarkDefinition, "Dusklark", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Skirmisher", [glasswing]),
                new CombatSide("Duelist", [dusklark]),
            ], dice, encounter, new CombatSetup([new Cell(0, 0), new Cell(2, 0)]));
            Definition track = rules.Reference(combat, "$.track");
            glasswing.Creature.Track(track.Id).Current = 40;
            glasswing.Creature.Track(track.Id).Max = 40;
            dusklark.Creature.Track(track.Id).Current = 40;
            dusklark.Creature.Track(track.Id).Max = 40;
            Assert.True(runner.SetController(glasswing.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(dusklark.Id, CombatControlMode.Manual));

            CombatField field = CombatField.Of(combat, encounter)!;
            CombatBehaviorController controller = new(rules, combat, field);
            controller.Register(runner.Sides.SelectMany(side => side.Members));
            CombatObservation observation = runner.Start(1);
            Dictionary<string, CombatBehaviorProposal> proposals = [];

            for (int turn = 0; turn < 2; turn++)
            {
                Assert.Equal(CombatPhase.AwaitingAction, observation.Phase);
                Combatant actor = runner.Sides.SelectMany(side => side.Members).Single(member => member.Id == observation.ActiveActorId);
                actor.Controller = CombatControlMode.Automatic;
                CombatBehaviorProposal? proposed = controller.Propose(actor, observation);
                Assert.NotNull(proposed);
                CombatBehaviorProposal proposal = proposed!;
                Assert.NotNull(proposal.Command);
                CombatCommandResult result = runner.Submit(proposal.Command!);
                Assert.True(result.Accepted, result.Reason);
                controller.Commit(proposal);
                proposals.Add(actor.Id, proposal);
                observation = result.Observation;
            }

            CombatCommand.UseAction glasswingCommand = Assert.IsType<CombatCommand.UseAction>(proposals[glasswing.Id].Command);
            CombatCommand.UseAction dusklarkCommand = Assert.IsType<CombatCommand.UseAction>(proposals[dusklark.Id].Command);
            Assert.Contains("tactical-bestiaire:withdraw", glasswingCommand.ActionId, StringComparison.Ordinal);
            Assert.Contains("classic:missile_attack", dusklarkCommand.ActionId, StringComparison.Ordinal);
            Assert.Equal("tactical-bestiaire:skirmisher", proposals[glasswing.Id].Trace?.BehaviorId);
            Assert.Equal("tactical-bestiaire", proposals[glasswing.Id].Trace?.BehaviorModule);
            string behaviorFile = (proposals[glasswing.Id].Trace?.BehaviorFile ?? string.Empty).Replace('\\', '/');
            Assert.EndsWith("behaviors/skirmisher.json", behaviorFile, StringComparison.Ordinal);
            Assert.Equal("tactical-bestiaire:duelist", proposals[dusklark.Id].Trace?.BehaviorId);
            Assert.Contains(observation.Facts.OfType<MoveFact>(), fact => fact.Who == glasswing.Name);
            Assert.Contains(observation.Facts.OfType<ActionFact>(), fact => fact.Who == dusklark.Name);
        });
    }

    [Fact]
    public void ClassicGridCombatUsesProfilesOnTheAutomaticRunnerPath()
    {
        ModuleSet set = ModuleLoader.Load(Path.Combine(ModulesRoot, "tactical-expedition"), [ModulesRoot]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "tactical-bestiaire:tactical_grid", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "tactical-expedition:gallery", out _)!;
        Definition glasswingDefinition = rules.Find(DefinitionTypes.Monster, "tactical-bestiaire:glasswing", out _)!;
        Definition dusklarkDefinition = rules.Find(DefinitionTypes.Monster, "tactical-bestiaire:dusklark", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(9344, "authored-grid-automatic"));
            DiceRoller dice = new(engine.Random, stream);
            Evaluator evaluator = new(rules, dice);
            Combatant glasswing = Combatant.FromMonster(rules, glasswingDefinition, "Glasswing", evaluator);
            Combatant dusklark = Combatant.FromMonster(rules, dusklarkDefinition, "Dusklark", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Skirmisher", [glasswing]),
                new CombatSide("Duelist", [dusklark]),
            ], dice, encounter, new CombatSetup([new Cell(0, 0), new Cell(2, 0)]));
            Definition track = rules.Reference(combat, "$.track");
            glasswing.Creature.Track(track.Id).Current = 40;
            glasswing.Creature.Track(track.Id).Max = 40;
            dusklark.Creature.Track(track.Id).Current = 40;
            dusklark.Creature.Track(track.Id).Max = 40;

            CombatObservation observation = runner.Start(1);
            Assert.Equal(CombatPhase.Ended, observation.Phase);
            Assert.Contains(observation.Facts.OfType<ActionFact>(), fact => fact.Who == glasswing.Name && fact.Action == "Withdraw");
            Assert.Contains(observation.Facts.OfType<ActionFact>(), fact => fact.Who == dusklark.Name && fact.Action == "Needle volley");
            Assert.Contains(observation.Facts.OfType<MoveFact>(), fact => fact.Who == glasswing.Name);
        });
    }

    [Fact]
    public void AutomaticBehaviorPlanStateRoundTripsThroughRunnerContinuation()
    {
        ModuleSet set = ModuleLoader.Load(Path.Combine(ModulesRoot, "tactical-expedition"), [ModulesRoot]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "tactical-bestiaire:tactical_grid", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "tactical-expedition:gallery", out _)!;
        Definition glasswingDefinition = rules.Find(DefinitionTypes.Monster, "tactical-bestiaire:glasswing", out _)!;
        Definition dusklarkDefinition = rules.Find(DefinitionTypes.Monster, "tactical-bestiaire:dusklark", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(9344, "authored-grid-restore"));
            DiceRoller dice = new(engine.Random, stream);
            Evaluator evaluator = new(rules, dice);
            Combatant glasswing = Combatant.FromMonster(rules, glasswingDefinition, "Glasswing", evaluator);
            Combatant dusklark = Combatant.FromMonster(rules, dusklarkDefinition, "Dusklark", evaluator);
            IReadOnlyList<CombatSide> sides =
            [
                new CombatSide("Skirmisher", [glasswing]),
                new CombatSide("Duelist", [dusklark]),
            ];
            CombatSetup setup = new([new Cell(0, 0), new Cell(2, 0)]);
            CombatRunner runner = CombatRunner.Create(rules, combat, sides, dice, encounter, setup);
            Definition track = rules.Reference(combat, "$.track");
            glasswing.Creature.Track(track.Id).Current = 40;
            glasswing.Creature.Track(track.Id).Max = 40;
            dusklark.Creature.Track(track.Id).Current = 40;
            dusklark.Creature.Track(track.Id).Max = 40;

            runner.Start(1);
            CombatContinuationState snapshot = runner.Capture();
            CombatContinuationState fromJson = CombatContinuationState.FromJson(CombatContinuationState.ToJson(snapshot));
            Assert.Contains(glasswing.Id, fromJson.BehaviorStates.Keys);
            Assert.Equal(1, fromJson.BehaviorStates[glasswing.Id].StepIndex);

            CombatRunner restored = CombatRunner.Restore(rules, combat, sides, dice, fromJson, encounter, setup);
            Assert.Equal(
                fromJson.BehaviorStates[glasswing.Id].StepIndex,
                restored.BehaviorController.States[glasswing.Id].StepIndex);
            Assert.Equal(CombatPhase.Ended, restored.Observe().Phase);
        });
    }

    [Fact]
    public void OriginalZonesCombatExecutesAThreeActionAuthoredPlan()
    {
        ModuleSet set = ModuleLoader.Load(Path.Combine(FixturesRoot, "tactical-zones"), [FixturesRoot]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "tactical-zones:zones", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "tactical-zones:zones", out _)!;
        Definition monster = rules.Find(DefinitionTypes.Monster, "tactical-zones:zone_duelist", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(9344, "authored-zones-combat"));
            DiceRoller dice = new(engine.Random, stream);
            Evaluator evaluator = new(rules, dice);
            Combatant first = Combatant.FromMonster(rules, monster, "Zone Alpha", evaluator);
            Combatant second = Combatant.FromMonster(rules, monster, "Zone Beta", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Alpha", [first]),
                new CombatSide("Beta", [second]),
            ], dice, encounter, new CombatSetup([new Cell(0, 0), new Cell(0, 0)]));
            Definition track = rules.Reference(combat, "$.track");
            first.Creature.Track(track.Id).Current = 100;
            first.Creature.Track(track.Id).Max = 100;
            second.Creature.Track(track.Id).Current = 100;
            second.Creature.Track(track.Id).Max = 100;
            Assert.True(runner.SetController(first.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(second.Id, CombatControlMode.Manual));

            CombatField field = CombatField.Of(combat, encounter)!;
            Assert.True(field.Zones);
            Assert.Equal(new Cell(0, 0), first.Creature.Position);
            Assert.Equal(new Cell(0, 0), second.Creature.Position);
            CombatBehaviorController controller = new(rules, combat, field);
            controller.Register(runner.Sides.SelectMany(side => side.Members));
            CombatObservation observation = runner.Start(1);
            Combatant actor = runner.Sides.SelectMany(side => side.Members).Single(member => member.Id == observation.ActiveActorId);
            actor.Controller = CombatControlMode.Automatic;

            for (int step = 0; step < 3; step++)
            {
                Assert.Equal(CombatPhase.AwaitingAction, observation.Phase);
                CombatBehaviorProposal? proposed = controller.Propose(actor, observation);
                Assert.NotNull(proposed);
                CombatBehaviorProposal proposal = proposed!;
                Assert.Contains("ascend:melee_attack", Assert.IsType<CombatCommand.UseAction>(proposal.Command).ActionId, StringComparison.Ordinal);
                CombatCommandResult result = runner.Submit(proposal.Command!);
                Assert.True(result.Accepted, result.Reason);
                controller.Commit(proposal);
                observation = result.Observation;
            }

            Assert.Equal(3, observation.Facts.OfType<ActionFact>().Count(fact => fact.Who == actor.Name));
            Assert.Equal("tactical-zones:three_beats", controller.ProfileFor(actor)?.Definition.QualifiedId);
            Assert.Empty(controller.States);
        });
    }

    [Fact]
    public void OriginalZonesAutomaticRunnerUsesTheThreeActionPlanAndPreservesTraceOptIn()
    {
        ModuleSet set = ModuleLoader.Load(Path.Combine(FixturesRoot, "tactical-zones"), [FixturesRoot]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "tactical-zones:zones", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "tactical-zones:zones", out _)!;
        Definition monster = rules.Find(DefinitionTypes.Monster, "tactical-zones:zone_duelist", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            static (CombatObservation Observation, string Facts) Run(
                RuleSet rules,
                Definition combat,
                Definition encounter,
                Definition monster,
                IEngineContext engine,
                bool collect)
            {
                using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(9344, "authored-zones-trace-compare"));
                DiceRoller dice = new(engine.Random, stream);
                Evaluator evaluator = new(rules, dice);
                Combatant first = Combatant.FromMonster(rules, monster, "Zone Alpha", evaluator);
                Combatant second = Combatant.FromMonster(rules, monster, "Zone Beta", evaluator);
                CombatRunner runner = CombatRunner.Create(rules, combat,
                [
                    new CombatSide("Alpha", [first]),
                    new CombatSide("Beta", [second]),
                ], dice, encounter, new CombatSetup([new Cell(0, 0), new Cell(0, 0)]));
                Definition track = rules.Reference(combat, "$.track");
                first.Creature.Track(track.Id).Current = 100;
                first.Creature.Track(track.Id).Max = 100;
                second.Creature.Track(track.Id).Current = 100;
                second.Creature.Track(track.Id).Max = 100;
                runner.CollectBehaviorTraces = collect;
                CombatObservation observation = runner.Start(1);
                string facts = string.Join("\n", observation.Facts.Select(fact => $"{fact.Kind}|{fact.Describe()}|{string.Join(',', fact.Rolls)}"));
                return (observation, facts);
            }

            (CombatObservation withoutTrace, string withoutFacts) = Run(rules, combat, encounter, monster, engine, collect: false);
            (CombatObservation withTrace, string withFacts) = Run(rules, combat, encounter, monster, engine, collect: true);
            Assert.Equal(CombatPhase.Ended, withTrace.Phase);
            Assert.Equal(withoutFacts, withFacts);
            Assert.Empty(withoutTrace.BehaviorTraces);
            Assert.Equal(6, withTrace.BehaviorTraces.Count);
            Assert.All(withTrace.BehaviorTraces, trace => Assert.True(trace.Round > 0));
            Assert.Equal(3, withTrace.Facts.OfType<ActionFact>().Count(fact => fact.Who == "Zone Alpha"));
            Assert.Equal(3, withTrace.Facts.OfType<ActionFact>().Count(fact => fact.Who == "Zone Beta"));
            Assert.Equal(0, withTrace.Combatants.Single(member => member.Name == "Zone Alpha").Budget["standard"]);
            Assert.Equal(0, withTrace.Combatants.Single(member => member.Name == "Zone Beta").Budget["standard"]);
        });
    }

    [Fact]
    public void AutomaticRunnerUsesAuthoredRetreatFallbackAndTerminatesUnsupportedAction()
    {
        using TempModules scratch = new();
        string module = scratch.Module("retreat", "extension", requires: TempModules.Require("classic", "*"));
        scratch.Write("retreat/behaviors/retreat.json", """
            {
              "type": "combat-behavior",
              "id": "retreat",
              "name": "Retreat when unavailable",
              "fallback": "flee",
              "rules": [
                {
                  "steps": [
                    { "action": { "action": "classic:melee_attack", "damage": "1d6" }, "target": "enemy" }
                  ]
                }
              ]
            }
            """);

        ModuleSet set = ModuleLoader.Load(module, [scratch.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        Definition combat = rules.Find(DefinitionTypes.Combat, "classic:standard", out _)!;
        Definition close = rules.Find(DefinitionTypes.Action, "classic:close", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(9344, "authored-retreat"));
            DiceRoller dice = new(engine.Random, stream);
            Creature retreatCreature = new("Retreater");
            retreatCreature.Values["max_hit_points"] = 20;
            retreatCreature.Track("hit_points").Current = 20;
            retreatCreature.Track("hit_points").Max = 20;
            Combatant retreating = new("Retreater", retreatCreature,
                [new UseOption(close, "Close", new Dictionary<string, CompiledExpression>())], "retreater")
            {
                Side = 0,
                Controller = CombatControlMode.Automatic,
            };
            Creature targetCreature = new("Target");
            targetCreature.Values["max_hit_points"] = 20;
            targetCreature.Track("hit_points").Current = 20;
            targetCreature.Track("hit_points").Max = 20;
            Combatant target = new("Target", targetCreature, [], "target")
            {
                Side = 1,
                Controller = CombatControlMode.Automatic,
            };
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Party", [retreating]),
                new CombatSide("Enemy", [target]),
            ], dice);
            runner.BehaviorController.Assign(retreating.Id, profile);
            runner.CollectBehaviorTraces = true;

            CombatObservation observation = runner.Start(2);
            Assert.Equal(CombatPhase.Ended, observation.Phase);
            Assert.True(retreating.Escaped, $"phase={observation.Phase}; facts={string.Join(" || ", observation.Facts.Select(fact => fact.Describe()))}; traces={string.Join(" || ", observation.BehaviorTraces.Select(trace => $"{trace.ActorId}:{trace.Fallback}:{trace.Reason}"))}");
            Assert.Equal(1, observation.Winner);
            CombatBehaviorTrace trace = Assert.Single(observation.BehaviorTraces);
            Assert.Equal(CombatBehaviorFallback.Flee, trace.Fallback);
            Assert.Contains(trace.Alternatives, alternative => alternative.Status == "unavailable");
        });
    }

    [Fact]
    public void OriginalZonesCommonAdvanceIsAReachableMovementChoice()
    {
        ModuleSet set = ModuleLoader.Load(Path.Combine(FixturesRoot, "tactical-zones"), [FixturesRoot]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "tactical-zones:zones", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "tactical-zones:zones", out _)!;
        Definition monster = rules.Find(DefinitionTypes.Monster, "tactical-zones:zone_duelist", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(9344, "authored-zones-movement"));
            DiceRoller dice = new(engine.Random, stream);
            Evaluator evaluator = new(rules, dice);
            Combatant first = Combatant.FromMonster(rules, monster, "Zone Alpha", evaluator);
            Combatant second = Combatant.FromMonster(rules, monster, "Zone Beta", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Alpha", [first]),
                new CombatSide("Beta", [second]),
            ], dice, encounter, new CombatSetup([new Cell(0, 0), new Cell(3, 0)]));
            Assert.True(runner.SetController(first.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(second.Id, CombatControlMode.Manual));

            CombatObservation observation = runner.Start(1);
            Assert.Equal(CombatPhase.AwaitingAction, observation.Phase);
            Assert.NotNull(observation.PendingDecision);
            Assert.Contains(
                observation.PendingDecision!.Actions,
                choice => choice.ActionId == "tactical-zones:advance" && choice.Moves.Count > 0);
        });
    }

    [Fact]
    public void TacticalCampaignDeploysTwelveMembersAndSelectsIvyWarder()
    {
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Path.Combine(ModulesRoot, "classic"));
        ModuleSet set = ModuleLoader.Load(Path.Combine(ModulesRoot, "tactical-expedition"), [ModulesRoot]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = ReadParty(set, scratch, 11);
        Definition campaign = set.Rules!.Find(DefinitionTypes.Campaign, "tactical-expedition:tactical_expedition", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            CampaignState state = CampaignRunner.NewState(set.Rules!, campaign, party, 9344);
            CampaignRunner runner = new(set.Rules!, state);
            runner.Begin(engine.Random);
            runner.Execute("choose 1", engine.Random);
            Assert.Equal(12, state.Party.Count);
            Character ivy = Assert.Single(state.Party, character => character.Npc is not null);
            Combatant companion = Combatant.FromCharacter(set.Rules!, ivy);
            Definition combat = set.Rules!.Find(DefinitionTypes.Combat, "tactical-bestiaire:tactical_grid", out _)!;
            CombatBehaviorController controller = new(set.Rules!, combat, CombatField.Of(combat));
            controller.Register([companion]);
            Assert.Equal("tactical-bestiaire:warder", controller.ProfileFor(companion)?.Definition.QualifiedId);

            List<PlayFact> fightFacts = runner.Execute("forward", engine.Random);
            FightFact fight = Assert.Single(fightFacts.OfType<FightFact>());
            IReadOnlyList<FightMember> partyMembers = fight.Members.Where(member => member.Side == 0).ToList();
            Assert.Equal(12, partyMembers.Count);
            Assert.All(partyMembers, member => Assert.NotNull(member.Position));
            Assert.Equal(partyMembers.Count, partyMembers.Select(member => member.Position).Distinct().Count());
        });
    }

    [Fact]
    public void TacticalCampaignAutomaticProfilesAndControllerSwitchPreserveIvyResources()
    {
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Path.Combine(ModulesRoot, "classic"));
        ModuleSet set = ModuleLoader.Load(Path.Combine(ModulesRoot, "tactical-expedition"), [ModulesRoot]);
        Assert.Empty(set.Diagnostics);
        List<Character> party = ReadParty(set, scratch, 11);
        Definition campaign = set.Rules!.Find(DefinitionTypes.Campaign, "tactical-expedition:tactical_expedition", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            CampaignState state = CampaignRunner.NewState(set.Rules!, campaign, party, 9344);
            CampaignRunner runner = new(set.Rules!, state)
            {
                DefaultCombatControl = CombatControlMode.Manual,
            };
            runner.Begin(engine.Random);
            runner.Execute("choose 1", engine.Random);
            Character ivy = Assert.Single(state.Party, character => character.Npc is not null);
            decimal? spellsBefore = ivy.Tracks["spells_1"].Current;

            runner.Execute("forward", engine.Random);
            Assert.NotNull(state.PendingCombat);
            CombatObservation waiting = Assert.IsType<CombatObservation>(runner.Combat);
            Assert.Equal(CombatPhase.AwaitingAction, waiting.Phase);
            CombatantObservation ivyView = Assert.Single(waiting.Combatants, member => member.Name == ivy.Name);
            Assert.Equal(CombatControlMode.Automatic, ivyView.Controller);
            Assert.Equal(spellsBefore, ivyView.Tracks["spells_1"]);

            CampaignCombatCommandResult manualResult = runner.SetCombatController(ivyView.Id, CombatControlMode.Manual, engine.Random);
            Assert.True(manualResult.Accepted, manualResult.Reason);
            CombatantObservation ivyManual = Assert.Single(manualResult.Observation.Combatants, member => member.Id == ivyView.Id);
            Assert.Equal(CombatControlMode.Manual, ivyManual.Controller);
            Assert.Equal(spellsBefore, ivyManual.Tracks["spells_1"]);

            CampaignCombatCommandResult switchResult = runner.SetCombatController(ivyView.Id, CombatControlMode.Automatic, engine.Random);
            Assert.True(switchResult.Accepted, switchResult.Reason);
            Assert.Equal(CombatControlMode.Automatic, switchResult.Observation.Combatants.Single(member => member.Id == ivyView.Id).Controller);
            Assert.Equal(spellsBefore, switchResult.Observation.Combatants.Single(member => member.Id == ivyView.Id).Tracks["spells_1"]);

            List<PlayFact> facts = [.. switchResult.Facts];
            while (state.PendingCombat is not null)
            {
                CombatObservation observation = Assert.IsType<CombatObservation>(runner.Combat);
                List<string> manualParty = observation.Combatants
                    .Where(member => member.Side == 0 && member.Controller == CombatControlMode.Manual && !member.Defeated)
                    .Select(member => member.Id)
                    .ToList();
                Assert.NotEmpty(manualParty);
                foreach (string actorId in manualParty)
                {
                    CampaignCombatCommandResult result = runner.SetCombatController(actorId, CombatControlMode.Automatic, engine.Random);
                    Assert.True(result.Accepted, result.Reason);
                    facts.AddRange(result.Facts);
                    if (state.PendingCombat is null)
                    {
                        break;
                    }
                }
            }

            FightFact fight = Assert.Single(facts.OfType<FightFact>());
            Assert.Equal(FightOutcome.Won, fight.Outcome);
            Assert.Contains(fight.Facts.OfType<ActionFact>(), fact => fact.Who == "Glasswing" && fact.Action == "Withdraw");
            Assert.Contains(fight.Facts.OfType<ActionFact>(), fact => fact.Who == "Dusklark" && fact.Action == "Needle volley");
        });
    }

    [Fact]
    public void TacticalCampaignPackedContinuationReachesACombatOutcome()
    {
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Path.Combine(ModulesRoot, "classic"));
        ModuleSet source = ModuleLoader.Load(Path.Combine(ModulesRoot, "tactical-expedition"), [ModulesRoot]);
        Assert.Empty(source.Diagnostics);
        List<Character> party = ReadParty(source, scratch, 11);
        Definition campaign = source.Rules!.Find(DefinitionTypes.Campaign, "tactical-expedition:tactical_expedition", out _)!;
        string pendingSave;

        using (EngineTestHost sourceHost = EngineTestHost.Create())
        {
            pendingSave = sourceHost.Call(engine =>
            {
                CampaignState state = CampaignRunner.NewState(source.Rules!, campaign, party, 9344);
                CampaignRunner runner = new(source.Rules!, state)
                {
                    DefaultCombatControl = CombatControlMode.Manual,
                };
                runner.Begin(engine.Random);
                runner.Execute("choose 1", engine.Random);
                runner.Execute("forward", engine.Random);
                Assert.NotNull(state.PendingCombat);
                return SaveFile.ToJson(state, source);
            });
        }

        string library = Path.Combine(scratch.Root, "library");
        string[] ids = ["classic", "placeholder-art", "tactical-bestiaire", "tactical-expedition"];
        List<string> containers = [];
        foreach (string id in ids)
        {
            string output = Path.Combine(library, $"{id}-0.1.0.rpak");
            (int code, string printed) = CampaignTests.Run(scratch, "module", "pack", Path.Combine(ModulesRoot, id), "--modules", ModulesRoot, "--output", output);
            Assert.True(code == 0, printed);
            containers.Add(output);
        }

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            List<ProductContentBundle> bundles = containers
                .Select(path => ProductContentBundle.OpenContainer(engine.Content, path))
                .ToList();
            try
            {
                List<ModuleSource> sources = bundles.Select(bundle => (ModuleSource)new BundleModuleSource(bundle)).ToList();
                ModuleSet installed = ModuleLoader.Load(sources[^1], sources, containers, "Install the tactical module containers.");
                Assert.Empty(installed.Diagnostics);
                List<ModuleDiagnostic> problems = [];
                CampaignState? state = SaveFile.Read(System.Text.Encoding.UTF8.GetBytes(pendingSave), "packed-continuation", installed, problems);
                Assert.Empty(problems);
                Assert.NotNull(state);
                CampaignRunner runner = new(installed.Rules!, state!);
                Assert.NotNull(runner.ObserveCombat(engine.Random));

                List<PlayFact> facts = [];
                while (state!.PendingCombat is not null)
                {
                    CombatObservation observation = Assert.IsType<CombatObservation>(runner.Combat);
                    List<string> manualParty = observation.Combatants
                        .Where(member => member.Side == 0 && member.Controller == CombatControlMode.Manual && !member.Defeated)
                        .Select(member => member.Id)
                        .ToList();
                    Assert.NotEmpty(manualParty);
                    foreach (string actorId in manualParty)
                    {
                        CampaignCombatCommandResult result = runner.SetCombatController(actorId, CombatControlMode.Automatic, engine.Random);
                        Assert.True(result.Accepted, result.Reason);
                        facts.AddRange(result.Facts);
                        if (state.PendingCombat is null)
                        {
                            break;
                        }
                    }
                }

                FightFact fight = Assert.Single(facts.OfType<FightFact>());
                Assert.Equal(FightOutcome.Won, fight.Outcome);
                Assert.Contains(fight.Facts, fact => fact.Kind == "action");
            }
            finally
            {
                bundles.ForEach(bundle => bundle.Dispose());
            }
        });
    }

    [Fact]
    public void TacticalCampaignPacksAndPlaysFromInstalledContainers()
    {
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Path.Combine(ModulesRoot, "classic"));
        string library = Path.Combine(scratch.Root, "library");
        string[] ids = ["classic", "placeholder-art", "tactical-bestiaire", "tactical-expedition"];
        List<string> containers = [];
        foreach (string id in ids)
        {
            string output = Path.Combine(library, $"{id}-0.1.0.rpak");
            (int code, string printed) = CampaignTests.Run(scratch, "module", "pack", Path.Combine(ModulesRoot, id), "--modules", ModulesRoot, "--output", output);
            Assert.True(code == 0, printed);
            containers.Add(output);
        }

        ModuleSet fromDirectories = ModuleLoader.Load(Path.Combine(ModulesRoot, "tactical-expedition"), [ModulesRoot]);
        Assert.Empty(fromDirectories.Diagnostics);

        using EngineTestHost host = EngineTestHost.Create();
        string fromContainers = host.Call(engine =>
        {
            List<ProductContentBundle> bundles = containers
                .Select(path => ProductContentBundle.OpenContainer(engine.Content, path))
                .ToList();
            try
            {
                List<ModuleSource> sources = bundles.Select(bundle => (ModuleSource)new BundleModuleSource(bundle)).ToList();
                ModuleSet set = ModuleLoader.Load(sources[^1], sources, containers, "Install the tactical module containers.");
                Assert.Empty(set.Diagnostics);
                return PlayInitial(set, scratch, engine);
            }
            finally
            {
                bundles.ForEach(bundle => bundle.Dispose());
            }
        });

        string directoryPlay = host.Call(engine => PlayInitial(fromDirectories, scratch, engine));
        Assert.Equal(directoryPlay, fromContainers);
    }

    private static CombatBehaviorProfile Profile(RuleSet rules, string qualifiedId)
    {
        Definition behavior = rules.Find(DefinitionTypes.CombatBehavior, qualifiedId, out _)!;
        return rules.CombatBehaviorOf(behavior)!;
    }

    private static string PlayInitial(ModuleSet set, TempModules scratch, IEngineContext engine)
    {
        List<Character> party = ReadParty(set, scratch, 12);

        Definition campaign = set.Rules!.Find(DefinitionTypes.Campaign, "tactical-expedition:tactical_expedition", out _)!;
        CampaignState state = CampaignRunner.NewState(set.Rules!, campaign, party, 17);
        CampaignRunner runner = new(set.Rules!, state);
        runner.Begin(engine.Random);
        runner.Execute("status", engine.Random);
        return SaveFile.ToJson(state, set);
    }

    private static List<Character> ReadParty(ModuleSet set, TempModules scratch, int count)
    {
        List<Character> party = [];
        for (int index = 0; index < count; index++)
        {
            string file = index % 2 == 0 ? "ada.json" : "brom.json";
            List<ModuleDiagnostic> problems = [];
            Character? character = CharacterFile.Read(Path.Combine(scratch.Root, file), set, problems);
            Assert.Empty(problems);
            Assert.NotNull(character);
            if (index >= 2)
            {
                character!.Name = $"Expeditioner {index + 1:00}";
            }

            party.Add(character!);
        }

        return party;
    }
}
