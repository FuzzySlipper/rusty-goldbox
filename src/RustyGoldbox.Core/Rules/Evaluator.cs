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
/// <summary>
/// The fight an evaluator is resolving: the round, whether anyone is
/// surprised in it, how far apart two creatures are, and how far a creature
/// is from its nearest standing enemy. Without a field everyone is 1 apart.
/// </summary>
/// <param name="AlliesNear">How many of self's allies still fighting (not self) stand within 1 cell of target.</param>
public sealed record CombatMoment(int Round, bool SurpriseRound, Func<Creature, Creature, decimal> Distance, Func<Creature, decimal> Nearest, Func<Creature, Creature, bool> Sight, Func<Creature, Creature, decimal> AlliesNear);

public sealed record Scope(
    Creature? Self,
    Creature? Target,
    IReadOnlyDictionary<string, CompiledExpression>? Use = null,
    CheckResult? Check = null,
    IReadOnlyDictionary<string, Value>? Variables = null,
    int? ClassLevel = null,
    CheckResult? Outer = null,
    IReadOnlyDictionary<string, decimal>? ConditionValues = null,
    Definition? Class = null,
    Definition? Item = null,
    IEnumerable<Definition>? PartyItems = null,
    IReadOnlyDictionary<string, Value>? AreaVariables = null,
    int? PartySize = null);

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

    /// <summary>The fight this evaluator is resolving, read as combat.&lt;field&gt;; null outside one.</summary>
    public CombatMoment? Combat { get; set; }

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

            if (creature.AdvancementBonuses.TryGetValue(name, out decimal advancementBonus))
            {
                total = Add(total, advancementBonus, 1);
            }

            return Value.Of(total);
        }
        finally
        {
            _trail.RemoveAt(_trail.Count - 1);
            _computing.Remove((creature, name));
        }
    }

    /// <param name="extra">Added to the modifiers by whatever makes the check, such as an action's check_bonus (a range penalty, a charge).</param>
    public CheckResult Check(Definition check, Creature self, Creature? target, decimal extra = 0)
    {
        decimal roll = Roll(check, self, target);
        return CheckWithRoll(check, self, target, roll, extra);
    }

    /// <summary>Makes the dice roll for a check without resolving its target or result.</summary>
    public decimal Roll(Definition check, Creature self, Creature? target)
    {
        return Evaluate(rules.Expression(check, "$.roll"), self, target).Number;
    }

    /// <summary>Resolves a check using a roll already made, such as a post-roll bonus.</summary>
    public CheckResult CheckWithRoll(Definition check, Creature self, Creature? target, decimal roll, decimal extra = 0, decimal? targetOverride = null)
    {
        decimal bonus = rules.TryExpression(check, "$.bonus", out CompiledExpression? bonusExpression)
            ? Evaluate(bonusExpression!, self, target).Number
            : 0;
        decimal modifier = extra;
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

        decimal needed = targetOverride ?? Evaluate(rules.Expression(check, "$.target"), self, target).Number;
        return ResolveCheck(check, self, target, roll, bonus, modifier, needed);
    }

    /// <summary>Classifies a check from components already resolved, such as a post-roll result.</summary>
    public CheckResult ResolveCheck(Definition check, Creature self, Creature? target, decimal roll, decimal bonus, decimal modifier, decimal needed)
    {
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
        bool isClass = source.Type == DefinitionTypes.Class;
        int? classLevel = isClass ? creature.ClassLevels[source] : null;
        IReadOnlyDictionary<string, decimal>? values = source.Type == DefinitionTypes.Condition ? ConditionValuesOf(creature, source) : null;
        return new Scope(creature, null, ClassLevel: classLevel, ConditionValues: values, Class: isClass ? source : null);
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
            return Add(track == rules.LevelTrack ? Add(own, LevelBonus(creature), 1) : own, TrackModifiers(creature, track), 1);
        }

        if (rules.TryExpression(track, "$.max", out CompiledExpression? max))
        {
            return Add(Evaluate(max!, creature, null).Number, TrackModifiers(creature, track), 1);
        }

        string fix = creature.Monster is not null
            ? $"Give monster {creature.Monster.QualifiedId} \"tracks\": {{ \"{track.Id}\": <expression> }}"
            : $"Give the track a \"max\", or give {creature.Label} \"max_{track.Id}\"";
        throw new ExpressionException($"{creature.Label} has no maximum {track.Id}, and track '{track.Id}' doesn't compute one. {fix}.", 1);
    }

    /// <summary>What modifiers on the track add to its maximum, such as a toughness feat.</summary>
    private decimal TrackModifiers(Creature creature, Definition track)
    {
        decimal total = 0;
        foreach (Definition source in creature.ModifierSources())
        {
            foreach (Modifier modifier in rules.ModifiersOf(source).Where(modifier => modifier.Track == track))
            {
                total = Add(total, Evaluate(modifier.Value, ModifierScope(creature, source)).Number, 1);
            }
        }

        return total;
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
                total = Add(total, Evaluate(bonus!, new Scope(creature, null, ClassLevel: classLevel, Class: characterClass)).Number, 1);
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
            case "classes":
                return Value.Of(creature.AdvancingClasses ?? creature.ClassLevels.Count);
            case "former_level":
                return Value.Of(creature.FormerLevel);
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

            if (path.Root == "area")
            {
                return scope.AreaVariables is not null && scope.AreaVariables.TryGetValue(path.Name, out Value variable)
                    ? variable
                    : throw new ExpressionException($"area.var.{path.Name} has no value here: there is no current area.", path.Column);
            }

            if (path.Root == "class")
            {
                if (scope.ClassLevel is not int classLevel || scope.Class is not Definition current)
                {
                    throw new ExpressionException($"class.{path.Name} has no value here: it is read in a class's own fields and inside class_min() and the like.", path.Column);
                }

                return path.Name == "id" ? Value.Of(current.Id) : Value.Of(classLevel);
            }

            if (path.Root == "combat")
            {
                CombatMoment fight = evaluator.Combat ?? throw new ExpressionException($"combat.{path.Name} has no value outside a fight.", path.Column);
                switch (path.Name)
                {
                    case "round":
                        return Value.Of(fight.Round);
                    case "surprise_round":
                        return Value.Of(fight.SurpriseRound);
                    case "nearest":
                        return Value.Of(fight.Nearest(scope.Self ?? throw new ExpressionException("combat.nearest needs a self creature.", path.Column)));
                    case "allies_near":
                        Creature flanker = scope.Self ?? throw new ExpressionException("combat.allies_near needs a self creature.", path.Column);
                        Creature flanked = scope.Target ?? throw new ExpressionException("combat.allies_near needs a target creature; it is read where an action looks at its target.", path.Column);
                        return Value.Of(fight.AlliesNear(flanker, flanked));
                    case "sight":
                        Creature looking = scope.Self ?? throw new ExpressionException("combat.sight needs a self creature.", path.Column);
                        Creature seen = scope.Target ?? throw new ExpressionException("combat.sight needs a target creature; it is read where an action looks at its target.", path.Column);
                        return Value.Of(fight.Sight(looking, seen));
                    default:
                        Creature from = scope.Self ?? throw new ExpressionException("combat.distance needs a self creature.", path.Column);
                        Creature to = scope.Target ?? throw new ExpressionException("combat.distance needs a target creature; it is read where an action looks at its target.", path.Column);
                        return Value.Of(fight.Distance(from, to));
                }
            }

            if (path.Root == "item")
            {
                Definition item = scope.Item ?? throw new ExpressionException($"item.{path.Name} has no value here: it is read in a class's equipment rule and inside equipped().", path.Column);
                return path.Name switch
                {
                    "id" => Value.Of(item.Id),
                    "kind" => Value.Of(item.Json.GetProperty("kind").GetString()!),
                    "weight" => Value.Of(item.Json.GetProperty("weight").GetDecimal()),
                    _ => Value.Of(item.Json.GetProperty("cost").GetDecimal()),
                };
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

            if (call.Function is "class_min" or "class_max" or "class_sum")
            {
                Creature self = scope.Self ?? throw new ExpressionException($"{call.Function}() needs a self creature, but none was given.", call.Column);

                // A creature given only a class and a level has that one class.
                IReadOnlyDictionary<Definition, int> classes = self.ClassLevels.Count > 0 || self.Class is null || self.Level is not int level
                    ? self.ClassLevels
                    : new Dictionary<Definition, int> { [self.Class] = level };
                if (classes.Count == 0)
                {
                    throw new ExpressionException($"{self.Label} has no class for {call.Function}() to go over. Give {self.Label} a \"class\" and \"level\".", call.Column);
                }

                List<decimal> each = classes
                    .Select(entry => new Run(evaluator, expression, scope with { Class = entry.Key, ClassLevel = entry.Value }).Evaluate(call.Arguments[0]).Number)
                    .ToList();
                return Value.Of(call.Function switch
                {
                    "class_min" => each.Min(),
                    "class_max" => each.Max(),
                    _ => each.Aggregate(0m, (total, value) => Add(total, value, call.Column)),
                });
            }

            if (call.Function == "equipped")
            {
                Creature self = scope.Self ?? throw new ExpressionException("equipped() needs a self creature, but none was given.", call.Column);
                int count = self.Equipment.Count(item => new Run(evaluator, expression, scope with { Item = item }).Evaluate(call.Arguments[0]).Boolean);
                return Value.Of(count);
            }

            if (call.Function == "carried")
            {
                IEnumerable<Definition> items = scope.PartyItems ?? throw new ExpressionException("carried() needs a running campaign's party items, but none were given.", call.Column);
                int count = items.Count(item => new Run(evaluator, expression, scope with { Item = item }).Evaluate(call.Arguments[0]).Boolean);
                return Value.Of(count);
            }

            if (call.Function == "party_size")
            {
                return scope.PartySize is int count
                    ? Value.Of(count)
                    : throw new ExpressionException("party_size() needs a running campaign, but no party was given.", call.Column);
            }

            if (call.Function == "spell_slots")
            {
                Creature self = scope.Self ?? throw new ExpressionException("spell_slots() needs a self creature, but none was given.", call.Column);
                decimal spellLevel = Evaluate(call.Arguments[0]).Number;
                decimal slots = 0;
                foreach ((Definition characterClass, int classLevel) in self.ClassLevels)
                {
                    if (spellLevel == decimal.Truncate(spellLevel) && spellLevel >= 1
                        && characterClass.Json.TryGetProperty("spell_slots", out JsonElement table) && classLevel <= table.GetArrayLength()
                        && table[classLevel - 1].GetArrayLength() >= spellLevel)
                    {
                        slots += table[classLevel - 1][(int)spellLevel - 1].GetInt32();
                    }
                }

                return Value.Of(slots);
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
                "roll_fudge" => Value.Of(RollFudge(arguments[0], call.Column)),
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

        private long RollFudge(decimal count, int column)
        {
            if (count != decimal.Truncate(count) || count < 0 || count > int.MaxValue)
            {
                throw new ExpressionException($"roll_fudge({count}) needs a whole number of dice, 0 or more.", column);
            }

            if (evaluator.Dice is null)
            {
                throw new ExpressionException("This expression rolls dice, but no dice roller was given.", column);
            }

            return count == 0 ? 0 : evaluator.Dice.Fudge((int)count);
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
