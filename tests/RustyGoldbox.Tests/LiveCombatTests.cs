using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

/// <summary>Focused checks for the resumable live combat owner.</summary>
public sealed class LiveCombatTests
{
    [Fact]
    public void ObservationUsesStableIdsAndRejectedCommandsDoNotMutate()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteLiveRuleset(modules));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Same name", evaluator);
            Combatant target = Combatant.FromMonster(rules, targetDefinition, "Same name", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Targets", [target]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatDecision decision = Assert.IsType<CombatDecision>(waiting.PendingDecision);
            CombatActionChoice strike = Assert.Single(decision.Actions);
            Assert.NotEqual(attacker.Id, target.Id);
            Assert.Equal("side-1-member-1", attacker.Id);
            Assert.Equal("side-2-member-1", target.Id);
            Assert.Equal($"{attacker.Id}/use/0/rules:strike", strike.Id);
            Assert.DoesNotContain(strike.Name, strike.Id, StringComparison.Ordinal);
            Assert.Equal(decision, runner.Observe().PendingDecision);

            int rollsBefore = dice.Rolls.Count;
            int factsBefore = waiting.Facts.Count;
            int budgetBefore = waiting.Combatants.Single(member => member.Id == attacker.Id).Budget["turn"];
            CombatCommandResult rejected = runner.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, ["missing-target"]));

            Assert.False(rejected.Accepted);
            Assert.Equal(rollsBefore, dice.Rolls.Count);
            Assert.Equal(factsBefore, rejected.Observation.Facts.Count);
            Assert.Equal(budgetBefore, rejected.Observation.Combatants.Single(member => member.Id == attacker.Id).Budget["turn"]);

            CombatCommandResult accepted = runner.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, [target.Id]));
            Assert.True(accepted.Accepted, accepted.Reason);
            DamageFact damage = Assert.Single(accepted.Observation.Facts.OfType<DamageFact>());
            Assert.Contains(target.Id, damage.TargetIds);
            Assert.Contains(attacker.Id, damage.SubjectIds);
        });
    }

    [Fact]
    public void ContinuationRestoresAnAwaitingActionWithoutRepeatingSetup()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteLiveRuleset(modules));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant target = Combatant.FromMonster(rules, targetDefinition, "Target", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [new CombatSide("Attackers", [attacker]), new CombatSide("Targets", [target])], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            CombatObservation waiting = runner.Start(1);
            CombatDecision decision = Assert.IsType<CombatDecision>(waiting.PendingDecision);
            CombatActionChoice strike = Assert.Single(decision.Actions);
            CombatContinuationState state = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            int rollsBeforeRestore = dice.Rolls.Count;

            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                new CombatSide("Targets", [Combatant.FromMonster(rules, targetDefinition, "Target", restoredEvaluator)]),
            ], dice, state);

            CombatObservation restoredWaiting = restored.Observe();
            Assert.Equal(rollsBeforeRestore, dice.Rolls.Count);
            Assert.Equal(decision.Id, restoredWaiting.PendingDecision?.Id);
            Assert.Equal(attacker.Id, restoredWaiting.ActiveActorId);
            CombatCommandResult accepted = restored.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, [target.Id]));

            Assert.True(accepted.Accepted, accepted.Reason);
            Assert.Contains(accepted.Observation.Facts.OfType<DamageFact>(), fact => fact.TargetIds.Contains(target.Id));
        });
    }

    [Fact]
    public void MaximumTargetsRollsAtCommitAndObservationDoesNotRoll()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteLiveRuleset(modules, includeSpray: true));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Caster", evaluator);
            Combatant first = Combatant.FromMonster(rules, targetDefinition, "Duplicate", evaluator);
            Combatant second = Combatant.FromMonster(rules, targetDefinition, "Duplicate", evaluator);
            Combatant third = Combatant.FromMonster(rules, targetDefinition, "Duplicate", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [new CombatSide("Attackers", [attacker]), new CombatSide("Targets", [first, second, third])], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatDecision action = Assert.IsType<CombatDecision>(waiting.PendingDecision);
            CombatActionChoice spray = Assert.Single(action.Actions, choice => choice.ActionId.EndsWith(":spray", StringComparison.Ordinal));
            int rollsBefore = dice.Rolls.Count;
            CombatCommandResult rejected = runner.Submit(new CombatCommand.UseAction(attacker.Id, spray.Id, ["unknown"]));
            Assert.False(rejected.Accepted);
            Assert.Equal(rollsBefore, dice.Rolls.Count);

            CombatCommandResult committed = runner.Submit(new CombatCommand.UseAction(attacker.Id, spray.Id, []));
            Assert.True(committed.Accepted, committed.Reason);
            Assert.Equal(rollsBefore + 1, dice.Rolls.Count);
            CombatDecision targets = Assert.IsType<CombatDecision>(committed.Observation.PendingDecision);
            Assert.Equal(CombatDecisionKind.Targets, targets.Kind);
            Assert.InRange(targets.MaximumTargets!.Value, 1, 2);
            int rollsAtTargetChoice = dice.Rolls.Count;
            Assert.Equal(rollsAtTargetChoice, runner.Observe().PendingDecision is not null ? dice.Rolls.Count : -1);

            CombatTargetChoice selected = targets.Actions.Single().Targets[0];
            CombatCommandResult resolved = runner.Submit(new CombatCommand.UseAction(attacker.Id, targets.ActionId!, [selected.Id]));
            Assert.True(resolved.Accepted, resolved.Reason);
            Assert.Equal(rollsAtTargetChoice, dice.Rolls.Count);
            Assert.Contains(resolved.Observation.Facts.OfType<DamageFact>(), fact => fact.TargetIds.Contains(selected.Id));
        });
    }

    [Fact]
    public void ExplicitMovementUsesTheAuthoredActionCostAndPath()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteLiveRuleset(modules, includeMovement: true));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Mover", evaluator);
            Combatant target = Combatant.FromMonster(rules, targetDefinition, "Target", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [new CombatSide("Movers", [attacker]), new CombatSide("Targets", [target])], dice,
                setup: new CombatSetup([new Cell(0, 0), new Cell(4, 0)], SurprisedSide: -1));
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatDecision decision = Assert.IsType<CombatDecision>(waiting.PendingDecision);
            CombatActionChoice advance = Assert.Single(decision.Actions);
            CombatMoveChoice move = Assert.Single(advance.Moves, candidate => candidate.Destination == new Cell(3, 0));
            int budgetBefore = waiting.Combatants.Single(member => member.Id == attacker.Id).Budget["turn"];

            CombatCommandResult result = runner.Submit(new CombatCommand.UseAction(attacker.Id, advance.Id, [target.Id], move.Path));
            Assert.True(result.Accepted, result.Reason);
            Assert.Equal(new Cell(3, 0), result.Observation.Combatants.Single(member => member.Id == attacker.Id).Position);
            Assert.Equal(budgetBefore - 1, result.Observation.Combatants.Single(member => member.Id == attacker.Id).Budget["turn"]);
            MoveFact moved = Assert.Single(result.Observation.Facts.OfType<MoveFact>());
            Assert.Equal(3, moved.Cells);
            Assert.Equal(attacker.Id, moved.SubjectIds.Single());
        });
    }

    [Fact]
    public void HumanCannotAddTargetsToSingleOrWholeSideActionWithoutMutation()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteCardinalityRuleset(modules));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant first = Combatant.FromMonster(rules, targetDefinition, "First", evaluator);
            Combatant second = Combatant.FromMonster(rules, targetDefinition, "Second", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
                [new CombatSide("Attackers", [attacker]), new CombatSide("Targets", [first, second])], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatDecision decision = Assert.IsType<CombatDecision>(waiting.PendingDecision);
            CombatActionChoice strike = Assert.Single(decision.Actions, action => action.ActionId.EndsWith(":strike", StringComparison.Ordinal));
            CombatActionChoice all = Assert.Single(decision.Actions, action => action.ActionId.EndsWith(":all", StringComparison.Ordinal));
            Assert.Equal("enemy", strike.TargetKind);
            Assert.Equal("one", strike.TargetMode);
            Assert.Equal("all_enemies", all.TargetKind);
            Assert.Equal("all", all.TargetMode);
            int rollsBefore = dice.Rolls.Count;
            int factsBefore = waiting.Facts.Count;
            int budgetBefore = waiting.Combatants.Single(member => member.Id == attacker.Id).Budget["turn"];

            CombatCommandResult extraStrike = runner.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, [first.Id, second.Id]));
            Assert.False(extraStrike.Accepted);
            Assert.Contains("exactly one", extraStrike.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(rollsBefore, dice.Rolls.Count);
            Assert.Equal(factsBefore, extraStrike.Observation.Facts.Count);
            Assert.Equal(budgetBefore, extraStrike.Observation.Combatants.Single(member => member.Id == attacker.Id).Budget["turn"]);

            CombatCommandResult partialAll = runner.Submit(new CombatCommand.UseAction(attacker.Id, all.Id, [first.Id]));
            Assert.False(partialAll.Accepted);
            Assert.Contains("every legal target", partialAll.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(rollsBefore, dice.Rolls.Count);
            Assert.Equal(factsBefore, partialAll.Observation.Facts.Count);
            Assert.Equal(budgetBefore, partialAll.Observation.Combatants.Single(member => member.Id == attacker.Id).Budget["turn"]);
        });
    }

    [Fact]
    public void HumanMayChooseALegalNonpreferredTarget()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteCardinalityRuleset(modules));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant low = Combatant.FromMonster(rules, targetDefinition, "Low", evaluator);
            Combatant high = Combatant.FromMonster(rules, targetDefinition, "High", evaluator);
            high.Creature.Values["str"] = 18;
            CombatRunner runner = CombatRunner.Create(rules, combat,
                [new CombatSide("Attackers", [attacker]), new CombatSide("Targets", [low, high])], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatActionChoice preferred = Assert.Single(
                Assert.IsType<CombatDecision>(waiting.PendingDecision).Actions,
                action => action.ActionId.EndsWith(":preferred", StringComparison.Ordinal));
            CombatCommandResult result = runner.Submit(new CombatCommand.UseAction(attacker.Id, preferred.Id, [low.Id]));

            Assert.True(result.Accepted, result.Reason);
            Assert.Contains(result.Observation.Facts.OfType<ActionFact>(), fact => fact.Action == "Preferred" && fact.Target == low.Name);
            Assert.Contains(result.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == low.Name);
            Assert.DoesNotContain(result.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == high.Name);
        });
    }

    [Fact]
    public void HumanCanUseASpellChoiceAsOneLegalTarget()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteCardinalityRuleset(modules));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Caster", evaluator);
            Combatant target = Combatant.FromMonster(rules, targetDefinition, "Target", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
                [new CombatSide("Attackers", [attacker]), new CombatSide("Targets", [target])], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatActionChoice spell = Assert.Single(
                Assert.IsType<CombatDecision>(waiting.PendingDecision).Actions,
                action => action.SpellId is not null);
            CombatCommandResult result = runner.Submit(new CombatCommand.UseAction(attacker.Id, spell.Id, [target.Id]));

            Assert.True(result.Accepted, result.Reason);
            Assert.Equal("Bolt", spell.Name);
            Assert.Contains(result.Observation.Facts.OfType<ActionFact>(), fact => fact.Action == "Bolt" && fact.Target == target.Name);
            Assert.Contains(result.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == target.Name);
            Assert.Equal(0, attacker.CastsLeft.Values.Single());
        });
    }

    [Fact]
    public void HumanCanSplitPortionsButCannotSubmitTooManyAllocations()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteCardinalityRuleset(modules));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant first = Combatant.FromMonster(rules, targetDefinition, "First", evaluator);
            Combatant second = Combatant.FromMonster(rules, targetDefinition, "Second", evaluator);
            Combatant third = Combatant.FromMonster(rules, targetDefinition, "Third", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
                [new CombatSide("Attackers", [attacker]), new CombatSide("Targets", [first, second, third])], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatActionChoice barrage = Assert.Single(
                Assert.IsType<CombatDecision>(waiting.PendingDecision).Actions,
                action => action.ActionId.EndsWith(":barrage", StringComparison.Ordinal));
            Assert.Equal("portions", barrage.TargetMode);
            Assert.Equal(2, barrage.PortionCount);
            int rollsBefore = dice.Rolls.Count;
            int factsBefore = waiting.Facts.Count;
            int budgetBefore = waiting.Combatants.Single(member => member.Id == attacker.Id).Budget["turn"];

            CombatCommandResult tooMany = runner.Submit(new CombatCommand.UseAction(attacker.Id, barrage.Id, [first.Id, second.Id, third.Id]));
            Assert.False(tooMany.Accepted);
            Assert.Contains("2 portions", tooMany.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(rollsBefore, dice.Rolls.Count);
            Assert.Equal(factsBefore, tooMany.Observation.Facts.Count);
            Assert.Equal(budgetBefore, tooMany.Observation.Combatants.Single(member => member.Id == attacker.Id).Budget["turn"]);

            CombatCommandResult split = runner.Submit(new CombatCommand.UseAction(attacker.Id, barrage.Id, [first.Id, second.Id]));
            Assert.True(split.Accepted, split.Reason);
            Assert.Equal(2, split.Observation.Facts.OfType<PortionFact>().Count());
            Assert.Contains(split.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == first.Name);
            Assert.Contains(split.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == second.Name);
        });
    }

    [Fact]
    public void ExplicitMaximumTargetsRespectTheCapAndSelectionOrder()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteCardinalityRuleset(modules));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant low = Combatant.FromMonster(rules, targetDefinition, "Low", evaluator);
            Combatant high = Combatant.FromMonster(rules, targetDefinition, "High", evaluator);
            high.Creature.Values["str"] = 18;
            CombatRunner runner = CombatRunner.Create(rules, combat,
                [new CombatSide("Attackers", [attacker]), new CombatSide("Targets", [low, high])], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatActionChoice capped = Assert.Single(
                Assert.IsType<CombatDecision>(waiting.PendingDecision).Actions,
                action => action.ActionId.EndsWith(":capped", StringComparison.Ordinal));
            Assert.Equal("maximum", capped.TargetMode);
            Assert.Equal("all_enemies", capped.TargetKind);
            CombatCommandResult result = runner.Submit(new CombatCommand.UseAction(attacker.Id, capped.Id, [low.Id, high.Id]));

            Assert.True(result.Accepted, result.Reason);
            Assert.Contains(result.Observation.Facts.OfType<ActionFact>(), fact => fact.Action == "Capped" && fact.Target == low.Name);
            Assert.Contains(result.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == low.Name);
            Assert.DoesNotContain(result.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == high.Name);
        });
    }

    [Fact]
    public void ExplicitSingleMaximumTargetRollsItsCapAtCommit()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteLiveRuleset(modules, includeSpray: true));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Caster", evaluator);
            Combatant target = Combatant.FromMonster(rules, targetDefinition, "Target", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
                [new CombatSide("Attackers", [attacker]), new CombatSide("Targets", [target])], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatActionChoice spray = Assert.Single(
                Assert.IsType<CombatDecision>(waiting.PendingDecision).Actions,
                action => action.ActionId.EndsWith(":spray", StringComparison.Ordinal));
            int rollsBefore = dice.Rolls.Count;
            CombatCommandResult result = runner.Submit(new CombatCommand.UseAction(attacker.Id, spray.Id, [target.Id]));

            Assert.True(result.Accepted, result.Reason);
            Assert.Equal(rollsBefore + 1, dice.Rolls.Count);
            Assert.Contains(result.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == target.Name);
        });
    }

    [Fact]
    public void AutomaticMaximumTargetsKeepsAllLegalCandidatesBeforeApplyingCap()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteLiveRuleset(modules, includeSpray: true, sprayMaximum: "2"));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Caster", evaluator);
            attacker.Uses.RemoveAll(use => use.Action.Id == "strike");
            Combatant first = Combatant.FromMonster(rules, targetDefinition, "First", evaluator);
            Combatant second = Combatant.FromMonster(rules, targetDefinition, "Second", evaluator);
            Combatant third = Combatant.FromMonster(rules, targetDefinition, "Third", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
                [new CombatSide("Attackers", [attacker]), new CombatSide("Targets", [first, second, third])], dice);

            CombatObservation result = runner.Start(1);
            ActionFact spray = Assert.Single(result.Facts.OfType<ActionFact>(), fact => fact.Action == "Spray");
            Assert.Equal($"{first.Name}, {second.Name}", spray.Target);
        });
    }

    [Fact]
    public void SuspendedParameterizedUseRestoresTheExactUseIdentity()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteDuplicateUseInterruptRuleset(modules));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
                [new CombatSide("Attackers", [attacker]), new CombatSide("Guards", [guard])], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(guard.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatDecision decision = Assert.IsType<CombatDecision>(waiting.PendingDecision);
            CombatActionChoice strong = Assert.Single(decision.Actions, action => action.Name == "Strong");
            Assert.NotEqual(strong.Id, Assert.Single(decision.Actions, action => action.Name == "Weak").Id);

            CombatCommandResult interrupted = runner.Submit(new CombatCommand.UseAction(attacker.Id, strong.Id, [guard.Id]));
            CombatDecision reaction = Assert.IsType<CombatDecision>(interrupted.Observation.PendingDecision);
            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            Assert.Contains("/use/1/", saved.PendingOperation!.UseId, StringComparison.Ordinal);

            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
            ], dice, saved);

            CombatCommand decline = new CombatCommand.Decide(reaction.Id);
            CombatCommandResult left = runner.Submit(decline);
            CombatCommandResult right = restored.Submit(decline);
            Assert.True(left.Accepted, left.Reason);
            Assert.True(right.Accepted, right.Reason);
            Assert.Single(left.Observation.Facts.OfType<DamageFact>(), fact => fact.Amount == 7);
            Assert.Single(right.Observation.Facts.OfType<DamageFact>(), fact => fact.Amount == 7);
        });
    }

    [Fact]
    public void AutomaticControllerFinishesACommittedMaximumTargetChoice()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteLiveRuleset(modules, includeSpray: true, sprayMaximum: "2", targetCanAct: true));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant first = Combatant.FromMonster(rules, targetDefinition, "First", evaluator);
            Combatant second = Combatant.FromMonster(rules, targetDefinition, "Second", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
                [new CombatSide("Attackers", [attacker]), new CombatSide("Targets", [first, second])], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(first.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(second.Id, CombatControlMode.Manual));

            CombatDecision action = Assert.IsType<CombatDecision>(runner.Start(1).PendingDecision);
            CombatActionChoice spray = Assert.Single(action.Actions, choice => choice.ActionId.EndsWith(":spray", StringComparison.Ordinal));
            CombatCommandResult committed = runner.Submit(new CombatCommand.UseAction(attacker.Id, spray.Id, []));
            Assert.True(committed.Accepted, committed.Reason);
            Assert.Equal(CombatDecisionKind.Targets, committed.Observation.PendingDecision?.Kind);

            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Automatic));
            CombatObservation next = runner.Observe();
            Assert.DoesNotContain(next.Facts.OfType<ActionFact>(), fact => fact.Who == attacker.Name && fact.Target.Split(", ", StringSplitOptions.RemoveEmptyEntries).Length > 2);
            Assert.Null(runner.Capture().CommittedActorId);
            Assert.Equal(first.Id, next.PendingDecision?.ActorId);
            Assert.DoesNotContain(next.Facts, fact => fact.Describe().Contains("pending target", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void MovementChoicesExposeAllLegalDestinations()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(WriteWideMovementRuleset(modules));
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition targetDefinition = rules.Find(DefinitionTypes.Monster, "target", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Mover", evaluator);
            Combatant target = Combatant.FromMonster(rules, targetDefinition, "Target", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
                [new CombatSide("Movers", [attacker]), new CombatSide("Targets", [target])], dice,
                setup: new CombatSetup([new Cell(15, 15), new Cell(18, 15)], SurprisedSide: -1));
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatDecision decision = Assert.IsType<CombatDecision>(runner.Start(1).PendingDecision);
            CombatActionChoice advance = Assert.Single(decision.Actions);
            Assert.True(advance.Moves.Count > 64);
        });
    }

    private static string WriteCardinalityRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/combat.json", """
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature",
              "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6,
              "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/strike.json", """
            { "type": "action", "id": "strike", "name": "Strike", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "2" } ] }
            """);
        modules.Write("rules/preferred.json", """
            { "type": "action", "id": "preferred", "name": "Preferred", "cost": { "turn": 1 }, "target": "enemy", "prefer": "target.str",
              "always": [ { "op": "damage", "amount": "3" } ] }
            """);
        modules.Write("rules/all.json", """
            { "type": "action", "id": "all", "name": "All", "cost": { "turn": 1 }, "target": "all_enemies",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/barrage.json", """
            { "type": "action", "id": "barrage", "name": "Barrage", "cost": { "turn": 1 }, "target": "enemy", "portions": "2",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/capped.json", """
            { "type": "action", "id": "capped", "name": "Capped", "cost": { "turn": 1 }, "target": "all_enemies", "max_targets": "1", "prefer": "target.str",
              "always": [ { "op": "damage", "amount": "4" } ] }
            """);
        modules.Write("rules/bolt_action.json", """
            { "type": "action", "id": "bolt_action", "name": "Bolt", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "5" } ] }
            """);
        modules.Write("rules/bolt.json", """
            { "type": "spell", "id": "bolt", "name": "Bolt", "lists": { "warrior": 1 }, "range": "10", "duration": "instant", "area": "one target", "casting_time": "instant", "description": "A test bolt.",
              "effect": { "action": "bolt_action" } }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "strike" }, { "action": "preferred" }, { "action": "all" }, { "action": "barrage" }, { "action": "capped" } ],
              "spells": [ { "spell": "bolt", "per_day": 1 } ], "xp": 0 }
            """);
        modules.Write("rules/target.json", """
            { "type": "monster", "id": "target", "name": "Target", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "5" }, "actions": [], "xp": 0 }
            """);
        return root;
    }

    private static string WriteDuplicateUseInterruptRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/combat.json", """
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature",
              "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6,
              "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ],
              "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/smite.json", """
            { "type": "action", "id": "smite", "name": "Smite", "cost": { "turn": 1 }, "target": "enemy", "parameters": ["damage"],
              "always": [ { "op": "damage", "amount": "use.damage" } ] }
            """);
        modules.Write("rules/brace.json", """
            { "type": "action", "id": "brace", "name": "Brace", "cost": { "reaction": 0 }, "target": "self", "always": [] }
            """);
        modules.Write("rules/guard_reaction.json", """
            { "type": "reaction", "id": "guard_reaction", "name": "Brace", "trigger": "targeted", "cost": { "reaction": 1 },
              "use": { "action": "brace" } }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "smite", "name": "Weak", "damage": "2" }, { "action": "smite", "name": "Strong", "damage": "7" } ], "xp": 0 }
            """);
        modules.Write("rules/guard.json", """
            { "type": "monster", "id": "guard", "name": "Guard", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "5" }, "actions": [],
              "reactions": [ "guard_reaction" ], "xp": 0 }
            """);
        return root;
    }

    private static string WriteLiveRuleset(TempModules modules, bool includeSpray = false, bool includeMovement = false, string sprayMaximum = "1d2", bool targetCanAct = false)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/movement.json", """{ "type": "attribute", "id": "movement", "name": "Movement", "min": 0, "max": 100, "default": 3 }""");
        string field = includeMovement ? "\"field\": { \"width\": 8, \"height\": 2, \"metric\": \"manhattan\" }," : "";
        modules.Write("rules/combat.json", $$"""
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature",
              "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6,
              {{field}}
              "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/strike.json", """
            { "type": "action", "id": "strike", "name": "Strike", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "2" } ] }
            """);
        if (includeSpray)
        {
            modules.Write("rules/spray.json", $$"""
                { "type": "action", "id": "spray", "name": "Spray", "cost": { "turn": 1 }, "target": "enemy", "max_targets": "{{sprayMaximum}}",
                  "always": [ { "op": "damage", "amount": "1" } ] }
                """);
        }

        if (includeMovement)
        {
            modules.Write("rules/advance.json", """
                { "type": "action", "id": "advance", "name": "Advance", "cost": { "turn": 1 }, "target": "enemy",
                  "valid_target": "combat.distance > 1", "always": [ { "op": "move", "distance": "self.movement" } ] }
                """);
        }

        string actions = includeMovement
            ? "[ { \"action\": \"advance\" } ]"
            : includeSpray
                ? "[ { \"action\": \"strike\" }, { \"action\": \"spray\" } ]"
                : "[ { \"action\": \"strike\" } ]";
        modules.Write("rules/attacker.json", $$"""
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15", "movement": "3" }, "actions": {{actions}}, "xp": 0 }
            """);
        string targetActions = targetCanAct ? "[ { \"action\": \"strike\" } ]" : "[]";
        modules.Write("rules/target.json", $$"""
            { "type": "monster", "id": "target", "name": "Target", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "5", "movement": "3" }, "actions": {{targetActions}}, "xp": 0 }
            """);
        return root;
    }

    private static string WriteWideMovementRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/movement.json", """{ "type": "attribute", "id": "movement", "name": "Movement", "min": 0, "max": 100, "default": 12 }""");
        modules.Write("rules/combat.json", """
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature",
              "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6,
              "field": { "width": 31, "height": 31, "metric": "manhattan" },
              "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/advance.json", """
            { "type": "action", "id": "advance", "name": "Advance", "cost": { "turn": 1 }, "target": "enemy",
              "valid_target": "combat.distance > 1", "always": [ { "op": "move", "distance": "self.movement", "toward": "away", "beyond": "2" } ] }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Mover", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15", "movement": "12" },
              "actions": [ { "action": "advance" } ], "xp": 0 }
            """);
        modules.Write("rules/target.json", """
            { "type": "monster", "id": "target", "name": "Target", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "5", "movement": "12" }, "actions": [], "xp": 0 }
            """);
        return root;
    }

    private static void WithDice(Action<DiceRoller> work)
    {
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(1, "live-combat-tests"));
            work(new DiceRoller(engine.Random, stream));
        });
    }
}
