using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

public sealed class CombatRollTests
{
    [Fact]
    public void SkillUseIsMarkedAfterAPostRollChangesFailureToSuccess()
    {
        using TempModules modules = new();
        string root = RollRuleset(modules, "9");
        modules.Write("rules/creation.json", """{ "type": "character-creation", "id": "standard", "name": "Standard", "attributes": ["str"], "attribute_roll": "15" }""");
        modules.Write("rules/advancement.json", """{ "type": "advancement", "id": "practice", "name": "Practice", "kind": "improvement", "improvement": { "checks": [{ "skill": "str", "when": "true", "amount": "1" }] } }""");
        string checkFile = Path.Combine(root, "check.json");
        File.WriteAllText(checkFile, File.ReadAllText(checkFile).Replace("\"name\": \"Strike\"", "\"name\": \"Strike\", \"skill\": \"str\"", StringComparison.Ordinal));
        ModuleSet set = ModuleLoader.Load(root, []);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        Character character = WithDice(dice => CharacterRules.Create(rules, Character.StampsOf(set), new CreationRequest("Ada", "warrior", null), dice, []))!;
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition monster = rules.Find(DefinitionTypes.Monster, "attacker", out _)!;
        Definition dummy = rules.Find(DefinitionTypes.Monster, "dummy", out _)!;

        CombatResult result = WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant rolled = Combatant.FromMonster(rules, monster, character.Name, evaluator);
            Combatant actor = new(character.Name, rolled.Creature, rolled.Uses) { Character = character };
            return CombatRunner.Run(rules, combat,
                [new CombatSide("Party", [actor]), new CombatSide("Enemies", [Combatant.FromMonster(rules, dummy, dummy.Name, evaluator)])], dice, maxRounds: 1);
        });

        Assert.Equal("success", Assert.Single(result.Facts.OfType<CheckFact>()).Result.Tier);
        Assert.Equal(1, character.SkillMarks["str"]);
    }

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
    public void APostRollRerollKeepsRandomizedBonusAndModifier()
    {
        using TempModules modules = new();
        string root = RollRuleset(modules, "1", "100", "1d6");
        RuleSet rules = Rules.LoadValid(root);
        Definition lucky = rules.Find(DefinitionTypes.Condition, "lucky", out _)!;
        CombatResult result = Fight(rules, "attacker", "dummy", lucky);

        CheckFact check = Assert.Single(result.Facts.OfType<CheckFact>());
        Assert.Equal("reroll", Assert.Single(result.Facts.OfType<PostRollFact>()).Effect);
        Assert.Equal(2, check.Rolls.Count);
        Assert.NotEqual(0, check.Result.Bonus);
        Assert.NotEqual(0, check.Result.Modifier);
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
        Assert.True(uncanny.Json.GetProperty("attack").GetBoolean());
        Assert.Contains("fraction", fifth.Rules.Find(DefinitionTypes.Action, "uncanny_dodge", out _)!.Json.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void FifthUncannyDodgeAnswersWeaponAndSpellAttacksOnly()
    {
        foreach (string action in new[] {
            "{ \"action\": \"fifth-srd:monster_melee\", \"to_hit\": \"20\", \"damage\": \"6\", \"dice\": \"1\" }",
            "{ \"action\": \"fifth-srd:spell_bolt\", \"range\": \"24\", \"damage\": \"6\", \"dice\": \"1\" }" })
        {
            CombatResult result = FifthReactionFight(action);
            Assert.Contains(result.Facts.OfType<ReactionFact>(), reaction => reaction.Reaction == "Uncanny Dodge");
            Assert.Contains(result.Facts.OfType<DamageFact>(), damage => damage.Who == "Rogue" && damage.Amount > 0 && damage.Amount < 6);
        }

        foreach (string action in new[] {
            "{ \"action\": \"fifth-srd:fireball\" }",
            "{ \"action\": \"fifth-srd:magic_missile\" }" })
        {
            CombatResult result = FifthReactionFight(action);
            Assert.DoesNotContain(result.Facts.OfType<ReactionFact>(), reaction => reaction.Reaction == "Uncanny Dodge");
            Assert.Contains(result.Facts.OfType<DamageFact>(), damage => damage.Who == "Rogue" && damage.Amount > 0);
        }
    }

    [Fact]
    public void SpellShieldBlockAnswersPhysicalStrikesButNotFireball()
    {
        CombatResult strike = ThreeActionShieldFight("{ \"action\": \"three-action:melee_strike\", \"damage\": \"10\", \"die\": \"0\", \"deadly\": \"0\", \"map\": \"0\", \"martial\": \"1\", \"slashing\": \"1\" }");
        Assert.Contains(strike.Facts.OfType<ReactionFact>(), reaction => reaction.Reaction == "Shield Block");
        Assert.Contains(strike.Facts.OfType<DamageFact>(), damage => damage.Who == "Shield bearer" && damage.Amount == 15);

        CombatResult fireball = ThreeActionShieldFight("{ \"action\": \"three-action:fireball\" }");
        Assert.DoesNotContain(fireball.Facts.OfType<ReactionFact>(), reaction => reaction.Reaction == "Shield Block");
        Assert.Contains(fireball.Facts.OfType<DamageFact>(), damage => damage.Who == "Shield bearer" && damage.Amount > 0);
        Assert.Contains(fireball.Sides.SelectMany(side => side.Members), member => member.Name == "Shield bearer" && member.Creature.Conditions.Any(condition => condition.Id == "shield_spell"));
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

    private static string RollRuleset(TempModules modules, string roll, string target = "10", string? bonus = null)
    {
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/luck.json", """
            { "type": "track", "id": "luck", "name": "Luck", "max": "1", "start": "1", "min": "0" }
            """);
        modules.Write("rules/lucky.json", """
            { "type": "condition", "id": "lucky", "name": "Lucky", "modifiers": [ { "check": "strike", "value": "1d6" } ] }
            """);
        modules.Write("rules/combat.json", """
            { "type": "combat", "id": "duel", "name": "Duel", "initiative": "self.str", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "combat",
              "round_seconds": 6, "budget": [ { "id": "turn", "per_turn": 1 } ], "track": "hit_points", "defeated": "self.hit_points <= 0" }
            """);
        string bonusField = bonus is null ? "" : $", \"bonus\": \"{bonus}\"";
        modules.Write("rules/check.json", $$"""
            { "type": "check", "id": "strike", "name": "Strike", "roll": "{{roll}}"{{bonusField}}, "target": "{{target}}", "succeeds": "at-least",
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

    private static CombatResult Fight(RuleSet rules, string first, string second, Definition? firstCondition = null)
    {
        Definition combat = rules.Find(DefinitionTypes.Combat, "duel", out _)!;
        Definition one = rules.Find(DefinitionTypes.Monster, first, out _)!;
        Definition two = rules.Find(DefinitionTypes.Monster, second, out _)!;
        return WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant actor = Combatant.FromMonster(rules, one, one.Name, evaluator);
            if (firstCondition is not null)
            {
                actor.Creature.Conditions.Add(firstCondition);
            }

            return CombatRunner.Run(rules, combat,
            [
                new CombatSide("First", [actor]),
                new CombatSide("Second", [Combatant.FromMonster(rules, two, two.Name, evaluator)]),
            ], dice, maxRounds: 1);
        });
    }

    private static CombatResult FifthReactionFight(string attackerUse)
    {
        using TempModules modules = new();
        string root = modules.Module("probe", "extension", requires: TempModules.Require("fifth-srd", "*"));
        modules.Write("probe/combat.json", """
            { "type": "combat", "id": "probe", "name": "Probe", "initiative": "self.initiative_bonus", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "combat",
              "round_seconds": 6, "budget": [ { "id": "action", "per_turn": 1 }, { "id": "reaction", "per_turn": 1 } ], "track": "fifth-srd:hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("probe/rogue.json", """
            { "type": "monster", "id": "rogue", "name": "Shield bearer", "class": "fifth-srd:rogue", "level": 5,
              "tracks": { "fifth-srd:hit_points": "20" },
              "stats": { "initiative_bonus": "1", "ac": "10", "dex_save": "-10", "str": "10", "dex": "10" },
              "actions": [], "reactions": [ "fifth-srd:uncanny_dodge" ], "xp": 0 }
            """);
        modules.Write("probe/attacker.json", $$"""
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "fifth-srd:fighter", "level": 1, "tracks": { "fifth-srd:hit_points": "20" },
              "stats": { "initiative_bonus": "2", "ac": "10", "spell_dc": "100", "spell_mod": "20", "proficiency": "0", "dex_save": "0", "str": "10", "dex": "10" },
              "actions": [ {{attackerUse}} ], "xp": 0 }
            """);

        ModuleSet set = ModuleLoader.Load(root, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        return WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, rules.Find(DefinitionTypes.Monster, "attacker", out _ )!, "Attacker", evaluator);
            Combatant rogue = Combatant.FromMonster(rules, rules.Find(DefinitionTypes.Monster, "rogue", out _ )!, "Rogue", evaluator);
            return CombatRunner.Run(rules, rules.Find(DefinitionTypes.Combat, "probe", out _ )!,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Defenders", [rogue]),
            ], dice, maxRounds: 1);
        });
    }

    private static CombatResult ThreeActionShieldFight(string attackerUse)
    {
        using TempModules modules = new();
        string root = modules.Module("probe", "extension", requires: TempModules.Require("three-action", "*"));
        modules.Write("probe/combat.json", """
            { "type": "combat", "id": "probe", "name": "Probe", "initiative": "self.perception", "initiative_by": "creature", "initiative_order": "highest-first", "initiative_each": "combat",
              "round_seconds": 6, "budget": [ { "id": "action", "per_turn": 3 }, { "id": "reaction", "per_turn": 1 } ], "track": "three-action:hit_points", "defeated": "self.hit_points <= 0" }
            """);
        modules.Write("probe/shield_bearer.json", """
            { "type": "monster", "id": "shield_bearer", "name": "Shield bearer", "class": "three-action:fighter", "level": 1, "tracks": { "three-action:hit_points": "20" },
              "stats": { "perception": "1", "ac": "10", "reflex": "-10", "save_dc": "10", "str": "10", "dex": "10" },
              "actions": [], "reactions": [ "three-action:spell_shield_block" ], "xp": 0 }
            """);
        modules.Write("probe/attacker.json", $$"""
            { "type": "monster", "id": "attacker", "name": "Attacker", "class": "three-action:fighter", "level": 1, "tracks": { "three-action:hit_points": "20" },
              "stats": { "perception": "2", "ac": "10", "reflex": "0", "save_dc": "100", "status_attack": "20", "spell_attack": "20", "str_mod": "0", "dex_mod": "0", "prone_penalty": "0", "martial_prof": "0", "simple_prof": "0", "str": "10", "dex": "10" },
              "actions": [ {{attackerUse}} ], "xp": 0 }
            """);

        ModuleSet set = ModuleLoader.Load(root, [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.Empty(set.Diagnostics);
        RuleSet rules = set.Rules!;
        return WithDice(dice =>
        {
            Evaluator evaluator = new(rules, dice);
            Combatant attacker = Combatant.FromMonster(rules, rules.Find(DefinitionTypes.Monster, "attacker", out _ )!, "Attacker", evaluator);
            Combatant defender = Combatant.FromMonster(rules, rules.Find(DefinitionTypes.Monster, "shield_bearer", out _ )!, "Shield bearer", evaluator);
            defender.Creature.Conditions.Add(rules.Find(DefinitionTypes.Condition, "shield_spell", out _ )!);
            return CombatRunner.Run(rules, rules.Find(DefinitionTypes.Combat, "probe", out _ )!,
            [
                new CombatSide("Attackers", [attacker]),
                new CombatSide("Defenders", [defender]),
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
