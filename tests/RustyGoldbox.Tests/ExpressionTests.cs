using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Tests;

public sealed class ExpressionTests
{
    [Theory]
    [InlineData("1 + 2 * 3", "7")]
    [InlineData("(1 + 2) * 3", "9")]
    [InlineData("-2 - -3", "1")]
    [InlineData("7 / 2", "3.5")]
    [InlineData("floor(7 / 2) + ceil(7 / 2)", "7")]
    [InlineData("min(3, 1, 2) + max(3, 1, 2) + abs(-4)", "8")]
    [InlineData("if 1 < 2 then 'yes' else 'no'", "yes")]
    [InlineData("not true or 1 == 1 and 'a' != 'b'", "true")]
    [InlineData("if false then 1 else if true then 2 else 3", "2")]
    [InlineData("table(bonus, 3) + table(bonus, 7) + table(bonus, 40)", "-2")]
    [InlineData("table(class_bonus, 'warrior', 5)", "2")]
    [InlineData("self.hit + self.str", "18")]
    [InlineData("self.strong and self.class == 'warrior'", "true")]
    [InlineData("target.hit", "0")]
    [InlineData("7 % 3 + 9 % 3", "1")]
    [InlineData("self.condition.ready and not target.condition.ready", "true")]
    public void Evaluates(string text, string expected)
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(Rules.WriteSmallRuleset(modules));
        Creature self = Character(rules, str: 15, conditions: ["ready"]);
        Creature target = new("target");

        Value value = new Evaluator(rules, null).Evaluate(rules.Compile(text, "rules", Roots.Self | Roots.Target), self, target);

        Assert.Equal(expected, value.ToString());
    }

    [Theory]
    [InlineData("1 +", 4, "ends too early")]
    [InlineData("(1 + 2", 7, "Expected ')'")]
    [InlineData("1 = 2", 3, "use '=='")]
    [InlineData("'open", 1, "never closed")]
    [InlineData("1 < 2 < 3", 7, "can't be chained")]
    [InlineData("if true then 1", 15, "Expected 'else'")]
    [InlineData("3x", 1, "not a number")]
    public void SyntaxErrorsNameTheColumn(string text, int column, string message)
    {
        ExpressionException exception = Assert.Throws<ExpressionException>(() => Parser.Parse(text));

        Assert.Equal(column, exception.Column);
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1 + 'a'", "'+' needs a number")]
    [InlineData("if 1 then 2 else 3", "condition after 'if' needs a boolean")]
    [InlineData("if true then 1 else 'a'", "same type")]
    [InlineData("self.nope", "'nope' is not a stat")]
    [InlineData("self.condition.nope", "There is no condition 'nope'")]
    [InlineData("outer.margin", "'outer.margin' isn't available here")]
    [InlineData("target.str", "'target.str' isn't available here; this field is an expression (any type; may read self)")]
    [InlineData("table(nope, 1)", "no table 'nope' in module 'rules'")]
    [InlineData("table(bonus)", "has 1 key")]
    [InlineData("table(bonus, 'x')", "needs a number")]
    [InlineData("sqrt(4)", "'sqrt' is not a function")]
    [InlineData("min(1)", "takes 2 or more arguments")]
    [InlineData("str + 1", "'str' is not a value")]
    [InlineData("other:bonus", "'other:bonus' is not a value")]
    public void TypeErrorsExplainTheFix(string text, string message)
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(Rules.WriteSmallRuleset(modules));

        ExpressionException exception = Assert.Throws<ExpressionException>(() => rules.Compile(text, "rules", Roots.Self));

        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluationErrorsNameWhatIsMissing()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(Rules.WriteSmallRuleset(modules));
        Evaluator evaluator = new(rules, null);

        Assert.Contains("has no class", Throws(evaluator, rules, "self.class", new Creature("self")), StringComparison.Ordinal);
        Assert.Contains("no row for (warrior, 0)", Throws(evaluator, rules, "table(class_bonus, 'warrior', 0)", null), StringComparison.Ordinal);
        Assert.Contains("Division by zero", Throws(evaluator, rules, "1 / (2 - 2)", null), StringComparison.Ordinal);
        Assert.Contains("rolls dice", Throws(evaluator, rules, "1d6", null), StringComparison.Ordinal);
        Assert.Contains("too large", Throws(evaluator, rules, "79228162514264337593543950335 + 1", null), StringComparison.Ordinal);
        Assert.Contains("too large", Throws(evaluator, rules, "79228162514264337593543950335 * 2", null), StringComparison.Ordinal);
    }

    [Fact]
    public void ModifierLoopsThroughOtherStatsNameTheModifier()
    {
        using TempModules modules = new();
        string root = Rules.WriteSmallRuleset(modules);
        modules.Write("rules/loop.json", """{ "type": "condition", "id": "loop", "name": "Loop", "modifiers": [ { "stat": "str", "value": "self.hit" } ] }""");
        RuleSet rules = Rules.LoadValid(root);
        Creature self = Character(rules, str: 12, conditions: ["loop"]);

        string message = Throws(new Evaluator(rules, null), rules, "self.str", self);

        Assert.Contains("str -> modifier at rules:loop $.modifiers[0]", message, StringComparison.Ordinal);
        Assert.Contains("-> hit -> str", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("campaign.gate_open", "Read campaign variables as campaign.var.<name>")]
    [InlineData("self.str.x", "Reads have one level")]
    [InlineData("99999999999999999999999999999999", "too large to be a number")]
    public void UnsupportedReadsAndHugeNumbersAreSyntaxErrors(string text, string message)
    {
        Assert.Contains(message, Assert.Throws<ExpressionException>(() => Parser.Parse(text)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiceRollThroughEngineRandomAndRepeatForASeed()
    {
        using TempModules modules = new();
        RuleSet rules = Rules.LoadValid(Rules.WriteSmallRuleset(modules));
        CompiledExpression expression = rules.Compile("3d6 + roll(2, 4) + roll(0, 8)", "rules", Roots.None);

        (decimal first, List<DiceRoll> firstRolls) = Roll(rules, expression, seed: 42);
        (decimal again, List<DiceRoll> againRolls) = Roll(rules, expression, seed: 42);
        (_, List<DiceRoll> otherRolls) = Roll(rules, expression, seed: 43);

        Assert.Equal(first, again);
        Assert.Equal(firstRolls.Select(roll => roll.ToString()), againRolls.Select(roll => roll.ToString()));
        Assert.NotEqual(firstRolls.Select(roll => roll.ToString()), otherRolls.Select(roll => roll.ToString()));
        Assert.Equal([(3, 6), (2, 4)], firstRolls.Select(roll => (roll.Count, roll.Sides)));
        Assert.All(firstRolls.SelectMany(roll => roll.Faces.Zip(Enumerable.Repeat(roll.Sides, roll.Count))), face => Assert.InRange(face.First, 1, face.Second));
        Assert.Equal(firstRolls.Sum(roll => roll.Total), first);
    }

    private static (decimal Total, List<DiceRoll> Rolls) Roll(RuleSet rules, CompiledExpression expression, ulong seed)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(seed, "tests"));
            DiceRoller dice = new(engine.Random, stream);
            decimal total = new Evaluator(rules, dice).Evaluate(expression, null, null).Number;
            return (total, dice.Rolls.ToList());
        });
    }

    private static string Throws(Evaluator evaluator, RuleSet rules, string text, Creature? self)
    {
        CompiledExpression expression = rules.Compile(text, "rules", Roots.Self);
        return Assert.Throws<ExpressionException>(() => evaluator.Evaluate(expression, self, null)).Message;
    }

    private static Creature Character(RuleSet rules, int str, string[] conditions)
    {
        Creature creature = new("self")
        {
            Class = rules.Find(DefinitionTypes.Class, "warrior", out _),
            Level = 1,
        };
        creature.Values["str"] = str;
        creature.Conditions.AddRange(conditions.Select(id => rules.Find(DefinitionTypes.Condition, id, out _)!));
        return creature;
    }
}
