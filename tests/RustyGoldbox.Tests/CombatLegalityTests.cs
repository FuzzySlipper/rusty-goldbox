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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicCloseRejectsAPathForAnotherTargetWithoutMutatingCombat(bool standaloneMoveCommand)
    {
        using TempModules modules = new();
        string tactics = modules.Module("tactics", "extension", requires: TempModules.Require("classic", "*"));
        modules.Write("tactics/cross_targets.json", """
            {
              "type": "combat",
              "id": "cross_targets",
              "name": "Cross-target movement",
              "initiative": "1",
              "initiative_by": "side",
              "initiative_order": "highest-first",
              "initiative_each": "combat",
              "round_seconds": 6,
              "field": {
                "width": 10,
                "height": 6,
                "metric": "manhattan",
                "terrain": { "#": { "name": "Pillar", "passable": false, "blocks_sight": true } }
              },
              "budget": [ { "id": "action", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ],
              "track": "classic:hit_points",
              "defeated": "self.hit_points <= 0"
            }
            """);

        ModuleSet set = ModuleLoader.Load(tactics, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "tactics:cross_targets", out _)!;
        Definition close = rules.Find(DefinitionTypes.Action, "classic:close", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            const ulong seed = 9341;
            const string scope = "cross-target-close";
            DiceRoller dice = new(engine.Random, seed, scope);
            Creature actorCreature = new("Mover");
            actorCreature.Track("hit_points").Current = 20;
            actorCreature.Track("hit_points").Max = 20;
            actorCreature.Values["movement"] = 120;
            Combatant actor = new(
                "Mover",
                actorCreature,
                [new UseOption(close, "Close", new Dictionary<string, CompiledExpression>())],
                "mover")
            {
                Side = 0,
                Controller = CombatControlMode.Manual,
            };
            Combatant firstTarget = new("First target", CreatureWithHitPoints("first", 20), [], "target-a")
            {
                Side = 1,
                Controller = CombatControlMode.Manual,
            };
            Combatant secondTarget = new("Second target", CreatureWithHitPoints("second", 20), [], "target-b")
            {
                Side = 2,
                Controller = CombatControlMode.Manual,
            };
            CombatRunner runner = CombatRunner.Create(
                rules,
                combat,
                [
                    new CombatSide("Mover", [actor]),
                    new CombatSide("First", [firstTarget]),
                    new CombatSide("Second", [secondTarget]),
                ],
                dice,
                setup: new CombatSetup([
                    new Cell(0, 0),
                    new Cell(5, 0),
                    new Cell(0, 5),
                ]));
            Assert.True(runner.SetController(actor.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(firstTarget.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(secondTarget.Id, CombatControlMode.Manual));

            CombatObservation observation = runner.Start(1);
            while (observation.PendingDecision?.ActorId != actor.Id)
            {
                CombatDecision turn = Assert.IsType<CombatDecision>(observation.PendingDecision);
                CombatCommandResult ended = runner.Submit(new CombatCommand.EndTurn(turn.ActorId));
                Assert.True(ended.Accepted, ended.Reason);
                observation = ended.Observation;
            }

            CombatDecision decision = Assert.IsType<CombatDecision>(observation.PendingDecision);
            CombatActionChoice closeChoice = Assert.Single(decision.Actions, action => action.ActionId == close.QualifiedId);
            CombatTargetChoice first = Assert.Single(closeChoice.Targets, target => target.Id == firstTarget.Id);
            CombatTargetChoice second = Assert.Single(closeChoice.Targets, target => target.Id == secondTarget.Id);
            CombatMoveChoice pathForFirst = Assert.Single(closeChoice.Moves, move => move.Destination == new Cell(4, 0));
            CombatMoveChoice pathForSecond = Assert.Single(closeChoice.Moves, move => move.Destination == new Cell(0, 4));
            Assert.NotEqual(pathForFirst.Path, pathForSecond.Path);
            Assert.Contains(pathForFirst, decision.Moves);
            Assert.Contains(pathForSecond, decision.Moves);

            long cursorBeforeMismatch = dice.NextRandomKey;
            int rollsBeforeMismatch = dice.Rolls.Count;
            int budgetBeforeMismatch = observation.Combatants.Single(member => member.Id == actor.Id).Budget["action"];
            Cell positionBeforeMismatch = observation.Combatants.Single(member => member.Id == actor.Id).Position!.Value;
            string[] factsBeforeMismatch = observation.Facts.Select(FactLine).ToArray();

            CombatCommand MismatchedCommand(string targetId, IReadOnlyList<Cell> path) => standaloneMoveCommand
                ? new CombatCommand.Move(actor.Id, closeChoice.Id, targetId, path)
                : new CombatCommand.UseAction(actor.Id, closeChoice.Id, [targetId], path);

            CombatCommandResult mismatched = runner.Submit(MismatchedCommand(second.Id, pathForFirst.Path));
            Assert.False(mismatched.Accepted, mismatched.Reason);
            Assert.Equal(decision.Id, mismatched.Observation.PendingDecision?.Id);
            Assert.Equal(cursorBeforeMismatch, dice.NextRandomKey);
            Assert.Equal(rollsBeforeMismatch, dice.Rolls.Count);
            Assert.Equal(budgetBeforeMismatch, mismatched.Observation.Combatants.Single(member => member.Id == actor.Id).Budget["action"]);
            Assert.Equal(positionBeforeMismatch, mismatched.Observation.Combatants.Single(member => member.Id == actor.Id).Position);
            Assert.Equal(factsBeforeMismatch, mismatched.Observation.Facts.Select(FactLine));

            CombatCommandResult accepted = runner.Submit(MismatchedCommand(first.Id, pathForFirst.Path));
            Assert.True(accepted.Accepted, accepted.Reason);
            Assert.Equal(budgetBeforeMismatch - 1, accepted.Observation.Combatants.Single(member => member.Id == actor.Id).Budget["action"]);
            Assert.Equal(pathForFirst.Destination, accepted.Observation.Combatants.Single(member => member.Id == actor.Id).Position);
            MoveFact moved = Assert.Single(accepted.Observation.Facts.OfType<MoveFact>(), fact => fact.Who == actor.Name);
            Assert.Equal(pathForFirst.Destination, moved.To);
            Assert.Equal(firstTarget.Name, accepted.Observation.Facts.OfType<ActionFact>().Single(fact => fact.Who == actor.Name).Target);
        });
    }

    [Fact]
    public void RefusedRandomSpellCostCommandDoesNotQuoteOrConsumeRandomnessAcrossObservationAndJsonRestore()
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
            CombatActionChoice spell = Assert.Single(offered.Actions, action => action.SpellId == "rules:spark");
            Assert.Null(spell.SpellCosts);
            Assert.Equal(cursorBeforeOffer, dice.NextRandomKey);
            Assert.Equal(rollsBeforeOffer, dice.Rolls.Count);

            CombatContinuationState saved = RoundTrip(runner.Capture());
            CombatActionChoice savedSpell = Assert.Single(saved.PendingDecision!.Actions, action => action.Id == spell.Id);
            Assert.Null(savedSpell.SpellCosts);

            CombatObservation before = runner.Observe();
            string[] factsBefore = before.Facts.Select(FactLine).ToArray();
            decimal? manaBefore = TrackValue(before, attacker.Id, "mana");
            long cursorBefore = dice.NextRandomKey;
            int rollsBefore = dice.Rolls.Count;
            Assert.Equal(spell.Id, before.PendingDecision!.Actions.Single(action => action.Id == spell.Id).Id);

            CombatObservation repeated = runner.Observe();
            Assert.Equal(cursorBefore, dice.NextRandomKey);
            Assert.Equal(rollsBefore, dice.Rolls.Count);
            Assert.Null(repeated.PendingDecision!.Actions.Single(action => action.Id == spell.Id).SpellCosts);

            CombatCommandResult refused = runner.Submit(new CombatCommand.UseAction(
                attacker.Id,
                spell.Id,
                ["missing-target"]));
            Assert.False(refused.Accepted, refused.Reason);
            Assert.Equal(cursorBefore, dice.NextRandomKey);
            Assert.Equal(rollsBefore, dice.Rolls.Count);
            Assert.Equal(factsBefore, refused.Observation.Facts.Select(FactLine));
            Assert.Equal(manaBefore, TrackValue(refused.Observation, attacker.Id, "mana"));
            Assert.Null(refused.Observation.PendingDecision!.Actions.Single(action => action.Id == spell.Id).SpellCosts);

            CombatCommandResult refusedPath = runner.Submit(new CombatCommand.UseAction(
                attacker.Id,
                spell.Id,
                [guard.Id],
                [new Cell(9, 9)]));
            Assert.False(refusedPath.Accepted, refusedPath.Reason);
            Assert.Equal(cursorBefore, dice.NextRandomKey);
            Assert.Equal(rollsBefore, dice.Rolls.Count);
            Assert.Equal(factsBefore, refusedPath.Observation.Facts.Select(FactLine));
            Assert.Null(refusedPath.Observation.PendingDecision!.Actions.Single(action => action.Id == spell.Id).SpellCosts);

            CombatCommandResult refusedActor = runner.Submit(new CombatCommand.UseAction(
                "missing-actor",
                spell.Id,
                [guard.Id]));
            Assert.False(refusedActor.Accepted, refusedActor.Reason);
            Assert.Equal(cursorBefore, dice.NextRandomKey);
            Assert.Equal(rollsBefore, dice.Rolls.Count);
            Assert.Equal(factsBefore, refusedActor.Observation.Facts.Select(FactLine));

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
            Assert.Null(restoredSpell.SpellCosts);
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
            Assert.Null(restoredRefused.Observation.PendingDecision!.Actions.Single(action => action.Id == spell.Id).SpellCosts);
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
            CombatActionChoice spell = Assert.Single(offered.Actions, action => action.SpellId == "rules:spark");
            Assert.Null(spell.SpellCosts);
            long cursorAtOffer = dice.NextRandomKey;
            int rollsAtOffer = dice.Rolls.Count;
            Assert.Equal(cursorBeforeOffer, cursorAtOffer);
            Assert.Equal(rollsBeforeOffer, rollsAtOffer);
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
            Assert.Null(restoredSpell.SpellCosts);

            Assert.Equal(restoredOffered.Id, restored.Observe().PendingDecision!.Id);
            Assert.Equal(cursorAtOffer, dice.NextRandomKey);
            Assert.Equal(saved.NextRandomKey, restoredDice.NextRandomKey);

            CombatCommandResult direct = runner.Submit(new CombatCommand.UseAction(
                attacker.Id,
                spell.Id,
                [guard.Id]));
            Assert.True(direct.Accepted, direct.Reason);
            Assert.Equal(cursorAtOffer + 1, dice.NextRandomKey);
            Assert.Equal(rollsAtOffer + 1, dice.Rolls.Count);

            SpentFact directSpent = Assert.Single(direct.Observation.Facts.OfType<SpentFact>());
            Assert.Equal("mana", directSpent.Track.Id);
            decimal quotedMana = directSpent.Amount;
            Assert.InRange(quotedMana, 1, 2);
            Assert.Equal(quotedMana, directSpent.Amount);
            Assert.Equal(10 - quotedMana, TrackValue(direct.Observation, attacker.Id, "mana"));
            Assert.Single(direct.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == guard.Name && fact.Amount == 1);

            CombatCommandResult resumed = restored.Submit(new CombatCommand.UseAction(
                restoredOffered.ActorId,
                restoredSpell.Id,
                [restored.Sides.Single(side => side.Name == "Guards").Members.Single().Id]));
            Assert.True(resumed.Accepted, resumed.Reason);
            Assert.Equal(saved.NextRandomKey + 1, restoredDice.NextRandomKey);
            Assert.Single(restoredDice.Rolls);

            SpentFact resumedSpent = Assert.Single(resumed.Observation.Facts.OfType<SpentFact>());
            Assert.Equal("mana", resumedSpent.Track.Id);
            Assert.Equal(quotedMana, resumedSpent.Amount);
            Assert.Equal(10 - quotedMana, TrackValue(resumed.Observation, attacker.Id, "mana"));
            Assert.Single(resumed.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == "Guard" && fact.Amount == 1);
            Assert.Equal(direct.Observation.Facts.Select(FactLine), resumed.Observation.Facts.Select(FactLine));
        });
    }

    [Fact]
    public void UnselectedRandomCostSpellDoesNotShiftLaterActionDraws()
    {
        using TempModules baselineModules = new();
        using TempModules candidateModules = new();
        string baselineRoot = WriteSpellCostCandidateRuleset(baselineModules, includeRandomSpell: false);
        string candidateRoot = WriteSpellCostCandidateRuleset(candidateModules, includeRandomSpell: true);
        RuleSet baselineRules = Rules.LoadValid(baselineRoot);
        RuleSet candidateRules = Rules.LoadValid(candidateRoot);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            CandidateResult baseline = RunSlashCandidate(baselineRules, engine.Random, "combat-spell-candidate", automatic: false);
            CandidateResult candidate = RunSlashCandidate(candidateRules, engine.Random, "combat-spell-candidate", automatic: false);

            Assert.Equal(baseline.DamageFact, candidate.DamageFact);
            Assert.Equal(baseline.CursorAfterAction, candidate.CursorAfterAction);
            Assert.Equal(baseline.Rolls, candidate.Rolls);
        });
    }

    [Fact]
    public void AutomaticChoiceDoesNotInspectAnUnselectedRandomSpellPrice()
    {
        using TempModules baselineModules = new();
        using TempModules candidateModules = new();
        string baselineRoot = WriteSpellCostCandidateRuleset(baselineModules, includeRandomSpell: false);
        string candidateRoot = WriteSpellCostCandidateRuleset(candidateModules, includeRandomSpell: true);
        RuleSet baselineRules = Rules.LoadValid(baselineRoot);
        RuleSet candidateRules = Rules.LoadValid(candidateRoot);

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            CandidateResult baseline = RunSlashCandidate(baselineRules, engine.Random, "combat-spell-automatic", automatic: true);
            CandidateResult candidate = RunSlashCandidate(candidateRules, engine.Random, "combat-spell-automatic", automatic: true);

            Assert.Equal(baseline.DamageFact, candidate.DamageFact);
            Assert.Equal(baseline.CursorAfterAction, candidate.CursorAfterAction);
            Assert.Equal(baseline.Rolls, candidate.Rolls);
        });
    }

    [Fact]
    public void ConstantSpellCostIsQuotedWithoutConsumingRandomness()
    {
        using TempModules modules = new();
        string root = WriteSpellCostRuleset(modules, spellCost: "1");
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            const ulong seed = 9337;
            const string scope = "combat-spell-cost-constant";
            DiceRoller dice = new(engine.Random, seed, scope);
            (CombatRunner runner, Combatant attacker, Combatant guard) = CreateSpellCostCombat(
                rules, combat, attackerDefinition, guardDefinition, dice);

            long cursorBeforeOffer = dice.NextRandomKey;
            int rollsBeforeOffer = dice.Rolls.Count;
            CombatDecision offered = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            CombatActionChoice spell = Assert.Single(offered.Actions, action => action.SpellId == "rules:spark");
            Assert.Equal(1m, spell.SpellCosts!["mana"]);
            Assert.Equal(cursorBeforeOffer, dice.NextRandomKey);
            Assert.Equal(rollsBeforeOffer, dice.Rolls.Count);

            CombatCommandResult accepted = runner.Submit(new CombatCommand.UseAction(
                attacker.Id,
                spell.Id,
                [guard.Id]));
            Assert.True(accepted.Accepted, accepted.Reason);
            Assert.Equal(cursorBeforeOffer, dice.NextRandomKey);
            Assert.Equal(rollsBeforeOffer, dice.Rolls.Count);
            SpentFact spent = Assert.Single(accepted.Observation.Facts.OfType<SpentFact>());
            Assert.Equal(1m, spent.Amount);
            Assert.Equal(9m, TrackValue(accepted.Observation, attacker.Id, "mana"));
        });
    }

    [Fact]
    public void SelectedUnaffordableRandomSpellRetainsItsCommittedPriceForDrawFreeRetryAndRestore()
    {
        using TempModules modules = new();
        string root = WriteSpellCostRuleset(modules, manaStart: "0");
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            const ulong seed = 9337;
            const string scope = "combat-spell-cost-unaffordable";
            DiceRoller dice = new(engine.Random, seed, scope);
            (CombatRunner runner, Combatant attacker, Combatant guard) = CreateSpellCostCombat(
                rules, combat, attackerDefinition, guardDefinition, dice);

            CombatDecision offered = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            CombatActionChoice spell = Assert.Single(offered.Actions, action => action.SpellId == "rules:spark");
            Assert.Null(spell.SpellCosts);
            long cursorAtOffer = dice.NextRandomKey;
            int rollsAtOffer = dice.Rolls.Count;

            CombatCommandResult selected = runner.Submit(new CombatCommand.UseAction(
                attacker.Id,
                spell.Id,
                [guard.Id]));
            Assert.True(selected.Accepted, selected.Reason);
            Assert.Contains("price", SelectedMessage(selected), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(cursorAtOffer + 1, dice.NextRandomKey);
            Assert.Equal(rollsAtOffer + 1, dice.Rolls.Count);
            Assert.Equal(0m, TrackValue(selected.Observation, attacker.Id, "mana"));
            Assert.Empty(selected.Observation.Facts.OfType<ActionFact>());
            Assert.Empty(selected.Observation.Facts.OfType<SpentFact>());
            Assert.Empty(selected.Observation.Facts.OfType<DamageFact>());
            CombatActionChoice committed = Assert.Single(
                selected.Observation.PendingDecision!.Actions,
                action => action.Id == spell.Id);
            decimal committedPrice = committed.SpellCosts!["mana"];
            Assert.InRange(committedPrice, 1, 2);

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
            CombatActionChoice restoredSpell = Assert.Single(
                restored.Observe().PendingDecision!.Actions,
                action => action.Id == spell.Id);
            Assert.Equal(committedPrice, restoredSpell.SpellCosts!["mana"]);
            long cursorBeforeRetry = restoredDice.NextRandomKey;
            int rollsBeforeRetry = restoredDice.Rolls.Count;

            CombatCommandResult retry = restored.Submit(new CombatCommand.UseAction(
                restored.Observe().PendingDecision!.ActorId,
                restoredSpell.Id,
                [restored.Sides.Single(side => side.Name == "Guards").Members.Single().Id]));
            Assert.False(retry.Accepted, retry.Reason);
            Assert.Equal(cursorBeforeRetry, restoredDice.NextRandomKey);
            Assert.Equal(rollsBeforeRetry, restoredDice.Rolls.Count);
            Assert.Equal(committedPrice, retry.Observation.PendingDecision!.Actions
                .Single(action => action.Id == spell.Id).SpellCosts!["mana"]);
        });
    }

    [Fact]
    public void SelectedRandomSpellCostSurvivesMaximumTargetSelectionAndJsonRestore()
    {
        using TempModules modules = new();
        string root = WriteSpellCostRuleset(modules);
        modules.Write("rules/spark_action.json", """
            { "type": "action", "id": "spark_action", "name": "Spark action", "cost": { "turn": 1 }, "target": "all_enemies", "max_targets": "1",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            const ulong seed = 9337;
            const string scope = "combat-spell-cost-max-target";
            DiceRoller dice = new(engine.Random, seed, scope);
            (CombatRunner runner, Combatant attacker, Combatant guard) = CreateSpellCostCombat(
                rules, combat, attackerDefinition, guardDefinition, dice);

            CombatDecision offered = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            CombatActionChoice spell = Assert.Single(offered.Actions, action => action.SpellId == "rules:spark");
            Assert.Null(spell.SpellCosts);
            long cursorAtOffer = dice.NextRandomKey;
            int rollsAtOffer = dice.Rolls.Count;

            CombatCommandResult committed = runner.Submit(new CombatCommand.UseAction(
                attacker.Id,
                spell.Id,
                []));
            Assert.True(committed.Accepted, committed.Reason);
            Assert.Equal(cursorAtOffer + 1, dice.NextRandomKey);
            Assert.Equal(rollsAtOffer + 1, dice.Rolls.Count);
            Assert.Equal(CombatDecisionKind.Targets, committed.Observation.PendingDecision?.Kind);
            CombatActionChoice committedSpell = Assert.Single(
                committed.Observation.PendingDecision!.Actions,
                action => action.Id == spell.Id);
            decimal quotedMana = committedSpell.SpellCosts!["mana"];
            Assert.InRange(quotedMana, 1, 2);
            Assert.Single(committed.Observation.Facts.OfType<SpentFact>(), fact => fact.Amount == quotedMana);

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
            CombatDecision restoredTargets = AssertDecision(restored.Observe(), CombatDecisionKind.Targets);
            CombatActionChoice restoredSpell = Assert.Single(
                restoredTargets.Actions,
                action => action.Id == spell.Id);
            Assert.Equal(quotedMana, restoredSpell.SpellCosts!["mana"]);
            long restoredCursor = restoredDice.NextRandomKey;
            int restoredRolls = restoredDice.Rolls.Count;

            CombatCommandResult direct = runner.Submit(new CombatCommand.UseAction(
                attacker.Id,
                committed.Observation.PendingDecision.ActionId!,
                [guard.Id]));
            Assert.True(direct.Accepted, direct.Reason);
            Assert.Equal(cursorAtOffer + 1, dice.NextRandomKey);
            Assert.Equal(rollsAtOffer + 1, dice.Rolls.Count);
            Assert.Single(direct.Observation.Facts.OfType<SpentFact>(), fact => fact.Amount == quotedMana);
            Assert.Single(direct.Observation.Facts.OfType<DamageFact>(), fact => fact.Who == guard.Name && fact.Amount == 1);

            CombatCommandResult resumed = restored.Submit(new CombatCommand.UseAction(
                restoredTargets.ActorId,
                restoredTargets.ActionId!,
                [restored.Sides.Single(side => side.Name == "Guards").Members.Single().Id]));
            Assert.True(resumed.Accepted, resumed.Reason);
            Assert.Equal(restoredCursor, restoredDice.NextRandomKey);
            Assert.Equal(restoredRolls, restoredDice.Rolls.Count);
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

    private static CandidateResult RunSlashCandidate(
        RuleSet rules,
        Rusty.Engine.IRandomService random,
        string scope,
        bool automatic)
    {
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attackerDefinition = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guardDefinition = rules.Find(DefinitionTypes.Monster, "guard", out _)!;
        DiceRoller dice = new(random, 9337, scope);
        (CombatRunner runner, Combatant attacker, Combatant guard) = CreateSpellCostCombat(
            rules, combat, attackerDefinition, guardDefinition, dice);

        CombatObservation observation;
        if (automatic)
        {
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Automatic));
            observation = runner.Start(1);
        }
        else
        {
            CombatDecision offered = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            if (offered.Actions.SingleOrDefault(action => action.SpellId == "rules:spark") is CombatActionChoice randomSpell)
            {
                Assert.Null(randomSpell.SpellCosts);
            }

            CombatActionChoice slash = Assert.Single(offered.Actions, action => action.ActionId == "rules:slash_action");
            CombatCommandResult accepted = runner.Submit(new CombatCommand.UseAction(
                attacker.Id,
                slash.Id,
                [guard.Id]));
            Assert.True(accepted.Accepted, accepted.Reason);
            observation = accepted.Observation;
        }

        DamageFact damage = Assert.Single(observation.Facts.OfType<DamageFact>(), fact => fact.Who == guard.Name);
        return new(
            FactLine(damage),
            dice.NextRandomKey,
            dice.Rolls.Select(roll => roll.ToString()).ToArray());
    }

    private static string WriteSpellCostRuleset(
        TempModules modules,
        string spellCost = "1d2",
        string manaStart = "10")
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/mana.json", $$"""
            { "type": "track", "id": "mana", "name": "Mana", "max": "10", "start": "{{manaStart}}", "min": "0" }
            """);
        WriteCombat(modules);
        modules.Write("rules/spark_action.json", """
            { "type": "action", "id": "spark_action", "name": "Spark action", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1" } ] }
            """);
        modules.Write("rules/spark.json", $$"""
            { "type": "spell", "id": "spark", "name": "Spark", "lists": { "warrior": 1 },
              "range": "Sight", "duration": "Instant", "area": "One creature", "casting_time": "One action",
              "cost": { "mana": "{{spellCost}}" },
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

    private static string WriteSpellCostCandidateRuleset(TempModules modules, bool includeRandomSpell)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/mana.json", """
            { "type": "track", "id": "mana", "name": "Mana", "max": "10", "start": "10", "min": "0" }
            """);
        WriteCombat(modules);
        modules.Write("rules/slash_action.json", """
            { "type": "action", "id": "slash_action", "name": "Slash", "score": "10", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1d2" } ] }
            """);
        if (includeRandomSpell)
        {
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
        }

        string spells = includeRandomSpell ? "\"spells\": [ { \"spell\": \"spark\" } ]," : "\"spells\": [],";
        modules.Write("rules/attacker.json", $$"""
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20", "mana": "10" }, "stats": { "str": "15" },
              {{spells}} "actions": [ { "action": "slash_action" } ], "xp": 0 }
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

    private static Creature CreatureWithHitPoints(string label, decimal points)
    {
        Creature creature = new(label);
        creature.Track("hit_points").Current = points;
        creature.Track("hit_points").Max = points;
        return creature;
    }

    private static string FactLine(CombatFact fact) =>
        $"{fact.Kind}|{fact.Describe()}|{string.Join(",", fact.Rolls.Select(roll => roll.ToString()))}";

    private static string SelectedMessage(CombatCommandResult result) =>
        string.Join(" | ",
            new[] { result.Reason }
                .Where(message => message is not null)
                .Select(message => message!)
                .Concat(result.Observation.Facts.Select(FactLine)));

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

    private sealed record CandidateResult(
        string DamageFact,
        long CursorAfterAction,
        IReadOnlyList<string> Rolls);

}
