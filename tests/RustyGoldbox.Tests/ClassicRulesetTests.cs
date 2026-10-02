using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

/// <summary>
/// The first-party classic ruleset against values printed in its source
/// tables (see modules/classic/PROVENANCE.md).
/// </summary>
public sealed class ClassicRulesetTests
{
    private static readonly RuleSet Classic = Rules.Classic();

    [Theory]
    [InlineData("fighter", 1, 20)]
    [InlineData("fighter", 5, 16)]
    [InlineData("fighter", 12, 9)]
    [InlineData("fighter", 13, 8)]
    [InlineData("fighter", 20, 1)]
    [InlineData("cleric", 4, 18)]
    [InlineData("cleric", 19, 9)]
    [InlineData("magic_user", 3, 21)]
    [InlineData("magic_user", 10, 19)]
    [InlineData("thief", 9, 16)]
    public void Thac0(string className, int level, int expected)
    {
        Assert.Equal(expected, Evaluate("self.thac0", Character(className, level), null));
    }

    // Rolls needed to hit, read from the class "to hit" matrices, including
    // the six armour classes where the matrices stay at 20.
    [Theory]
    [InlineData("fighter", 1, 10, 10)]
    [InlineData("fighter", 1, -1, 20)]
    [InlineData("fighter", 1, -5, 20)]
    [InlineData("fighter", 1, -6, 21)]
    [InlineData("fighter", 1, -10, 25)]
    [InlineData("fighter", 5, -10, 21)]
    [InlineData("fighter", 12, 10, -1)]
    [InlineData("magic_user", 1, 1, 20)]
    [InlineData("magic_user", 1, 2, 19)]
    [InlineData("magic_user", 1, -5, 21)]
    [InlineData("cleric", 1, -6, 21)]
    [InlineData("thief", 9, -4, 20)]
    [InlineData("thief", 9, -3, 19)]
    public void AttackTargetFollowsTheMatrix(string className, int level, int armourClass, int needed)
    {
        Creature target = new("target");
        target.Values["ac"] = armourClass;

        CheckResult result = Check("attack", Character(className, level), target, seed: 1);

        Assert.Equal(needed, result.Target);
    }

    [Fact]
    public void ArmourAndDexterityLowerArmourClass()
    {
        Creature fighter = Character("fighter", 1);
        fighter.Values["dex"] = 16;
        fighter.Equipment.Add(Find(DefinitionTypes.Item, "chain_mail"));
        fighter.Equipment.Add(Find(DefinitionTypes.Item, "shield"));

        Assert.Equal(2, Evaluate("self.ac", fighter, null));
    }

    [Fact]
    public void DwarvesAddConstitutionToSavesAgainstMagic()
    {
        Creature dwarf = Character("fighter", 3);
        dwarf.Race = Find(DefinitionTypes.Race, "dwarf");
        dwarf.Values["con"] = 17;

        CheckResult spell = Check("save_spell", dwarf, null, seed: 3);
        CheckResult breath = Check("save_breath", dwarf, null, seed: 3);

        Assert.Equal((4, 16), (spell.Modifier, spell.Target));
        Assert.Equal((0, 16), (breath.Modifier, breath.Target));
    }

    [Fact]
    public void BlessingAddsToAttacks()
    {
        Creature fighter = Character("fighter", 1);
        Creature blessed = Character("fighter", 1);
        blessed.Conditions.Add(Find(DefinitionTypes.Condition, "blessed"));
        Creature target = new("target") { Monster = Find(DefinitionTypes.Monster, "skeleton") };

        CheckResult plain = Check("attack", fighter, target, seed: 9);
        CheckResult bonus = Check("attack", blessed, target, seed: 9);

        Assert.Equal(plain.Roll, bonus.Roll);
        Assert.Equal(plain.Total + 1, bonus.Total);
        Assert.Equal(13, plain.Target);
    }

    [Fact]
    public void MonstersUseTheirStatsAndFighterTables()
    {
        Creature ogre = new("self") { Monster = Find(DefinitionTypes.Monster, "ogre") };
        ogre.Class = Classic.Reference(ogre.Monster, "$.class");
        ogre.Level = ogre.Monster.Json.GetProperty("level").GetInt32();

        Assert.Equal(5, Evaluate("self.ac", ogre, null));
        Assert.Equal(15, Evaluate("self.thac0", ogre, null));
        Assert.Equal(0, Evaluate("self.str_to_hit", ogre, null));
        Assert.Equal(11, Check("save_death", ogre, null, seed: 1).Target);
    }

    [Theory]
    [InlineData("skeleton", 8)]
    [InlineData("ogre", 12)]
    public void LongSwordsDealMoreDamageToLargeTargets(string monster, int sides)
    {
        Definition sword = Find(DefinitionTypes.Item, "long_sword");
        Creature target = new("target") { Monster = Find(DefinitionTypes.Monster, monster) };

        using EngineTestHost host = EngineTestHost.Create();
        List<DiceRoll> rolls = host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(1, "tests"));
            DiceRoller dice = new(engine.Random, stream);
            new Evaluator(Classic, dice).Evaluate(Classic.Expression(sword, "$.parameters.damage"), Character("fighter", 1), target);
            return dice.Rolls.ToList();
        });

        Assert.Equal(sides, Assert.Single(rolls).Sides);
    }

    [Fact]
    public void EveryClassHasXpAndHitDiceForTenLevels()
    {
        foreach (Definition definition in Classic.OfType(DefinitionTypes.Class))
        {
            Assert.Equal(10, definition.Json.GetProperty("levels").GetArrayLength());
        }
    }

    private static Creature Character(string className, int level)
    {
        return new Creature("self") { Class = Find(DefinitionTypes.Class, className), Level = level };
    }

    private static Definition Find(DefinitionType type, string id)
    {
        return Classic.Find(type, id, out string? problem) ?? throw new InvalidOperationException(problem);
    }

    private static decimal Evaluate(string text, Creature? self, Creature? target)
    {
        CompiledExpression expression = Classic.Compile(text, "classic", Roots.Self | Roots.Target);
        return new Evaluator(Classic, null).Evaluate(expression, self, target).Number;
    }

    private static CheckResult Check(string check, Creature self, Creature? target, ulong seed)
    {
        Definition definition = Find(DefinitionTypes.Check, check);
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(seed, "tests"));
            return new Evaluator(Classic, new DiceRoller(engine.Random, stream)).Check(definition, self, target);
        });
    }
}
