using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

/// <summary>
/// Manual action choices must use the authored legality rules without making
/// observation or save/restore consume a fresh random result.
/// </summary>
public sealed class CombatLegalityTests
{
    [Theory]
    [InlineData("blocked_available")]
    [InlineData("blocked_target")]
    public void DiceBackedLegalityIsRejectedBeforeManualExecution(string blockedAction)
    {
        using TempModules modules = new();
        string root = WriteRandomLegalityRuleset(modules, blockedAction);
        ModuleSet set = ModuleLoader.Load(root, []);
        string path = blockedAction == "blocked_available" ? "$.available" : "$.valid_target";
        ModuleDiagnostic diagnostic = Assert.Single(set.Diagnostics, item => item.Rule == "action.random" && item.JsonPath == path);
        Assert.Contains("deterministic", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("blocked_available")]
    [InlineData("blocked_target")]
    public void DeterministicFalseLegalityRefusalPreservesStateAcrossObservationAndRestore(string blockedAction)
    {
        using TempModules modules = new();
        string root = WriteDeterministicLegalityRuleset(modules, blockedAction);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attacker = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guard = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        LegalityResult uninterrupted = RunIllegalAction(rules, combat, attacker, guard, blockedAction, restore: false);
        LegalityResult restored = RunIllegalAction(rules, combat, attacker, guard, blockedAction, restore: true);

        Assert.Equal(uninterrupted.FinalFacts, restored.FinalFacts);
        Assert.Equal(uninterrupted.AttackerHitPoints, restored.AttackerHitPoints);
        Assert.Equal(uninterrupted.GuardHitPoints, restored.GuardHitPoints);
        Assert.Equal(uninterrupted.CursorAtOffer, restored.CursorAtOffer);
        Assert.Equal(uninterrupted.CursorAfterRefusals, restored.CursorAfterRefusals);
        Assert.Equal(uninterrupted.CursorAfterAcceptedFallback, restored.CursorAfterAcceptedFallback);
        Assert.Equal(uninterrupted.RollsAtOffer, restored.RollsAtOffer);
        Assert.Equal(uninterrupted.RollsAfterRefusals, restored.RollsAfterRefusals);
        Assert.Equal(uninterrupted.RollsAfterAcceptedFallback, restored.RollsAfterAcceptedFallback);

        Assert.DoesNotContain(uninterrupted.ActionIds, id => id == $"rules:{blockedAction}");
        Assert.Empty(uninterrupted.TargetIdsForBlockedAction);
        Assert.Equal(2, uninterrupted.RefusalCount);
        Assert.Equal(2, restored.RefusalCount);
        Assert.Equal(1, uninterrupted.FallbackFactCount);
        Assert.Equal(0, uninterrupted.BlockedDamageFactCount);
        Assert.Equal(11, uninterrupted.AttackerHitPoints);
        Assert.Equal(20, uninterrupted.GuardHitPoints);
    }

    [Theory]
    [InlineData("blocked_available")]
    [InlineData("blocked_target")]
    public void RuntimeLegalityEvaluationFailureIsUnavailableAndCannotMutateState(string blockedAction)
    {
        using TempModules modules = new();
        string root = WriteRuntimeFailureLegalityRuleset(modules, blockedAction);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attacker = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guard = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        LegalityResult uninterrupted = RunIllegalAction(rules, combat, attacker, guard, blockedAction, restore: false);
        LegalityResult restored = RunIllegalAction(rules, combat, attacker, guard, blockedAction, restore: true);

        Assert.Equal(uninterrupted.FinalFacts, restored.FinalFacts);
        Assert.Equal(uninterrupted.AttackerHitPoints, restored.AttackerHitPoints);
        Assert.Equal(uninterrupted.GuardHitPoints, restored.GuardHitPoints);
        Assert.Equal(uninterrupted.CursorAfterRefusals, restored.CursorAfterRefusals);
        Assert.Equal(uninterrupted.CursorAfterAcceptedFallback, restored.CursorAfterAcceptedFallback);
        Assert.DoesNotContain(uninterrupted.ActionIds, id => id == $"rules:{blockedAction}");
        Assert.Empty(uninterrupted.TargetIdsForBlockedAction);
        Assert.Equal(2, uninterrupted.RefusalCount);
        Assert.Equal(1, uninterrupted.FallbackFactCount);
        Assert.Equal(0, uninterrupted.BlockedDamageFactCount);
    }

    [Theory]
    [InlineData("range", "$.range")]
    [InlineData("portions", "$.portions")]
    public void DiceBackedNumericLegalityIsRejectedBeforeChoiceInspection(string numericField, string path)
    {
        using TempModules modules = new();
        string root = WriteNumericRuleset(modules, numericField);
        ModuleSet set = ModuleLoader.Load(root, []);
        ModuleDiagnostic diagnostic = Assert.Single(set.Diagnostics, item => item.Rule == "action.random" && item.JsonPath == path);
        Assert.Contains("deterministic", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("range")]
    [InlineData("portions")]
    public void RuntimeNumericLegalityFailureIsUnavailableAndCannotMutateState(string numericField)
    {
        using TempModules modules = new();
        string root = WriteNumericRuleset(modules, numericField, "1 / 0");
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attacker = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guard = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        LegalityResult result = RunIllegalAction(rules, combat, attacker, guard, "numeric_action", restore: true);

        Assert.DoesNotContain("rules:numeric_action", result.ActionIds);
        Assert.Empty(result.TargetIdsForBlockedAction);
        Assert.Equal(2, result.RefusalCount);
        Assert.Equal(1, result.FallbackFactCount);
        Assert.Equal(0, result.BlockedDamageFactCount);
    }

    [Fact]
    public void DiceBackedDerivedLegalityIsUnavailableToManualAndAutomaticResolvers()
    {
        using TempModules modules = new();
        string root = WriteDerivedLegalityRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            const ulong seed = 9337;
            const string scope = "combat-derived-legality";
            DiceRoller dice = new(engine.Random, seed, scope);
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

            long cursorBeforeOffer = dice.NextRandomKey;
            CombatDecision offered = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            Assert.Contains(offered.Actions, action => action.ActionId == "rules:fallback");
            Assert.DoesNotContain(offered.Actions, action => action.ActionId == "rules:derived_blocked");
            Assert.Equal(cursorBeforeOffer, dice.NextRandomKey);
            Assert.Empty(dice.Rolls);

            CombatCommandResult automatic = runner.StepAutomaticTurn(attacker.Id);
            Assert.True(automatic.Accepted, automatic.Reason);
            Assert.Equal(cursorBeforeOffer, dice.NextRandomKey);
            Assert.DoesNotContain(automatic.Observation.Facts.OfType<ActionFact>(), fact => fact.Action == "Derived blocked");
            Assert.Empty(automatic.Observation.Facts.OfType<DamageFact>());
            Assert.Equal(20, TrackValue(automatic.Observation, guard.Id));
        });
    }

    [Fact]
    public void RefusedRandomSpellCostCommandPreservesQuoteAcrossObservationAndJsonRestore()
    {
        using TempModules modules = new();
        string root = WriteSpellCostRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            const ulong seed = 9337;
            const string scope = "combat-spell-cost-refusal";
            DiceRoller dice = new(engine.Random, seed, scope);
            (CombatRunner runner, Combatant attacker, Combatant guard) = CreateSpellCostCombat(
                rules, combat, attackerDefinition, guardDefinition, dice);

            long cursorBeforeOffer = dice.NextRandomKey;
            int rollsBeforeOffer = dice.Rolls.Count;
            CombatDecision offered = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            CombatActionChoice spell = Assert.Single(offered.Actions, action => action.SpellCosts is not null);
            IReadOnlyDictionary<string, decimal> quote = spell.SpellCosts!;
            decimal quotedMana = Assert.Single(quote).Value;
            Assert.InRange(quotedMana, 1, 2);
            Assert.Equal(cursorBeforeOffer + 1, dice.NextRandomKey);
            Assert.Equal(rollsBeforeOffer + 1, dice.Rolls.Count);

            CombatContinuationState saved = RoundTrip(runner.Capture());
            CombatActionChoice savedSpell = Assert.Single(saved.PendingDecision!.Actions, action => action.Id == spell.Id);
            Assert.Equal(quotedMana, savedSpell.SpellCosts!["mana"]);

            CombatObservation before = runner.Observe();
            string[] factsBefore = before.Facts.Select(FactLine).ToArray();
            decimal? manaBefore = TrackValue(before, attacker.Id, "mana");
            long cursorBefore = dice.NextRandomKey;
            int rollsBefore = dice.Rolls.Count;
            Assert.Equal(spell.Id, before.PendingDecision!.Actions.Single(action => action.Id == spell.Id).Id);

            CombatObservation repeated = runner.Observe();
            Assert.Equal(cursorBefore, dice.NextRandomKey);
            Assert.Equal(rollsBefore, dice.Rolls.Count);
            Assert.Equal(quotedMana, repeated.PendingDecision!.Actions.Single(action => action.Id == spell.Id).SpellCosts!["mana"]);

            CombatCommandResult refused = runner.Submit(new CombatCommand.UseAction(
                attacker.Id,
                spell.Id,
                ["missing-target"]));
            Assert.False(refused.Accepted, refused.Reason);
            Assert.Equal(cursorBefore, dice.NextRandomKey);
            Assert.Equal(rollsBefore, dice.Rolls.Count);
            Assert.Equal(factsBefore, refused.Observation.Facts.Select(FactLine));
            Assert.Equal(manaBefore, TrackValue(refused.Observation, attacker.Id, "mana"));
            Assert.Equal(quotedMana, refused.Observation.PendingDecision!.Actions.Single(action => action.Id == spell.Id).SpellCosts!["mana"]);

            DiceRoller restoredDice = new(engine.Random, seed, scope, saved.NextRandomKey);
            Evaluator restoredEvaluator = new(rules, restoredDice);
            CombatRunner restored = CombatRunner.Restore(
                rules,
                combat,
                [
                    new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                    new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
                ],
                restoredDice,
                saved);
            CombatObservation restoredOffered = restored.Observe();
            CombatActionChoice restoredSpell = Assert.Single(restoredOffered.PendingDecision!.Actions, action => action.Id == spell.Id);
            Assert.Equal(quotedMana, restoredSpell.SpellCosts!["mana"]);
            long restoredCursorBefore = restoredDice.NextRandomKey;
            int restoredRollsBefore = restoredDice.Rolls.Count;

            Assert.Equal(restoredOffered.PendingDecision.Id, restored.Observe().PendingDecision!.Id);
            Assert.Equal(restoredCursorBefore, restoredDice.NextRandomKey);
            Assert.Equal(restoredRollsBefore, restoredDice.Rolls.Count);

            CombatCommandResult restoredRefused = restored.Submit(new CombatCommand.UseAction(
                restoredOffered.PendingDecision.ActorId,
                restoredSpell.Id,
                ["missing-target"]));
            Assert.False(restoredRefused.Accepted, restoredRefused.Reason);
            Assert.Equal(restoredCursorBefore, restoredDice.NextRandomKey);
            Assert.Equal(restoredRollsBefore, restoredDice.Rolls.Count);
            Assert.Equal(factsBefore, restoredRefused.Observation.Facts.Select(FactLine));
            Assert.Equal(manaBefore, TrackValue(restoredRefused.Observation, attacker.Id, "mana"));
            Assert.Equal(quotedMana, restoredRefused.Observation.PendingDecision!.Actions.Single(action => action.Id == spell.Id).SpellCosts!["mana"]);
        });
    }

    [Fact]
    public void AcceptedRandomSpellPaysCommittedQuoteOnceAcrossJsonRestore()
    {
        using TempModules modules = new();
        string root = WriteSpellCostRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            const ulong seed = 9337;
            const string scope = "combat-spell-cost-accept";
            DiceRoller dice = new(engine.Random, seed, scope);
            (CombatRunner runner, Combatant attacker, Combatant guard) = CreateSpellCostCombat(
                rules, combat, attackerDefinition, guardDefinition, dice);

            long cursorBeforeOffer = dice.NextRandomKey;
            int rollsBeforeOffer = dice.Rolls.Count;
            CombatDecision offered = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            CombatActionChoice spell = Assert.Single(offered.Actions, action => action.SpellCosts is not null);
            decimal quotedMana = spell.SpellCosts!["mana"];
            Assert.InRange(quotedMana, 1, 2);
            long cursorAtOffer = dice.NextRandomKey;
            int rollsAtOffer = dice.Rolls.Count;
            Assert.Equal(cursorBeforeOffer + 1, cursorAtOffer);
            Assert.Equal(rollsBeforeOffer + 1, rollsAtOffer);
            CombatContinuationState saved = RoundTrip(runner.Capture());

            DiceRoller restoredDice = new(engine.Random, seed, scope, saved.NextRandomKey);
            Evaluator restoredEvaluator = new(rules, restoredDice);
            CombatRunner restored = CombatRunner.Restore(
                rules,
                combat,
                [
                    new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                    new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
                ],
                restoredDice,
                saved);
            CombatDecision restoredOffered = AssertDecision(restored.Observe(), CombatDecisionKind.Action);
            CombatActionChoice restoredSpell = Assert.Single(restoredOffered.Actions, action => action.Id == spell.Id);
            Assert.Equal(quotedMana, restoredSpell.SpellCosts!["mana"]);

            Assert.Equal(restoredOffered.Id, restored.Observe().PendingDecision!.Id);
            Assert.Equal(cursorAtOffer, dice.NextRandomKey);
            Assert.Equal(saved.NextRandomKey, restoredDice.NextRandomKey);

            CombatCommandResult direct = runner.Submit(new CombatCommand.UseAction(
                attacker.Id,
                spell.Id,
                [guard.Id]));
            Assert.True(direct.Accepted, direct.Reason);
            Assert.Equal(cursorAtOffer, dice.NextRandomKey);
            Assert.Equal(rollsAtOffer, dice.Rolls.Count);

            SpentFact directSpent = Assert.Single(direct.Observation.Facts.OfType<SpentFact>());
            Assert.Equal("mana", directSpent.Track.Id);
            Assert.Equal(quotedMana, directSpent.Amount);
            Assert.Equal(10 - quotedMana, TrackValue(direct.Observation, attacker.Id, "mana"));
            Assert.Single(direct.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == guard.Name && fact.Amount == 1);

            CombatCommandResult resumed = restored.Submit(new CombatCommand.UseAction(
                restoredOffered.ActorId,
                restoredSpell.Id,
                [restored.Sides.Single(side => side.Name == "Guards").Members.Single().Id]));
            Assert.True(resumed.Accepted, resumed.Reason);
            Assert.Equal(saved.NextRandomKey, restoredDice.NextRandomKey);
            Assert.Empty(restoredDice.Rolls);

            SpentFact resumedSpent = Assert.Single(resumed.Observation.Facts.OfType<SpentFact>());
            Assert.Equal("mana", resumedSpent.Track.Id);
            Assert.Equal(quotedMana, resumedSpent.Amount);
            Assert.Equal(10 - quotedMana, TrackValue(resumed.Observation, attacker.Id, "mana"));
            Assert.Single(resumed.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Guard" && fact.Amount == 1);
            Assert.Equal(direct.Observation.Facts.Select(FactLine), resumed.Observation.Facts.Select(FactLine));
        });
    }

    private static (CombatRunner Runner, Combatant Attacker, Combatant Guard) CreateSpellCostCombat(
        RuleSet rules,
        Definition combat,
        Definition attackerDefinition,
        Definition guardDefinition,
        DiceRoller dice)
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
        return (runner, attacker, guard);
    }

    private static string WriteSpellCostRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/mana.json", """
            { "type": "track", "id": "mana", "name": "Mana", "max": "10", "start": "10", "min": "0" }
            """);
        WriteCombat(modules);
        modules.Write("rules/spark_action.json", """
            { "type": "action", "id": "spark_action", "name": "Spark action", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/spark.json", """
            { "type": "spell", "id": "spark", "name": "Spark", "lists": { "warrior": 1 },
              "range": "Sight", "duration": "Instant", "area": "One creature", "casting_time": "One action",
              "cost": { "mana": "1d2" },
              "effect": { "action": "spark_action" }, "description": "A small flash of disciplined energy." }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20", "mana": "10" }, "stats": { "str": "15" },
              "spells": [ { "spell": "spark" } ], "actions": [], "xp": 0 }
            """);
        WriteGuard(modules);
        return root;
    }

    private static string WriteDerivedLegalityRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        WriteCombat(modules);
        modules.Write("rules/dice_derived.json", """
            { "type": "derived", "id": "dice_derived", "name": "Dice derived", "value": "1d2" }
            """);
        modules.Write("rules/derived_blocked.json", """
            { "type": "action", "id": "derived_blocked", "name": "Derived blocked", "cost": { "turn": 1 }, "target": "enemy",
              "available": "self.dice_derived > 0", "always": [ { "op": "damage", "amount": "3" } ] }
            """);
        modules.Write("rules/fallback.json", """
            { "type": "action", "id": "fallback", "name": "Fallback", "cost": { "turn": 1 }, "target": "self",
              "always": [ { "op": "heal", "amount": "0", "to": "self" } ] }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "derived_blocked" }, { "action": "fallback" } ], "xp": 0 }
            """);
        WriteGuard(modules);
        return root;
    }

    private static LegalityResult RunIllegalAction(
        RuleSet rules,
        Definition combat,
        Definition attackerDefinition,
        Definition guardDefinition,
        string blockedAction,
        bool restore)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            const ulong seed = 9336;
            const string scope = "combat-legality-false";
            DiceRoller dice = new(engine.Random, seed, scope);
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            attacker.Creature.Track("hit_points").Current = 10;
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(guard.Id, CombatControlMode.Manual));

            CombatDecision offered = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            CombatContinuationState saved = RoundTrip(runner.Capture());
            string actorId = offered.ActorId;
            string guardId = guard.Id;
            long cursorAtOffer = dice.NextRandomKey;
            string[] actionIds = offered.Actions.Select(action => action.ActionId).ToArray();
            string[] targetIdsForBlockedAction = offered.Actions
                .Where(action => action.ActionId == $"rules:{blockedAction}")
                .SelectMany(action => action.Targets)
                .Select(target => target.Id)
                .ToArray();

            DiceRoller activeDice = dice;
            CombatRunner active = runner;
            CombatDecision activeOffered = offered;
            if (restore)
            {
                activeDice = new DiceRoller(engine.Random, seed, scope, saved.NextRandomKey);
                Evaluator restoredEvaluator = new(rules, activeDice);
                active = CombatRunner.Restore(rules, combat,
                [
                    new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                    new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
                ], activeDice, saved);
                activeOffered = AssertDecision(active.Observe(), CombatDecisionKind.Action);
                Assert.Equal(saved.PendingDecision!.Id, activeOffered.Id);
                Assert.Equal(cursorAtOffer, activeDice.NextRandomKey);
                actorId = activeOffered.ActorId;
                guardId = active.Observe().Combatants.Single(member => member.Name == "Guard").Id;
            }

            int rollsAtActiveOffer = activeDice.Rolls.Count;
            CombatObservation unchanged = active.Observe();
            int rollsAfterObservation = activeDice.Rolls.Count;
            long cursorAfterObservation = activeDice.NextRandomKey;
            Assert.Equal(activeOffered.Id, unchanged.PendingDecision!.Id);
            Assert.Equal(cursorAtOffer, cursorAfterObservation);
            Assert.Equal(rollsAtActiveOffer, rollsAfterObservation);

            int refusalCount = 0;
            CombatObservation beforeRefusal = unchanged;
            long cursorBeforeRefusal = activeDice.NextRandomKey;
            int rollsBeforeRefusal = rollsAtActiveOffer;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                CombatCommandResult refused = active.Submit(new CombatCommand.UseAction(
                    actorId,
                    $"rules:{blockedAction}",
                    [guardId]));
                Assert.False(refused.Accepted, refused.Reason);
                refusalCount++;
                Assert.Equal(beforeRefusal.Facts.Select(FactLine), refused.Observation.Facts.Select(FactLine));
                Assert.Equal(TrackValue(beforeRefusal, actorId), TrackValue(refused.Observation, actorId));
                Assert.Equal(TrackValue(beforeRefusal, guardId), TrackValue(refused.Observation, guardId));
                Assert.Equal(cursorBeforeRefusal, activeDice.NextRandomKey);
                Assert.Equal(rollsBeforeRefusal, activeDice.Rolls.Count);
            }

            CombatActionChoice fallback = Assert.Single(activeOffered.Actions, action => action.ActionId == "rules:fallback");
            CombatCommandResult accepted = active.Submit(new CombatCommand.UseAction(actorId, fallback.Id, [actorId]));
            Assert.True(accepted.Accepted, accepted.Reason);
            return new LegalityResult(
                actionIds,
                targetIdsForBlockedAction,
                accepted.Observation.Facts.Select(FactLine).ToArray(),
                TrackValue(accepted.Observation, actorId),
                TrackValue(accepted.Observation, guardId),
                cursorAtOffer,
                activeDice.NextRandomKey,
                rollsAfterObservation - rollsAtActiveOffer,
                activeDice.Rolls.Count - rollsAtActiveOffer,
                cursorAfterObservation,
                rollsAfterObservation - rollsAtActiveOffer,
                refusalCount,
                accepted.Observation.Facts.Count(fact => fact is HealFact),
                accepted.Observation.Facts.Count(fact => fact is DamageFact damage && damage.Who == "Guard"));
        });
    }

    private static string WriteRandomLegalityRuleset(TempModules modules, string blockedAction) =>
        WriteLegalityRuleset(modules, blockedAction, "1d1 == 2");

    private static string WriteDeterministicLegalityRuleset(TempModules modules, string blockedAction) =>
        WriteLegalityRuleset(modules, blockedAction, "false");

    private static string WriteRuntimeFailureLegalityRuleset(TempModules modules, string blockedAction) =>
        WriteLegalityRuleset(modules, blockedAction, "1 / 0 == 2");

    private static string WriteLegalityRuleset(TempModules modules, string blockedAction, string predicate)
    {
        string root = Rules.WriteSmallRuleset(modules);
        WriteCombat(modules);
        string blocked = blockedAction == "blocked_available"
            ? "{ \"action\": \"blocked_available\" }"
            : "{ \"action\": \"blocked_target\" }";
        if (blockedAction == "blocked_available")
        {
            modules.Write("rules/blocked_available.json", $$"""
                { "type": "action", "id": "blocked_available", "name": "Blocked availability", "cost": { "turn": 1 }, "target": "enemy",
                  "available": "{{predicate}}", "always": [ { "op": "damage", "amount": "3" } ] }
                """);
        }
        else
        {
            modules.Write("rules/blocked_target.json", $$"""
                { "type": "action", "id": "blocked_target", "name": "Blocked target", "cost": { "turn": 1 }, "target": "enemy",
                  "valid_target": "{{predicate}}", "always": [ { "op": "damage", "amount": "3" } ] }
                """);
        }

        modules.Write("rules/fallback.json", """
            { "type": "action", "id": "fallback", "name": "Fallback", "cost": { "turn": 1 }, "target": "self",
              "always": [ { "op": "heal", "amount": "1", "to": "self" } ] }
            """);
        modules.Write("rules/attacker.json", $$"""
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ {{blocked}}, { "action": "fallback" } ], "xp": 0 }
            """);
        WriteGuard(modules);
        return root;
    }

    private static string WriteNumericRuleset(TempModules modules, string numericField, string expression = "1d1")
    {
        string root = Rules.WriteSmallRuleset(modules);
        WriteCombat(modules, field: true);
        string field = numericField == "range" ? $"\"range\": \"{expression}\"" : $"\"portions\": \"{expression}\"";
        modules.Write("rules/numeric_action.json", $$"""
            { "type": "action", "id": "numeric_action", "name": "Numeric action", "cost": { "turn": 1 }, "target": "enemy", {{field}},
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "numeric_action" }, { "action": "fallback" } ], "xp": 0 }
            """);
        modules.Write("rules/fallback.json", """
            { "type": "action", "id": "fallback", "name": "Fallback", "cost": { "turn": 1 }, "target": "self",
              "always": [ { "op": "heal", "amount": "1", "to": "self" } ] }
            """);
        modules.Write("rules/guard.json", """
            { "type": "monster", "id": "guard", "name": "Guard", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "5" }, "actions": [], "xp": 0 }
            """);
        return root;
    }

    private static void WriteCombat(TempModules modules, bool field = false)
    {
        string fieldJson = field ? ", \"field\": { \"width\": 4, \"height\": 1, \"metric\": \"manhattan\" }" : string.Empty;
        modules.Write("rules/duel.json", $$"""
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature",
              "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6{{fieldJson}},
              "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ],
              "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
    }

    private static void WriteGuard(TempModules modules)
    {
        modules.Write("rules/guard.json", """
            { "type": "monster", "id": "guard", "name": "Guard", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "5" }, "actions": [], "xp": 0 }
            """);
    }

    private static CombatDecision AssertDecision(CombatObservation observation, CombatDecisionKind kind)
    {
        Assert.Equal(kind, observation.PendingDecision?.Kind);
        Assert.NotNull(observation.PendingDecision);
        return observation.PendingDecision!;
    }

    private static CombatContinuationState RoundTrip(CombatContinuationState state) =>
        CombatContinuationState.FromJson(CombatContinuationState.ToJson(state));

    private static decimal? TrackValue(CombatObservation observation, string id, string trackId = "hit_points") =>
        observation.Combatants.Single(member => member.Id == id).Tracks[trackId];

    private static string FactLine(CombatFact fact) =>
        $"{fact.Kind}|{fact.Describe()}|{string.Join(",", fact.Rolls.Select(roll => roll.ToString()))}";

    private sealed record LegalityResult(
        IReadOnlyList<string> ActionIds,
        IReadOnlyList<string> TargetIdsForBlockedAction,
        IReadOnlyList<string> FinalFacts,
        decimal? AttackerHitPoints,
        decimal? GuardHitPoints,
        long CursorAtOffer,
        long CursorAfterAcceptedFallback,
        int RollsAtOffer,
        int RollsAfterAcceptedFallback,
        long CursorAfterRefusals,
        int RollsAfterRefusals,
        int RefusalCount,
        int FallbackFactCount,
        int BlockedDamageFactCount);

}
