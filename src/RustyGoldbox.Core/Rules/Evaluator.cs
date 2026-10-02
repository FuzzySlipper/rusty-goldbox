using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Rules;

/// <summary>
/// The outcome of a check. <see cref="Total"/> is the roll plus its bonus and
/// modifiers; <see cref="Margin"/> is how far it beat the target (negative when
/// it failed); <see cref="Tier"/> is the first matching tier, or success/failure.
/// </summary>
public sealed record CheckResult(decimal Roll, decimal Bonus, decimal Modifier, decimal Total, decimal Target, decimal Margin, bool Success, string Tier);

/// <summary>
/// What an expression can read: the creatures, the parameters of the action
/// being used, the check being resolved, and for a class's modifier the
/// creature's level in that class. While a nested check resolves,
/// <see cref="Outer"/> is the check it was made during. Inside a condition's
/// own fields, <see cref="ConditionValues"/> are the values it was applied with.
/// </summary>
public sealed record Scope(
    Creature? Self,
    Creature? Target,
    IReadOnlyDictionary<string, CompiledExpression>? Use = null,
    CheckResult? Check = null,
    IReadOnlyDictionary<string, Value>? Variables = null,
    int? ClassLevel = null,
    CheckResult? Outer = null,
    IReadOnlyDictionary<string, decimal>? ConditionValues = null);

/// <summary>
/// Evaluates checked expressions against creatures. Dice need a
/// <see cref="DiceRoller"/>; without one, rolling is an error.
/// </summary>
/// <exception cref="ExpressionException">Evaluation fails: a missing stat or table row, division by zero, and so on.</exception>
public sealed class Evaluator(RuleSet rules, DiceRoller? dice)
{
    private readonly HashSet<(Creature, string)> _computing = [];
    private readonly List<string> _trail = [];

    private DiceRoller? Dice => dice;

    public Value Evaluate(CompiledExpression expression, Creature? self, Creature? target)
    {
        return Evaluate(expression, new Scope(self, target));
    }

    public Value Evaluate(CompiledExpression expression, Scope scope)
    {
        return new Run(this, expression, scope).Evaluate(expression.Root);
    }

    /// <summary>A stat's value for a creature, including modifiers; also built-ins and track reads.</summary>
    public Value Stat(Creature creature, string name)
    {
        if (RuleSet.BuiltInStats.ContainsKey(name))
        {
            return BuiltIn(creature, name);
        }

        if (!_computing.Add((creature, name)))
        {
            throw new ExpressionException(
                $"{creature.Label}.{name} depends on itself: {string.Join(" -> ", _trail)} -> {name}. Change one of these so the loop is broken.", 1);
        }

        _trail.Add(name);
        try
        {
            if (rules.TryTrack(name, out Definition? track, out bool maximum))
            {
                return Value.Of(maximum ? TrackMax(creature, track!) : TrackCurrent(creature, track!));
            }

            Value value = BaseStat(creature, name);
            if (value.Type != ExprType.Number)
            {
                return value;
            }

            decimal total = value.Number;
            foreach (Definition source in creature.ModifierSources())
            {
                foreach (Modifier modifier in rules.ModifiersOf(source))
                {
                    if (modifier.Stat == name)
                    {
                        _trail.Add($"modifier at {source.QualifiedId} {modifier.Path} ({source.File})");
                        total = Add(total, Evaluate(modifier.Value, ModifierScope(creature, source)).Number, 1);
                        _trail.RemoveAt(_trail.Count - 1);
                    }
                }
            }

            return Value.Of(total);
        }
        finally
        {
            _trail.RemoveAt(_trail.Count - 1);
            _computing.Remove((creature, name));
        }
    }

    public CheckResult Check(Definition check, Creature self, Creature? target)
    {
        decimal roll = Evaluate(rules.Expression(check, "$.roll"), self, target).Number;
        decimal bonus = rules.TryExpression(check, "$.bonus", out CompiledExpression? bonusExpression)
            ? Evaluate(bonusExpression!, self, target).Number
            : 0;
        decimal modifier = 0;
        foreach (Definition source in self.ModifierSources())
        {
            foreach (Modifier entry in rules.ModifiersOf(source))
            {
                // A modifier with "against" applies only when its target matches.
                if (entry.Check == check
                    && (entry.Against is null || (target is not null && Evaluate(entry.Against, ModifierScope(self, source) with { Target = target }).Boolean)))
                {
                    modifier = Add(modifier, Evaluate(entry.Value, ModifierScope(self, source)).Number, 1);
                }
            }
        }

        decimal needed = Evaluate(rules.Expression(check, "$.target"), self, target).Number;
        decimal total = Add(Add(roll, bonus, 1), modifier, 1);
        bool atLeast = check.Json.GetProperty("succeeds").GetString() == "at-least";
        bool success = atLeast ? total >= needed : total <= needed;
        decimal margin = Arithmetic(() => atLeast ? total - needed : needed - total, 1);
        CheckResult result = new(roll, bonus, modifier, total, needed, margin, success, success ? "success" : "failure");
        if (check.Json.TryGetProperty("tiers", out JsonElement tiers))
        {
            int index = 0;
            foreach (JsonElement tier in tiers.EnumerateArray())
            {
                CompiledExpression when = rules.Expression(check, $"$.tiers[{index}].when");
                if (Evaluate(when, new Scope(self, target, null, result)).Boolean)
                {
                    return result with { Tier = tier.GetProperty("name").GetString()! };
                }

                index++;
            }
        }

        return result;
    }

    /// <summary>
    /// What a modifier from <paramref name="source"/> reads: the creature, its
    /// level in the source when that is a class, and the source's values when
    /// that is a condition.
    /// </summary>
    private static Scope ModifierScope(Creature creature, Definition source)
    {
        int? classLevel = source.Type == DefinitionTypes.Class ? creature.ClassLevels[source] : null;
        IReadOnlyDictionary<string, decimal>? values = source.Type == DefinitionTypes.Condition ? ConditionValuesOf(creature, source) : null;
        return new Scope(creature, null, ClassLevel: classLevel, ConditionValues: values);
    }

    /// <summary>A condition's values for a creature that has it: the values it was applied with, over the condition's defaults.</summary>
    public static IReadOnlyDictionary<string, decimal> ConditionValuesOf(Creature creature, Definition condition)
    {
        Dictionary<string, decimal> values = [];
        if (condition.Json.TryGetProperty("values", out JsonElement defaults))
        {
            foreach (JsonProperty value in defaults.EnumerateObject())
            {
                values[value.Name] = value.Value.GetDecimal();
            }
        }

        if (creature.ConditionValues.TryGetValue(condition, out Dictionary<string, decimal>? given))
        {
            foreach ((string name, decimal value) in given)
            {
                values[name] = value;
            }
        }

        return values;
    }

    private Value BaseStat(Creature creature, string name)
    {
        if (creature.Values.TryGetValue(name, out decimal given))
        {
            return Value.Of(given);
        }

        if (creature.Monster is not null && rules.TryExpression(creature.Monster, $"$.stats.{name}", out CompiledExpression? monsterStat))
        {
            _trail.Add($"monster stat at {creature.Monster.QualifiedId} $.stats.{name} ({creature.Monster.File})");
            try
            {
                return Evaluate(monsterStat!, creature, null);
            }
            finally
            {
                _trail.RemoveAt(_trail.Count - 1);
            }
        }

        Stat stat = rules.Stats[name];
        if (stat.IsAttribute)
        {
            if (stat.Definition.Json.TryGetProperty("default", out JsonElement fallback))
            {
                return Value.Of(fallback.GetInt32());
            }

            throw new ExpressionException(
                $"{creature.Label} has no {name}, and attribute '{name}' has no default. Give {creature.Label} a \"{name}\" value.", 1);
        }

        return Evaluate(rules.Expression(stat.Definition, "$.value"), creature, null);
    }

    /// <summary>The track's current value; a creature that has none yet starts it (its start, else its maximum).</summary>
    public decimal TrackCurrent(Creature creature, Definition track)
    {
        StartTrack(creature, track);
        return creature.Track(track.Id).Current!.Value;
    }

    /// <summary>
    /// The creature's own maximum for the track (for the level track, plus
    /// each level's hp_bonus as the creature is now), or the track's maximum
    /// expression.
    /// </summary>
    public decimal TrackMax(Creature creature, Definition track)
    {
        if (creature.Tracks.TryGetValue(track.Id, out TrackValue? value) && value.Max is decimal own)
        {
            return track == rules.LevelTrack ? Add(own, LevelBonus(creature), 1) : own;
        }

        if (rules.TryExpression(track, "$.max", out CompiledExpression? max))
        {
            return Evaluate(max!, creature, null).Number;
        }

        string fix = creature.Monster is not null
            ? $"Give monster {creature.Monster.QualifiedId} \"tracks\": {{ \"{track.Id}\": <expression> }}"
            : $"Give the track a \"max\", or give {creature.Label} \"max_{track.Id}\"";
        throw new ExpressionException($"{creature.Label} has no maximum {track.Id}, and track '{track.Id}' doesn't compute one. {fix}.", 1);
    }

    /// <summary>The track's maximum for the creature, or null when neither the creature nor the track gives one.</summary>
    public decimal? KnownTrackMax(Creature creature, Definition track)
    {
        bool known = (creature.Tracks.TryGetValue(track.Id, out TrackValue? value) && value.Max is not null) || rules.TryExpression(track, "$.max", out _);
        return known ? TrackMax(creature, track) : null;
    }

    /// <summary>What the hp_bonus of every level the creature has adds to the level track, as it is now.</summary>
    public decimal LevelBonus(Creature creature)
    {
        decimal total = 0;
        foreach ((Definition characterClass, int classLevel) in creature.LevelsTaken)
        {
            if (rules.TryExpression(characterClass, $"$.levels[{classLevel - 1}].hp_bonus", out CompiledExpression? bonus))
            {
                total = Add(total, Evaluate(bonus!, creature, null).Number, 1);
            }
        }

        return total;
    }

    /// <summary>The track's minimum for the creature, or null when it has no floor.</summary>
    public decimal? TrackMin(Creature creature, Definition track)
    {
        return rules.TryExpression(track, "$.min", out CompiledExpression? min) ? Evaluate(min!, creature, null).Number : null;
    }

    /// <summary>How high healing can take the track: its restore cap, else its maximum.</summary>
    public decimal TrackRestoreCap(Creature creature, Definition track)
    {
        return rules.TryExpression(track, "$.restore_cap", out CompiledExpression? cap) ? Evaluate(cap!, creature, null).Number : TrackMax(creature, track);
    }

    /// <summary>Sets the track's current value to its start (else its maximum) if the creature has none.</summary>
    public void StartTrack(Creature creature, Definition track)
    {
        TrackValue value = creature.Track(track.Id);
        value.Current ??= rules.TryExpression(track, "$.start", out CompiledExpression? start)
            ? Evaluate(start!, creature, null).Number
            : TrackMax(creature, track);
    }

    private static decimal Add(decimal left, decimal right, int column) => Arithmetic(() => left + right, column);

    private static decimal Arithmetic(Func<decimal> operation, int column)
    {
        try
        {
            return operation();
        }
        catch (OverflowException)
        {
            throw new ExpressionException("A result is too large to be a number.", column);
        }
    }

    private static Value BuiltIn(Creature creature, string name)
    {
        switch (name)
        {
            case "level":
                return creature.Level is int level
                    ? Value.Of(level)
                    : throw new ExpressionException($"{creature.Label} has no level. Give {creature.Label} a \"level\".", 1);
            case "class":
                return creature.Class is not null
                    ? Value.Of(creature.Class.Id)
                    : throw new ExpressionException($"{creature.Label} has no class. Give {creature.Label} a \"class\" (or a \"monster\").", 1);

            default:
                return creature.Race is not null
                    ? Value.Of(creature.Race.Id)
                    : throw new ExpressionException($"{creature.Label} has no race. Give {creature.Label} a \"race\".", 1);
        }
    }

    private sealed class Run(Evaluator evaluator, CompiledExpression expression, Scope scope)
    {
        public Value Evaluate(Expr expr)
        {
            switch (expr)
            {
                case NumberLiteral number:
                    return Value.Of(number.Value);
                case BooleanLiteral boolean:
                    return Value.Of(boolean.Value);
                case TextLiteral text:
                    return Value.Of(text.Value);
                case DiceLiteral diceLiteral:
                    return Value.Of(Roll(diceLiteral.Count, diceLiteral.Sides, diceLiteral.Column));
                case PathExpr path:
                    return ReadPath(path);
                case UnaryExpr unary:
                    Value operand = Evaluate(unary.Operand);
                    return unary.Operator == "not" ? Value.Of(!operand.Boolean) : Value.Of(-operand.Number);
                case BinaryExpr binary:
                    return EvaluateBinary(binary);
                case ConditionalExpr conditional:
                    return Evaluate(conditional.Condition).Boolean ? Evaluate(conditional.Then) : Evaluate(conditional.Else);
                case CallExpr call:
                    return EvaluateCall(call);
                default:
                    throw new InvalidOperationException($"Unhandled expression {expr}.");
            }
        }

        private Value ReadPath(PathExpr path)
        {
            if (path.Root == "use")
            {
                if (scope.Use is null || !scope.Use.TryGetValue(path.Name, out CompiledExpression? parameter))
                {
                    throw new ExpressionException($"use.{path.Name} has no value: the action wasn't used with that parameter.", path.Column);
                }

                // Parameters are evaluated where they're read, against the same creatures.
                return evaluator.Evaluate(parameter, scope with { Use = null, Check = null });
            }

            if (path.Root == "campaign")
            {
                return scope.Variables is not null && scope.Variables.TryGetValue(path.Name, out Value variable)
                    ? variable
                    : throw new ExpressionException($"campaign.var.{path.Name} has no value here: there is no campaign running.", path.Column);
            }

            if (path.Root == "class")
            {
                return scope.ClassLevel is int classLevel
                    ? Value.Of(classLevel)
                    : throw new ExpressionException("class.level has no value outside a class's modifiers.", path.Column);
            }

            if (path.Root == "condition")
            {
                return scope.ConditionValues is not null && scope.ConditionValues.TryGetValue(path.Name, out decimal conditionValue)
                    ? Value.Of(conditionValue)
                    : throw new ExpressionException($"condition.{path.Name} has no value here: it is read inside a condition's own fields.", path.Column);
            }

            if (path.Root is "check" or "outer")
            {
                CheckResult check = (path.Root == "check" ? scope.Check : scope.Outer)
                    ?? throw new ExpressionException(path.Root == "check"
                        ? $"check.{path.Name} has no value outside a check."
                        : $"outer.{path.Name} has no value here: it reads the check a nested check was made during, and there is none.", path.Column);
                return Value.Of(path.Name switch
                {
                    "roll" => check.Roll,
                    "total" => check.Total,
                    "target" => check.Target,
                    _ => check.Margin,
                });
            }

            Creature? creature = path.Root == "self" ? scope.Self : scope.Target;
            if (creature is null)
            {
                throw new ExpressionException($"'{path.Root}.{path.Name}' needs a {path.Root} creature, but none was given.", path.Column);
            }

            if (path.Key is string key)
            {
                return path.Name == "rolled"
                    ? Value.Of(creature.Rolled.GetValueOrDefault(key))
                    : Value.Of(creature.Conditions.Any(held => held.Id == key));
            }

            try
            {
                return evaluator.Stat(creature, path.Name);
            }
            catch (ExpressionException exception) when (exception.Column == 1 && path.Column != 1)
            {
                throw new ExpressionException(exception.Message, path.Column);
            }
        }

        private Value EvaluateBinary(BinaryExpr binary)
        {
            if (binary.Operator == "and")
            {
                return Value.Of(Evaluate(binary.Left).Boolean && Evaluate(binary.Right).Boolean);
            }

            if (binary.Operator == "or")
            {
                return Value.Of(Evaluate(binary.Left).Boolean || Evaluate(binary.Right).Boolean);
            }

            Value left = Evaluate(binary.Left);
            Value right = Evaluate(binary.Right);
            switch (binary.Operator)
            {
                case "+":
                    return Value.Of(Add(left.Number, right.Number, binary.Column));
                case "-":
                    return Value.Of(Arithmetic(() => left.Number - right.Number, binary.Column));
                case "*":
                    return Value.Of(Arithmetic(() => left.Number * right.Number, binary.Column));
                case "/":
                    if (right.Number == 0)
                    {
                        throw new ExpressionException("Division by zero.", binary.Column);
                    }

                    return Value.Of(Arithmetic(() => left.Number / right.Number, binary.Column));
                case "%":
                    if (right.Number == 0)
                    {
                        throw new ExpressionException("Remainder of division by zero.", binary.Column);
                    }

                    return Value.Of(left.Number % right.Number);
                case "<":
                    return Value.Of(left.Number < right.Number);
                case "<=":
                    return Value.Of(left.Number <= right.Number);
                case ">":
                    return Value.Of(left.Number > right.Number);
                case ">=":
                    return Value.Of(left.Number >= right.Number);
                case "==":
                    return Value.Of(left == right);
                default:
                    return Value.Of(left != right);
            }
        }

        private Value EvaluateCall(CallExpr call)
        {
            if (call.Function == "table")
            {
                CompiledTable table = expression.Tables[call];
                List<Value> keys = call.Arguments.Skip(1).Select(Evaluate).ToList();
                return table.Lookup(keys)
                    ?? throw new ExpressionException(
                        $"Table '{table.Definition.Id}' has no row for ({string.Join(", ", keys)}).", call.Column);
            }

            List<decimal> arguments = call.Arguments.Select(argument => Evaluate(argument).Number).ToList();
            return call.Function switch
            {
                "min" => Value.Of(arguments.Min()),
                "max" => Value.Of(arguments.Max()),
                "floor" => Value.Of(decimal.Floor(arguments[0])),
                "ceil" => Value.Of(decimal.Ceiling(arguments[0])),
                "abs" => Value.Of(Math.Abs(arguments[0])),
                "roll_keep" => Value.Of(RollKeep(arguments[0], arguments[1], arguments[2], call.Column)),
                "roll_count" => Value.Of(RollCount(arguments[0], arguments[1], arguments[2], call.Column)),
                "roll_explode" => Value.Of(RollPool(call.Function, arguments[0], arguments[1], null, arguments[2], 0, call.Column)),
                "roll_pool" => Value.Of(RollPool(call.Function, arguments[0], arguments[1], arguments[2], arguments[3], arguments[4], call.Column)),
                _ => Value.Of(RollDynamic(arguments[0], arguments[1], call.Column)),
            };
        }

        private long RollDynamic(decimal count, decimal sides, int column, int? keep = null)
        {
            if (count != decimal.Truncate(count) || sides != decimal.Truncate(sides) || count < 0 || sides < 1 || count > int.MaxValue || sides > int.MaxValue)
            {
                string call = keep is null ? $"roll({count}, {sides})" : $"roll_keep({count}, {sides}, {keep})";
                throw new ExpressionException($"{call} needs a whole number of dice (0 or more) with a whole number of sides (1 or more).", column);
            }

            return count == 0 ? 0 : Roll((int)count, (int)sides, column, keep);
        }

        private long RollCount(decimal count, decimal sides, decimal atLeast, int column)
        {
            if (count != decimal.Truncate(count) || sides != decimal.Truncate(sides) || atLeast != decimal.Truncate(atLeast)
                || count < 0 || sides < 1 || count > int.MaxValue || sides > int.MaxValue || atLeast < int.MinValue || atLeast > int.MaxValue)
            {
                throw new ExpressionException($"roll_count({count}, {sides}, {atLeast}) needs whole numbers: dice 0 or more, sides 1 or more.", column);
            }

            if (evaluator.Dice is null)
            {
                throw new ExpressionException("This expression rolls dice, but no dice roller was given.", column);
            }

            return count == 0 ? 0 : evaluator.Dice.Count((int)count, (int)sides, (int)atLeast);
        }

        /// <summary>roll_explode (no at_least: a sum) and roll_pool; again below 2 would never stop rolling.</summary>
        private long RollPool(string function, decimal count, decimal sides, decimal? atLeast, decimal again, decimal cancel, int column)
        {
            decimal[] numbers = atLeast is decimal threshold ? [count, sides, threshold, again, cancel] : [count, sides, again];
            if (numbers.Any(number => number != decimal.Truncate(number) || number < int.MinValue || number > int.MaxValue)
                || count < 0 || sides < 1 || again < 2)
            {
                string arguments = string.Join(", ", numbers);
                throw new ExpressionException($"{function}({arguments}) needs whole numbers: dice 0 or more, sides 1 or more, and again 2 or more (a face of 1 rerolling would never stop).", column);
            }

            if (evaluator.Dice is null)
            {
                throw new ExpressionException("This expression rolls dice, but no dice roller was given.", column);
            }

            if (count == 0)
            {
                return 0;
            }

            int? rerolls = again <= sides ? (int)again : null;
            return atLeast is decimal at
                ? evaluator.Dice.Pool((int)count, (int)sides, (int)at, rerolls, cancel > 0 ? (int)cancel : null)
                : rerolls is int explode ? evaluator.Dice.Explode((int)count, (int)sides, explode) : evaluator.Dice.Roll((int)count, (int)sides);
        }

        private long RollKeep(decimal count, decimal sides, decimal keep, int column)
        {
            if (keep != decimal.Truncate(keep) || keep < 0 || keep > count)
            {
                throw new ExpressionException($"roll_keep({count}, {sides}, {keep}) needs a whole number to keep, from 0 to the number of dice.", column);
            }

            return RollDynamic(count, sides, column, (int)keep);
        }

        private long Roll(int count, int sides, int column, int? keep = null)
        {
            if (evaluator.Dice is null)
            {
                throw new ExpressionException("This expression rolls dice, but no dice roller was given.", column);
            }

            return evaluator.Dice.Roll(count, sides, keep ?? count);
        }
    }
}
