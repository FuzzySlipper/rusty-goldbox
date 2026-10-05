using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Rules;

/// <summary>
/// Type-checks one expression against a rule set; throws
/// <see cref="ExpressionException"/> at the first problem. Records the table
/// each <c>table()</c> call names.
/// </summary>
internal sealed class ExpressionChecker(
    RuleSet rules,
    string module,
    Roots roots,
    IReadOnlyList<string> useParameters,
    IReadOnlyList<string> conditionValues,
    Func<Definition, ExprType?> derivedType,
    Func<Definition, bool> isInferring,
    IReadOnlyList<string>? behaviorParameters = null)
{
    private readonly IReadOnlyList<string> _behaviorParameters = behaviorParameters ?? [];

    public Dictionary<Expr, CompiledTable> Tables { get; } = new(ReferenceEqualityComparer.Instance);

    public ExprType Check(Expr expr)
    {
        switch (expr)
        {
            case NumberLiteral or DiceLiteral:
                return ExprType.Number;
            case BooleanLiteral:
                return ExprType.Boolean;
            case TextLiteral:
                return ExprType.Text;
            case PathExpr path:
                return CheckPath(path);
            case NameExpr name:
                throw new ExpressionException(
                    $"'{name.Name}' is not a value. Read a stat as self.{name.Name}, write text in quotes ('{name.Name}'), or call a function like {name.Name}(...).",
                    name.Column);
            case UnaryExpr unary:
                return CheckUnary(unary);
            case BinaryExpr binary:
                return CheckBinary(binary);
            case ConditionalExpr conditional:
                return CheckConditional(conditional);
            case CallExpr call:
                return CheckCall(call);
            default:
                throw new InvalidOperationException($"Unhandled expression {expr}.");
        }
    }

    private ExprType CheckPath(PathExpr path)
    {
        Roots root = path.Root switch
        {
            "self" => Roots.Self,
            "target" => Roots.Target,
            "use" => Roots.Use,
            "check" => Roots.Check,
            "campaign" => Roots.Campaign,
            "area" => Roots.Area,
            "class" => Roots.Class,
            "outer" => Roots.Outer,
            "condition" => Roots.Condition,
            "item" => Roots.Item,
            "combat" => Roots.Combat,
            "behavior" => Roots.Behavior,
            _ => Roots.None,
        };
        if (root == Roots.None)
        {
            throw new ExpressionException($"'{path.Root}' is not something an expression can read. Reads are self.<stat>, target.<stat>, self.condition.<id>, self.rolled.<check>, use.<parameter>, behavior.<parameter>, check.<result>, outer.<result>, campaign.var.<name>, class.level, condition.<value>, item.<field> and combat.<field>.", path.Column);
        }

        if (!roots.HasFlag(root))
        {
            string allowed = new ExpressionKind(null, roots).Describe();
            throw new ExpressionException($"'{path.Root}.{path.Name}' isn't available here; this field is an {allowed}.", path.Column);
        }

        if (root == Roots.Use)
        {
            if (!useParameters.Contains(path.Name))
            {
                string known = useParameters.Count == 0 ? "This action has no parameters." : $"Parameters: {string.Join(", ", useParameters)}.";
                throw new ExpressionException($"'{path.Name}' is not a parameter of this action. {known} Declare it in the action's \"parameters\".", path.Column);
            }

            return ExprType.Number;
        }

        if (root == Roots.Behavior)
        {
            if (!_behaviorParameters.Contains(path.Name))
            {
                string known = _behaviorParameters.Count == 0 ? "This behavior has no parameters." : $"Parameters: {string.Join(", ", _behaviorParameters)}.";
                throw new ExpressionException($"'{path.Name}' is not a parameter of this combat behavior. {known} Declare it in the behavior's \"parameters\".", path.Column);
            }

            return ExprType.Number;
        }

        if (root is Roots.Campaign or Roots.Area)
        {
            Dictionary<string, Definition> variables = root == Roots.Campaign ? rules.Variables : rules.AreaVariables;
            string scope = root == Roots.Campaign ? "campaign" : "area";
            if (!variables.TryGetValue(path.Name, out Definition? variable))
            {
                string known = variables.Count == 0 ? $"No {scope} variables are declared." : $"{scope} variables: {string.Join(", ", variables.Keys.Order(StringComparer.Ordinal))}.";
                string missing = root == Roots.Campaign
                    ? $"'{path.Name}' is not a declared variable. {known} Declare it with a variable definition whose scope is 'campaign'."
                    : $"'{path.Name}' is not a declared area variable. {known} Declare it with a variable definition whose scope is 'area'.";
                throw new ExpressionException(missing, path.Column);
            }

            if (!rules.VisibleModules[module].Contains(variable.Module))
            {
                throw new ExpressionException($"Variable '{path.Name}' belongs to module '{variable.Module}', which '{module}' does not require. Add it to requires.", path.Column);
            }

            ExprTypes.TryParse(variable.Json.GetProperty("value_type").GetString()!, out ExprType variableType);
            return variableType;
        }

        if (root == Roots.Class)
        {
            return path.Name switch
            {
                "level" => ExprType.Number,
                "id" => ExprType.Text,
                _ => throw new ExpressionException($"'class.{path.Name}' is not something a class can give. Read class.level (the creature's level in that class) or class.id (its ID).", path.Column),
            };
        }

        if (root == Roots.Combat)
        {
            return path.Name switch
            {
                "round" or "distance" or "nearest" or "allies_near" => ExprType.Number,
                "surprise_round" or "sight" => ExprType.Boolean,
                _ => throw new ExpressionException($"'combat.{path.Name}' is not something a fight gives. Read combat.round, combat.surprise_round, combat.distance (self to target, in cells), combat.sight (whether self can see target), combat.nearest (self to its nearest enemy) or combat.allies_near (self's allies within 1 cell of target).", path.Column),
            };
        }

        if (root == Roots.Item)
        {
            return path.Name switch
            {
                "id" or "kind" => ExprType.Text,
                "weight" or "cost" => ExprType.Number,
                _ => throw new ExpressionException($"'item.{path.Name}' is not something an item gives. Read item.id, item.kind, item.weight or item.cost.", path.Column),
            };
        }

        if (root == Roots.Condition)
        {
            if (!conditionValues.Contains(path.Name))
            {
                string known = conditionValues.Count == 0 ? "This condition declares no values." : $"Its values: {string.Join(", ", conditionValues)}.";
                throw new ExpressionException($"'{path.Name}' is not a value of this condition. {known} Declare it in the condition's \"values\".", path.Column);
            }

            return ExprType.Number;
        }

        if (root is Roots.Check or Roots.Outer)
        {
            if (!RuleSet.CheckFields.Contains(path.Name))
            {
                throw new ExpressionException($"'{path.Root}.{path.Name}' is not a check result. Results: {string.Join(", ", RuleSet.CheckFields)}.", path.Column);
            }

            return ExprType.Number;
        }

        if (path.Key is string checkId && path.Name == "rolled")
        {
            if (rules.Find(DefinitionTypes.Check, checkId, out string? missing) is not Definition check)
            {
                throw new ExpressionException($"{path.Root}.rolled.{checkId}: {missing}", path.Column);
            }

            if (!rules.VisibleModules[module].Contains(check.Module))
            {
                throw new ExpressionException($"Check '{checkId}' belongs to module '{check.Module}', which '{module}' does not require. Add it to requires.", path.Column);
            }

            return ExprType.Number;
        }

        if (path.Key is string conditionId)
        {
            if (rules.Find(DefinitionTypes.Condition, conditionId, out string? problem) is not Definition condition)
            {
                throw new ExpressionException($"{path.Root}.condition.{conditionId}: {problem}", path.Column);
            }

            if (!rules.VisibleModules[module].Contains(condition.Module))
            {
                throw new ExpressionException($"Condition '{conditionId}' belongs to module '{condition.Module}', which '{module}' does not require. Add it to requires.", path.Column);
            }

            return ExprType.Boolean;
        }

        if (RuleSet.BuiltInStats.TryGetValue(path.Name, out ExprType builtIn))
        {
            return builtIn;
        }

        if (rules.TryTrack(path.Name, out _, out _))
        {
            return ExprType.Number;
        }

        if (!rules.Stats.TryGetValue(path.Name, out Stat? stat))
        {
            throw new ExpressionException($"'{path.Name}' is not a stat. Built in: {string.Join(", ", RuleSet.BuiltInStats.Keys)}. {rules.StatList(false)}", path.Column);
        }

        if (stat.IsAttribute)
        {
            return ExprType.Number;
        }

        ExprType? type = derivedType(stat.Definition);
        if (type is null)
        {
            string why = isInferring(stat.Definition)
                ? $"Derived value '{stat.Id}' depends on itself. Break the loop between the derived values involved."
                : $"Derived value '{stat.Id}' has an error of its own; fix that first.";
            throw new ExpressionException(why, path.Column);
        }

        return type.Value;
    }

    private ExprType CheckUnary(UnaryExpr unary)
    {
        ExprType operand = Check(unary.Operand);
        ExprType needed = unary.Operator == "not" ? ExprType.Boolean : ExprType.Number;
        Expect(operand, needed, unary.Column, $"'{unary.Operator}'");
        return needed;
    }

    private ExprType CheckBinary(BinaryExpr binary)
    {
        ExprType left = Check(binary.Left);
        ExprType right = Check(binary.Right);
        switch (binary.Operator)
        {
            case "+" or "-" or "*" or "/" or "%":
                Expect(left, ExprType.Number, binary.Column, $"'{binary.Operator}'");
                Expect(right, ExprType.Number, binary.Column, $"'{binary.Operator}'");
                return ExprType.Number;
            case "<" or "<=" or ">" or ">=":
                Expect(left, ExprType.Number, binary.Column, $"'{binary.Operator}'");
                Expect(right, ExprType.Number, binary.Column, $"'{binary.Operator}'");
                return ExprType.Boolean;
            case "==" or "!=":
                if (left != right)
                {
                    throw new ExpressionException(
                        $"'{binary.Operator}' compares values of one type, but this compares a {ExprTypes.Name(left)} with a {ExprTypes.Name(right)}.",
                        binary.Column);
                }

                return ExprType.Boolean;
            default:
                Expect(left, ExprType.Boolean, binary.Column, $"'{binary.Operator}'");
                Expect(right, ExprType.Boolean, binary.Column, $"'{binary.Operator}'");
                return ExprType.Boolean;
        }
    }

    private ExprType CheckConditional(ConditionalExpr conditional)
    {
        Expect(Check(conditional.Condition), ExprType.Boolean, conditional.Column, "the condition after 'if'");
        ExprType then = Check(conditional.Then);
        ExprType otherwise = Check(conditional.Else);
        if (then != otherwise)
        {
            throw new ExpressionException(
                $"Both branches of 'if' must give the same type, but 'then' gives a {ExprTypes.Name(then)} and 'else' a {ExprTypes.Name(otherwise)}.",
                conditional.Column);
        }

        return then;
    }

    private ExprType CheckCall(CallExpr call)
    {
        ExpressionFunction? function = ExpressionFunctions.Find(call.Function);
        if (function is null)
        {
            throw new ExpressionException(
                $"'{call.Function}' is not a function. Functions: {string.Join(", ", ExpressionFunctions.All.Select(f => f.Name))}.",
                call.Column);
        }

        if (function.Name == "table")
        {
            return CheckTable(call);
        }

        if (function.Name is "class_min" or "class_max" or "class_sum")
        {
            return CheckEachClass(call, function);
        }

        if (function.Name is "equipped" or "carried")
        {
            return CheckEquipped(call, function);
        }

        if (function.Name == "party_size")
        {
            if (!roots.HasFlag(Roots.Campaign))
            {
                throw new ExpressionException("party_size() reads the active campaign party, but this field isn't a campaign expression.", call.Column);
            }

            if (call.Arguments.Count != 0)
            {
                throw new ExpressionException($"{function.Signature} takes no arguments, but got {call.Arguments.Count}.", call.Column);
            }

            return ExprType.Number;
        }

        if (function.Name == "spell_slots" && !roots.HasFlag(Roots.Self))
        {
            throw new ExpressionException("spell_slots() reads self's classes, but this field can't read self.", call.Column);
        }

        if (call.Arguments.Count < function.MinArguments || call.Arguments.Count > function.MaxArguments)
        {
            throw new ExpressionException($"{function.Signature} takes {function.ArgumentCountText}, but got {call.Arguments.Count}.", call.Column);
        }

        foreach (Expr argument in call.Arguments)
        {
            Expect(Check(argument), ExprType.Number, argument.Column, $"{function.Name}()");
        }

        return ExprType.Number;
    }

    /// <summary>class_min and the like: the argument is checked as if inside a class, reading class.id and class.level.</summary>
    private ExprType CheckEachClass(CallExpr call, ExpressionFunction function)
    {
        if (call.Arguments.Count != 1)
        {
            throw new ExpressionException($"{function.Signature} takes {function.ArgumentCountText}, but got {call.Arguments.Count}.", call.Column);
        }

        if (!roots.HasFlag(Roots.Self))
        {
            throw new ExpressionException($"{function.Name}() goes over self's classes, but this field can't read self.", call.Column);
        }

        ExpressionChecker inner = new(rules, module, roots | Roots.Class, useParameters, conditionValues, derivedType, isInferring, _behaviorParameters);
        Expect(inner.Check(call.Arguments[0]), ExprType.Number, call.Arguments[0].Column, $"{function.Name}()");
        foreach ((Expr expr, CompiledTable table) in inner.Tables)
        {
            Tables[expr] = table;
        }

        return ExprType.Number;
    }

    /// <summary>Item counters check a boolean for each item, reading item.id and the like.</summary>
    private ExprType CheckEquipped(CallExpr call, ExpressionFunction function)
    {
        if (call.Arguments.Count != 1)
        {
            throw new ExpressionException($"{function.Signature} takes {function.ArgumentCountText}, but got {call.Arguments.Count}.", call.Column);
        }

        Roots needed = function.Name == "equipped" ? Roots.Self : Roots.Campaign;
        if (!roots.HasFlag(needed))
        {
            throw new ExpressionException(function.Name == "equipped"
                ? "equipped() goes over self's equipment, but this field can't read self."
                : "carried() reads the party's items, but this field isn't a campaign expression.", call.Column);
        }

        ExpressionChecker inner = new(rules, module, roots | Roots.Item, useParameters, conditionValues, derivedType, isInferring, _behaviorParameters);
        Expect(inner.Check(call.Arguments[0]), ExprType.Boolean, call.Arguments[0].Column, $"{function.Name}()");
        foreach ((Expr expr, CompiledTable table) in inner.Tables)
        {
            Tables[expr] = table;
        }

        return ExprType.Number;
    }

    private ExprType CheckTable(CallExpr call)
    {
        if (call.Arguments.Count == 0 || call.Arguments[0] is not NameExpr name)
        {
            throw new ExpressionException("table() needs a table ID first, like table(str_to_hit, self.str).", call.Column);
        }

        Definition definition = ResolveTable(name.Name, name.Column);
        CompiledTable table = rules.Tables[definition];
        int keyCount = call.Arguments.Count - 1;
        if (keyCount != table.KeyTypes.Count)
        {
            throw new ExpressionException(
                $"Table '{name.Name}' has {table.KeyTypes.Count} key{(table.KeyTypes.Count == 1 ? "" : "s")} ({string.Join(", ", table.KeyNames)}), but {keyCount} {(keyCount == 1 ? "was" : "were")} given.",
                call.Column);
        }

        for (int i = 0; i < keyCount; i++)
        {
            Expr argument = call.Arguments[i + 1];
            Expect(Check(argument), table.KeyTypes[i], argument.Column, $"key '{table.KeyNames[i]}' of table '{name.Name}'");
        }

        Tables[call] = table;
        return table.ValueType;
    }

    private static void Expect(ExprType actual, ExprType needed, int column, string what)
    {
        if (actual != needed)
        {
            throw new ExpressionException($"{what} needs a {ExprTypes.Name(needed)}, but got a {ExprTypes.Name(actual)}.", column);
        }
    }

    private Definition ResolveTable(string text, int column)
    {
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        string target = colon < 0 ? module : text[..colon];
        string id = colon < 0 ? text : text[(colon + 1)..];
        if (!rules.VisibleModules[module].Contains(target))
        {
            throw new ExpressionException($"'{text}' refers to module '{target}', which '{module}' does not require. Add it to requires.", column);
        }

        if (rules.ByKey.TryGetValue((DefinitionTypes.Table.Name, target, id), out Definition? table))
        {
            return table;
        }

        List<string> known = rules.OfType(DefinitionTypes.Table).Where(definition => definition.Module == target).Select(definition => definition.Id).ToList();
        string list = known.Count == 0 ? $"'{target}' has no tables." : $"Tables in '{target}': {string.Join(", ", known)}.";
        throw new ExpressionException($"There is no table '{id}' in module '{target}'. {list}", column);
    }
}
