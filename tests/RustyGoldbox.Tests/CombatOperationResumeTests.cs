using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

/// <summary>
/// Regression checks for continuing an authored operation list after a live
/// decision suspends one of its operations.
/// </summary>
public sealed class CombatOperationResumeTests
{
    [Fact]
    public void DamageThenHealTailRunsOnceAfterManualHitChoiceAndJsonRestore()
    {
        using TempModules modules = new();
        string root = WriteTailRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attacker = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guard = rules.Find(DefinitionTypes.Monster, "guard_monster", out _)!;

        ScenarioResult uninterrupted = RunTailAction(rules, combat, attacker, guard, restore: false);
        ScenarioResult restored = RunTailAction(rules, combat, attacker, guard, restore: true);

        Assert.Equal(uninterrupted.Facts, restored.Facts);
        Assert.Equal(uninterrupted.ActorHitPoints, restored.ActorHitPoints);
        Assert.Equal(uninterrupted.TargetHitPoints, restored.TargetHitPoints);
        Assert.Equal(uninterrupted.ReactionCount, restored.ReactionCount);
        Assert.Equal(uninterrupted.PendingRolls, restored.PendingRolls);
        Assert.Equal(1, uninterrupted.Facts.Count(fact => fact.StartsWith("damage|", StringComparison.Ordinal)));
        Assert.Equal(1, uninterrupted.Facts.Count(fact => fact.StartsWith("heal|", StringComparison.Ordinal)));
        Assert.Equal(1, uninterrupted.ReactionCount);
        Assert.True(uninterrupted.TargetHitPoints < 20);
        Assert.Equal(8, uninterrupted.ActorHitPoints);
    }

    [Fact]
    public void NestedCheckBranchAndOuterTailKeepTheirOrderAfterJsonRestore()
    {
        using TempModules modules = new();
        string root = WriteNestedTailRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attacker = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guard = rules.Find(DefinitionTypes.Monster, "guard_monster", out _)!;

        ScenarioResult uninterrupted = RunNestedTail(rules, combat, attacker, guard, restore: false);
        ScenarioResult restored = RunNestedTail(rules, combat, attacker, guard, restore: true);

        Assert.Equal(uninterrupted.Facts, restored.Facts);
        Assert.Equal(uninterrupted.ActorHitPoints, restored.ActorHitPoints);
        Assert.Equal(uninterrupted.TargetHitPoints, restored.TargetHitPoints);
        Assert.Equal(uninterrupted.PendingRolls, restored.PendingRolls);

        int damage = AssertFactIndex(uninterrupted.Facts, "damage|Guard");
        int branchHeal = AssertFactIndex(uninterrupted.Facts, "heal|Attacker regains 2");
        int outerHeal = AssertFactIndex(uninterrupted.Facts, "heal|Attacker regains 3");
        Assert.True(damage < branchHeal, string.Join(Environment.NewLine, uninterrupted.Facts));
        Assert.True(branchHeal < outerHeal, string.Join(Environment.NewLine, uninterrupted.Facts));
        Assert.Equal(2, uninterrupted.Facts.Count(fact => fact.StartsWith("heal|", StringComparison.Ordinal)));
        Assert.True(uninterrupted.TargetHitPoints < 20);
        Assert.Equal(10, uninterrupted.ActorHitPoints);
    }

    [Fact]
    public void MovementInterruptContinuesWithTheNextOperationAfterJsonRestore()
    {
        using TempModules modules = new();
        string root = WriteMovementTailRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "movement_duel", out _)!;
        Definition mover = rules.Find(DefinitionTypes.Monster, "mover", out _)!;
        Definition watcher = rules.Find(DefinitionTypes.Monster, "watcher", out _)!;
        CombatSetup setup = new([new Cell(1, 0), new Cell(2, 0)], SurprisedSide: -1);

        ScenarioResult uninterrupted = RunMovementTail(rules, combat, mover, watcher, setup, restore: false);
        ScenarioResult restored = RunMovementTail(rules, combat, mover, watcher, setup, restore: true);

        Assert.Equal(uninterrupted.Facts, restored.Facts);
        Assert.Equal(uninterrupted.ActorHitPoints, restored.ActorHitPoints);
        Assert.Equal(uninterrupted.TargetHitPoints, restored.TargetHitPoints);
        Assert.Equal(uninterrupted.ActorPosition, restored.ActorPosition);
        Assert.Equal(uninterrupted.PendingRolls, restored.PendingRolls);
        Assert.Contains(uninterrupted.Facts, fact => fact.StartsWith("move|", StringComparison.Ordinal));
        Assert.Equal(1, uninterrupted.Facts.Count(fact => fact.StartsWith("heal|", StringComparison.Ordinal)));
        Assert.Equal(8, uninterrupted.ActorHitPoints);
        Assert.Equal(new Cell(0, 0), uninterrupted.ActorPosition);
    }

    [Fact]
    public void NestedCheckPostRollUsesAcceptedResultAndResumesTheTailOnceAfterJsonRestore()
    {
        using TempModules modules = new();
        string root = WriteNestedPostRollRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attacker = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guard = rules.Find(DefinitionTypes.Monster, "guard_monster", out _)!;

        PostRollResult uninterrupted = RunNestedPostRoll(rules, combat, attacker, guard, restore: false);
        PostRollResult restored = RunNestedPostRoll(rules, combat, attacker, guard, restore: true);

        Assert.Equal(uninterrupted.Facts, restored.Facts);
        Assert.Equal(uninterrupted.ActorHitPoints, restored.ActorHitPoints);
        Assert.Equal(uninterrupted.Luck, restored.Luck);
        Assert.Equal(uninterrupted.CheckTotal, restored.CheckTotal);
        Assert.Equal(uninterrupted.CheckSuccess, restored.CheckSuccess);
        Assert.Equal(uninterrupted.PendingCheckRolls, restored.PendingCheckRolls);
        Assert.Equal(uninterrupted.DrawCursorAtOffer, uninterrupted.DrawCursorAfterChoice);
        Assert.Equal(restored.DrawCursorAtOffer, restored.DrawCursorAfterChoice);
        Assert.Equal(uninterrupted.DrawCursorAfterChoice, restored.DrawCursorAfterChoice);
        Assert.Equal(10, uninterrupted.ActorHitPoints);
        Assert.Equal(0, uninterrupted.Luck);
        Assert.True(uninterrupted.CheckSuccess);
        Assert.Equal(1, uninterrupted.CheckFactCount);
        Assert.Equal(1, uninterrupted.PostRollFactCount);
        Assert.Equal(2, uninterrupted.Facts.Count(fact => fact.StartsWith("heal|", StringComparison.Ordinal)));
        int branchHeal = AssertFactIndex(uninterrupted.Facts, "heal|Attacker regains 2");
        int outerHeal = AssertFactIndex(uninterrupted.Facts, "heal|Attacker regains 3");
        Assert.True(branchHeal < outerHeal, string.Join(Environment.NewLine, uninterrupted.Facts));
    }

    [Fact]
    public void ConditionCheckPostRollResumesItsRemainingOperationsWhenJsonRestored()
    {
        using TempModules modules = new();
        string root = WriteConditionPostRollRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attacker = rules.Find(DefinitionTypes.Monster, "conditioned", out _)!;
        Definition guard = rules.Find(DefinitionTypes.Monster, "guard_monster", out _)!;

        PostRollResult uninterrupted = RunConditionPostRoll(rules, combat, attacker, guard, restore: false);
        PostRollResult restored = RunConditionPostRoll(rules, combat, attacker, guard, restore: true);

        Assert.Equal(uninterrupted.Facts, restored.Facts);
        Assert.Equal(uninterrupted.ActorHitPoints, restored.ActorHitPoints);
        Assert.Equal(uninterrupted.Luck, restored.Luck);
        Assert.Equal(uninterrupted.CheckTotal, restored.CheckTotal);
        Assert.Equal(uninterrupted.CheckSuccess, restored.CheckSuccess);
        Assert.Equal(uninterrupted.PendingCheckRolls, restored.PendingCheckRolls);
        Assert.Equal(uninterrupted.DrawCursorAtOffer, uninterrupted.DrawCursorAfterChoice);
        Assert.Equal(restored.DrawCursorAtOffer, restored.DrawCursorAfterChoice);
        Assert.Equal(uninterrupted.DrawCursorAfterChoice, restored.DrawCursorAfterChoice);
        Assert.Equal(10, uninterrupted.ActorHitPoints);
        Assert.Equal(0, uninterrupted.Luck);
        Assert.True(uninterrupted.CheckSuccess);
        Assert.Equal(CombatDecisionKind.Action, uninterrupted.NextDecisionKind);
        Assert.Equal(2, uninterrupted.Facts.Count(fact => fact.StartsWith("heal|", StringComparison.Ordinal)));
        int branchHeal = AssertFactIndex(uninterrupted.Facts, "heal|Conditioned regains 2");
        int outerHeal = AssertFactIndex(uninterrupted.Facts, "heal|Conditioned regains 3");
        Assert.True(branchHeal < outerHeal, string.Join(Environment.NewLine, uninterrupted.Facts));
    }

    [Fact]
    public void NestedSkillCheckMarksImprovementOnceWithOrWithoutPostRollAfterJsonRestore()
    {
        using TempModules modules = new();
        string root = WriteSkillPostRollRuleset(modules);
        ModuleSet set = ModuleLoader.Load(root, []);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition attacker = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition guard = rules.Find(DefinitionTypes.Monster, "guard_monster", out _)!;

        SkillMarkResult? declined = null;
        foreach (bool accept in new[] { false, true })
        {
            SkillMarkResult uninterrupted = RunSkillPostRoll(set, combat, attacker, guard, accept, restore: false);
            SkillMarkResult restored = RunSkillPostRoll(set, combat, attacker, guard, accept, restore: true);

            Assert.Equal(uninterrupted.Facts, restored.Facts);
            Assert.Equal(uninterrupted.SkillMark, restored.SkillMark);
            Assert.Equal(uninterrupted.SkillMarkCount, restored.SkillMarkCount);
            Assert.Equal(uninterrupted.CheckTotal, restored.CheckTotal);
            Assert.Equal(uninterrupted.CheckSuccess, restored.CheckSuccess);
            Assert.Equal(uninterrupted.CheckFactCount, restored.CheckFactCount);
            Assert.Equal(uninterrupted.PostRollFactCount, restored.PostRollFactCount);
            Assert.Equal(uninterrupted.PendingCheckRolls, restored.PendingCheckRolls);
            Assert.Equal(uninterrupted.DrawCursorAtOffer, uninterrupted.DrawCursorAfterChoice);
            Assert.Equal(restored.DrawCursorAtOffer, restored.DrawCursorAfterChoice);
            Assert.Equal(uninterrupted.DrawCursorAfterChoice, restored.DrawCursorAfterChoice);
            Assert.Equal(1, uninterrupted.SkillMark);
            Assert.Equal(1, uninterrupted.SkillMarkCount);
            Assert.True(uninterrupted.CheckSuccess);
            Assert.Equal(1, uninterrupted.CheckFactCount);
            Assert.Equal(accept ? 1 : 0, uninterrupted.PostRollFactCount);

            if (accept)
            {
                Assert.NotNull(declined);
                Assert.Equal(declined!.CheckTotal + 4, uninterrupted.CheckTotal);
            }
            else
            {
                declined = uninterrupted;
            }
        }
    }

    private static PostRollResult RunNestedPostRoll(
        RuleSet rules,
        Definition combat,
        Definition attackerDefinition,
        Definition guardDefinition,
        bool restore)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            const ulong seed = 7799;
            const string scope = "operation-tail-post-roll";
            DiceRoller dice = new(engine.Random, seed, scope);
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            attacker.Creature.Track("hit_points").Current = 5;
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatDecision action = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            CombatActionChoice nested = Assert.Single(action.Actions);
            CombatCommandResult waiting = runner.Submit(new CombatCommand.UseAction(attacker.Id, nested.Id, [guard.Id]));
            CombatDecision offered = AssertDecision(waiting.Observation, CombatDecisionKind.PostRoll);
            CombatDecisionOption option = Assert.Single(offered.Options!);
            CombatContinuationState saved = RoundTrip(runner.Capture());
            Assert.Single(saved.PendingCheckRolls);

            DiceRoller activeDice = dice;
            CombatRunner active = runner;
            if (restore)
            {
                activeDice = new DiceRoller(engine.Random, seed, scope, saved.NextRandomKey);
                Evaluator restoredEvaluator = new(rules, activeDice);
                active = CombatRunner.Restore(rules, combat,
                [
                    new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                    new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
                ], activeDice, saved);
                CombatDecision restoredOffered = AssertDecision(active.Observe(), CombatDecisionKind.PostRoll);
                Assert.Equal(offered.Id, restoredOffered.Id);
                option = Assert.Single(restoredOffered.Options!);
            }

            CombatCommandResult completed = active.Submit(new CombatCommand.Decide(offered.Id, option.Id));
            Assert.True(completed.Accepted, completed.Reason);
            return PostRollSnapshot(completed.Observation, saved, activeDice);
        });
    }

    private static PostRollResult RunConditionPostRoll(
        RuleSet rules,
        Definition combat,
        Definition attackerDefinition,
        Definition guardDefinition,
        bool restore)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            const ulong seed = 7799;
            const string scope = "condition-post-roll";
            DiceRoller dice = new(engine.Random, seed, scope);
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Conditioned", evaluator);
            attacker.Creature.Conditions.Add(rules.Find(DefinitionTypes.Condition, "focus", out _)!);
            attacker.Creature.Track("hit_points").Current = 5;
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));

            CombatDecision offered = AssertDecision(runner.Start(1), CombatDecisionKind.PostRoll);
            CombatDecisionOption option = Assert.Single(offered.Options!);
            CombatContinuationState saved = RoundTrip(runner.Capture());
            Assert.Single(saved.PendingCheckRolls);

            DiceRoller activeDice = dice;
            CombatRunner active = runner;
            if (restore)
            {
                activeDice = new DiceRoller(engine.Random, seed, scope, saved.NextRandomKey);
                Evaluator restoredEvaluator = new(rules, activeDice);
                Combatant restoredAttacker = Combatant.FromMonster(rules, attackerDefinition, "Conditioned", restoredEvaluator);
                restoredAttacker.Creature.Conditions.Add(rules.Find(DefinitionTypes.Condition, "focus", out _)!);
                active = CombatRunner.Restore(rules, combat,
                [
                    new CombatSide("Attackers", [restoredAttacker]),
                    new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
                ], activeDice, saved);
                CombatDecision restoredOffered = AssertDecision(active.Observe(), CombatDecisionKind.PostRoll);
                Assert.Equal(offered.Id, restoredOffered.Id);
                option = Assert.Single(restoredOffered.Options!);
            }

            CombatCommandResult completed = active.Submit(new CombatCommand.Decide(offered.Id, option.Id));
            Assert.True(completed.Accepted, completed.Reason);
            return PostRollSnapshot(completed.Observation, saved, activeDice);
        });
    }

    private static PostRollResult PostRollSnapshot(
        CombatObservation observation,
        CombatContinuationState saved,
        DiceRoller dice)
    {
        CombatantObservation actor = observation.Combatants.First(member => member.Side == 0);
        CheckFact check = Assert.Single(observation.Facts.OfType<CheckFact>());
        return new PostRollResult(
            observation.Facts.Select(fact => $"{fact.Kind}|{fact.Describe()}|{string.Join(",", fact.Rolls.Select(roll => roll.ToString()))}").ToArray(),
            actor.Tracks["hit_points"]!.Value,
            actor.Tracks["luck"]!.Value,
            check.Result.Total,
            check.Result.Success,
            observation.Facts.Count(fact => fact is CheckFact),
            observation.Facts.Count(fact => fact is PostRollFact),
            saved.PendingCheckRolls.Select(roll => roll.ToString()).ToArray(),
            saved.NextRandomKey,
            dice.NextRandomKey,
            observation.PendingDecision?.Kind);
    }

    private sealed record PostRollResult(
        IReadOnlyList<string> Facts,
        decimal ActorHitPoints,
        decimal Luck,
        decimal CheckTotal,
        bool CheckSuccess,
        int CheckFactCount,
        int PostRollFactCount,
        IReadOnlyList<string> PendingCheckRolls,
        long DrawCursorAtOffer,
        long DrawCursorAfterChoice,
        CombatDecisionKind? NextDecisionKind);

    private sealed record SkillMarkResult(
        IReadOnlyList<string> Facts,
        int SkillMark,
        int SkillMarkCount,
        decimal CheckTotal,
        bool CheckSuccess,
        int CheckFactCount,
        int PostRollFactCount,
        IReadOnlyList<string> PendingCheckRolls,
        long DrawCursorAtOffer,
        long DrawCursorAfterChoice);

    private static string WriteNestedPostRollRuleset(TempModules modules)
    {
        string root = WritePostRollBasics(modules);
        modules.Write("rules/nested_post_roll.json", """
            { "type": "action", "id": "nested_post_roll", "name": "Nested post-roll", "cost": { "turn": 1 }, "target": "enemy",
              "always": [
                { "op": "if", "when": "self.str >= 10", "then": [
                  { "op": "check", "by": "self", "check": "post_check", "outcomes": {
                    "success": [ { "op": "heal", "amount": "2", "to": "self" } ]
                  } }
                ] },
                { "op": "heal", "amount": "3", "to": "self" }
              ] }
            """);
        WriteMonster(modules, "attacker", "Attacker", "nested_post_roll", []);
        WriteMonster(modules, "guard_monster", "Guard", null, []);
        return root;
    }

    private static string WriteConditionPostRollRuleset(TempModules modules)
    {
        string root = WritePostRollBasics(modules);
        modules.Write("rules/wait.json", """
            { "type": "action", "id": "wait", "name": "Wait", "cost": { "turn": 1 }, "target": "self", "always": [] }
            """);
        modules.Write("rules/focus.json", """
            { "type": "condition", "id": "focus", "name": "Focus", "modifiers": [],
              "each_turn": [
                { "op": "check", "by": "self", "check": "post_check", "outcomes": {
                  "success": [ { "op": "heal", "amount": "2", "to": "self" } ]
                } },
                { "op": "heal", "amount": "3", "to": "self" }
              ] }
            """);
        modules.Write("rules/conditioned.json", """
            { "type": "monster", "id": "conditioned", "name": "Conditioned", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "wait" } ], "xp": 0 }
            """);
        modules.Write("rules/guard_monster.json", """
            { "type": "monster", "id": "guard_monster", "name": "Guard", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "5" }, "actions": [], "xp": 0 }
            """);
        return root;
    }

    private static string WritePostRollBasics(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/luck.json", """
            { "type": "track", "id": "luck", "name": "Luck", "max": "1", "start": "1", "min": "0" }
            """);
        WriteCombat(modules, "duel");
        modules.Write("rules/post_check.json", """
            { "type": "check", "id": "post_check", "name": "Post check", "roll": "1d4", "target": "5", "succeeds": "at-least",
              "post_roll": [ { "name": "Lucky", "track": "luck", "cost": "1", "bonus": "4", "score": "1" } ] }
            """);
        return root;
    }

    private static string WriteSkillPostRollRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/craft.json", """{ "type": "derived", "id": "craft", "name": "Craft", "value": "self.str" }""");
        modules.Write("rules/creation.json", """{ "type": "character-creation", "id": "standard", "name": "Standard", "attributes": ["str"], "attribute_roll": "10", "default": true }""");
        modules.Write("rules/advancement.json", """
            { "type": "advancement", "id": "improving", "name": "Improving skills", "kind": "improvement",
              "improvement": { "checks": [ { "skill": "craft", "when": "true", "amount": "1" } ] } }
            """);
        modules.Write("rules/luck.json", """
            { "type": "track", "id": "luck", "name": "Luck", "max": "1", "start": "1", "min": "0" }
            """);
        WriteCombat(modules, "duel");
        modules.Write("rules/post_check.json", """
            { "type": "check", "id": "post_check", "name": "Post check", "roll": "1d4", "skill": "craft", "target": "1", "succeeds": "at-least",
              "post_roll": [ { "name": "Lucky", "track": "luck", "cost": "1", "bonus": "4", "score": "1" } ] }
            """);
        modules.Write("rules/nested_skill_post_roll.json", """
            { "type": "action", "id": "nested_skill_post_roll", "name": "Nested skill post-roll", "cost": { "turn": 1 }, "target": "enemy",
              "always": [
                { "op": "if", "when": "self.str >= 10", "then": [
                  { "op": "check", "by": "self", "check": "post_check", "outcomes": {
                    "success": [ { "op": "heal", "amount": "2", "to": "self" } ]
                  } }
                ] },
                { "op": "heal", "amount": "3", "to": "self" }
              ] }
            """);
        WriteMonster(modules, "attacker", "Attacker", "nested_skill_post_roll", []);
        WriteMonster(modules, "guard_monster", "Guard", null, []);
        return root;
    }

    private static SkillMarkResult RunSkillPostRoll(
        ModuleSet set,
        Definition combat,
        Definition attackerDefinition,
        Definition guardDefinition,
        bool accept,
        bool restore)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            const ulong seed = 7799;
            const string scope = "operation-skill-post-roll";
            DiceRoller dice = new(engine.Random, seed, scope);
            RuleSet rules = set.Rules!;
            Character character = CreateImprovementCharacter(set, engine.Random, "skill-post-roll-character");
            Evaluator evaluator = new(rules, dice);
            Combatant template = Combatant.FromMonster(rules, attackerDefinition, character.Name, evaluator);
            template.Creature.Track("hit_points").Current = 5;
            Combatant attacker = new(character.Name, template.Creature, template.Uses) { Character = character };
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(guard.Id, CombatControlMode.Manual));

            CombatDecision action = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            CombatActionChoice nested = Assert.Single(action.Actions);
            CombatCommandResult waiting = runner.Submit(new CombatCommand.UseAction(attacker.Id, nested.Id, [guard.Id]));
            CombatDecision offered = AssertDecision(waiting.Observation, CombatDecisionKind.PostRoll);
            CombatDecisionOption option = Assert.Single(offered.Options!);
            CombatContinuationState saved = RoundTrip(runner.Capture());
            Assert.Single(saved.PendingCheckRolls);

            DiceRoller activeDice = dice;
            CombatRunner active = runner;
            Character activeCharacter = character;
            if (restore)
            {
                activeDice = new DiceRoller(engine.Random, seed, scope, saved.NextRandomKey);
                Evaluator restoredEvaluator = new(rules, activeDice);
                activeCharacter = CreateImprovementCharacter(set, engine.Random, "skill-post-roll-character-restored");
                Combatant restoredTemplate = Combatant.FromMonster(rules, attackerDefinition, activeCharacter.Name, restoredEvaluator);
                restoredTemplate.Creature.Track("hit_points").Current = 5;
                Combatant restoredAttacker = new(activeCharacter.Name, restoredTemplate.Creature, restoredTemplate.Uses) { Character = activeCharacter };
                active = CombatRunner.Restore(rules, combat,
                [
                    new CombatSide("Attackers", [restoredAttacker]),
                    new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
                ], activeDice, saved);
                CombatDecision restoredOffered = AssertDecision(active.Observe(), CombatDecisionKind.PostRoll);
                Assert.Equal(offered.Id, restoredOffered.Id);
                option = Assert.Single(restoredOffered.Options!);
            }

            CombatCommandResult completed = active.Submit(new CombatCommand.Decide(offered.Id, accept ? option.Id : null));
            Assert.True(completed.Accepted, completed.Reason);
            return SkillMarkSnapshot(completed.Observation, activeCharacter, saved, activeDice);
        });
    }

    private static Character CreateImprovementCharacter(ModuleSet set, Rusty.Engine.IRandomService random, string scope)
    {
        List<ModuleDiagnostic> problems = [];
        Character character = CharacterRules.Create(
            set.Rules!,
            Character.StampsOf(set),
            new CreationRequest("Ada", "warrior", null, Attributes: new Dictionary<string, decimal> { ["str"] = 15 }),
            new DiceRoller(random, 7798, scope),
            problems)!;
        Assert.Empty(problems);
        return character;
    }

    private static SkillMarkResult SkillMarkSnapshot(
        CombatObservation observation,
        Character character,
        CombatContinuationState saved,
        DiceRoller dice)
    {
        CheckFact check = Assert.Single(observation.Facts.OfType<CheckFact>());
        return new SkillMarkResult(
            observation.Facts.Select(fact => $"{fact.Kind}|{fact.Describe()}|{string.Join(",", fact.Rolls.Select(roll => roll.ToString()))}").ToArray(),
            character.SkillMarks.GetValueOrDefault("craft"),
            character.SkillMarks.Count,
            check.Result.Total,
            check.Result.Success,
            observation.Facts.Count(fact => fact is CheckFact),
            observation.Facts.Count(fact => fact is PostRollFact),
            saved.PendingCheckRolls.Select(roll => roll.ToString()).ToArray(),
            saved.NextRandomKey,
            dice.NextRandomKey);
    }

    private static ScenarioResult RunTailAction(
        RuleSet rules,
        Definition combat,
        Definition attackerDefinition,
        Definition guardDefinition,
        bool restore)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            const ulong seed = 9335;
            const string scope = "operation-tail-hit";
            DiceRoller dice = new(engine.Random, seed, scope);
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            attacker.Creature.Track("hit_points").Current = 5;
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(guard.Id, CombatControlMode.Manual));

            CombatDecision action = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            CombatActionChoice tail = Assert.Single(action.Actions);
            CombatCommandResult waiting = runner.Submit(new CombatCommand.UseAction(attacker.Id, tail.Id, [guard.Id]));
            CombatDecision interrupt = AssertDecision(waiting.Observation, CombatDecisionKind.Interrupt);
            CombatContinuationState saved = RoundTrip(runner.Capture());
            Assert.Single(saved.PendingInterruptRolls);

            CombatRunner active = runner;
            if (restore)
            {
                DiceRoller restoredDice = new(engine.Random, seed, scope, saved.NextRandomKey);
                Evaluator restoredEvaluator = new(rules, restoredDice);
                active = CombatRunner.Restore(rules, combat,
                [
                    new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                    new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
                ], restoredDice, saved);
            }

            CombatCommandResult completed = active.Submit(new CombatCommand.Decide(
                interrupt.Id,
                Assert.Single(interrupt.Options!).Id));
            Assert.True(completed.Accepted, completed.Reason);
            return Snapshot(completed.Observation, saved);
        });
    }

    private static ScenarioResult RunNestedTail(
        RuleSet rules,
        Definition combat,
        Definition attackerDefinition,
        Definition guardDefinition,
        bool restore)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            const ulong seed = 9335;
            const string scope = "operation-tail-nested";
            DiceRoller dice = new(engine.Random, seed, scope);
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, attackerDefinition, "Attacker", evaluator);
            attacker.Creature.Track("hit_points").Current = 5;
            Combatant guard = Combatant.FromMonster(rules, guardDefinition, "Guard", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Guards", [guard]),
            ], dice);
            Assert.True(runner.SetController(attacker.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(guard.Id, CombatControlMode.Manual));

            CombatDecision action = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            CombatActionChoice nested = Assert.Single(action.Actions);
            CombatCommandResult waiting = runner.Submit(new CombatCommand.UseAction(attacker.Id, nested.Id, [guard.Id]));
            CombatDecision interrupt = AssertDecision(waiting.Observation, CombatDecisionKind.Interrupt);
            CombatContinuationState saved = RoundTrip(runner.Capture());
            Assert.Single(saved.PendingInterruptRolls);

            CombatRunner active = runner;
            if (restore)
            {
                DiceRoller restoredDice = new(engine.Random, seed, scope, saved.NextRandomKey);
                Evaluator restoredEvaluator = new(rules, restoredDice);
                active = CombatRunner.Restore(rules, combat,
                [
                    new CombatSide("Attackers", [Combatant.FromMonster(rules, attackerDefinition, "Attacker", restoredEvaluator)]),
                    new CombatSide("Guards", [Combatant.FromMonster(rules, guardDefinition, "Guard", restoredEvaluator)]),
                ], restoredDice, saved);
            }

            CombatCommandResult completed = active.Submit(new CombatCommand.Decide(interrupt.Id));
            Assert.True(completed.Accepted, completed.Reason);
            return Snapshot(completed.Observation, saved);
        });
    }

    private static ScenarioResult RunMovementTail(
        RuleSet rules,
        Definition combat,
        Definition moverDefinition,
        Definition watcherDefinition,
        CombatSetup setup,
        bool restore)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            const ulong seed = 9335;
            const string scope = "operation-tail-movement";
            DiceRoller dice = new(engine.Random, seed, scope);
            Evaluator evaluator = new(rules, dice);
            Combatant mover = Combatant.FromMonster(rules, moverDefinition, "Mover", evaluator);
            mover.Creature.Track("hit_points").Current = 5;
            Combatant watcher = Combatant.FromMonster(rules, watcherDefinition, "Watcher", evaluator);
            CombatRunner runner = CombatRunner.Create(rules, combat,
            [
                new CombatSide("Movers", [mover]),
                new CombatSide("Watchers", [watcher]),
            ], dice, setup: setup);
            Assert.True(runner.SetController(mover.Id, CombatControlMode.Manual));
            Assert.True(runner.SetController(watcher.Id, CombatControlMode.Manual));

            CombatDecision action = AssertDecision(runner.Start(1), CombatDecisionKind.Action);
            CombatActionChoice advance = Assert.Single(action.Actions);
            CombatMoveChoice path = Assert.Single(advance.Moves, move => move.Destination == new Cell(0, 0));
            CombatCommandResult waiting = runner.Submit(new CombatCommand.UseAction(mover.Id, advance.Id, [watcher.Id], path.Path));
            CombatDecision interrupt = AssertDecision(waiting.Observation, CombatDecisionKind.Interrupt);
            CombatContinuationState saved = RoundTrip(runner.Capture());
            Assert.NotNull(saved.PendingMovement);

            CombatRunner active = runner;
            if (restore)
            {
                DiceRoller restoredDice = new(engine.Random, seed, scope, saved.NextRandomKey);
                Evaluator restoredEvaluator = new(rules, restoredDice);
                active = CombatRunner.Restore(rules, combat,
                [
                    new CombatSide("Movers", [Combatant.FromMonster(rules, moverDefinition, "Mover", restoredEvaluator)]),
                    new CombatSide("Watchers", [Combatant.FromMonster(rules, watcherDefinition, "Watcher", restoredEvaluator)]),
                ], restoredDice, saved, setup: setup);
            }

            CombatCommandResult completed = active.Submit(new CombatCommand.Decide(interrupt.Id));
            Assert.True(completed.Accepted, completed.Reason);
            return Snapshot(completed.Observation, saved);
        });
    }

    private static ScenarioResult Snapshot(CombatObservation observation, CombatContinuationState saved)
    {
        CombatantObservation actor = observation.Combatants.First(member => member.Side == 0);
        CombatantObservation target = observation.Combatants.First(member => member.Side == 1);
        return new ScenarioResult(
            observation.Facts.Select(fact => $"{fact.Kind}|{fact.Describe()}").ToArray(),
            actor.Tracks["hit_points"]!.Value,
            target.Tracks["hit_points"]!.Value,
            observation.Facts.Count(fact => fact is ReactionFact),
            actor.Position,
            saved.PendingInterruptRolls.Select(roll => roll.ToString()).ToArray());
    }

    private static int AssertFactIndex(IReadOnlyList<string> facts, string prefix)
    {
        int index = -1;
        for (int candidate = 0; candidate < facts.Count; candidate++)
        {
            if (facts[candidate].StartsWith(prefix, StringComparison.Ordinal))
            {
                index = candidate;
                break;
            }
        }
        Assert.True(index >= 0, $"No fact starts with '{prefix}': {string.Join(Environment.NewLine, facts)}");
        return index;
    }

    private static CombatDecision AssertDecision(CombatObservation observation, CombatDecisionKind kind)
    {
        Assert.Equal(kind, observation.PendingDecision?.Kind);
        Assert.NotNull(observation.PendingDecision);
        return observation.PendingDecision!;
    }

    private static CombatContinuationState RoundTrip(CombatContinuationState state) =>
        CombatContinuationState.FromJson(CombatContinuationState.ToJson(state));

    private sealed record ScenarioResult(
        IReadOnlyList<string> Facts,
        decimal ActorHitPoints,
        decimal TargetHitPoints,
        int ReactionCount,
        Cell? ActorPosition,
        IReadOnlyList<string> PendingRolls);

    private static string WriteTailRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        WriteCombat(modules, "duel");
        modules.Write("rules/tail_strike.json", """
            { "type": "action", "id": "tail_strike", "name": "Tail strike", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "damage", "amount": "1d4+2" }, { "op": "heal", "amount": "3", "to": "self" } ] }
            """);
        WriteGuard(modules);
        WriteMonster(modules, "attacker", "Attacker", "tail_strike", []);
        WriteMonster(modules, "guard_monster", "Guard", null, ["guard_reaction"]);
        return root;
    }

    private static string WriteNestedTailRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        WriteCombat(modules, "duel");
        modules.Write("rules/branch_check.json", """
            { "type": "check", "id": "branch_check", "name": "Branch", "roll": "1d4", "target": "1", "succeeds": "at-least" }
            """);
        modules.Write("rules/nested_tail.json", """
            { "type": "action", "id": "nested_tail", "name": "Nested tail", "cost": { "turn": 1 }, "target": "enemy",
              "always": [
                { "op": "if", "when": "self.str >= 10", "then": [
                  { "op": "check", "by": "self", "check": "branch_check", "outcomes": {
                    "success": [ { "op": "damage", "amount": "1d4+2" }, { "op": "heal", "amount": "2", "to": "self" } ]
                  } }
                ] },
                { "op": "heal", "amount": "3", "to": "self" }
              ] }
            """);
        WriteGuard(modules);
        WriteMonster(modules, "attacker", "Attacker", "nested_tail", []);
        WriteMonster(modules, "guard_monster", "Guard", null, ["guard_reaction"]);
        return root;
    }

    private static string WriteMovementTailRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/movement.json", """{ "type": "attribute", "id": "movement", "name": "Movement", "min": 0, "max": 10, "default": 3 }""");
        modules.Write("rules/movement_duel.json", """
            { "type": "combat", "id": "movement_duel", "name": "Movement duel", "initiative": "self.str", "initiative_by": "creature",
              "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6,
              "field": { "width": 8, "height": 1, "metric": "manhattan" },
              "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ],
              "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/advance.json", """
            { "type": "action", "id": "advance", "name": "Withdraw", "cost": { "turn": 1 }, "target": "enemy",
              "always": [ { "op": "move", "distance": "self.movement", "toward": "away", "beyond": "1" },
                           { "op": "heal", "amount": "3", "to": "self" } ] }
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
              "tracks": { "hit_points": "20" }, "stats": { "str": "15", "movement": "3" },
              "actions": [ { "action": "advance" } ], "xp": 0 }
            """);
        modules.Write("rules/watcher.json", """
            { "type": "monster", "id": "watcher", "name": "Watcher", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "5", "movement": "3" },
              "actions": [], "reactions": [ "parting_reaction" ], "xp": 0 }
            """);
        return root;
    }

    private static void WriteCombat(TempModules modules, string id)
    {
        modules.Write($"rules/{id}.json", $$"""
            { "type": "combat", "id": "{{id}}", "name": "Duel", "initiative": "self.str", "initiative_by": "creature",
              "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6,
              "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ],
              "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
    }

    private static void WriteGuard(TempModules modules)
    {
        modules.Write("rules/guard.json", """
            { "type": "action", "id": "guard", "name": "Guard", "cost": { "reaction": 0 }, "target": "self",
              "always": [ { "op": "reduce_damage", "amount": "1", "to": "self" } ] }
            """);
        modules.Write("rules/guard_reaction.json", """
            { "type": "reaction", "id": "guard_reaction", "name": "Guard", "trigger": "hit", "cost": { "reaction": 1 },
              "use": { "action": "guard" } }
            """);
    }

    private static void WriteMonster(TempModules modules, string id, string name, string? action, IReadOnlyList<string> reactions)
    {
        string actions = action is null ? "[]" : $$"""[ { "action": "{{action}}" } ]""";
        string reactionJson = reactions.Count == 0
            ? "[]"
            : $"[ {string.Join(", ", reactions.Select(reaction => $"\"{reaction}\""))} ]";
        modules.Write($"rules/{id}.json", $$"""
            { "type": "monster", "id": "{{id}}", "name": "{{name}}", "class": "warrior", "level": 1,
              "tracks": { "hit_points": "20" }, "stats": { "str": "{{(id == "attacker" ? "15" : "5")}}" },
              "actions": {{actions}}, "reactions": {{reactionJson}}, "xp": 0 }
            """);
    }
}
