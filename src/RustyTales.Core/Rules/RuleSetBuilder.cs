using System.Text.Json;
using RustyTales.Core.Definitions;
using RustyTales.Core.Expressions;
using RustyTales.Core.Modules;

namespace RustyTales.Core.Rules;

/// <summary>
/// Builds a <see cref="RuleSet"/> from the definitions of a resolved module
/// set: resolves references, compiles tables and type-checks expressions.
/// </summary>
public sealed partial class RuleSetBuilder
{
    private readonly RuleSet _rules = new();
    private readonly List<ModuleDiagnostic> _diagnostics;
    private readonly Dictionary<Definition, ExprType?> _derivedTypes = [];
    private readonly HashSet<Definition> _inferring = [];
    private readonly Dictionary<string, ModuleManifest> _manifests = [];

    private RuleSetBuilder(IReadOnlyList<LoadedModule> modules, List<ModuleDiagnostic> diagnostics)
    {
        _diagnostics = diagnostics;
        foreach (LoadedModule module in modules)
        {
            _manifests[module.Manifest.Id] = module.Manifest;
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
        builder.CheckAdvancement();
        builder.CheckLifepaths();
        builder.CheckCurrencies();
        builder.CheckEconomy();
        builder.CheckResting();
        builder.CheckGrants();
        builder.CheckActions();
        builder.CheckCombatBehaviors();
        builder.CheckCampaigns();
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

        foreach (Definition variable in _rules.OfType(DefinitionTypes.Variable))
        {
            bool area = variable.Json.TryGetProperty("scope", out JsonElement scope) && scope.GetString() == "area";
            Dictionary<string, Definition> variables = area ? _rules.AreaVariables : _rules.Variables;
            if (!variables.TryAdd(variable.Id, variable))
            {
                Error(variable, "variable.duplicate", "$.id", $"{(area ? "Area" : "Campaign")} variable '{variable.Id}' is already declared in {variables[variable.Id].QualifiedId}. Variable names are shared by the module set within each scope.");
            }
        }

        foreach (Definition currency in _rules.OfType(DefinitionTypes.Currency))
        {
            if (!_rules.Currencies.TryAdd(currency.Id, currency))
            {
                Error(currency, "currency.duplicate", "$.id", $"Currency '{currency.Id}' is already defined in {_rules.Currencies[currency.Id].QualifiedId}. Currency IDs are shared by the whole module set; use another ID.");
            }
        }

        HashSet<string> trackNames = [];
        foreach (Definition track in _rules.OfType(DefinitionTypes.Track))
        {
            if (RuleSet.BuiltInStats.ContainsKey(track.Id) || trackNames.Contains(track.Id) || RuleSet.BuiltInStats.ContainsKey($"max_{track.Id}"))
            {
                Error(track, "track.duplicate", "$.id", $"'{track.Id}' would hide a built-in or another track's max_ name. Use another ID.");
                continue;
            }

            if (!_rules.Tracks.TryAdd(track.Id, track))
            {
                Error(track, "track.duplicate", "$.id", $"Track '{track.Id}' is already defined in {_rules.Tracks[track.Id].QualifiedId}. Track IDs are shared by the whole module set.");
                continue;
            }

            trackNames.Add(track.Id);
            trackNames.Add($"max_{track.Id}");
        }

        List<Definition> levelTracks = _rules.Tracks.Values
            .Where(track => track.Json.TryGetProperty("from_levels", out JsonElement fromLevels) && fromLevels.GetBoolean())
            .ToList();
        foreach (Definition extra in levelTracks.Skip(1))
        {
            Error(extra, "track.from-levels", "$.from_levels", $"Only one track can have from_levels; {levelTracks[0].QualifiedId} already does.");
        }

        foreach (Definition track in _rules.Tracks.Values.Where(track => !track.Json.TryGetProperty("max", out _) && !levelTracks.Contains(track)))
        {
            Error(track, "track.max", "$", $"Track '{track.Id}' needs a \"max\" expression (or \"from_levels\": true): characters have nothing else to take its maximum from.");
        }

        foreach (Definition monster in _rules.OfType(DefinitionTypes.Monster))
        {
            List<string> given = monster.Json.TryGetProperty("tracks", out JsonElement monsterTracks)
                ? monsterTracks.EnumerateObject().Select(entry => entry.Name[(entry.Name.IndexOf(':', StringComparison.Ordinal) + 1)..]).ToList()
                : [];
            foreach (Definition track in _rules.Tracks.Values.Where(track => !track.Json.TryGetProperty("max", out _) && !given.Contains(track.Id)))
            {
                Error(monster, "track.max", "$.tracks",
                    $"Track '{track.Id}' doesn't compute a maximum, so the monster must give one: \"tracks\": {{ \"{track.Id}\": \"2d8\" }}.");
            }
        }

        // Class levels give hp exactly when a track is built from level gains.
        foreach (Definition characterClass in _rules.OfType(DefinitionTypes.Class))
        {
            int index = 0;
            foreach (JsonElement level in characterClass.Json.GetProperty("levels").EnumerateArray())
            {
                bool gains = level.TryGetProperty("hp", out _) || level.TryGetProperty("hp_bonus", out _);
                if (gains && levelTracks.Count == 0)
                {
                    Error(characterClass, "track.from-levels", $"$.levels[{index}]",
                        "Class levels gain \"hp\", but no track has \"from_levels\": true to receive it. Add one, for example { \"type\": \"track\", \"id\": \"hit_points\", \"name\": \"Hit points\", \"from_levels\": true }, or leave hp out.");
                    break;
                }

                if (!level.TryGetProperty("hp", out _) && levelTracks.Count > 0)
                {
                    Error(characterClass, "track.from-levels", $"$.levels[{index}]",
                        $"{levelTracks[0].QualifiedId} is built from class levels, so each level needs \"hp\": what it gains.");
                }

                index++;
            }
        }

        foreach (Definition definition in _rules.Definitions)
        {
            if (definition.Type != DefinitionTypes.Attribute && definition.Type != DefinitionTypes.Derived)
            {
                continue;
            }

            if (trackNames.Contains(definition.Id))
            {
                Error(definition, "stat.duplicate", "$.id", $"'{definition.Id}' is a track's name (tracks are read as <id> and max_<id>). Use another ID.");
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
                        Definition? target = Resolve(definition, site.SourceModule ?? definition.Module, DefinitionTypes.Find(reference.DefinitionType)!, site.Text, site.JsonPath);
                        if (target is not null)
                        {
                            _rules.References[(definition, site.JsonPath)] = target;
                            if (reference.Slot is string slot && Media.Problem(slot, target) is string unfit)
                            {
                                Error(definition, "reference.media", site.JsonPath, unfit);
                            }
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

    private Definition? Resolve(Definition from, string sourceModule, DefinitionType type, string text, string path)
    {
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        string module = colon < 0 ? sourceModule : text[..colon];
        string id = colon < 0 ? text : text[(colon + 1)..];
        if (!_rules.VisibleModules[sourceModule].Contains(module))
        {
            Error(from, "reference.module", path,
                $"'{text}' refers to module '{module}', which '{sourceModule}' does not require. A module can refer only to itself and the modules in its own requires; add {{ \"id\": \"{module}\", \"version\": \"...\" }} to requires.");
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

        ExpressionChecker checker = new(_rules, site.SourceModule ?? definition.Module, site.Kind.Roots, UseParameters(definition), ConditionValues(definition), InferDerived, IsInferring, BehaviorParameters(definition));
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
                else if (_rules.References.TryGetValue((definition, $"{path}.track"), out Definition? track))
                {
                    list.Add(new Modifier(null, null, value!, path, Track: track));
                }
                else if (_rules.References.TryGetValue((definition, $"{path}.check"), out Definition? check))
                {
                    _rules.TryExpression(definition, $"{path}.against", out CompiledExpression? against);
                    list.Add(new Modifier(null, check, value!, path, against));
                }
            }

            _rules.Modifiers[definition] = list;
        }
    }

    private void CheckValueType(Definition owner, string path, Definition variable)
    {
        ExprTypes.TryParse(variable.Json.GetProperty("value_type").GetString()!, out ExprType expected);
        if (_rules.TryExpression(owner, path, out CompiledExpression? value) && value!.Type != expected)
        {
            Error(owner, "expression.type", path, $"In '{value.Text}': variable '{variable.Id}' is {ExprTypes.Name(expected)}, but this gives a {ExprTypes.Name(value.Type)}.");
        }
    }

    private void CheckEntry(Definition owner, string areaPath, string entry, string entryPath)
    {
        if (_rules.References.TryGetValue((owner, areaPath), out Definition? area) && !area.Json.GetProperty("entries").TryGetProperty(entry, out _))
        {
            string known = string.Join(", ", area.Json.GetProperty("entries").EnumerateObject().Select(property => property.Name));
            Error(owner, "area.entry", entryPath, $"Area {area.QualifiedId} has no entry '{entry}'. Its entries: {known}.");
        }
    }

    /// <summary>Finds check operations under an element and checks their outcome tiers.</summary>
    private void WalkOperations(Definition definition, JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("op", out JsonElement applied) && applied.ValueKind == JsonValueKind.String && applied.GetString() == "apply_condition"
                && element.TryGetProperty("values", out JsonElement given)
                && _rules.References.TryGetValue((definition, $"{path}.condition"), out Definition? condition))
            {
                List<string> declared = ConditionValues(condition);
                foreach (JsonProperty value in given.EnumerateObject().Where(value => !declared.Contains(value.Name)))
                {
                    string known = declared.Count == 0 ? $"{condition.QualifiedId} declares no values." : $"Its values: {string.Join(", ", declared)}.";
                    Error(definition, "condition.value", $"{path}.values.{value.Name}", $"'{value.Name}' is not a value of {condition.QualifiedId}. {known}");
                }
            }

            if (element.TryGetProperty("op", out JsonElement op) && op.ValueKind == JsonValueKind.String && op.GetString() == "check"
                && element.TryGetProperty("outcomes", out JsonElement outcomes)
                && _rules.References.TryGetValue((definition, $"{path}.check"), out Definition? check))
            {
                CheckOutcomes(definition, check, outcomes, $"{path}.outcomes");
            }

            if (element.TryGetProperty("op", out op) && op.ValueKind == JsonValueKind.String && op.GetString() == "reduce_damage"
                && !element.TryGetProperty("amount", out _) && !element.TryGetProperty("fraction", out _))
            {
                Error(definition, "operation.reduce-damage", path, "reduce_damage needs an \"amount\" or \"fraction\" to change pending damage.");
            }

            if (element.TryGetProperty("op", out op) && op.ValueKind == JsonValueKind.String && op.GetString() == "grant_budget"
                && _rules.References.TryGetValue((definition, $"{path}.track"), out Definition? resource)
                && resource.Type != DefinitionTypes.Track)
            {
                Error(definition, "operation.grant-budget", $"{path}.track", $"'{resource.QualifiedId}' is not a resource track.");
            }

            if (element.TryGetProperty("op", out op) && op.ValueKind == JsonValueKind.String && op.GetString() == "reduce_damage"
                && _rules.References.TryGetValue((definition, $"{path}.shield_track"), out Definition? shield)
                && shield.Type != DefinitionTypes.Track)
            {
                Error(definition, "operation.reduce-damage", $"{path}.shield_track", $"'{shield.QualifiedId}' is not a resource track.");
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

    /// <summary>The values a condition's own expressions may read as condition.&lt;name&gt;.</summary>
    private static List<string> ConditionValues(Definition definition)
    {
        return definition.Type == DefinitionTypes.Condition && definition.Json.TryGetProperty("values", out JsonElement values) && values.ValueKind == JsonValueKind.Object
            ? values.EnumerateObject().Select(value => value.Name).ToList()
            : [];
    }

    /// <summary>The parameters an action's expressions may read as use.&lt;name&gt;.</summary>
    private static List<string> UseParameters(Definition definition)
    {
        return definition.Json.TryGetProperty("parameters", out JsonElement parameters) && parameters.ValueKind == JsonValueKind.Array
            ? parameters.EnumerateArray().Select(parameter => parameter.GetString()!).ToList()
            : [];
    }

    /// <summary>The named values an authored combat behavior may read as behavior.&lt;name&gt;.</summary>
    private static List<string> BehaviorParameters(Definition definition)
    {
        return definition.Type == DefinitionTypes.CombatBehavior
            && definition.Json.TryGetProperty("parameters", out JsonElement parameters)
            && parameters.ValueKind == JsonValueKind.Object
            ? parameters.EnumerateObject().Select(parameter => parameter.Name).ToList()
            : [];
    }

    private void ExpressionError(Definition definition, ExpressionSite site, string rule, ExpressionException exception)
    {
        Error(definition, rule, site.JsonPath, $"In '{site.Text}' at column {exception.Column}: {exception.Message}");
    }

    private void Error(Definition definition, string rule, string path, string message)
    {
        (string module, string file, string jsonPath) = definition.DiagnosticLocation(path);
        _diagnostics.Add(new ModuleDiagnostic(rule, message, module, file, jsonPath));
    }
}
