using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

public sealed class CombatRollTests
{
    [Fact]
    public void ACheckCanSpendAResourceForABonusAfterItsRoll()
    {
        using TempModules modules = new();
        string root = RollRuleset(modules, "9");
        RuleSet rules = Rules.LoadValid(root);
        CombatResult result = Fight(rules, "attacker", "dummy");

        PostRollFact choice = Assert.Single(result.Facts.OfType<PostRollFact>());
        Assert.Equal("+2", choice.Effect);
        Assert.Equal("luck", choice.Track.Id);
        Assert.Equal(11, choice.After);
        Assert.Contains(result.Facts.OfType<SpentFact>(), spent => spent.Track.Id == "luck" && spent.Left == 0);
        CheckFact check = Assert.Single(result.Facts.OfType<CheckFact>());
        Assert.Equal("success", check.Result.Tier);
        Assert.Contains(result.Facts.OfType<DamageFact>(), damage => damage.Who == "Dummy" && damage.Amount == 1);
    }

    [Fact]
    public void AScoredPostRollPolicyCanChooseAReroll()
    {
        using TempModules modules = new();
        string root = RollRuleset(modules, "1");
        RuleSet rules = Rules.LoadValid(root);
        CombatResult result = Fight(rules, "attacker", "dummy");

        PostRollFact choice = Assert.Single(result.Facts.OfType<PostRollFact>());
        Assert.Equal("reroll", choice.Effect);
        Assert.Equal("Luck reroll", choice.Option);
        Assert.Contains(result.Facts.OfType<CheckFact>(), check => check.Result.Tier == "failure");
        Assert.DoesNotContain(result.Facts.OfType<DamageFact>(), damage => damage.Who == "Dummy");
    }

    [Fact]
    public void APostRollRerollKeepsAnOpponentsRolledTarget()
    {
        using TempModules modules = new();
        string root = RollRuleset(modules, "1", "target.str + 1d6");
        RuleSet rules = Rules.LoadValid(root);
        CombatResult result = Fight(rules, "attacker", "dummy");

        CheckFact check = Assert.Single(result.Facts.OfType<CheckFact>());
        Assert.Equal("reroll", Assert.Single(result.Facts.OfType<PostRollFact>()).Effect);
        Assert.Single(check.Rolls);
        Assert.Equal(11, check.Result.Target);
    }

    [Fact]
    public void QuickenedAddsToTheCurrentTurnBudgetInTheOriginalFixture()
    {
        RuleSet rules = Rules.LoadValid(Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "degrees"));
        Definition combat = rules.Find(DefinitionTypes.Combat, "standard", out _)!;
        Definition monster = rules.Find(DefinitionTypes.Monster, "quickened_raider", out _)!;
        CombatResult result = WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant actor = Combatant.FromMonster(rules, monster, "Surger", evaluator);
            Combatant target = Combatant.FromMonster(rules, monster, "Target", evaluator);
            return CombatRunner.Run(rules, combat,
            [
                new CombatSide("Surging", [actor]),
                new CombatSide("Targeting", [target]),
            ], dice, maxRounds: 1);
        });

        Assert.Single(result.Facts.OfType<ActionFact>(), action => action.Who == "Surger" && action.Action == "Quickened");
        Assert.Equal(4, result.Facts.OfType<ActionFact>().Count(action => action.Who == "Surger" && action.Action == "Poke"));
        Assert.Contains(result.Facts.OfType<SpentFact>(), spent => spent.Who == "Surger" && spent.Track.Id == "quickened");
    }

    [Fact]
    public void HitReactionReducesDamageInsideAnInstantCondition()
    {
        using TempModules modules = new();
        string root = HitReactionRuleset(modules);
        RuleSet rules = Rules.LoadValid(root);
        CombatResult result = Fight(rules, "attacker", "guard");

        int reaction = IndexOf(result.Facts, fact => fact is ReactionFact { Reaction: "Guard" });
        int damage = IndexOf(result.Facts, fact => fact is DamageFact { Who: "Guard" });
        Assert.True(reaction >= 0 && reaction < damage);
        DamageFact taken = Assert.Single(result.Facts.OfType<DamageFact>(), fact => fact.Who == "Guard" && fact.Track.Id == "hit_points");
        Assert.Equal(3, taken.Amount);
        Assert.Equal(17, taken.Left);
        DamageFact shield = Assert.Single(result.Facts.OfType<DamageFact>(), fact => fact.Who == "Guard" && fact.Track.Id == "shield_points");
        Assert.Equal(3, shield.Amount);
        Assert.Equal(2, shield.Left);
    }

    [Fact]
    public void LicensedReactionDataUsesConfirmedHitsAndTracksShieldDamage()
    {
        ModuleSet three = ModuleLoader.Load(Path.Combine(Rules.RepositoryRoot, "modules", "three-action"), []);
        Assert.Empty(three.Diagnostics);
        Definition shield = three.Rules!.Find(DefinitionTypes.Reaction, "shield_block", out _)!;
        Assert.Equal("hit", shield.Json.GetProperty("trigger").GetString());
        Assert.True(shield.Json.GetProperty("physical").GetBoolean());
        Definition block = three.Rules.Find(DefinitionTypes.Action, "block", out _)!;
        Assert.Contains("shield_track", block.Json.GetRawText(), StringComparison.Ordinal);

        ModuleSet fifth = ModuleLoader.Load(Path.Combine(Rules.RepositoryRoot, "modules", "fifth-srd"), []);
        Assert.Empty(fifth.Diagnostics);
        Definition uncanny = fifth.Rules!.Find(DefinitionTypes.Reaction, "uncanny_dodge", out _)!;
        Assert.Equal("hit", uncanny.Json.GetProperty("trigger").GetString());
        Assert.Contains("fraction", fifth.Rules.Find(DefinitionTypes.Action, "uncanny_dodge", out _)!.Json.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void NewCombatDataReportsActionableOperationPaths()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/combat.json", """
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "combat",
              "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/bad_check.json", """
            { "type": "check", "id": "bad_check", "name": "Bad check", "roll": "1", "target": "1", "succeeds": "at-least",
              "post_roll": [ { "track": "hit_points", "cost": "1", "bonus": "1", "reroll": true, "score": "1" } ] }
            """);
        modules.Write("rules/bad_action.json", """
            { "type": "action", "id": "bad_action", "name": "Bad action", "cost": { "turn": 1 }, "target": "self",
              "always": [ { "op": "reduce_damage", "to": "self" } ] }
            """);

        ModuleSet set = ModuleLoader.Load(root, []);

        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.Rule == "check.post-roll" && diagnostic.JsonPath == "$.post_roll[0]");
        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.Rule == "operation.reduce-damage" && diagnostic.JsonPath == "$.always[0]");
    }

    private static string RollRuleset(TempModules modules, string roll, string target = "10")
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/luck.json", """
            { "type": "track", "id": "luck", "name": "Luck", "max": "1", "start": "1", "min": "0" }
            """);
        modules.Write("rules/combat.json", """
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "combat",
              "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/check.json", $$"""
            { "type": "check", "id": "strike", "name": "Strike", "roll": "{{roll}}", "target": "{{target}}", "succeeds": "at-least",
              "post_roll": [
                { "name": "Luck +2", "track": "luck", "cost": "1", "bonus": "2", "score": "if check.margin < 0 then 2 else -1" },
                { "name": "Luck reroll", "track": "luck", "cost": "1", "reroll": true, "score": "if check.margin < -2 then 3 else -1" }
              ] }
            """);
        modules.Write("rules/strike.json", """
            { "type": "action", "id": "strike", "name": "Strike", "cost": { "turn": 1 }, "target": "enemy", "check": "strike",
              "outcomes": { "success": [ { "op": "damage", "amount": "1" } ] } }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1, "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "strike" } ], "xp": 0 }
            """);
        modules.Write("rules/dummy.json", """
            { "type": "monster", "id": "dummy", "name": "Dummy", "class": "warrior", "level": 1, "tracks": { "hit_points": "20" }, "stats": { "str": "5" },
              "actions": [], "xp": 0 }
            """);
        return root;
    }

    private static string HitReactionRuleset(TempModules modules)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/shield_points.json", """
            { "type": "track", "id": "shield_points", "name": "Shield hit points", "max": "5", "start": "0", "start_on_combat": "true", "min": "0" }
            """);
        modules.Write("rules/combat.json", """
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "combat",
              "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("rules/impact.json", """
            { "type": "condition", "id": "impact", "name": "Impact", "instant": true, "values": { "amount": 0, "physical": 0 }, "modifiers": [],
              "on_apply": [ { "op": "damage", "amount": "condition.amount", "to": "self" } ] }
            """);
        modules.Write("rules/check.json", """
            { "type": "check", "id": "strike", "name": "Strike", "roll": "20", "target": "10", "succeeds": "at-least" }
            """);
        modules.Write("rules/strike.json", """
            { "type": "action", "id": "strike", "name": "Strike", "cost": { "turn": 1 }, "target": "enemy", "check": "strike",
              "outcomes": { "success": [ { "op": "apply_condition", "condition": "impact", "values": { "amount": "5", "physical": "1" } } ] } }
            """);
        modules.Write("rules/block.json", """
            { "type": "action", "id": "block", "name": "Guard", "cost": { "reaction": 0 }, "target": "self",
              "always": [ { "op": "reduce_damage", "amount": "2", "shield_track": "shield_points", "to": "self" } ] }
            """);
        modules.Write("rules/guard_reaction.json", """
            { "type": "reaction", "id": "guard_reaction", "name": "Guard", "trigger": "hit", "physical": true, "cost": { "reaction": 1 }, "use": { "action": "block" } }
            """);
        modules.Write("rules/attacker.json", """
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "warrior", "level": 1, "tracks": { "hit_points": "20" }, "stats": { "str": "15" },
              "actions": [ { "action": "strike" } ], "xp": 0 }
            """);
        modules.Write("rules/guard.json", """
            { "type": "monster", "id": "guard", "name": "Guard", "class": "warrior", "level": 1, "tracks": { "hit_points": "20" }, "stats": { "str": "5" },
              "actions": [], "reactions": [ "guard_reaction" ], "xp": 0 }
            """);
        return root;
    }

    private static CombatResult Fight(RuleSet rules, string first, string second)
    {
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition one = rules.Find(DefinitionTypes.Monster, first, out _)!;
        Definition two = rules.Find(DefinitionTypes.Monster, second, out _)!;
        return WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            return CombatRunner.Run(rules, combat,
            [
                new CombatSide("First", [Combatant.FromMonster(rules, one, one.Name, evaluator)]),
                new CombatSide("Second", [Combatant.FromMonster(rules, two, two.Name, evaluator)]),
            ], dice, maxRounds: 1);
        });
    }

    private static T WithDice<T>(Func<DiceRoller, T> work)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(1, "combat-roll-tests"));
            return work(new DiceRoller(engine.Random, stream));
        });
    }

    private static int IndexOf(IReadOnlyList<CombatFact> facts, Func<CombatFact, bool> predicate)
    {
        for (int index = 0; index < facts.Count; index++)
        {
            if (predicate(facts[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
