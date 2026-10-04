using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

/// <summary>
/// Live decision checks for optional reactions, post-roll resources and
/// elective initiative. These tests deliberately observe and serialize at the
/// suspension boundary so a pending choice cannot hide a replay or a second
/// resource spend.
/// </summary>
public sealed class LiveCombatInterruptTests
{
    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 2)]
    public void AManualHitReactionCanBeDeclinedOrAcceptedOnce(bool accept, decimal expectedDamage)
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(guard.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatDecision decision = AssertDecision(waiting, CombatDecisionKind.Interrupt);
            CombatDecisionOption option = Assert.Single(decision.Options!);
            int rollsBeforeObservation = dice.Rolls.Count;
            int factsBeforeObservation = waiting.Facts.Count;

            CombatObservation observed = runner.Observe();
            CombatObservation observedAgain = runner.Observe();
            Assert.Equal(rollsBeforeObservation, dice.Rolls.Count);
            Assert.Equal(factsBeforeObservation, observedAgain.Facts.Count);
            Assert.Equal(decision.Id, observed.PendingDecision!.Id);
            Assert.Equal(decision.Id, observedAgain.PendingDecision!.Id);

            CombatCommand.Decide answer = new(decision.Id, accept ? option.Id : null);
            CombatCommandResult result = runner.Submit(answer);
            Assert.True(result.Accepted, result.Reason);

            DamageFact damage = Assert.Single(result.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Guard");
            Assert.Equal(expectedDamage, damage.Amount);
            Assert.Equal(accept ? 1 : 0, result.Observation.Facts.OfType<ReactionFact>().Count());

            int rollsAfterAnswer = dice.Rolls.Count;
            int factsAfterAnswer = result.Observation.Facts.Count;
            CombatCommandResult repeated = runner.Submit(answer);
            Assert.False(repeated.Accepted);
            Assert.Equal(rollsAfterAnswer, dice.Rolls.Count);
            Assert.Equal(factsAfterAnswer, repeated.Observation.Facts.Count);
        });
    }

    [Fact]
    public void APostRollChoiceCommitsTheInitialRollAndSpendsOnce()
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatObservation turn = runner.Start(1);
            CombatDecision action = AssertDecision(turn, CombatDecisionKind.Action);
            CombatActionChoice strike = Assert.Single(action.Actions);
            CombatCommandResult submitted = runner.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, [guard.Id]));
            Assert.True(submitted.Accepted, submitted.Reason);

            CombatDecision postRoll = AssertDecision(submitted.Observation, CombatDecisionKind.PostRoll);
            Assert.NotNull(postRoll.Check);
            int rollsAtChoice = dice.Rolls.Count;
            int luckBefore = decimal.ToInt32(submitted.Observation.Combatants.Single(member => member.Id == attacker.Id).Tracks["luck"]!.Value);
            Assert.Equal(1, luckBefore);

            CombatObservation repeated = runner.Observe();
            Assert.Equal(rollsAtChoice, dice.Rolls.Count);
            Assert.Equal(postRoll.Id, repeated.PendingDecision!.Id);
            Assert.Equal(postRoll.Check, repeated.PendingDecision.Check);

            CombatDecisionOption option = Assert.Single(postRoll.Options!);
            CombatCommand.Decide answer = new(postRoll.Id, option.Id);
            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner resumed = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
            ], dice, saved);
            Assert.Equal(postRoll.Check, resumed.Observe().PendingDecision!.Check);
            CombatCommandResult accepted = resumed.Submit(answer);
            Assert.True(accepted.Accepted, accepted.Reason);
            Assert.Equal(0, decimal.ToInt32(accepted.Observation.Combatants.Single(member => member.Id == attacker.Id).Tracks["luck"]!.Value));
            Assert.Single(accepted.Observation.Facts.OfType<PostRollFact>());
            Assert.Single(accepted.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Guard");

            int rollsAfterAnswer = dice.Rolls.Count;
            CombatCommandResult repeatedAnswer = resumed.Submit(answer);
            Assert.False(repeatedAnswer.Accepted);
            Assert.Equal(rollsAfterAnswer, dice.Rolls.Count);
            Assert.Equal(0, decimal.ToInt32(repeatedAnswer.Observation.Combatants.Single(member => member.Id == attacker.Id).Tracks["luck"]!.Value));
        });
    }

    [Fact]
    public void PostRollPolicyValuesAreCommittedBeforeJsonRestore()
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        modules.Write("rules/strike_check.json", """
            { "type": "check", "id": "strike_check", "name": "Strike", "roll": "9", "target": "10", "succeeds": "at-least",
              "post_roll": [ { "name": "Luck", "track": "luck", "cost": "1", "bonus": "1d4", "score": "1d6" } ] }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatObservation turn = runner.Start(1);
            CombatActionChoice strike = Assert.Single(AssertDecision(turn, CombatDecisionKind.Action).Actions);
            CombatCommandResult submitted = runner.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, [guard.Id]));
            CombatDecision offered = AssertDecision(submitted.Observation, CombatDecisionKind.PostRoll);
            CombatDecisionOption option = Assert.Single(offered.Options!);
            int drawsAtOffer = dice.Rolls.Count;
            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));

            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
            ], dice, saved);
            CombatDecision restoredDecision = AssertDecision(restored.Observe(), CombatDecisionKind.PostRoll);
            Assert.Equal(drawsAtOffer, dice.Rolls.Count);
            Assert.Equal(offered.Options, restoredDecision.Options);

            CombatCommandResult accepted = restored.Submit(new CombatCommand.Decide(restoredDecision.Id, option.Id));
            Assert.True(accepted.Accepted, accepted.Reason);
            Assert.Equal(drawsAtOffer, dice.Rolls.Count);
        });
    }

    [Fact]
    public void ReactionWhenIsCommittedBeforeJsonRestore()
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        modules.Write("rules/guard_reaction.json", """
            { "type": "reaction", "id": "guard_reaction", "name": "Guard", "trigger": "hit", "when": "1d2 >= 1", "cost": { "reaction": 1 },
              "use": { "action": "guard" } }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(guard.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatDecision decision = AssertDecision(waiting, CombatDecisionKind.Interrupt);
            int drawsAtOffer = dice.Rolls.Count;
            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
            ], dice, saved);

            CombatCommandResult accepted = restored.Submit(new CombatCommand.Decide(decision.Id, Assert.Single(decision.Options!).Id));
            Assert.True(accepted.Accepted, accepted.Reason);
            Assert.Equal(drawsAtOffer, dice.Rolls.Count);
            Assert.Single(accepted.Observation.Facts.OfType<ReactionFact>());
        });
    }

    [Fact]
    public void MultiTargetInterruptResumesRemainingTargetsAfterJsonRestore()
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        modules.Write("rules/strike.json", """
            { "type": "action", "id": "strike", "name": "Strike", "cost": { "turn": 1 }, "target": "all_enemies",
              "always": [ { "op": "damage", "amount": "4" } ] }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant first = Combatant.FromMonster(rules, guardDefinition, "First guard", evaluator);
            Combatant second = Combatant.FromMonster(rules, guardDefinition, "Second guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [first, second]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(first.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(second.Id, CombatControlMode.Manual));

            CombatObservation turn = runner.Start(1);
            CombatActionChoice strike = Assert.Single(AssertDecision(turn, CombatDecisionKind.Action).Actions);
            CombatCommandResult submitted = runner.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, [first.Id, second.Id]));
            CombatDecision firstInterrupt = AssertDecision(submitted.Observation, CombatDecisionKind.Interrupt);
            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            Assert.Contains(saved.OperationStack, frame => frame.TargetIds?.Count == 2 && frame.TargetIndex == 0);

            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "First guard", restoredEvaluator), Combatant.FromMonster(rules, guardDefinition, "Second guard", restoredEvaluator)]),
            ], dice, saved);

            CombatCommandResult afterFirst = restored.Submit(new CombatCommand.Decide(firstInterrupt.Id));
            CombatDecision secondInterrupt = AssertDecision(afterFirst.Observation, CombatDecisionKind.Interrupt);
            Assert.Equal(second.Id, secondInterrupt.Interrupt!.ReactorId);
            CombatCommandResult finished = restored.Submit(new CombatCommand.Decide(secondInterrupt.Id));
            Assert.True(finished.Accepted, finished.Reason);
            Assert.Equal(2, finished.Observation.Facts.OfType<DamageFact>().Count(fact => fact.Who is "First guard" or "Second guard"));
        });
    }

    [Theory]
    [InlineData("damaged", false)]
    [InlineData("ally_defeated", true)]
    public void MultiTargetDamageInterruptResumesRemainingTargets(string trigger, bool defeatFirst)
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        modules.Write("rules/strike.json", """
            { "type": "action", "id": "strike", "name": "Strike", "cost": { "turn": 1 }, "target": "all_enemies",
              "always": [ { "op": "damage", "amount": "2" } ] }
            """);
        modules.Write("rules/guard.json", """
            { "type": "action", "id": "guard", "name": "Guard", "cost": { "reaction": 0 }, "target": "self", "always": [] }
            """);
        modules.Write("rules/guard_reaction.json", $$"""
            { "type": "reaction", "id": "guard_reaction", "name": "Guard", "trigger": "{{trigger}}", "cost": { "reaction": 1 },
              "use": { "action": "guard" } }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant first = Combatant.FromMonster(rules, guardDefinition, "First guard", evaluator);
            Combatant second = Combatant.FromMonster(rules, guardDefinition, "Second guard", evaluator);
            if (defeatFirst)
            {
                first.Creature.Track("hit_points").Current = 1;
            }

            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [first, second]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(first.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(second.Id, CombatControlMode.Manual));

            CombatObservation turn = runner.Start(1);
            CombatActionChoice strike = Assert.Single(AssertDecision(turn, CombatDecisionKind.Action).Actions);
            CombatCommandResult submitted = runner.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, [first.Id, second.Id]));
            CombatDecision firstInterrupt = AssertDecision(submitted.Observation, CombatDecisionKind.Interrupt);
            Assert.Equal(trigger, firstInterrupt.Interrupt!.Trigger);
            CombatCommandResult finished;
            if (defeatFirst)
            {
                // The first target is already out, so its surviving ally is
                // the only reactor for the first operation. The next target
                // does not have a surviving ally left to avenge it.
                Assert.Equal(second.Id, firstInterrupt.Interrupt.ReactorId);
                finished = runner.Submit(new CombatCommand.Decide(firstInterrupt.Id));
            }
            else
            {
                Assert.Equal(first.Id, firstInterrupt.Interrupt.ReactorId);
                CombatCommandResult afterFirst = runner.Submit(new CombatCommand.Decide(firstInterrupt.Id));
                CombatDecision secondInterrupt = AssertDecision(afterFirst.Observation, CombatDecisionKind.Interrupt);
                Assert.Equal(trigger, secondInterrupt.Interrupt!.Trigger);
                Assert.Equal(second.Id, secondInterrupt.Interrupt.ReactorId);
                finished = runner.Submit(new CombatCommand.Decide(secondInterrupt.Id));
            }

            Assert.True(finished.Accepted, finished.Reason);
            Assert.Single(finished.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "First guard" && fact.Amount == 2);
            Assert.Single(finished.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Second guard" && fact.Amount == 2);
        });
    }

    [Fact]
    public void CommittedTargetRollsSurviveJsonRestore()
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        modules.Write("rules/strike.json", """
            { "type": "action", "id": "strike", "name": "Strike", "cost": { "turn": 1 }, "target": "enemy", "max_targets": "1d2",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatActionChoice strike = Assert.Single(AssertDecision(runner.Start(1), CombatDecisionKind.Action).Actions);
            CombatCommandResult targetWaiting = runner.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, []));
            CombatDecision targetDecision = AssertDecision(targetWaiting.Observation, CombatDecisionKind.Targets);
            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            Assert.NotEmpty(saved.CommittedTargetRolls);

            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
            ], dice, saved);
            CombatCommandResult finished = restored.Submit(new CombatCommand.UseAction(attacker.Id, targetDecision.ActionId!, [guard.Id]));
            Assert.True(finished.Accepted, finished.Reason);
            ActionFact action = Assert.Single(finished.Observation.Facts.OfType<ActionFact>(), fact => fact.Who == "Attacker");
            Assert.Equal(saved.CommittedTargetRolls, action.Rolls);
        });
    }

    [Fact]
    public void NestedCounterInterruptResumesOuterDamageAfterJsonRestore()
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        modules.Write("rules/strike.json", """
            { "type": "action", "id": "strike", "name": "Strike", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "4" } ] }
            """);
        modules.Write("rules/guard.json", """
            { "type": "action", "id": "guard", "name": "Guard", "cost": { "reaction": 0 }, "target": "enemy",
              "always": [ { "op": "reduce_damage", "to": "self", "amount": "2" }, { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/counter.json", """
            { "type": "action", "id": "counter", "name": "Counter", "cost": { "reaction": 0 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/counter_reaction.json", """
            { "type": "reaction", "id": "counter_reaction", "name": "Counter", "trigger": "hit", "counter": true, "cost": { "reaction": 1 },
              "use": { "action": "counter" } }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15" }, "actions": [ { "action": "strike" } ], "reactions": [ "counter_reaction" ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(guard.Id, CombatControlMode.Manual));

            CombatObservation turn = runner.Start(1);
            CombatActionChoice strike = Assert.Single(AssertDecision(turn, CombatDecisionKind.Action).Actions);
            CombatCommandResult outerWaiting = runner.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, [guard.Id]));
            CombatDecision outer = AssertDecision(outerWaiting.Observation, CombatDecisionKind.Interrupt);
            CombatCommandResult nestedWaiting = runner.Submit(new CombatCommand.Decide(outer.Id, Assert.Single(outer.Options!).Id));
            CombatDecision nested = AssertDecision(nestedWaiting.Observation, CombatDecisionKind.Interrupt);
            Assert.Equal(attacker.Id, nested.Interrupt!.ReactorId);
            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            Assert.Single(saved.ParentInterrupts);

            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
            ], dice, saved);
            CombatCommandResult finished = restored.Submit(new CombatCommand.Decide(nested.Id));
            Assert.True(finished.Accepted, finished.Reason);
            Assert.Contains(finished.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Attacker" && fact.Amount == 1);
            Assert.Contains(finished.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Guard" && fact.Amount == 2);
        });
    }

    [Fact]
    public void NestedCounterInterruptResumesParentTargetedActionAfterJsonRestore()
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        modules.Write("rules/strike.json", """
            { "type": "action", "id": "strike", "name": "Strike", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "4" } ] }
            """);
        modules.Write("rules/guard.json", """
            { "type": "action", "id": "guard", "name": "Guard", "cost": { "reaction": 0 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/guard_reaction.json", """
            { "type": "reaction", "id": "guard_reaction", "name": "Guard", "trigger": "targeted", "cost": { "reaction": 1 },
              "use": { "action": "guard" } }
            """);
        modules.Write("rules/counter.json", """
            { "type": "action", "id": "counter", "name": "Counter", "cost": { "reaction": 0 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/counter_reaction.json", """
            { "type": "reaction", "id": "counter_reaction", "name": "Counter", "trigger": "hit", "counter": true, "cost": { "reaction": 1 },
              "use": { "action": "counter" } }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15" }, "actions": [ { "action": "strike" } ], "reactions": [ "counter_reaction" ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(guard.Id, CombatControlMode.Manual));

            CombatActionChoice strike = Assert.Single(AssertDecision(runner.Start(1), CombatDecisionKind.Action).Actions);
            CombatCommandResult outerWaiting = runner.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, [guard.Id]));
            CombatDecision outer = AssertDecision(outerWaiting.Observation, CombatDecisionKind.Interrupt);
            Assert.Equal("targeted", outer.Interrupt!.Trigger);
            CombatCommandResult nestedWaiting = runner.Submit(new CombatCommand.Decide(outer.Id, Assert.Single(outer.Options!).Id));
            CombatDecision nested = AssertDecision(nestedWaiting.Observation, CombatDecisionKind.Interrupt);
            Assert.Equal("hit", nested.Interrupt!.Trigger);
            Assert.Equal(attacker.Id, nested.Interrupt.ReactorId);
            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            Assert.Contains(saved.ParentInterrupts, frame => frame.Interrupt.Trigger == "targeted");

            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
            ], dice, saved);
            CombatCommandResult finished = restored.Submit(new CombatCommand.Decide(nested.Id));
            Assert.True(finished.Accepted, finished.Reason);
            Assert.Single(finished.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Attacker" && fact.Amount == 1);
            Assert.Single(finished.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Guard" && fact.Amount == 4);
        });
    }

    [Theory]
    [InlineData("damaged", false)]
    [InlineData("ally_defeated", true)]
    public void NestedCounterInterruptResumesParentAfterDamageOperationJsonRestore(string trigger, bool defeatFirst)
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        modules.Write("rules/strike.json", """
            { "type": "action", "id": "strike", "name": "Strike", "cost": { "turn": 1 }, "target": "all_enemies",
              "always": [ { "op": "damage", "amount": "4" } ] }
            """);
        modules.Write("rules/guard.json", """
            { "type": "action", "id": "guard", "name": "Guard", "cost": { "reaction": 0 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/guard_reaction.json", $$"""
            { "type": "reaction", "id": "guard_reaction", "name": "Guard", "trigger": "{{trigger}}", "cost": { "reaction": 1 },
              "use": { "action": "guard" } }
            """);
        modules.Write("rules/quiet_guard.json", """
            { "type": "monster", "id": "quiet_guard", "name": "Quiet guard", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "5" }, "actions": [], "xp": 0 }
            """);
        modules.Write("rules/counter.json", """
            { "type": "action", "id": "counter", "name": "Counter", "cost": { "reaction": 0 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/counter_reaction.json", """
            { "type": "reaction", "id": "counter_reaction", "name": "Counter", "trigger": "hit", "counter": true, "cost": { "reaction": 1 },
              "use": { "action": "counter" } }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15" }, "actions": [ { "action": "strike" } ], "reactions": [ "counter_reaction" ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;
        Definition quietGuardDefinition = rules.Find(DefinitionTypes.Monster, "quiet_guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant first = Combatant.FromMonster(
                rules,
                defeatFirst ? quietGuardDefinition : guardDefinition,
                "First guard",
                evaluator);
            Combatant second = Combatant.FromMonster(
                rules,
                defeatFirst ? guardDefinition : quietGuardDefinition,
                "Second guard",
                evaluator);
            if (defeatFirst)
            {
                first.Creature.Track("hit_points").Current = 1;
            }

            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [first, second]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(first.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(second.Id, CombatControlMode.Manual));

            CombatActionChoice strike = Assert.Single(AssertDecision(runner.Start(1), CombatDecisionKind.Action).Actions);
            CombatCommandResult outerWaiting = runner.Submit(new CombatCommand.UseAction(attacker.Id, strike.Id, [first.Id, second.Id]));
            CombatDecision outer = AssertDecision(outerWaiting.Observation, CombatDecisionKind.Interrupt);
            Assert.Equal(trigger, outer.Interrupt!.Trigger);
            Assert.Equal(defeatFirst ? second.Id : first.Id, outer.Interrupt.ReactorId);
            CombatCommandResult nestedWaiting = runner.Submit(new CombatCommand.Decide(outer.Id, Assert.Single(outer.Options!).Id));
            CombatDecision nested = AssertDecision(nestedWaiting.Observation, CombatDecisionKind.Interrupt);
            Assert.Equal("hit", nested.Interrupt!.Trigger);
            Assert.Equal(attacker.Id, nested.Interrupt.ReactorId);
            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            Assert.Contains(saved.ParentInterrupts, frame => frame.Interrupt.Trigger == trigger);

            Evaluator restoredEvaluator = new(rules, dice);
            Combatant restoredAttacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator);
            Combatant restoredFirst = Combatant.FromMonster(
                rules,
                defeatFirst ? quietGuardDefinition : guardDefinition,
                "First guard",
                restoredEvaluator);
            Combatant restoredSecond = Combatant.FromMonster(
                rules,
                defeatFirst ? guardDefinition : quietGuardDefinition,
                "Second guard",
                restoredEvaluator);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Attackers", [restoredAttacker]),
                new CombatSide("Guards", [restoredFirst, restoredSecond]),
            ], dice, saved);
            CombatCommandResult finished = restored.Submit(new CombatCommand.Decide(nested.Id));
            Assert.True(finished.Accepted, finished.Reason);
            Assert.Single(finished.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Attacker" && fact.Amount == 1);
            Assert.Equal(defeatFirst ? -3 : 16, finished.Observation.Combatants.Single(member => member.Name == "First guard").Tracks["hit_points"]);
            Assert.Single(finished.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Second guard" && fact.Amount == 4);
        });
    }

    [Fact]
    public void PendingReactionSurvivesJsonRoundTripAndResumeWithoutReroll()
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(guard.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatDecision decision = AssertDecision(waiting, CombatDecisionKind.Interrupt);
            CombatContinuationState captured = runner.Capture();
            string json = CombatContinuationState.ToJson(captured);
            CombatContinuationState roundTrip = CombatContinuationState.FromJson(json);
            Assert.Equal(decision.Id, roundTrip.PendingDecision!.Id);
            Assert.NotNull(roundTrip.PendingInterrupt);
            Assert.NotNull(roundTrip.PendingOperation);

            int rollsBeforeRestore = dice.Rolls.Count;
            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
            ], dice, roundTrip);
            Assert.Equal(rollsBeforeRestore, dice.Rolls.Count);
            Assert.Equal(decision.Id, restored.Observe().PendingDecision!.Id);

            CombatCommandResult declined = restored.Submit(new CombatCommand.Decide(decision.Id));
            Assert.True(declined.Accepted, declined.Reason);
            Assert.Single(declined.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Guard" && fact.Amount == 4);
            Assert.Empty(declined.Observation.Facts.OfType<ReactionFact>());
            Assert.Equal(rollsBeforeRestore, dice.Rolls.Count);
        });
    }

    [Fact]
    public void ElectiveInitiativeExposesChoiceAndRecordsItOnce()
    {
        using TempModules modules = new();
        string root = InterruptRuleset(modules);
        modules.Write("rules/elective.json", """
            { "type": "combat", "id": "elective", "name": "Elective", "initiative_mode": "elective", "initiative_score": "target.str",
              "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ],
              "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "elective", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant first = Combatant.FromMonster(rules, attackerDefinition, "First", evaluator);
            Combatant second = Combatant.FromMonster(rules, guardDefinition, "Second", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Party", [first]),
                new CombatSide("Foes", [second]),
            ], dice);
            Assert.True(runner.SetController(first.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(second.Id, CombatControlMode.Manual));

            CombatObservation waiting = runner.Start(1);
            CombatDecision initiative = AssertDecision(waiting, CombatDecisionKind.Initiative);
            Assert.NotEmpty(initiative.Options!);
            CombatDecisionOption choice = initiative.Options!.First();
            int rollsBeforeChoice = dice.Rolls.Count;
            Assert.Equal(initiative.Id, runner.Observe().PendingDecision!.Id);
            Assert.Equal(rollsBeforeChoice, dice.Rolls.Count);

            CombatCommandResult selected = runner.Submit(new CombatCommand.Decide(initiative.Id, choice.Id));
            Assert.True(selected.Accepted, selected.Reason);
            Assert.Single(selected.Observation.Facts.OfType<InitiativeChoiceFact>());
            Assert.Equal(rollsBeforeChoice, dice.Rolls.Count);

            CombatCommandResult repeated = runner.Submit(new CombatCommand.Decide(initiative.Id, choice.Id));
            Assert.False(repeated.Accepted);
            Assert.Single(repeated.Observation.Facts.OfType<InitiativeChoiceFact>());
        });
    }

    [Fact]
    public void ALeavesReachReactionResumesTheAuthoredMovementBoundary()
    {
        using TempModules modules = new();
        string root = MovementInterruptRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "movement_duel", out _)!;
        Definition moverDefinition = rules.Find(DefinitionTypes.Monster, "mover", out _)!;
        Definition watcherDefinition = rules.Find(DefinitionTypes.Monster, "watcher", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant mover = Combatant.FromMonster(rules, moverDefinition, "Mover", evaluator);
            Combatant watcher = Combatant.FromMonster(rules, watcherDefinition, "Watcher", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Movers", [mover]),
                new CombatSide("Watchers", [watcher]),
            ], dice, setup: new CombatSetup([new Cell(1, 0), new Cell(2, 0)], SurprisedSide: -1));
            Assert.True(runner.SetController(mover.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(watcher.Id, CombatControlMode.Manual));

            CombatObservation turn = runner.Start(1);
            CombatActionChoice advance = Assert.Single(AssertDecision(turn, CombatDecisionKind.Action).Actions);
            CombatMoveChoice path = Assert.Single(advance.Moves, move => move.Destination == new Cell(0, 0));
            CombatCommandResult waiting = runner.Submit(new CombatCommand.UseAction(mover.Id, advance.Id, [watcher.Id], path.Path));
            CombatDecision interrupt = AssertDecision(waiting.Observation, CombatDecisionKind.Interrupt);
            Assert.Equal("leaves_reach", interrupt.Interrupt!.Trigger);
            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            Assert.NotNull(saved.PendingMovement);

            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Movers", [Combatant.FromMonster(rules, moverDefinition, "Mover", restoredEvaluator)]),
                new CombatSide("Watchers", [Combatant.FromMonster(rules, watcherDefinition, "Watcher", restoredEvaluator)]),
            ], dice, saved, setup: new CombatSetup([new Cell(1, 0), new Cell(2, 0)], SurprisedSide: -1));
            CombatCommandResult declined = restored.Submit(new CombatCommand.Decide(interrupt.Id));
            Assert.True(declined.Accepted, declined.Reason);
            Assert.Equal(new Cell(0, 0), declined.Observation.Combatants.Single(member => member.Id == mover.Id).Position);
            Assert.Empty(declined.Observation.Facts.OfType<ReactionFact>());
            Assert.Single(declined.Observation.Facts.OfType<MoveFact>());
        });
    }

    private static CombatDecision AssertDecision(CombatObservation observation, CombatDecisionKind kind)
    {
        Assert.Equal(kind, observation.PendingDecision?.Kind);
        Assert.NotNull(observation.PendingDecision);
        return observation.PendingDecision!;
    }

    private static string InterruptRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/luck.json", """
            { "type": "track", "id": "luck", "name": "Luck", "max": "1", "start": "1", "min": "0" }
            """);
        modules.Write("rules/combat.json", """
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature",
              "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6,
              "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ],
              "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/strike_check.json", """
            { "type": "check", "id": "strike_check", "name": "Strike", "roll": "9", "target": "10", "succeeds": "at-least",
              "post_roll": [ { "name": "Luck +2", "track": "luck", "cost": "1", "bonus": "2", "score": "1" } ] }
            """);
        modules.Write("rules/strike.json", """
            { "type": "action", "id": "strike", "name": "Strike", "cost": { "turn": 1 }, "target": "enemy", "check": "strike_check",
              "outcomes": { "success": [ { "op": "damage", "amount": "4" } ] } }
            """);
        modules.Write("rules/guard.json", """
            { "type": "action", "id": "guard", "name": "Guard", "cost": { "reaction": 0 }, "target": "self",
              "always": [ { "op": "reduce_damage", "amount": "2", "to": "self" } ] }
            """);
        modules.Write("rules/guard_reaction.json", """
            { "type": "reaction", "id": "guard_reaction", "name": "Guard", "trigger": "hit", "cost": { "reaction": 1 },
              "use": { "action": "guard" } }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15" }, "actions": [ { "action": "strike" } ], "xp": 0 }
            """);
        modules.Write("rules/guard_monster.json", """
            { "type": "monster", "id": "guard", "name": "Guard", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "5" }, "actions": [], "reactions": [ "guard_reaction" ], "xp": 0 }
            """);
        return root;
    }

    private static string MovementInterruptRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/movement.json", """{ "type": "attribute", "id": "movement", "name": "Movement", "min": 0, "max": 10, "default": 3 }""");
        modules.Write("rules/movement_duel.json", """
            { "type": "combat", "id": "movement_duel", "name": "Movement duel", "initiative": "self.str", "initiative_by": "creature",
              "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6, "field": { "width": 8, "height": 1, "metric": "manhattan" },
              "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ],
              "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/advance.json", """
            { "type": "action", "id": "advance", "name": "Withdraw", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "move", "distance": "self.movement", "toward": "away", "beyond": "1" } ] }
            """);
        modules.Write("rules/parting.json", """
            { "type": "action", "id": "parting", "name": "Parting blow", "cost": { "reaction": 0 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/parting_reaction.json", """
            { "type": "reaction", "id": "parting_reaction", "name": "Parting blow", "trigger": "leaves_reach", "cost": { "reaction": 1 },
              "use": { "action": "parting" } }
            """);
        modules.Write("rules/mover.json", """
            { "type": "monster", "id": "mover", "name": "Mover", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15", "movement": "3" }, "actions": [ { "action": "advance" } ], "xp": 0 }
            """);
        modules.Write("rules/watcher.json", """
            { "type": "monster", "id": "watcher", "name": "Watcher", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "5", "movement": "3" }, "actions": [], "reactions": [ "parting_reaction" ], "xp": 0 }
            """);
        return root;
    }

    private static void WithDice(Action<DiceRoller> work)
    {
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(1, "live-combat-interrupt-tests"));
            work(new DiceRoller(engine.Random, stream));
        });
    }

    [Fact]
    public void NestedCounterInterruptResumesParentLeavesReachMovementAfterJsonRestore()
    {
        using TempModules modules = new();
        string root = MovementInterruptRuleset(modules);
        modules.Write("rules/counter.json", """
            { "type": "action", "id": "counter", "name": "Counter", "cost": { "reaction": 0 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/counter_reaction.json", """
            { "type": "reaction", "id": "counter_reaction", "name": "Counter", "trigger": "hit", "counter": true, "cost": { "reaction": 1 },
              "use": { "action": "counter" } }
            """);
        modules.Write("rules/mover.json", """
            { "type": "monster", "id": "mover", "name": "Mover", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15", "movement": "3" },
              "actions": [ { "action": "advance" } ], "reactions": [ "counter_reaction" ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "movement_duel", out _)!;
        Definition moverDefinition = rules.Find(DefinitionTypes.Monster, "mover", out _)!;
        Definition watcherDefinition = rules.Find(DefinitionTypes.Monster, "watcher", out _)!;

        WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant mover = Combatant.FromMonster(rules, moverDefinition, "Mover", evaluator);
            Combatant watcher = Combatant.FromMonster(rules, watcherDefinition, "Watcher", evaluator);
            CombatSetup setup = new([new Cell(1, 0), new Cell(2, 0)], SurprisedSide: -1);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Movers", [mover]),
                new CombatSide("Watchers", [watcher]),
            ], dice, setup: setup);
            Assert.True(runner.SetController(mover.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(watcher.Id, CombatControlMode.Manual));

            CombatActionChoice advance = Assert.Single(AssertDecision(runner.Start(1), CombatDecisionKind.Action).Actions);
            CombatMoveChoice path = Assert.Single(advance.Moves, move => move.Destination == new Cell(0, 0));
            CombatCommandResult outerWaiting = runner.Submit(new CombatCommand.UseAction(mover.Id, advance.Id, [watcher.Id], path.Path));
            CombatDecision outer = AssertDecision(outerWaiting.Observation, CombatDecisionKind.Interrupt);
            Assert.Equal("leaves_reach", outer.Interrupt!.Trigger);
            CombatCommandResult nestedWaiting = runner.Submit(new CombatCommand.Decide(outer.Id, Assert.Single(outer.Options!).Id));
            CombatDecision nested = AssertDecision(nestedWaiting.Observation, CombatDecisionKind.Interrupt);
            Assert.Equal("hit", nested.Interrupt!.Trigger);
            Assert.Equal(mover.Id, nested.Interrupt.ReactorId);
            CombatContinuationState saved = CombatContinuationState.FromJson(CombatContinuationState.ToJson(runner.Capture()));
            Assert.Contains(saved.ParentInterrupts, frame => frame.Interrupt.Trigger == "leaves_reach");

            Evaluator restoredEvaluator = new(rules, dice);
            CombatRunner restored = CombatRunner.Restore(rules, combat,
            [
                new CombatSide("Movers", [Combatant.FromMonster(rules, moverDefinition, "Mover", restoredEvaluator)]),
                new CombatSide("Watchers", [Combatant.FromMonster(rules, watcherDefinition, "Watcher", restoredEvaluator)]),
            ], dice, saved, setup: setup);
            CombatCommandResult finished = restored.Submit(new CombatCommand.Decide(nested.Id));
            Assert.True(finished.Accepted, finished.Reason);
            Assert.Equal(new Cell(0, 0), finished.Observation.Combatants.Single(member => member.Id == mover.Id).Position);
            Assert.Single(finished.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Mover" && fact.Amount == 1);
            Assert.Single(finished.Observation.Facts.OfType<MoveFact>());
        });
    }
}
