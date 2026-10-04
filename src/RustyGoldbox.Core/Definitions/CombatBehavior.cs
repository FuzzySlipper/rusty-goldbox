using System.Text.Json;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Definitions;

/// <summary>What an authored combat behavior does when its current plan is no longer legal.</summary>
public enum CombatBehaviorFallback
{
    Next,
    EndTurn,
    Flee,
}

/// <summary>How long an authored sequence remains selected after one step resolves.</summary>
public enum CombatBehaviorCommitment
{
    Step,
    Plan,
}

/// <summary>The group of creatures a behavior step may choose from.</summary>
public enum CombatBehaviorTarget
{
    Self,
    Enemy,
    Ally,
    HurtAlly,
    FallenAlly,
}

/// <summary>A relative destination preference evaluated against a selected target.</summary>
public sealed record CombatBehaviorDestination(
    Definition Owner,
    string Path,
    string Kind,
    CompiledExpression Distance);

/// <summary>One existing action use in an authored behavior plan.</summary>
public sealed record CombatBehaviorStep(
    Definition Owner,
    string Path,
    Definition Action,
    Definition? Spell,
    string? Name,
    string? FromItem,
    IReadOnlyDictionary<string, CompiledExpression> Parameters,
    CombatBehaviorTarget? Target,
    CompiledExpression? TargetScore,
    CombatBehaviorDestination? Destination);

/// <summary>An ordered policy alternative with a fixed, non-looping sequence of steps.</summary>
public sealed record CombatBehaviorRule(
    Definition Owner,
    int Index,
    CompiledExpression? When,
    CompiledExpression? Priority,
    CompiledExpression? Score,
    CombatBehaviorCommitment Commitment,
    IReadOnlyList<CombatBehaviorStep> Steps,
    CombatBehaviorFallback Fallback);

/// <summary>
/// A checked module-authored combat policy. The rule set owns one instance per
/// definition so controllers can share the same compiled expressions and references.
/// </summary>
public sealed class CombatBehaviorProfile
{
    private CombatBehaviorProfile(
        Definition definition,
        IReadOnlyDictionary<string, CompiledExpression> parameters,
        IReadOnlyList<CombatBehaviorRule> rules,
        CombatBehaviorFallback fallback)
    {
        Definition = definition;
        Parameters = parameters;
        Rules = rules;
        Fallback = fallback;
    }

    public Definition Definition { get; }

    /// <summary>Numeric defaults available as <c>behavior.&lt;name&gt;</c> to rules and uses.</summary>
    public IReadOnlyDictionary<string, CompiledExpression> Parameters { get; }

    public IReadOnlyList<CombatBehaviorRule> Rules { get; }

    public CombatBehaviorFallback Fallback { get; }

    /// <summary>
    /// Builds the runtime view after references and expressions have been checked.
    /// Missing compiled entries mean an earlier diagnostic already explains the
    /// problem, so this method skips the incomplete profile rather than guessing.
    /// </summary>
    internal static CombatBehaviorProfile? Build(RuleSet rules, Definition definition, List<ModuleDiagnostic> diagnostics)
    {
        Dictionary<string, CompiledExpression> parameters = [];
        if (definition.Json.TryGetProperty("parameters", out JsonElement declaredParameters)
            && declaredParameters.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty parameter in declaredParameters.EnumerateObject())
            {
                string path = $"$.parameters.{parameter.Name}";
                if (rules.TryExpression(definition, path, out CompiledExpression? expression))
                {
                    parameters[parameter.Name] = expression!;
                }
            }
        }

        CombatBehaviorFallback fallback = ParseFallback(definition.Json, "fallback", CombatBehaviorFallback.EndTurn);
        List<CombatBehaviorRule> policies = [];
        if (!definition.Json.TryGetProperty("rules", out JsonElement declaredRules)
            || declaredRules.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        for (int index = 0; index < declaredRules.GetArrayLength(); index++)
        {
            JsonElement declaredRule = declaredRules[index];
            if (declaredRule.ValueKind != JsonValueKind.Object
                || !declaredRule.TryGetProperty("steps", out JsonElement declaredSteps)
                || declaredSteps.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            string rulePath = $"$.rules[{index}]";
            CompiledExpression? when = Expression(rules, definition, declaredRule, "when", rulePath);
            CompiledExpression? priority = Expression(rules, definition, declaredRule, "priority", rulePath);
            CompiledExpression? score = Expression(rules, definition, declaredRule, "score", rulePath);
            CombatBehaviorCommitment commitment = declaredRule.TryGetProperty("commit", out JsonElement commit)
                && commit.GetString() == "plan"
                ? CombatBehaviorCommitment.Plan
                : CombatBehaviorCommitment.Step;
            CombatBehaviorFallback ruleFallback = ParseFallback(declaredRule, "fallback", fallback);

            List<CombatBehaviorStep> steps = [];
            for (int stepIndex = 0; stepIndex < declaredSteps.GetArrayLength(); stepIndex++)
            {
                JsonElement declaredStep = declaredSteps[stepIndex];
                if (declaredStep.ValueKind != JsonValueKind.Object
                    || !declaredStep.TryGetProperty("action", out JsonElement declaredUse)
                    || declaredUse.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string stepPath = $"{rulePath}.steps[{stepIndex}]";
                string actionPath = $"{stepPath}.action";
                if (!rules.References.TryGetValue((definition, $"{actionPath}.action"), out Definition? action))
                {
                    continue;
                }

                Definition? spell = rules.References.TryGetValue((definition, $"{stepPath}.spell"), out Definition? foundSpell)
                    ? foundSpell
                    : null;
                Dictionary<string, CompiledExpression> useParameters = [];
                foreach (JsonProperty property in declaredUse.EnumerateObject())
                {
                    if (property.Name is "action" or "name" or "from_item")
                    {
                        continue;
                    }

                    string parameterPath = $"{actionPath}.{property.Name}";
                    if (rules.TryExpression(definition, parameterPath, out CompiledExpression? parameter))
                    {
                        useParameters[property.Name] = parameter!;
                    }
                }

                CombatBehaviorTarget? target = declaredStep.TryGetProperty("target", out JsonElement targetElement)
                    && targetElement.ValueKind == JsonValueKind.String
                    ? ParseTarget(targetElement.GetString()!)
                    : null;
                CompiledExpression? targetScore = Expression(rules, definition, declaredStep, "target_score", stepPath);
                CombatBehaviorDestination? destination = BuildDestination(rules, definition, declaredStep, stepPath);
                string? name = declaredUse.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;
                string? fromItem = declaredUse.TryGetProperty("from_item", out JsonElement fromItemElement) && fromItemElement.ValueKind == JsonValueKind.String
                    ? fromItemElement.GetString()
                    : null;
                steps.Add(new CombatBehaviorStep(definition, stepPath, action, spell, name, fromItem, useParameters, target, targetScore, destination));
            }

            policies.Add(new CombatBehaviorRule(definition, index, when, priority, score, commitment, steps, ruleFallback));
        }

        return new CombatBehaviorProfile(definition, parameters, policies, fallback);
    }

    private static CombatBehaviorDestination? BuildDestination(RuleSet rules, Definition definition, JsonElement step, string stepPath)
    {
        if (!step.TryGetProperty("destination", out JsonElement destination))
        {
            return null;
        }

        string path = $"{stepPath}.destination";
        CompiledExpression? distance = Expression(rules, definition, destination, "distance", path);
        return distance is null
            || !destination.TryGetProperty("kind", out JsonElement kind)
            || kind.ValueKind != JsonValueKind.String
            ? null
            : new CombatBehaviorDestination(definition, path, kind.GetString()!, distance);
    }

    private static CompiledExpression? Expression(RuleSet rules, Definition definition, JsonElement owner, string name, string path)
    {
        return owner.TryGetProperty(name, out _) && rules.TryExpression(definition, $"{path}.{name}", out CompiledExpression? expression)
            ? expression
            : null;
    }

    private static CombatBehaviorFallback ParseFallback(JsonElement owner, string name, CombatBehaviorFallback fallback)
    {
        if (!owner.TryGetProperty(name, out JsonElement value))
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() switch
        {
            "next" => CombatBehaviorFallback.Next,
            "flee" => CombatBehaviorFallback.Flee,
            _ => CombatBehaviorFallback.EndTurn,
        } : fallback;
    }

    private static CombatBehaviorTarget ParseTarget(string value)
    {
        return value switch
        {
            "self" => CombatBehaviorTarget.Self,
            "ally" => CombatBehaviorTarget.Ally,
            "hurt_ally" => CombatBehaviorTarget.HurtAlly,
            "fallen_ally" => CombatBehaviorTarget.FallenAlly,
            _ => CombatBehaviorTarget.Enemy,
        };
    }
}
