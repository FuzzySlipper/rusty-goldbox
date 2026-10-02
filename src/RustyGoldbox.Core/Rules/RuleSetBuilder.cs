using System.Text.Json;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Core.Rules;

/// <summary>
/// Builds a <see cref="RuleSet"/> from the definitions of a resolved module
/// set: resolves references, compiles tables and type-checks expressions.
/// </summary>
public sealed class RuleSetBuilder
{
    private readonly RuleSet _rules = new();
    private readonly List<ModuleDiagnostic> _diagnostics;
    private readonly Dictionary<Definition, ExprType?> _derivedTypes = [];
    private readonly HashSet<Definition> _inferring = [];

    private RuleSetBuilder(IReadOnlyList<LoadedModule> modules, List<ModuleDiagnostic> diagnostics)
    {
        _diagnostics = diagnostics;
        foreach (LoadedModule module in modules)
        {
            HashSet<string> visible = [module.Manifest.Id];
            visible.UnionWith(module.Requires.Select(requirement => requirement.Id));
            _rules.VisibleModules[module.Manifest.Id] = visible;
        }
    }

    public static RuleSet Build(IReadOnlyList<LoadedModule> modules, IReadOnlyList<Definition> definitions, List<ModuleDiagnostic> diagnostics)
    {
        RuleSetBuilder builder = new(modules, diagnostics);
        builder.Index(definitions);
        builder.ResolveReferences();
        builder.CompileTables();
        builder.CheckExpressions();
        builder.CompileModifiers();
        builder.CheckMonsterStats();
        builder.CheckCreationAttributes();
        builder.CheckActions();
        return builder._rules;
    }

    private void Index(IReadOnlyList<Definition> definitions)
    {
        foreach (Definition definition in definitions)
        {
            (string, string, string) key = (definition.Type.Name, definition.Module, definition.Id);
            if (_rules.ByKey.TryGetValue(key, out Definition? existing))
            {
                Error(definition, "definition.duplicate", "$.id",
                    $"{definition.Module} already has a {definition.Type.Name} '{definition.Id}' in {existing.File}. Give one of them another ID.");
                continue;
            }

            _rules.ByKey[key] = definition;
            _rules.Definitions.Add(definition);
        }

        foreach (Definition definition in _rules.Definitions)
        {
            if (definition.Type != DefinitionTypes.Attribute && definition.Type != DefinitionTypes.Derived)
            {
                continue;
            }

            if (RuleSet.BuiltInStats.ContainsKey(definition.Id))
            {
                Error(definition, "stat.reserved", "$.id", $"'{definition.Id}' is a built-in stat every creature has. Use another ID.");
            }
            else if (_rules.Stats.TryGetValue(definition.Id, out Stat? existing))
            {
                Error(definition, "stat.duplicate", "$.id",
                    $"Stat '{definition.Id}' is already defined as a {existing.Definition.Type.Name} in {existing.Definition.QualifiedId} ({existing.Definition.File}). Stat IDs are shared by the whole module set; use another ID.");
            }
            else
            {
                // Attributes are numbers; a derived value's type is filled in when it is inferred.
                _rules.Stats[definition.Id] = new Stat(definition.Id, definition, ExprType.Number);
            }
        }
    }

    private void ResolveReferences()
    {
        foreach (Definition definition in _rules.Definitions)
        {
            foreach (ReferenceSite site in definition.References)
            {
                switch (site.Kind)
                {
                    case ReferenceKind reference:
                        Definition? target = Resolve(definition, DefinitionTypes.Find(reference.DefinitionType)!, site.Text, site.JsonPath);
                        if (target is not null)
                        {
                            _rules.References[(definition, site.JsonPath)] = target;
                        }

                        break;
                    case StatKind stat:
                        CheckStat(definition, site, stat);
                        break;
                }
            }
        }
    }

    private void CheckStat(Definition definition, ReferenceSite site, StatKind kind)
    {
        if (!_rules.Stats.TryGetValue(site.Text, out Stat? stat))
        {
            Error(definition, "reference.stat", site.JsonPath, $"'{site.Text}' is not a stat. {StatList(kind.AttributesOnly)}");
        }
        else if (kind.AttributesOnly && !stat.IsAttribute)
        {
            Error(definition, "reference.stat", site.JsonPath, $"'{site.Text}' is a derived value, but an attribute is needed here. {StatList(true)}");
        }
    }

    private Definition? Resolve(Definition from, DefinitionType type, string text, string path)
    {
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        string module = colon < 0 ? from.Module : text[..colon];
        string id = colon < 0 ? text : text[(colon + 1)..];
        if (!_rules.VisibleModules[from.Module].Contains(module))
        {
            Error(from, "reference.module", path,
                $"'{text}' refers to module '{module}', which '{from.Module}' does not require. A module can refer only to itself and the modules in its own requires; add {{ \"id\": \"{module}\", \"version\": \"...\" }} to requires.");
            return null;
        }

        if (_rules.ByKey.TryGetValue((type.Name, module, id), out Definition? found))
        {
            return found;
        }

        List<string> known = _rules.OfType(type).Where(definition => definition.Module == module).Select(definition => definition.Id).ToList();
        string list = known.Count == 0 ? $"'{module}' has no {type.Name} definitions." : $"{type.Name} IDs in '{module}': {string.Join(", ", known)}.";
        string hint = colon < 0 ? " To refer to another module's definition, write module:id." : "";
        Error(from, "reference.not-found", path, $"There is no {type.Name} '{id}' in module '{module}'. {list}{hint}");
        return null;
    }

    private void CompileTables()
    {
        foreach (Definition definition in _rules.OfType(DefinitionTypes.Table))
        {
            CompiledTable table = new(definition);
            _rules.Tables[definition] = table;
            foreach ((int first, int second) in table.Overlaps())
            {
                Error(definition, "table.overlap", $"$.rows[{second}]",
                    $"Rows {first} and {second} match the same keys, so a lookup could find either. Make their keys or ranges distinct.");
            }
        }
    }

    private void CheckExpressions()
    {
        foreach (Definition derived in _rules.OfType(DefinitionTypes.Derived))
        {
            InferDerived(derived);
        }

        foreach (Definition definition in _rules.Definitions)
        {
            foreach (ExpressionSite site in definition.Expressions)
            {
                if (definition.Type == DefinitionTypes.Derived)
                {
                    continue;
                }

                Compile(definition, site);
            }
        }
    }

    private ExprType? InferDerived(Definition derived)
    {
        if (_derivedTypes.TryGetValue(derived, out ExprType? known))
        {
            return known;
        }

        ExpressionSite site = derived.Expressions.Single();
        if (!_inferring.Add(derived))
        {
            return null;
        }

        CompiledExpression? compiled = Compile(derived, site);
        _inferring.Remove(derived);
        _derivedTypes[derived] = compiled?.Type;
        if (compiled is not null && _rules.Stats.TryGetValue(derived.Id, out Stat? stat) && stat.Definition == derived)
        {
            _rules.Stats[derived.Id] = stat with { Type = compiled.Type };
        }

        return compiled?.Type;
    }

    private CompiledExpression? Compile(Definition definition, ExpressionSite site)
    {
        Expr root;
        try
        {
            root = Parser.Parse(site.Text);
        }
        catch (ExpressionException exception)
        {
            ExpressionError(definition, site, "expression.syntax", exception);
            return null;
        }

        ExpressionChecker checker = new(_rules, definition.Module, site.Kind.Roots, UseParameters(definition), InferDerived, IsInferring);
        ExprType type;
        try
        {
            type = checker.Check(root);
        }
        catch (ExpressionException exception)
        {
            ExpressionError(definition, site, "expression.type", exception);
            return null;
        }

        if (site.Kind.Expected is ExprType expected && type != expected)
        {
            Error(definition, "expression.type", site.JsonPath,
                $"In '{site.Text}': this field needs a {ExprTypes.Name(expected)}, but the expression gives a {ExprTypes.Name(type)}.");
            return null;
        }

        CompiledExpression compiled = new(site.Text, root, type, checker.Tables);
        _rules.Expressions[(definition, site.JsonPath)] = compiled;
        return compiled;
    }

    private void CompileModifiers()
    {
        foreach (Definition definition in _rules.Definitions)
        {
            if (!definition.Json.TryGetProperty("modifiers", out JsonElement modifiers))
            {
                continue;
            }

            List<Modifier> list = [];
            int index = 0;
            foreach (JsonElement modifier in modifiers.EnumerateArray())
            {
                string path = $"$.modifiers[{index}]";
                index++;
                if (!_rules.TryExpression(definition, $"{path}.value", out CompiledExpression? value))
                {
                    continue;
                }

                if (modifier.TryGetProperty("stat", out JsonElement stat))
                {
                    string id = stat.GetString()!;
                    if (_rules.Stats.TryGetValue(id, out Stat? target) && target.Type != ExprType.Number)
                    {
                        Error(definition, "expression.type", $"{path}.stat", $"Modifiers add numbers, but stat '{id}' is {ExprTypes.Name(target.Type)}.");
                        continue;
                    }

                    if (Reads(value!.Root, id))
                    {
                        Error(definition, "modifier.loop", $"{path}.value",
                            $"This modifier adds to {id} but reads self.{id}, so {id} would depend on itself. Read other stats instead.");
                        continue;
                    }

                    list.Add(new Modifier(id, null, value, path));
                }
                else if (_rules.References.TryGetValue((definition, $"{path}.check"), out Definition? check))
                {
                    list.Add(new Modifier(null, check, value!, path));
                }
            }

            _rules.Modifiers[definition] = list;
        }
    }

    private void CheckMonsterStats()
    {
        foreach (Definition monster in _rules.OfType(DefinitionTypes.Monster))
        {
            if (!monster.Json.TryGetProperty("stats", out JsonElement stats))
            {
                continue;
            }

            foreach (JsonProperty stat in stats.EnumerateObject())
            {
                string path = $"$.stats.{stat.Name}";
                if (!_rules.TryExpression(monster, path, out CompiledExpression? compiled))
                {
                    continue;
                }

                if (_rules.Stats.TryGetValue(stat.Name, out Stat? target) && target.Type != compiled!.Type)
                {
                    Error(monster, "expression.type", path,
                        $"In '{compiled.Text}': stat '{stat.Name}' is {ExprTypes.Name(target.Type)}, but this gives a {ExprTypes.Name(compiled.Type)}.");
                }
                else if (_rules.TryExpression(monster, path, out CompiledExpression? value) && Reads(value!.Root, stat.Name))
                {
                    Error(monster, "modifier.loop", path, $"This stat reads self.{stat.Name}, so it would depend on itself. Give it a value or read other stats.");
                }
            }
        }
    }

    private void CheckCreationAttributes()
    {
        List<string> attributes = _rules.Stats.Values.Where(stat => stat.IsAttribute).Select(stat => stat.Id).Order(StringComparer.Ordinal).ToList();
        foreach (Definition creation in _rules.OfType(DefinitionTypes.CharacterCreation))
        {
            List<string> listed = creation.Json.GetProperty("attributes").EnumerateArray().Select(entry => entry.GetString()!).ToList();
            if (!listed.Order(StringComparer.Ordinal).SequenceEqual(attributes))
            {
                Error(creation, "creation.attributes", "$.attributes",
                    $"attributes must list every attribute of the module set exactly once: {string.Join(", ", attributes)}.");
            }
        }
    }

    private void CheckActions()
    {
        HashSet<string> budget = _rules.OfType(DefinitionTypes.Combat)
            .SelectMany(combat => combat.Json.GetProperty("budget").EnumerateArray().Select(entry => entry.GetProperty("id").GetString()!))
            .ToHashSet();
        foreach (Definition action in _rules.OfType(DefinitionTypes.Action))
        {
            JsonElement cost = action.Json.GetProperty("cost");
            foreach (JsonProperty entry in cost.EnumerateObject())
            {
                if (!budget.Contains(entry.Name))
                {
                    string known = budget.Count == 0 ? "No combat definition declares a budget." : $"Budget IDs: {string.Join(", ", budget.Order(StringComparer.Ordinal))}.";
                    Error(action, "action.cost", $"$.cost.{entry.Name}", $"'{entry.Name}' is not a budget in any combat definition. {known}");
                }
                else if (entry.Value.GetInt32() < 0)
                {
                    Error(action, "action.cost", $"$.cost.{entry.Name}", "Costs can't be negative.");
                }
            }

            if (!cost.EnumerateObject().Any(entry => entry.Value.GetInt32() > 0))
            {
                Error(action, "action.cost", "$.cost", "An action must cost at least 1 of some budget, or a creature could take it forever.");
            }

            bool hasCheck = action.Json.TryGetProperty("check", out _);
            if (action.Json.TryGetProperty("outcomes", out JsonElement outcomes))
            {
                if (!hasCheck)
                {
                    Error(action, "action.outcomes", "$.outcomes", "Outcomes need a check to choose between them. Add \"check\", or put the operations in \"always\".");
                }
                else if (_rules.References.TryGetValue((action, "$.check"), out Definition? check))
                {
                    CheckOutcomes(action, check, outcomes, "$.outcomes");
                }
            }

            if (!hasCheck && !action.Json.TryGetProperty("always", out _))
            {
                Error(action, "action.outcomes", "$", "An action needs \"always\" operations, or a \"check\" with \"outcomes\"; otherwise it does nothing.");
            }

            WalkOperations(action, action.Json, "$");
        }

        foreach (Definition definition in _rules.Definitions)
        {
            foreach (ReferenceSite site in definition.References)
            {
                if (site.Kind is ReferenceKind { DefinitionType: "action" } && site.JsonPath.EndsWith(".action", StringComparison.Ordinal)
                    && _rules.References.TryGetValue((definition, site.JsonPath), out Definition? action))
                {
                    string usePath = site.JsonPath[..^".action".Length];
                    CheckUse(definition, JsonAt(definition.Json, usePath), usePath, action);
                }
            }

            if (definition.Type == DefinitionTypes.Condition)
            {
                WalkOperations(definition, definition.Json, "$");
            }
        }
    }

    /// <summary>Finds check operations under an element and checks their outcome tiers.</summary>
    private void WalkOperations(Definition definition, JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("op", out JsonElement op) && op.ValueKind == JsonValueKind.String && op.GetString() == "check"
                && element.TryGetProperty("outcomes", out JsonElement outcomes)
                && _rules.References.TryGetValue((definition, $"{path}.check"), out Definition? check))
            {
                CheckOutcomes(definition, check, outcomes, $"{path}.outcomes");
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                WalkOperations(definition, property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                WalkOperations(definition, item, $"{path}[{index}]");
                index++;
            }
        }
    }

    private void CheckOutcomes(Definition owner, Definition check, JsonElement outcomes, string path)
    {
        List<string> tiers = ["success", "failure"];
        if (check.Json.TryGetProperty("tiers", out JsonElement declared))
        {
            tiers.AddRange(declared.EnumerateArray().Select(tier => tier.GetProperty("name").GetString()!));
        }

        foreach (JsonProperty outcome in outcomes.EnumerateObject())
        {
            if (!tiers.Contains(outcome.Name))
            {
                Error(owner, "action.outcomes", $"{path}.{outcome.Name}",
                    $"'{outcome.Name}' is not an outcome of check {check.QualifiedId}. Its outcomes: {string.Join(", ", tiers)}.");
            }
        }
    }

    private void CheckUse(Definition owner, JsonElement use, string path, Definition action)
    {
        List<string> parameters = UseParameters(action);
        bool fromItem = use.TryGetProperty("from_item", out _);
        foreach (JsonProperty argument in use.EnumerateObject())
        {
            if (argument.Name is "action" or "name" or "from_item")
            {
                continue;
            }

            if (!parameters.Contains(argument.Name))
            {
                string known = parameters.Count == 0 ? $"{action.QualifiedId} has no parameters." : $"Parameters of {action.QualifiedId}: {string.Join(", ", parameters)}.";
                Error(owner, "use.parameter", $"{path}.{argument.Name}", $"'{argument.Name}' is not a parameter of the action. {known}");
            }
        }

        foreach (string parameter in parameters.Where(parameter => !use.TryGetProperty(parameter, out _)))
        {
            if (!fromItem)
            {
                Error(owner, "use.parameter", path,
                    $"This use of {action.QualifiedId} doesn't give its parameter '{parameter}'. Add \"{parameter}\": an expression, or \"from_item\" to take it from an equipped item.");
            }
        }
    }

    /// <summary>The element at a path this builder recorded, such as <c>$.actions[0]</c>.</summary>
    private static JsonElement JsonAt(JsonElement root, string path)
    {
        JsonElement current = root;
        int i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                int end = i + 1;
                while (end < path.Length && path[end] != '.' && path[end] != '[')
                {
                    end++;
                }

                current = current.GetProperty(path[(i + 1)..end]);
                i = end;
            }
            else
            {
                int close = path.IndexOf(']', i);
                current = current[int.Parse(path[(i + 1)..close], System.Globalization.CultureInfo.InvariantCulture)];
                i = close + 1;
            }
        }

        return current;
    }

    /// <summary>Whether an expression reads self.<paramref name="stat"/> directly.</summary>
    private static bool Reads(Expr expr, string stat)
    {
        return expr switch
        {
            PathExpr path => path.Root == "self" && path.Name == stat,
            UnaryExpr unary => Reads(unary.Operand, stat),
            BinaryExpr binary => Reads(binary.Left, stat) || Reads(binary.Right, stat),
            ConditionalExpr conditional => Reads(conditional.Condition, stat) || Reads(conditional.Then, stat) || Reads(conditional.Else, stat),
            CallExpr call => call.Arguments.Any(argument => Reads(argument, stat)),
            _ => false,
        };
    }

    private string StatList(bool attributesOnly) => _rules.StatList(attributesOnly);

    private bool IsInferring(Definition derived) => _inferring.Contains(derived);

    /// <summary>The parameters an action's expressions may read as use.&lt;name&gt;.</summary>
    private static List<string> UseParameters(Definition definition)
    {
        return definition.Json.TryGetProperty("parameters", out JsonElement parameters) && parameters.ValueKind == JsonValueKind.Array
            ? parameters.EnumerateArray().Select(parameter => parameter.GetString()!).ToList()
            : [];
    }

    private void ExpressionError(Definition definition, ExpressionSite site, string rule, ExpressionException exception)
    {
        Error(definition, rule, site.JsonPath, $"In '{site.Text}' at column {exception.Column}: {exception.Message}");
    }

    private void Error(Definition definition, string rule, string path, string message)
    {
        _diagnostics.Add(new ModuleDiagnostic(rule, message, definition.Module, definition.File, path));
    }
}
