using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

public sealed class TacticalPlanTests
{
    [Fact]
    public void UsesAnAuthoredMovementActionBeforeAnOutOfRangeStep()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/sequence.json", """
            {
              "type": "combat-behavior",
              "id": "sequence",
              "name": "Close and strike",
              "rules": [
                {
                  "priority": 4,
                  "steps": [
                    {
                      "destination": { "kind": "within", "distance": 1 },
                      "action": { "action": "classic:melee_attack", "damage": "1d6" },
                      "target": "enemy"
                    }
                  ]
                }
              ]
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        Definition attack = rules.Find(DefinitionTypes.Action, "classic:melee_attack", out _)!;
        Definition close = rules.Find(DefinitionTypes.Action, "classic:close", out _)!;

        Creature actorCreature = CreatureWithHitPoints("actor", 10);
        Combatant actor = new("Actor", actorCreature,
            [
                new UseOption(attack, "strike", profile.Rules[0].Steps[0].Parameters),
                new UseOption(close, "Close", new Dictionary<string, CompiledExpression>()),
            ],
            "actor")
        {
            Side = 0,
            Controller = CombatControlMode.Automatic,
        };
        Creature targetCreature = CreatureWithHitPoints("target", 10);
        Combatant target = new("Target", targetCreature, [], "target") { Side = 1 };
        actorCreature.Position = new Cell(0, 0);
        targetCreature.Position = new Cell(3, 0);

        Definition combat = rules.Find(DefinitionTypes.Combat, "classic:standard", out _)!;
        CombatBehaviorController controller = new(rules, combat, CombatField.Of(combat));
        controller.Assign(actor.Id, profile);
        controller.Register([actor, target]);

        CombatActionChoice closeChoice = ActionChoice(close, target, [
            new CombatMoveChoice(new Cell(2, 0), [new Cell(1, 0), new Cell(2, 0)], 2),
        ]) with { Id = "close-choice", ActionId = close.QualifiedId };
        CombatBehaviorProposal? movementProposal = controller.Propose(actor, Observation(actor.Id, closeChoice));
        Assert.NotNull(movementProposal);
        CombatBehaviorProposal movement = movementProposal;
        Assert.True(movement.MovementOnly);
        CombatCommand.UseAction movementCommand = Assert.IsType<CombatCommand.UseAction>(movement.Command);
        Assert.Equal(closeChoice.Id, movementCommand.ActionId);
        Assert.Equal([target.Id], movementCommand.TargetIds);
        Assert.Equal([new Cell(1, 0), new Cell(2, 0)], movementCommand.Path);
        Assert.Equal(profile.Definition.QualifiedId, movement.Trace?.BehaviorId);
        Assert.Equal("tactics", movement.Trace?.BehaviorModule);
        Assert.EndsWith("sequence.json", movement.Trace?.BehaviorFile, StringComparison.Ordinal);
        Assert.Equal(0, movement.Trace?.RuleIndex);
        Assert.Equal(0, movement.Trace?.StepIndex);
        Assert.Equal("selected", Assert.Single(movement.Trace!.Alternatives).Status);

        controller.Commit(movement);
        Assert.True(controller.States[actor.Id].Committed);
        actorCreature.Position = new Cell(2, 0);

        CombatActionChoice attackChoice = ActionChoice(attack, target, []);
        CombatBehaviorProposal? attackProposal = controller.Propose(actor, Observation(actor.Id, attackChoice));
        Assert.NotNull(attackProposal);
        CombatBehaviorProposal attackStep = attackProposal;
        Assert.False(attackStep.MovementOnly);
        CombatCommand.UseAction attackCommand = Assert.IsType<CombatCommand.UseAction>(attackStep.Command);
        Assert.Equal(attackChoice.Id, attackCommand.ActionId);
        Assert.Equal([target.Id], attackCommand.TargetIds);
        controller.Commit(attackStep);
        Assert.Empty(controller.States);
    }

    [Fact]
    public void ProposesLegalMoveAndActionThenAdvancesACommittedSequence()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/sequence.json", """
            {
              "type": "combat-behavior",
              "id": "sequence",
              "name": "Close and strike",
              "parameters": { "reach": 1 },
              "rules": [
                {
                  "when": "combat.distance >= 3 and self.hit_points > 0",
                  "priority": 4,
                  "commit": "plan",
                  "steps": [
                    {
                      "destination": { "kind": "within", "distance": "behavior.reach" },
                      "action": { "action": "classic:close" },
                      "target": "enemy"
                    },
                    {
                      "action": { "action": "classic:melee_attack", "damage": "1d6" },
                      "target": "enemy"
                    }
                  ]
                }
              ],
              "fallback": "end-turn"
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        Definition action = rules.Find(DefinitionTypes.Action, "classic:melee_attack", out _)!;
        Definition close = rules.Find(DefinitionTypes.Action, "classic:close", out _)!;

        Creature actorCreature = CreatureWithHitPoints("actor", 10);
        Combatant actor = new("Actor", actorCreature,
            [
                new UseOption(close, "close", profile.Rules[0].Steps[0].Parameters),
                new UseOption(action, "strike", profile.Rules[0].Steps[1].Parameters),
            ],
            "actor")
        {
            Side = 0,
            Controller = CombatControlMode.Automatic,
        };
        Creature targetCreature = CreatureWithHitPoints("target", 10);
        Combatant target = new("Target", targetCreature, [], "target") { Side = 1 };
        actorCreature.Position = new Cell(0, 0);
        targetCreature.Position = new Cell(3, 0);

        Definition combat = rules.Find(DefinitionTypes.Combat, "classic:standard", out _)!;
        CombatBehaviorController controller = new(rules, combat, CombatField.Of(combat));
        controller.Assign(actor.Id, profile);
        controller.Register([actor, target]);

        CombatActionChoice firstAction = ActionChoice(close, target, [
            new CombatMoveChoice(new Cell(2, 0), [new Cell(1, 0), new Cell(2, 0)], 2),
        ]);
        CombatBehaviorProposal? firstProposal = controller.Propose(actor, Observation(actor.Id, firstAction));
        Assert.NotNull(firstProposal);
        CombatBehaviorProposal first = firstProposal;

        CombatCommand.UseAction command = Assert.IsType<CombatCommand.UseAction>(first.Command);
        Assert.False(first.MovementOnly);
        Assert.Equal([target.Id], command.TargetIds);
        Assert.Equal([new Cell(1, 0), new Cell(2, 0)], command.Path);
        Assert.Equal(10m, actorCreature.Track("hit_points").Current);
        Assert.Empty(controller.States);

        controller.Commit(first);
        CombatBehaviorState state = Assert.Single(controller.States.Values);
        Assert.Equal(profile.Definition.QualifiedId, state.BehaviorId);
        Assert.Equal(0, state.RuleIndex);
        Assert.Equal(1, state.StepIndex);
        Assert.True(state.Committed);

        CombatBehaviorProposal? abandonedProposal = controller.Propose(actor, Observation(actor.Id, firstAction));
        Assert.NotNull(abandonedProposal);
        Assert.Equal(CombatBehaviorFallback.EndTurn, abandonedProposal!.Fallback);
        Assert.True(abandonedProposal.Trace!.Abandoned);
        Assert.Equal(0, abandonedProposal.Trace.RuleIndex);
        Assert.Equal(1, abandonedProposal.Trace.StepIndex);

        CombatContinuationState continuation = new()
        {
            BehaviorStates = controller.States.ToDictionary(entry => entry.Key, entry => entry.Value),
        };
        CombatContinuationState restored = CombatContinuationState.FromJson(CombatContinuationState.ToJson(continuation));
        Assert.Equal(1, restored.BehaviorStates[actor.Id].StepIndex);

        CombatActionChoice secondAction = ActionChoice(action, target, []);
        CombatBehaviorProposal? secondProposal = controller.Propose(actor, Observation(actor.Id, secondAction));
        Assert.NotNull(secondProposal);
        CombatBehaviorProposal second = secondProposal;
        CombatCommand.UseAction secondCommand = Assert.IsType<CombatCommand.UseAction>(second.Command);
        Assert.Equal([target.Id], secondCommand.TargetIds);
        Assert.Null(secondCommand.Path);

        controller.Commit(second);
        Assert.Empty(controller.States);
    }

    [Fact]
    public void StepCommitmentReassessesAndContinuesAtTheNextStep()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/sequence.json", """
            {
              "type": "combat-behavior",
              "id": "sequence",
              "name": "Step sequence",
              "fallback": "end-turn",
              "rules": [
                {
                  "commit": "step",
                  "steps": [
                    { "action": { "action": "classic:close" }, "target": "enemy" },
                    { "action": { "action": "classic:melee_attack", "damage": "1d6" }, "target": "enemy" }
                  ]
                }
              ]
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        Definition combat = rules.Find(DefinitionTypes.Combat, "classic:standard", out _)!;
        Definition close = rules.Find(DefinitionTypes.Action, "classic:close", out _)!;
        Definition attack = rules.Find(DefinitionTypes.Action, "classic:melee_attack", out _)!;

        Creature actorCreature = CreatureWithHitPoints("actor", 10);
        actorCreature.Position = new Cell(0, 0);
        Combatant actor = new("Actor", actorCreature,
            [
                new UseOption(close, "close", new Dictionary<string, CompiledExpression>()),
                new UseOption(attack, "strike", profile.Rules[0].Steps[1].Parameters),
            ],
            "actor")
        {
            Side = 0,
            Controller = CombatControlMode.Automatic,
        };
        Creature targetCreature = CreatureWithHitPoints("target", 10);
        targetCreature.Position = new Cell(1, 0);
        Combatant target = new("Target", targetCreature, [], "target") { Side = 1 };

        CombatBehaviorController controller = new(rules, combat, CombatField.Of(combat));
        controller.Assign(actor.Id, profile);
        controller.Register([actor, target]);

        CombatActionChoice firstChoice = new(
            $"{actor.Id}/use/0/{close.QualifiedId}",
            close.QualifiedId,
            "close",
            null,
            new Dictionary<string, int> { ["action"] = 1 },
            [TargetChoice(target)],
            []);
        CombatBehaviorProposal? firstProposal = controller.Propose(actor, Observation(actor.Id, firstChoice));
        Assert.NotNull(firstProposal);
        Assert.Equal(firstChoice.Id, Assert.IsType<CombatCommand.UseAction>(firstProposal!.Command).ActionId);
        controller.Commit(firstProposal);

        Assert.Equal(1, controller.States[actor.Id].StepIndex);
        CombatActionChoice secondChoice = new(
            $"{actor.Id}/use/1/{attack.QualifiedId}",
            attack.QualifiedId,
            "strike",
            null,
            new Dictionary<string, int> { ["action"] = 1 },
            [TargetChoice(target)],
            []);
        CombatBehaviorProposal? secondProposal = controller.Propose(actor, Observation(actor.Id, secondChoice));

        Assert.NotNull(secondProposal);
        Assert.Equal(1, secondProposal!.Trace?.StepIndex);
        Assert.Equal(secondChoice.Id, Assert.IsType<CombatCommand.UseAction>(secondProposal.Command).ActionId);
        controller.Commit(secondProposal);
        Assert.Empty(controller.States);
    }

    [Fact]
    public void RangedStepMovesToAVisibleEndpointBeforeAttacking()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/screened.json", """
            {
              "type": "encounter",
              "id": "screened",
              "name": "Screened",
              "monsters": [ { "monster": "classic:skeleton", "count": "1" } ],
              "terrain": [
                "..........",
                "..#.......",
                "..........",
                "..........",
                "..........",
                ".........."
              ]
            }
            """);
        modules.Write("tactics/line_of_sight.json", """
            {
              "type": "combat-behavior",
              "id": "line_of_sight",
              "name": "Find a clear shot",
              "rules": [
                {
                  "steps": [
                    {
                      "destination": { "kind": "within", "distance": "3" },
                      "action": { "action": "classic:missile_attack", "damage": "1d6", "increment": "1" },
                      "target": "enemy"
                    }
                  ]
                }
              ]
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        Definition combat = rules.Find(DefinitionTypes.Combat, "classic:standard", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "tactics:screened", out _)!;
        Definition attack = rules.Find(DefinitionTypes.Action, "classic:missile_attack", out _)!;
        Definition close = rules.Find(DefinitionTypes.Action, "classic:close", out _)!;
        CombatField field = CombatField.Of(combat, encounter)!;

        Creature actorCreature = CreatureWithHitPoints("actor", 10);
        actorCreature.Position = new Cell(0, 1);
        Combatant actor = new("Actor", actorCreature,
            [
                new UseOption(attack, "shoot", profile.Rules[0].Steps[0].Parameters),
                new UseOption(close, "close", new Dictionary<string, CompiledExpression>()),
            ],
            "actor")
        {
            Side = 0,
            Controller = CombatControlMode.Automatic,
        };
        Creature targetCreature = CreatureWithHitPoints("target", 10);
        targetCreature.Position = new Cell(3, 1);
        Combatant target = new("Target", targetCreature, [], "target") { Side = 1 };
        Assert.False(field.CanSee(actorCreature.Position!.Value, targetCreature.Position!.Value));

        CombatBehaviorController controller = new(rules, combat, field);
        controller.Assign(actor.Id, profile);
        controller.Register([actor, target]);
        CombatActionChoice closeChoice = new(
            $"{actor.Id}/use/1/{close.QualifiedId}",
            close.QualifiedId,
            "close",
            null,
            new Dictionary<string, int> { ["action"] = 1 },
            [TargetChoice(target)],
            [new CombatMoveChoice(new Cell(2, 0), [new Cell(1, 0), new Cell(2, 0)], 2)]);

        CombatBehaviorProposal? movementProposal = controller.Propose(actor, Observation(actor.Id, closeChoice));
        Assert.NotNull(movementProposal);
        Assert.True(movementProposal!.MovementOnly);
        CombatCommand.UseAction movement = Assert.IsType<CombatCommand.UseAction>(movementProposal.Command);
        Assert.Equal(closeChoice.Id, movement.ActionId);
        Assert.Equal([new Cell(1, 0), new Cell(2, 0)], movement.Path);
        controller.Commit(movementProposal);
        actorCreature.Position = new Cell(2, 0);

        CombatActionChoice attackChoice = new(
            $"{actor.Id}/use/0/{attack.QualifiedId}",
            attack.QualifiedId,
            "shoot",
            null,
            new Dictionary<string, int> { ["action"] = 1 },
            [TargetChoice(target)],
            []);
        CombatBehaviorProposal? attackProposal = controller.Propose(actor, Observation(actor.Id, attackChoice));

        Assert.NotNull(attackProposal);
        Assert.False(attackProposal!.MovementOnly);
        Assert.Equal(attackChoice.Id, Assert.IsType<CombatCommand.UseAction>(attackProposal.Command).ActionId);
        controller.Commit(attackProposal);
        Assert.Empty(controller.States);
    }

    [Fact]
    public void AutomaticRunnerMovesThroughTheResolverThenUsesTheRangedStep()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/grid.json", """
            {
              "type": "combat",
              "id": "grid",
              "name": "Two-action grid",
              "initiative": "1",
              "initiative_by": "side",
              "initiative_order": "highest-first",
              "initiative_each": "round",
              "round_seconds": 6,
              "field": {
                "width": 10,
                "height": 6,
                "metric": "chebyshev",
                "terrain": {
                  "#": { "name": "Pillar", "passable": false, "blocks_sight": true }
                }
              },
              "budget": [
                { "id": "action", "per_turn": 2 },
                { "id": "reaction", "per_turn": 1 }
              ],
              "track": "classic:hit_points",
              "defeated": "self.hit_points <= 0"
            }
            """);
        modules.Write("tactics/screened.json", """
            {
              "type": "encounter",
              "id": "screened",
              "name": "Screened",
              "monsters": [ { "monster": "classic:skeleton", "count": "1" } ],
              "terrain": [
                "..........",
                "..#.......",
                "..........",
                "..........",
                "..........",
                ".........."
              ]
            }
            """);
        modules.Write("tactics/line_of_sight.json", """
            {
              "type": "combat-behavior",
              "id": "line_of_sight",
              "name": "Find a clear shot",
              "rules": [
                {
                  "steps": [
                    {
                      "destination": { "kind": "within", "distance": "3" },
                      "action": { "action": "classic:missile_attack", "damage": "1d6", "increment": "1" },
                      "target": "enemy"
                    }
                  ]
                }
              ]
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        Definition combat = rules.Find(DefinitionTypes.Combat, "tactics:grid", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "tactics:screened", out _)!;
        Definition monster = rules.Find(DefinitionTypes.Monster, "classic:skeleton", out _)!;
        Definition close = rules.Find(DefinitionTypes.Action, "classic:close", out _)!;
        Definition missile = rules.Find(DefinitionTypes.Action, "classic:missile_attack", out _)!;
        Definition track = rules.Find(DefinitionTypes.Track, "classic:hit_points", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(9343, "tactical-ranged-resolver"));
            DiceRoller dice = new(engine.Random, stream);
            Evaluator evaluator = new(rules, dice);
            Combatant actor = Combatant.FromMonster(rules, monster, "Screened shooter", evaluator);
            actor.Uses.RemoveAll(use => use.Action != close);
            actor.Uses.Add(new UseOption(missile, "screened shot", profile.Rules[0].Steps[0].Parameters));
            Combatant target = Combatant.FromMonster(rules, monster, "Pillar target", evaluator);
            actor.Controller = CombatControlMode.Automatic;
            target.Controller = CombatControlMode.Manual;
            target.Creature.Track(track.Id).Max = 100;
            target.Creature.Track(track.Id).Current = 100;

            CombatRunner runner = CombatRunner.Create(
                rules,
                combat,
                [
                    new CombatSide("Party", [actor]),
                    new CombatSide("Enemy", [target]),
                ],
                dice,
                encounter,
                new CombatSetup([new Cell(0, 1), new Cell(3, 1)], SurprisedSide: -1));
            runner.BehaviorController.Assign(actor.Id, profile);
            runner.CollectBehaviorTraces = true;

            CombatObservation observation = runner.Start(1);
            while (observation.PendingDecision?.ActorId != target.Id
                && observation.Phase != CombatPhase.Ended)
            {
                if (observation.PendingDecision is not CombatDecision decision)
                {
                    break;
                }

                CombatCommandResult ended = runner.Submit(new CombatCommand.EndTurn(decision.ActorId));
                Assert.True(ended.Accepted, ended.Reason);
                observation = ended.Observation;
            }

            MoveFact move = Assert.Single(runner.Facts.OfType<MoveFact>(), fact => fact.Who == actor.Name);
            ActionFact shot = Assert.Single(runner.Facts.OfType<ActionFact>(), fact => fact.Who == actor.Name && fact.Action == "screened shot");
            Assert.Equal(new Cell(0, 1), move.From);
            Assert.NotEqual(move.From, move.To);
            Assert.Equal(target.Name, shot.Target);
            Assert.True(CombatField.Of(combat, encounter)!.CanSee(move.To, target.Creature.Position!.Value));
            Assert.Equal(move.To, runner.Observe().Combatants.Single(member => member.Id == actor.Id).Position);
            Assert.Contains(runner.BehaviorTraces, trace => trace.ActorId == actor.Id && trace.MovementOnly);
            Assert.Contains(runner.BehaviorTraces, trace => trace.ActorId == actor.Id && !trace.MovementOnly && trace.ActionId?.EndsWith(missile.QualifiedId, StringComparison.Ordinal) == true);
        });
    }

    [Theory]
    [InlineData("unavailable", "self.hit_points > 100", 1)]
    [InlineData("unaffordable", "self.hit_points > 0", 0)]
    public void RangedStepDoesNotMoveForAnIllegalIntendedAction(string caseId, string available, int budget)
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/screened_shot.json", $$"""
            {
              "type": "action",
              "id": "screened_shot_{{caseId}}",
              "name": "Screened shot",
              "cost": { "action": 1 },
              "target": "enemy",
              "range": "use.increment * 10",
              "available": "{{available}}",
              "parameters": [ "damage", "increment" ],
              "always": []
            }
            """);
        modules.Write("tactics/encounter.json", """
            {
              "type": "encounter",
              "id": "screened",
              "name": "Screened",
              "monsters": [ { "monster": "classic:skeleton", "count": "1" } ],
              "terrain": [
                "..........",
                "..#.......",
                "..........",
                "..........",
                "..........",
                ".........."
              ]
            }
            """);
        modules.Write("tactics/behavior.json", $$"""
            {
              "type": "combat-behavior",
              "id": "screened_{{caseId}}",
              "name": "Screened shot",
              "fallback": "end-turn",
              "rules": [
                {
                  "steps": [
                    {
                      "destination": { "kind": "within", "distance": "3" },
                      "action": { "action": "tactics:screened_shot_{{caseId}}", "damage": "1d6", "increment": "1" },
                      "target": "enemy"
                    }
                  ]
                }
              ]
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        Definition combat = rules.Find(DefinitionTypes.Combat, "classic:standard", out _)!;
        Definition encounter = rules.Find(DefinitionTypes.Encounter, "tactics:screened", out _)!;
        Definition shot = rules.Find(DefinitionTypes.Action, $"tactics:screened_shot_{caseId}", out _)!;
        Definition close = rules.Find(DefinitionTypes.Action, "classic:close", out _)!;

        Creature actorCreature = CreatureWithHitPoints("actor", 10);
        actorCreature.Position = new Cell(0, 1);
        Combatant actor = new("Actor", actorCreature,
            [
                new UseOption(shot, "shoot", profile.Rules[0].Steps[0].Parameters),
                new UseOption(close, "close", new Dictionary<string, CompiledExpression>()),
            ],
            "actor")
        {
            Side = 0,
            Controller = CombatControlMode.Automatic,
        };
        actor.Budget["action"] = budget;
        Creature targetCreature = CreatureWithHitPoints("target", 10);
        targetCreature.Position = new Cell(3, 1);
        Combatant target = new("Target", targetCreature, [], "target") { Side = 1 };
        CombatField field = CombatField.Of(combat, encounter)!;
        Assert.False(field.CanSee(actorCreature.Position.Value, targetCreature.Position.Value));

        CombatBehaviorController controller = new(rules, combat, field);
        controller.Assign(actor.Id, profile);
        controller.Register([actor, target]);
        CombatActionChoice closeChoice = new(
            $"{actor.Id}/use/1/{close.QualifiedId}",
            close.QualifiedId,
            "close",
            null,
            new Dictionary<string, int> { ["action"] = 1 },
            [TargetChoice(target)],
            [new CombatMoveChoice(new Cell(2, 0), [new Cell(1, 0), new Cell(2, 0)], 2)]);

        CombatBehaviorProposal? proposal = controller.Propose(actor, Observation(actor.Id, closeChoice));

        Assert.NotNull(proposal);
        Assert.Equal(CombatBehaviorFallback.EndTurn, proposal!.Fallback);
        Assert.Null(proposal.Command);
        Assert.Contains(proposal.Trace!.Alternatives, alternative => alternative.Status == "unavailable");
    }

    [Fact]
    public void ReturnsFallbackWhenAnAuthoredStepIsUnavailable()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/fallback.json", """
            {
              "type": "combat-behavior",
              "id": "fallback",
              "name": "Fallback when blocked",
              "fallback": "end-turn",
              "rules": [
                {
                  "steps": [
                    {
                      "destination": { "kind": "within", "distance": 1 },
                      "action": { "action": "classic:melee_attack", "damage": "1d6" },
                      "target": "enemy"
                    }
                  ]
                }
              ]
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        Definition close = rules.Find(DefinitionTypes.Action, "classic:close", out _)!;

        Creature actorCreature = CreatureWithHitPoints("actor", 10);
        Combatant actor = new("Actor", actorCreature,
            [new UseOption(close, "close", new Dictionary<string, CompiledExpression>())],
            "actor")
        {
            Side = 0,
            Controller = CombatControlMode.Automatic,
        };
        Creature targetCreature = CreatureWithHitPoints("target", 10);
        Combatant target = new("Target", targetCreature, [], "target") { Side = 1 };
        actorCreature.Position = new Cell(0, 0);
        targetCreature.Position = new Cell(3, 0);

        Definition combat = rules.Find(DefinitionTypes.Combat, "classic:standard", out _)!;
        CombatBehaviorController controller = new(rules, combat, CombatField.Of(combat));
        controller.Assign(actor.Id, profile);
        controller.Register([actor, target]);

        CombatActionChoice closeChoice = ActionChoice(close, target, []);
        CombatBehaviorProposal? proposal = controller.Propose(actor, Observation(actor.Id, closeChoice));

        Assert.NotNull(proposal);
        Assert.Null(proposal!.Command);
        Assert.Equal(CombatBehaviorFallback.EndTurn, proposal.Fallback);
        Assert.Contains("No authored step", proposal.Reason, StringComparison.Ordinal);
        Assert.Equal(CombatBehaviorFallback.EndTurn, proposal.Trace?.Fallback);
        Assert.Contains(proposal.Trace!.Alternatives, alternative => alternative.Status == "unavailable");
        Assert.Empty(controller.States);
    }

    [Fact]
    public void PreservesAllLegalTargetsForDiceBackedMaximumTargetActions()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/sleep.json", """
            {
              "type": "combat-behavior",
              "id": "sleep",
              "name": "Sleep",
              "rules": [
                {
                  "steps": [
                    {
                      "action": { "action": "classic:spell_sleep", "range": 3 },
                      "spell": "classic:sleep",
                      "target": "enemy"
                    }
                  ]
                }
              ]
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        Definition action = rules.Find(DefinitionTypes.Action, "classic:spell_sleep", out _)!;
        Definition spell = rules.Find(DefinitionTypes.Spell, "classic:sleep", out _)!;

        Combatant actor = new(
            "Caster",
            CreatureWithHitPoints("caster", 10),
            [new UseOption(action, "Sleep", profile.Rules[0].Steps[0].Parameters, spell)],
            "caster")
        {
            Side = 0,
            Controller = CombatControlMode.Automatic,
        };
        actor.CastsLeft[spell] = 1;
        Combatant firstTarget = new("First", CreatureWithHitPoints("first", 10), [], "first") { Side = 1 };
        Combatant secondTarget = new("Second", CreatureWithHitPoints("second", 10), [], "second") { Side = 1 };

        CombatBehaviorController controller = new(rules);
        controller.Assign(actor.Id, profile);
        controller.Register([actor, firstTarget, secondTarget]);
        CombatActionChoice choice = new(
            "sleep-choice",
            action.QualifiedId,
            "Sleep",
            spell.QualifiedId,
            new Dictionary<string, int> { ["action"] = 1 },
            [
                new CombatTargetChoice(firstTarget.Id, firstTarget.Name, firstTarget.Side, false, false, null, 10, 10),
                new CombatTargetChoice(secondTarget.Id, secondTarget.Name, secondTarget.Side, false, false, null, 10, 10),
            ],
            []);

        CombatBehaviorProposal? proposal = controller.Propose(actor, Observation(actor.Id, choice));
        Assert.NotNull(proposal);
        CombatCommand.UseAction command = Assert.IsType<CombatCommand.UseAction>(proposal!.Command);
        Assert.Equal([firstTarget.Id, secondTarget.Id], command.TargetIds);
    }

    [Fact]
    public void SelectsTheAuthoredUseParametersAndItemKind()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/item_use.json", """
            {
              "type": "combat-behavior",
              "id": "item_use",
              "name": "Use the wand strike",
              "rules": [
                {
                  "steps": [
                    {
                      "action": {
                        "action": "classic:melee_attack",
                        "name": "strike",
                        "from_item": "wand",
                        "damage": "1d6"
                      },
                      "target": "enemy"
                    }
                  ]
                }
              ]
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        CombatBehaviorStep step = Assert.Single(Assert.Single(profile.Rules).Steps);
        Definition attack = rules.Find(DefinitionTypes.Action, "classic:melee_attack", out _)!;
        CompiledExpression otherDamage = rules.Compile("1d8", "tactics", Roots.Self | Roots.Target | Roots.Behavior);

        Creature actorCreature = CreatureWithHitPoints("actor", 10);
        Combatant actor = new(
            "Actor",
            actorCreature,
            [
                new UseOption(attack, "strike", new Dictionary<string, CompiledExpression> { ["damage"] = otherDamage }, FromItem: "weapon"),
                new UseOption(attack, "strike", new Dictionary<string, CompiledExpression> { ["damage"] = step.Parameters["damage"] }, FromItem: "wand"),
            ],
            "actor")
        {
            Side = 0,
            Controller = CombatControlMode.Automatic,
        };
        Combatant target = new("Target", CreatureWithHitPoints("target", 10), [], "target") { Side = 1 };
        CombatTargetChoice targetChoice = new(target.Id, target.Name, target.Side, false, false, target.Creature.Position, 10, 10);
        CombatActionChoice firstChoice = new(
            "actor/use/0/classic:melee_attack",
            attack.QualifiedId,
            "strike",
            null,
            new Dictionary<string, int> { ["action"] = 1 },
            [targetChoice],
            []);
        CombatActionChoice secondChoice = firstChoice with { Id = "actor/use/1/classic:melee_attack" };
        CombatObservation observation = new(
            CombatPhase.AwaitingAction,
            1,
            actor.Id,
            new CombatDecision("decision:item-use", CombatDecisionKind.Action, actor.Id, 1, [firstChoice, secondChoice], [], true),
            null,
            null,
            [],
            []);

        CombatBehaviorController controller = new(rules);
        controller.Assign(actor.Id, profile);
        controller.Register([actor, target]);
        CombatBehaviorProposal? proposal = controller.Propose(actor, observation);

        Assert.NotNull(proposal);
        CombatCommand.UseAction command = Assert.IsType<CombatCommand.UseAction>(proposal!.Command);
        Assert.Equal(secondChoice.Id, command.ActionId);
        Assert.Equal([target.Id], command.TargetIds);

        Combatant mismatchedActor = new(
            "Mismatched",
            CreatureWithHitPoints("mismatched", 10),
            [new UseOption(attack, "strike", new Dictionary<string, CompiledExpression> { ["damage"] = otherDamage }, FromItem: "wand")],
            "mismatched")
        {
            Side = 0,
            Controller = CombatControlMode.Automatic,
        };
        CombatActionChoice mismatchedChoice = firstChoice with { Id = "mismatched/use/0/classic:melee_attack" };
        CombatObservation mismatchedObservation = new(
            CombatPhase.AwaitingAction,
            1,
            mismatchedActor.Id,
            new CombatDecision("decision:mismatched", CombatDecisionKind.Action, mismatchedActor.Id, 1, [mismatchedChoice], [], true),
            null,
            null,
            [],
            []);
        controller = new CombatBehaviorController(rules);
        controller.Assign(mismatchedActor.Id, profile);
        controller.Register([mismatchedActor, target]);
        CombatBehaviorProposal? rejected = controller.Propose(mismatchedActor, mismatchedObservation);

        Assert.NotNull(rejected);
        Assert.Null(rejected!.Command);
        Assert.Equal(CombatBehaviorFallback.EndTurn, rejected.Fallback);
        Assert.Contains(rejected.Trace!.Alternatives, alternative => alternative.Status == "unavailable");
    }

    [Fact]
    public void EvaluatesBehaviorParametersThroughTheirDependencies()
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: Require("classic", "*"));
        modules.Write("tactics/dependent.json", """
            {
              "type": "combat-behavior",
              "id": "dependent",
              "name": "Dependent distance",
              "parameters": { "base_distance": 1, "preferred_distance": "behavior.base_distance + 1" },
              "rules": [
                {
                  "steps": [
                    {
                      "destination": { "kind": "within", "distance": "behavior.preferred_distance" },
                      "action": { "action": "classic:close" },
                      "target": "enemy"
                    }
                  ]
                }
              ]
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        CombatBehaviorProfile profile = Assert.Single(rules.CombatBehaviors.Values);
        Definition close = rules.Find(DefinitionTypes.Action, "classic:close", out _)!;
        Creature actorCreature = CreatureWithHitPoints("actor", 10);
        actorCreature.Position = new Cell(0, 0);
        Combatant actor = new(
            "Actor",
            actorCreature,
            [new UseOption(close, "close", new Dictionary<string, CompiledExpression>())],
            "actor")
        {
            Side = 0,
            Controller = CombatControlMode.Automatic,
        };
        Creature targetCreature = CreatureWithHitPoints("target", 10);
        targetCreature.Position = new Cell(2, 0);
        Combatant target = new("Target", targetCreature, [], "target") { Side = 1 };
        CombatActionChoice choice = ActionChoice(close, target, []);
        CombatObservation observation = Observation(actor.Id, choice);

        CombatBehaviorController controller = new(rules);
        controller.Assign(actor.Id, profile);
        controller.Register([actor, target]);
        CombatBehaviorProposal? proposal = controller.Propose(actor, observation);

        Assert.NotNull(proposal);
        Assert.IsType<CombatCommand.UseAction>(proposal!.Command);
        Assert.Equal("behavior.preferred_distance", profile.Rules[0].Steps[0].Destination!.Distance.Text);
    }

    private static CombatObservation Observation(string actorId, CombatActionChoice action)
    {
        CombatDecision decision = new(
            "decision:sequence",
            CombatDecisionKind.Action,
            actorId,
            1,
            [action],
            action.Moves,
            true);
        return new CombatObservation(CombatPhase.AwaitingAction, 1, actorId, decision, null, null, [], []);
    }

    private static CombatActionChoice ActionChoice(Definition action, Combatant target, IReadOnlyList<CombatMoveChoice> moves)
    {
        CombatTargetChoice targetChoice = new(target.Id, target.Name, target.Side, false, false, target.Creature.Position, 10, 10);
        return new CombatActionChoice(
            "actor/use/0/classic:melee_attack/strike",
            action.QualifiedId,
            "strike",
            null,
            new Dictionary<string, int> { ["action"] = 1 },
            [targetChoice],
            moves);
    }

    private static CombatTargetChoice TargetChoice(Combatant target)
    {
        return new CombatTargetChoice(target.Id, target.Name, target.Side, target.Defeated, target.Escaped, target.Creature.Position, 10, 10);
    }

    private static Creature CreatureWithHitPoints(string label, decimal points)
    {
        Creature creature = new(label);
        creature.Track("hit_points").Current = points;
        creature.Track("hit_points").Max = points;
        return creature;
    }
}
