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

    private static Creature CreatureWithHitPoints(string label, decimal points)
    {
        Creature creature = new(label);
        creature.Track("hit_points").Current = points;
        creature.Track("hit_points").Max = points;
        return creature;
    }
}
