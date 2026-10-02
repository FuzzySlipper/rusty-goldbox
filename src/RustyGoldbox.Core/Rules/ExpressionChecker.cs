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
    Func<Definition, ExprType?> derivedType,
    Func<Definition, bool> isInferring)
{
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
            _ => Roots.None,
        };
        if (root == Roots.None)
        {
            throw new ExpressionException($"'{path.Root}' is not something an expression can read. Reads are self.<stat>, target.<stat>, use.<parameter> and check.<result>.", path.Column);
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

        if (root == Roots.Check)
        {
            if (!RuleSet.CheckFields.Contains(path.Name))
            {
                throw new ExpressionException($"'check.{path.Name}' is not a check result. Results: {string.Join(", ", RuleSet.CheckFields)}.", path.Column);
            }

            return ExprType.Number;
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
            case "+" or "-" or "*" or "/":
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
