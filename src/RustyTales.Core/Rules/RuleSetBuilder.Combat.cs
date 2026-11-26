using System.Text.Json;
using RustyTales.Core.Combat;
using RustyTales.Core.Definitions;
using RustyTales.Core.Expressions;
using RustyTales.Core.Modules;

namespace RustyTales.Core.Rules;

public sealed partial class RuleSetBuilder
{
    /// <summary>A field's terrain keys are single characters with a cost of 1 or more; an encounter's rows fit every field and use its keys.</summary>
    private void CheckTerrain()
    {
        List<Definition> fielded = _rules.OfType(DefinitionTypes.Combat).Where(combat => combat.Json.TryGetProperty("field", out _)).ToList();
        foreach (Definition combat in fielded)
        {
            if (!combat.Json.GetProperty("field").TryGetProperty("terrain", out JsonElement terrain))
            {
                continue;
            }

            foreach (JsonProperty entry in terrain.EnumerateObject())
            {
                if (entry.Name.Length != 1 || entry.Name[0] == CombatField.Open || char.IsWhiteSpace(entry.Name[0]))
                {
                    Error(combat, "combat.terrain", $"$.field.terrain.{entry.Name}", $"Terrain keys are one character other than '{CombatField.Open}' (open ground) and spaces, but '{entry.Name}' isn't.");
                }

                if (entry.Value.TryGetProperty("cost", out JsonElement cost) && cost.GetInt32() < 1)
                {
                    Error(combat, "combat.terrain", $"$.field.terrain.{entry.Name}.cost", "A terrain's cost must be at least 1.");
                }
            }
        }

        foreach (Definition encounter in _rules.OfType(DefinitionTypes.Encounter))
        {
            if (!encounter.Json.TryGetProperty("terrain", out JsonElement rows))
            {
                continue;
            }

            if (fielded.Count == 0)
            {
                Error(encounter, "encounter.terrain", "$.terrain", "Terrain needs a combat field to lie on, but no combat definition has a \"field\".");
                continue;
            }

            List<string> lines = rows.EnumerateArray().Select(row => row.GetString()!).ToList();
            foreach (Definition combat in fielded)
            {
                JsonElement field = combat.Json.GetProperty("field");
                int width = field.GetProperty("width").GetInt32();
                int height = field.GetProperty("height").GetInt32();
                if (lines.Count != height || lines.Any(line => line.Length != width))
                {
                    Error(encounter, "encounter.terrain", "$.terrain", $"Combat '{combat.Id}' has a {width} by {height} field, so terrain needs {height} rows of {width} characters, but it has {lines.Count} rows of {string.Join(", ", lines.Select(line => line.Length).Distinct())}.");
                    continue;
                }

                Dictionary<char, Terrain> kinds = CombatField.Kinds(field);
                for (int y = 0; y < lines.Count; y++)
                {
                    foreach (char key in lines[y].Where(key => key != CombatField.Open && !kinds.ContainsKey(key)).Distinct())
                    {
                        string known = kinds.Count == 0 ? "It declares no terrain." : $"Its terrain: {string.Join(", ", kinds.Keys.Select(k => $"'{k}'"))}.";
                        Error(encounter, "encounter.terrain", $"$.terrain[{y}]", $"'{key}' is not open ground ('{CombatField.Open}') or terrain the field of combat '{combat.Id}' declares. {known}");
                    }
                }
            }
        }
    }

    private void CheckActions()
    {
        CheckTerrain();
        foreach (Definition combat in _rules.OfType(DefinitionTypes.Combat))
        {
            if (combat.Json.TryGetProperty("round_limit", out JsonElement limit) && limit.GetInt32() < 1)
            {
                Error(combat, "combat.round-limit", "$.round_limit", "round_limit must be at least 1.");
            }

            bool elective = combat.Json.TryGetProperty("initiative_mode", out JsonElement mode) && mode.GetString() == "elective";
            if (!elective)
            {
                foreach (string field in new[] { "initiative", "initiative_by", "initiative_order", "initiative_each" })
                {
                    if (!combat.Json.TryGetProperty(field, out _))
                    {
                        Error(combat, "combat.initiative", $"$.{field}", $"Rolled initiative needs \"{field}\"; use initiative_mode \"elective\" when the last actor chooses the next creature.");
                    }
                }
            }
        }

        HashSet<string> budget = _rules.OfType(DefinitionTypes.Combat)
            .SelectMany(combat => combat.Json.GetProperty("budget").EnumerateArray().Select(entry => entry.GetProperty("id").GetString()!))
            .ToHashSet();
        foreach (Definition check in _rules.OfType(DefinitionTypes.Check))
        {
            if (!check.Json.TryGetProperty("post_roll", out JsonElement options))
            {
                continue;
            }

            for (int index = 0; index < options.GetArrayLength(); index++)
            {
                JsonElement option = options[index];
                bool bonus = option.TryGetProperty("bonus", out _);
                bool reroll = option.TryGetProperty("reroll", out JsonElement rerollValue) && rerollValue.GetBoolean();
                if (bonus == reroll)
                {
                    Error(check, "check.post-roll", $"$.post_roll[{index}]", "A post-roll option needs exactly one effect: a \"bonus\" expression or \"reroll\": true.");
                }

                if (_rules.References.TryGetValue((check, $"$.post_roll[{index}].track"), out Definition? track)
                    && track.Type != DefinitionTypes.Track)
                {
                    Error(check, "check.post-roll", $"$.post_roll[{index}].track", $"'{track.QualifiedId}' is not a resource track.");
                }
            }
        }

        foreach (Definition reaction in _rules.OfType(DefinitionTypes.Reaction))
        {
            foreach (JsonProperty entry in reaction.Json.GetProperty("cost").EnumerateObject().Where(entry => !budget.Contains(entry.Name)))
            {
                string known = budget.Count == 0 ? "No combat definition declares a budget." : $"Budget IDs: {string.Join(", ", budget.Order(StringComparer.Ordinal))}.";
                Error(reaction, "action.cost", $"$.cost.{entry.Name}", $"'{entry.Name}' is not a budget in any combat definition. {known}");
            }
        }

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

            if (!cost.EnumerateObject().Any(entry => entry.Value.GetInt32() > 0)
                && !(action.Json.TryGetProperty("available", out _)
                    && ContainsOperation(action.Json, "grant_budget"))
                && !IsReactionAction(action))
            {
                Error(action, "action.cost", "$.cost", "An action must cost at least 1 of some budget, or a creature could take it forever.");
            }

            if (action.Json.TryGetProperty("portions", out _) && action.Json.GetProperty("target").GetString() is "self" or "all_enemies" or "all_allies")
            {
                Error(action, "action.portions", "$.portions", "Portions go to one target at a time; a self or whole-side action can't divide its effect. Use a target of enemy, ally, hurt_ally or fallen_ally.");
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

            if (!hasCheck && action.Json.TryGetProperty("check_bonus", out _))
            {
                Error(action, "action.check-bonus", "$.check_bonus", "check_bonus adds to the action's check, but it has none. Add \"check\", or give the bonus to a check operation's \"bonus\".");
            }

            if (!hasCheck && !action.Json.TryGetProperty("always", out _))
            {
                Error(action, "action.outcomes", "$", "An action needs \"always\" operations, or a \"check\" with \"outcomes\"; otherwise it does nothing.");
            }

            CheckActionLegalityExpression(action, "available", "availability");
            CheckActionLegalityExpression(action, "valid_target", "target legality");
            CheckActionLegalityExpression(action, "range", "range");
            CheckActionLegalityExpression(action, "portions", "portion count");

            WalkOperations(action, action.Json, "$");
        }

        foreach (Definition definition in _rules.Definitions.Where(definition => definition.Type is { } type
                     && (type == DefinitionTypes.Action || type == DefinitionTypes.Condition)))
        {
            CheckBudgetOperations(definition, definition.Json, "$", budget);
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

    private void CheckCombatBehaviors()
    {
        foreach (Definition behavior in _rules.OfType(DefinitionTypes.CombatBehavior))
        {
            ModuleKind moduleKind = _manifests[behavior.Module].Kind;
            if (moduleKind is not (ModuleKind.Ruleset or ModuleKind.Extension))
            {
                Error(behavior, "behavior.module", "$",
                    $"Combat behavior definitions are reusable rules, so they belong in ruleset or extension modules; '{behavior.Module}' is a {ModuleKinds.Name(moduleKind)}. A campaign can assign a behavior by reference or require an extension that owns it.");
            }

            CheckBehaviorParameters(behavior);
            if (!behavior.Json.TryGetProperty("rules", out JsonElement rules) || rules.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            for (int index = 0; index < rules.GetArrayLength(); index++)
            {
                JsonElement rule = rules[index];
                if (rule.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string path = $"$.rules[{index}]";
                bool hasPriority = rule.TryGetProperty("priority", out _);
                bool hasScore = rule.TryGetProperty("score", out _);
                if (hasPriority && hasScore)
                {
                    Error(behavior, "behavior.policy", path, "A behavior rule chooses one ordering policy: give either \"priority\" or \"score\", not both.");
                }

                CheckBehaviorExpression(behavior, $"{path}.when", rule, "when", "a guard", rejectDice: true);
                CheckBehaviorExpression(behavior, $"{path}.priority", rule, "priority", "a priority", rejectDice: true);
                CheckBehaviorExpression(behavior, $"{path}.score", rule, "score", "a score", rejectDice: true);

                if (!rule.TryGetProperty("steps", out JsonElement steps) || steps.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                if (steps.GetArrayLength() == 0)
                {
                    Error(behavior, "behavior.steps", $"{path}.steps", "A behavior rule needs at least one action step; use fallback to end the turn when it has no useful action.");
                }

                for (int stepIndex = 0; stepIndex < steps.GetArrayLength(); stepIndex++)
                {
                    JsonElement step = steps[stepIndex];
                    if (step.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string stepPath = $"{path}.steps[{stepIndex}]";
                    CheckBehaviorExpression(behavior, $"{stepPath}.target_score", step, "target_score", "a target score", rejectDice: true);
                    CheckBehaviorSpell(behavior, step, stepPath);
                    if (step.TryGetProperty("destination", out JsonElement destination))
                    {
                        CheckBehaviorExpression(behavior, $"{stepPath}.destination.distance", destination, "distance", "a destination distance", rejectDice: true);
                    }
                }
            }

            CombatBehaviorProfile? profile = CombatBehaviorProfile.Build(_rules, behavior, _diagnostics);
            if (profile is not null && !_diagnostics.Any(diagnostic => BelongsTo(behavior, diagnostic)))
            {
                _rules.CombatBehaviors[behavior] = profile;
            }
        }
    }

    private static bool BelongsTo(Definition definition, ModuleDiagnostic diagnostic)
    {
        if (diagnostic.Module != definition.Module || diagnostic.File != definition.File)
        {
            return false;
        }

        if (definition.RootPath == "$")
        {
            return true;
        }

        string? path = diagnostic.JsonPath;
        return path is not null
            && (path == definition.RootPath
                || path.StartsWith($"{definition.RootPath}.", StringComparison.Ordinal)
                || path.StartsWith($"{definition.RootPath}[", StringComparison.Ordinal));
    }

    private void CheckBehaviorSpell(Definition behavior, JsonElement step, string stepPath)
    {
        string spellPath = $"{stepPath}.spell";
        if (!step.TryGetProperty("spell", out _)
            || !_rules.References.TryGetValue((behavior, spellPath), out Definition? spell)
            || !_rules.References.TryGetValue((behavior, $"{stepPath}.action.action"), out Definition? action))
        {
            return;
        }

        if (!spell.Json.TryGetProperty("effect", out JsonElement effect)
            || effect.ValueKind != JsonValueKind.Object)
        {
            Error(behavior, "behavior.spell", spellPath,
                $"Spell '{spell.QualifiedId}' has no combat effect. Give it an \"effect\" action use before assigning it to this behavior step.");
            return;
        }

        if (!_rules.References.TryGetValue((spell, "$.effect.action"), out Definition? effectAction))
        {
            return;
        }

        if (effectAction != action)
        {
            Error(behavior, "behavior.spell", spellPath,
                $"Spell '{spell.QualifiedId}' uses action '{effectAction.QualifiedId}', but this behavior step uses '{action.QualifiedId}'. Assign the spell to a step with the same action as its effect.");
        }
    }

    private void CheckBehaviorParameters(Definition behavior)
    {
        if (!behavior.Json.TryGetProperty("parameters", out JsonElement parameters)
            || parameters.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        HashSet<string> names = [];
        Dictionary<string, List<string>> dependencies = [];
        foreach (JsonProperty parameter in parameters.EnumerateObject())
        {
            if (!names.Add(parameter.Name) || !DefinitionIds.IsValid(parameter.Name))
            {
                Error(behavior, "behavior.parameter", $"$.parameters.{parameter.Name}",
                    $"'{parameter.Name}' is not a unique behavior parameter ID. Use {DefinitionIds.FormatDescription}.");
            }

            CheckBehaviorExpression(behavior, $"$.parameters.{parameter.Name}", parameters, parameter.Name, "a parameter", rejectDice: true);
            if (_rules.TryExpression(behavior, $"$.parameters.{parameter.Name}", out CompiledExpression? expression)
                && expression is not null)
            {
                dependencies[parameter.Name] = BehaviorReferences(expression.Root).Distinct(StringComparer.Ordinal).ToList();
            }
        }

        CheckBehaviorParameterCycles(behavior, dependencies);
    }

    private void CheckBehaviorParameterCycles(Definition behavior, IReadOnlyDictionary<string, List<string>> dependencies)
    {
        Dictionary<string, int> marks = [];
        HashSet<string> reported = new(StringComparer.Ordinal);

        foreach (string parameter in dependencies.Keys)
        {
            Visit(parameter, []);
        }

        void Visit(string parameter, List<string> path)
        {
            if (marks.TryGetValue(parameter, out int mark))
            {
                if (mark == 1 && reported.Add(parameter))
                {
                    int start = path.IndexOf(parameter);
                    IEnumerable<string> cycle = start >= 0
                        ? path.Skip(start).Append(parameter)
                        : [parameter, parameter];
                    Error(behavior, "behavior.parameter", $"$.parameters.{parameter}",
                        $"Behavior parameter expressions form a cycle ({string.Join(" -> ", cycle)}). Reference a self, target, combat field, or a non-cyclic parameter instead.");
                }

                return;
            }

            marks[parameter] = 1;
            path.Add(parameter);
            if (dependencies.TryGetValue(parameter, out List<string>? references))
            {
                foreach (string reference in references.Where(dependencies.ContainsKey))
                {
                    Visit(reference, path);
                }
            }

            path.RemoveAt(path.Count - 1);
            marks[parameter] = 2;
        }
    }

    private void CheckBehaviorExpression(Definition behavior, string path, JsonElement owner, string field, string what, bool rejectDice)
    {
        if (!owner.TryGetProperty(field, out _)
            || !_rules.TryExpression(behavior, path, out CompiledExpression? expression)
            || expression is null)
        {
            return;
        }

        if (rejectDice && ContainsDice(expression.Root))
        {
            Error(behavior, "behavior.random", path,
                $"A combat behavior {what} must be deterministic while candidates are inspected; remove dice and use a committed action/check for randomness.");
        }
    }

    private void CheckActionLegalityExpression(Definition action, string field, string what)
    {
        string path = $"$.{field}";
        if (!action.Json.TryGetProperty(field, out _)
            || !_rules.TryExpression(action, path, out CompiledExpression? expression)
            || expression is null
            || !ContainsDice(expression.Root))
        {
            return;
        }

        Error(action, "action.random", path,
            $"An action's {what} must be deterministic while legal choices are inspected; remove dice from this field. Randomness belongs in max_targets, checks or operations after the choice is committed.");
    }

    private static bool ContainsDice(Expr expression)
    {
        return expression switch
        {
            DiceLiteral => true,
            UnaryExpr unary => ContainsDice(unary.Operand),
            BinaryExpr binary => ContainsDice(binary.Left) || ContainsDice(binary.Right),
            ConditionalExpr conditional => ContainsDice(conditional.Condition) || ContainsDice(conditional.Then) || ContainsDice(conditional.Else),
            CallExpr call => call.Function is "roll" or "roll_keep" or "roll_count" or "roll_explode" or "roll_fudge" or "roll_pool"
                || call.Arguments.Any(ContainsDice),
            _ => false,
        };
    }

    private static IEnumerable<string> BehaviorReferences(Expr expression)
    {
        return expression switch
        {
            PathExpr path when path.Root == "behavior" => [path.Name],
            UnaryExpr unary => BehaviorReferences(unary.Operand),
            BinaryExpr binary => BehaviorReferences(binary.Left).Concat(BehaviorReferences(binary.Right)),
            ConditionalExpr conditional => BehaviorReferences(conditional.Condition)
                .Concat(BehaviorReferences(conditional.Then))
                .Concat(BehaviorReferences(conditional.Else)),
            CallExpr call => call.Arguments.SelectMany(BehaviorReferences),
            _ => [],
        };
    }

    private static bool ContainsOperation(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("op", out JsonElement op) && op.ValueKind == JsonValueKind.String && op.GetString() == name)
            {
                return true;
            }

            return element.EnumerateObject().Any(property => ContainsOperation(property.Value, name));
        }

        return element.ValueKind == JsonValueKind.Array && element.EnumerateArray().Any(item => ContainsOperation(item, name));
    }

    private bool IsReactionAction(Definition action)
    {
        return _rules.OfType(DefinitionTypes.Reaction).Any(reaction =>
            _rules.References.TryGetValue((reaction, "$.use.action"), out Definition? referenced) && referenced == action);
    }

    private void CheckBudgetOperations(Definition definition, JsonElement element, string path, HashSet<string> budget)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("op", out JsonElement op) && op.ValueKind == JsonValueKind.String && op.GetString() == "grant_budget"
                && element.TryGetProperty("budget", out JsonElement budgetId) && !budget.Contains(budgetId.GetString()!))
            {
                string known = budget.Count == 0 ? "No combat definition declares a budget." : $"Budget IDs: {string.Join(", ", budget.Order(StringComparer.Ordinal))}.";
                Error(definition, "operation.grant-budget", $"{path}.budget", $"'{budgetId.GetString()}' is not a budget in any combat definition. {known}");
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                CheckBudgetOperations(definition, property.Value, $"{path}.{property.Name}", budget);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                CheckBudgetOperations(definition, item, $"{path}[{index}]", budget);
                index++;
            }
        }
    }
}
