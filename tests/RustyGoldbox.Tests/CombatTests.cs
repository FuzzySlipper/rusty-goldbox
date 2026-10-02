using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;
using static RustyGoldbox.Tests.TempModules;

namespace RustyGoldbox.Tests;

/// <summary>
/// The combat loop under three ruleset shapes: classic (descending AC, one
/// action), ascend (ascending AC, critical tier, standard and move budget) and
/// percentile (d100 roll-under, special and fumble tiers, active parry).
/// </summary>
public sealed class CombatTests
{
    private static string Fixture(string name) => Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", name);

    [Fact]
    public void ClassicPartyFightsAnEncounter()
    {
        using TempModules scratch = new();
        WriteCharacter(scratch, Rules.ClassicPath, "ada.json", new CreationRequest("Ada", "fighter", "human", Attributes: Scores(("str", 16), ("dex", 13), ("con", 15), ("int", 10), ("wis", 9), ("cha", 11))), "long_sword", "chain_mail", "shield");
        WriteCharacter(scratch, Rules.ClassicPath, "brom.json", new CreationRequest("Brom", "cleric", "dwarf", Attributes: Scores(("str", 13), ("dex", 10), ("con", 14), ("int", 9), ("wis", 15), ("cha", 10))), "heavy_mace", "chain_mail");

        Golden.Verify("classic-combat.txt", CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", Rules.ClassicPath, "--party", "ada.json,brom.json", "--encounter", "crypt_guard", "--seed", "3"],
            ["sim", "combat", "--module", Rules.ClassicPath, "--party", "ada.json,brom.json", "--encounter", "ogre", "--seed", "1", "--runs", "200"]));
    }

    [Fact]
    public void AscendingArmourClassFightUsesCriticalsAndTwoBudgets()
    {
        using TempModules scratch = new();
        WriteCharacter(scratch, Fixture("ascend"), "kara.json", new CreationRequest("Kara", "warrior", "folk", Attributes: Scores(("might", 16), ("grace", 12), ("grit", 14), ("wit", 10))), "longsword");

        Golden.Verify("ascend-combat.txt", CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", Fixture("ascend"), "--party", "kara.json", "--encounter", "brutes", "--seed", "11"]));
    }

    [Fact]
    public void PercentileFightRollsUnderAndParries()
    {
        using TempModules scratch = new();
        WriteCharacter(scratch, Fixture("percentile"), "sten.json", new CreationRequest("Sten", "soldier", "human", Attributes: Scores(("body", 15), ("agility", 13), ("mind", 10))), "sword");

        Golden.Verify("percentile-combat.txt", CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", Fixture("percentile"), "--party", "sten.json", "--encounter", "wolves", "--seed", "13"],
            ["sim", "combat", "--module", Fixture("percentile"), "--party", "sten.json", "--encounter", "wolves", "--seed", "13", "--runs", "100", "--json"]));
    }

    [Theory]
    [InlineData("15", "5", "at-least", "great")]
    [InlineData("12", "5", "at-least", "success")]
    [InlineData("4", "5", "at-least", "failure")]
    [InlineData("1", "15", "at-least", "awful")]
    [InlineData("2", "10", "at-most", "special")]
    [InlineData("39", "40", "at-most", "success")]
    public void CheckTiersReadTheRollAndMargin(string roll, string target, string succeeds, string tier)
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/test.json", $$"""
            { "type": "check", "id": "test", "name": "Test", "roll": "{{roll}}", "target": "{{target}}", "succeeds": "{{succeeds}}",
              "tiers": [ { "name": "great", "when": "check.margin >= 10" }, { "name": "awful", "when": "check.margin <= -10" }, { "name": "special", "when": "check.roll <= floor(check.target / 5)" } ] }
            """);
        RuleSet rules = Rules.LoadValid(root);

        CheckResult result = new Evaluator(rules, null).Check(rules.Find(DefinitionTypes.Check, "test", out _)!, new Creature("self"), null);

        Assert.Equal(tier, result.Tier);
    }

    [Fact]
    public void DicePoolsCountSuccesses()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(Rules.WriteSmallRuleset(modules));
        CompiledExpression pool = rules.Compile("roll_count(6, 10, 8)", "rules", Roots.None);

        (decimal successes, DiceRoll roll) = WithDice(dice => (new Evaluator(rules, dice).Evaluate(pool, null, null).Number, dice.Rolls.Single()));

        Assert.Equal(roll.Faces.Count(face => face >= 8), successes);
        Assert.Equal(6, roll.Faces.Count);
    }

    [Fact]
    public void ConditionsTickLastAndStopActions()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition hexer = rules.Find(DefinitionTypes.Monster, "hexer", out _)!;
        Definition dummy = rules.Find(DefinitionTypes.Monster, "dummy", out _)!;

        CombatResult result = WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            return CombatRunner.Run(rules, combat,
            [
                new CombatSide("Hexers", [Combatant.FromMonster(rules, hexer, "Hexer", evaluator)]),
                new CombatSide("Dummies", [Combatant.FromMonster(rules, dummy, "Dummy", evaluator)]),
            ], dice, maxRounds: 10);
        });

        List<string> lines = result.Facts.Select(fact => fact.Describe()).ToList();
        Assert.Contains("Dummy is Hexed for 1 round.", lines);
        Assert.Contains("Dummy doesn't act (hexed).", lines);
        Assert.Contains("Dummy takes 3 damage (7 hit points left).", lines);
        Assert.Contains("Dummy is no longer Hexed.", lines);
        Assert.Equal("Hexers", result.Sides[result.Winner!.Value].Name);
    }

    [Fact]
    public void DurationsCoverTheHoldersNextTurnWhateverTheOrder()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/quick_dummy.json", """
            { "type": "monster", "id": "quick_dummy", "name": "Quick dummy", "hit_points": "10", "stats": { "str": "20" },
              "actions": [ { "action": "smite", "damage": "1" } ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = Fight(rules, "duel", "hexer", "quick_dummy", maxRounds: 2);

        // The dummy acts first in round 1, so the hex lands after its turn and must still cost it round 2.
        int hexed = lines.IndexOf("Quick dummy is Hexed for 1 round.");
        int round2 = lines.IndexOf("Round 2.");
        Assert.True(hexed >= 0 && hexed < round2);
        Assert.Contains("Quick dummy doesn't act (hexed).", lines.Skip(round2));
    }

    [Fact]
    public void SurprisedSidesLoseTheirFirstRound()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/ambush.json", """
            { "type": "combat", "id": "ambush", "name": "Ambush", "surprise": "1", "initiative": "self.str", "initiative_by": "side",
              "initiative_order": "highest-first", "initiative_each": "combat", "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 } ],
              "defeated": "self.hit_points <= 0" }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = Fight(rules, "ambush", "hexer", "dummy", maxRounds: 2);

        Assert.Equal(["Hexers is surprised for 1 round.", "Dummies is surprised for 1 round."], lines.Take(2));
        int round2 = lines.IndexOf("Round 2.");
        Assert.Contains("Hexer doesn't act (surprised).", lines.Take(round2));
        Assert.Contains("Hexer uses Hex on Dummy.", lines.Skip(round2));
        Assert.Single(lines, line => line.Contains("for initiative", StringComparison.Ordinal) && line.StartsWith("Hexers", StringComparison.Ordinal));
    }

    [Fact]
    public void DefeatIsCheckedAfterEveryOperation()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/rout.json", """
            { "type": "combat", "id": "rout", "name": "Rout", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first",
              "initiative_each": "round", "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 } ], "defeated": "self.hit_points <= 0 or self.hit < -5" }
            """);
        modules.Write("rules/crushed.json", """{ "type": "condition", "id": "crushed", "name": "Crushed", "modifiers": [ { "stat": "hit", "value": "-10" } ] }""");
        modules.Write("rules/crush.json", """
            { "type": "action", "id": "crush", "name": "Crush", "cost": { "turn": 1 }, "target": "enemy", "always": [ { "op": "apply_condition", "condition": "crushed" } ] }
            """);
        modules.Write("rules/crusher.json", """
            { "type": "monster", "id": "crusher", "name": "Crusher", "hit_points": "5", "stats": { "str": "15" }, "actions": [ { "action": "crush" } ], "xp": 0 }
            """);
        RuleSet rules = Rules.LoadValid(root);

        List<string> lines = Fight(rules, "rout", "crusher", "dummy", maxRounds: 3);

        Assert.Equal(["Dummy is Crushed.", "Dummy is out of the fight."], lines.SkipWhile(line => line != "Dummy is Crushed.").Take(2));
    }

    [Fact]
    public void FailuresDuringAFightNameTheirDefinition()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/smite.json", """
            { "type": "action", "id": "smite", "name": "Smite", "cost": { "turn": 1 }, "target": "enemy", "parameters": ["damage"],
              "always": [ { "op": "damage", "amount": "use.damage / (self.str - self.str)" } ] }
            """);
        RuleSet rules = Rules.LoadValid(root);

        CombatFailure failure = Assert.Throws<CombatFailure>(() => Fight(rules, "duel", "dummy", "dummy", maxRounds: 1));

        Assert.Equal(("combat.evaluate", "$.always[0].amount"), (failure.Diagnostic.Rule, failure.Diagnostic.JsonPath));
        Assert.EndsWith("smite.json", failure.Diagnostic.File, StringComparison.Ordinal);
        Assert.Contains("Division by zero", failure.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedNamesStayDistinctAcrossRuns()
    {
        using TempModules scratch = new();
        WriteCharacter(scratch, Fixture("percentile"), "sten.json", new CreationRequest("Sten", "soldier", "human", Attributes: Scores(("body", 15), ("agility", 13), ("mind", 10))), "sword");

        string transcript = CliTranscript.Run(scratch.Root,
            ["sim", "combat", "--module", Fixture("percentile"), "--party", "sten.json,sten.json", "--encounter", "wolves", "--seed", "1", "--runs", "3"]);

        Assert.Contains("Sten (2) still fighting", transcript, StringComparison.Ordinal);
        Assert.Contains("[exit 0]", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionsAndUsesAreCheckedAtLoad()
    {
        using TempModules modules = new();
        string root = DuelRuleset(modules);
        modules.Write("rules/bad_cost.json", """{ "type": "action", "id": "bad_cost", "name": "Bad", "cost": { "mana": 1 }, "target": "self", "always": [ { "op": "heal", "amount": "1" } ] }""");
        modules.Write("rules/free.json", """{ "type": "action", "id": "free", "name": "Free", "cost": { "turn": 0 }, "target": "self", "always": [ { "op": "heal", "amount": "1" } ] }""");
        modules.Write("rules/no_check.json", """{ "type": "action", "id": "no_check", "name": "No check", "cost": { "turn": 1 }, "target": "enemy", "outcomes": { "success": [] } }""");
        modules.Write("rules/bad_tier.json", """{ "type": "action", "id": "bad_tier", "name": "Bad tier", "cost": { "turn": 1 }, "target": "enemy", "check": "always_hits", "outcomes": { "critical": [] } }""");
        modules.Write("rules/bad_op.json", """{ "type": "condition", "id": "bad_op", "name": "Bad op", "modifiers": [], "each_turn": [ { "op": "damage", "amount": "1", "to": "target" }, { "op": "explode" } ] }""");
        modules.Write("rules/user.json", """
            { "type": "monster", "id": "user", "name": "User", "class": "warrior", "level": 1, "hit_points": "5",
              "actions": [ { "action": "smite" }, { "action": "smite", "damage": "1", "colour": "2" } ], "xp": 0 }
            """);

        ModuleSet set = ModuleLoader.Load(root, []);

        Assert.Equal(
            [
                ("definition.field-value", "bad_op.json", "$.each_turn[0].to"),
                ("definition.operation", "bad_op.json", "$.each_turn[1].op"),
            ],
            set.Diagnostics.Select(diagnostic => (diagnostic.Rule, Path.GetFileName(diagnostic.File!), diagnostic.JsonPath!)));
        modules.Write("rules/bad_op.json", """{ "type": "condition", "id": "bad_op", "name": "Bad op", "modifiers": [] }""");

        ModuleSet again = ModuleLoader.Load(root, []);

        Assert.Equal(
            [
                ("action.cost", "bad_cost.json", "$.cost.mana"),
                ("action.cost", "free.json", "$.cost"),
                ("action.outcomes", "bad_tier.json", "$.outcomes.critical"),
                ("action.outcomes", "no_check.json", "$"),
                ("action.outcomes", "no_check.json", "$.outcomes"),
                ("use.parameter", "user.json", "$.actions[0]"),
                ("use.parameter", "user.json", "$.actions[1].colour"),
            ],
            again.Diagnostics.Select(diagnostic => (diagnostic.Rule, Path.GetFileName(diagnostic.File!), diagnostic.JsonPath!)).Order());
    }

    /// <summary>
    /// A small ruleset without dice in its formulas: the hexer always hits and
    /// hexes (no actions, 3 damage a turn, 1 round); the dummy just takes it.
    /// </summary>
    private static string DuelRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/combat.json", """
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "round",
              "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 } ], "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/always_hits.json", """{ "type": "check", "id": "always_hits", "name": "Always hits", "roll": "20", "target": "10", "succeeds": "at-least" }""");
        modules.Write("rules/hexed.json", """
            { "type": "condition", "id": "hexed", "name": "Hexed", "modifiers": [], "prevents_actions": true,
              "each_turn": [ { "op": "damage", "amount": "3", "to": "self" } ] }
            """);
        modules.Write("rules/hex.json", """
            { "type": "action", "id": "hex", "name": "Hex", "cost": { "turn": 1 }, "target": "enemy", "available": "true", "check": "always_hits",
              "outcomes": { "success": [ { "op": "apply_condition", "condition": "hexed", "rounds": "1" } ] } }
            """);
        modules.Write("rules/smite.json", """
            { "type": "action", "id": "smite", "name": "Smite", "cost": { "turn": 1 }, "target": "enemy", "parameters": ["damage"],
              "always": [ { "op": "damage", "amount": "use.damage" } ] }
            """);
        modules.Write("rules/hexer.json", """
            { "type": "monster", "id": "hexer", "name": "Hexer", "class": "warrior", "level": 1, "hit_points": "20", "stats": { "str": "15" },
              "actions": [ { "action": "hex" }, { "action": "smite", "damage": "1" } ], "xp": 0 }
            """);
        modules.Write("rules/dummy.json", """
            { "type": "monster", "id": "dummy", "name": "Dummy", "class": "warrior", "level": 1, "hit_points": "10", "stats": { "str": "5" },
              "actions": [ { "action": "smite", "damage": "1" } ], "xp": 0 }
            """);
        return root;
    }

    private static List<string> Fight(RuleSet rules, string combatId, string first, string second, int maxRounds)
    {
        Definition combat = rules.Find(DefinitionTypes.Combat, combatId, out _)!;
        Definition a = rules.Find(DefinitionTypes.Monster, first, out _)!;
        Definition b = rules.Find(DefinitionTypes.Monster, second, out _)!;
        CombatResult result = WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            return CombatRunner.Run(rules, combat, Encounters.Distinct(
            [
                new CombatSide(a.Name + "s", [Combatant.FromMonster(rules, a, a.Name, evaluator)]),
                new CombatSide(b.Name.Replace("y", "ie", StringComparison.Ordinal) + "s", [Combatant.FromMonster(rules, b, b.Name, evaluator)]),
            ]), dice, maxRounds);
        });
        return result.Facts.Select(fact => fact.Describe()).ToList();
    }

    private static void WriteCharacter(TempModules scratch, string module, string file, CreationRequest request, params string[] equipment)
    {
        ModuleSet set = ModuleLoader.Load(module, []);
        List<ModuleDiagnostic> problems = [];
        Character character = WithDice(dice => CharacterRules.Create(set.Rules!, Character.StampsOf(set), request, dice, problems))!;
        Assert.Empty(problems);
        character.Equipment.AddRange(equipment.Select(id => set.Rules!.Find(DefinitionTypes.Item, id, out _)!));
        File.WriteAllText(Path.Combine(scratch.Root, file), CharacterFile.ToJson(character));
    }

    private static Dictionary<string, decimal> Scores(params (string Id, decimal Score)[] scores) => scores.ToDictionary(score => score.Id, score => score.Score);

    private static T WithDice<T>(Func<DiceRoller, T> work)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(1, "tests"));
            return work(new DiceRoller(engine.Random, stream));
        });
    }
}
