using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Cli;

/// <summary><c>goldbox module inspect</c>: the resolved definitions of a module set.</summary>
internal static class InspectCommand
{
    public static int Run(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--modules", "--extension"], ["--trace"]);
        if (error is null && parsed.Positionals.Count is < 1 or > 2)
        {
            error = "Usage: goldbox module inspect <path> [<type> | <id> | <module>:<id>] [--trace] [--modules <dir>]... [--extension <id>]...";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        ModuleSet set = ModuleSets.Load(
            Path.GetFullPath(parsed.Positionals[0], workingDirectory),
            parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList(),
            ModuleSets.Extensions(parsed));
        if (set.Rules is null || !set.IsValid)
        {
            return output.ModuleErrors(set);
        }

        RuleSet rules = set.Rules;
        if (parsed.Positionals.Count == 1)
        {
            if (parsed.Has("--trace"))
            {
                output.BehaviorDiagnostics(rules, rules.OfType(DefinitionTypes.CombatBehavior).ToList());
            }
            else
            {
                output.DefinitionList(rules, rules.Definitions, includeStats: true);
            }

            return GoldboxCli.Ok;
        }

        string selector = parsed.Positionals[1];
        DefinitionType? type = DefinitionTypes.Find(selector);
        if (type is not null)
        {
            List<Definition> definitions = rules.OfType(type).ToList();
            if (parsed.Has("--trace") && type == DefinitionTypes.CombatBehavior)
            {
                output.BehaviorDiagnostics(rules, definitions);
            }
            else
            {
                output.DefinitionList(rules, definitions, includeStats: false);
            }

            return GoldboxCli.Ok;
        }

        List<Definition> matches = rules.Definitions
            .Where(definition => definition.Id == selector || definition.QualifiedId == selector)
            .ToList();
        if (matches.Count == 0)
        {
            string types = string.Join(", ", DefinitionTypes.All.Select(definitionType => definitionType.Name));
            return output.UsageError($"Nothing matches '{selector}'. Select a definition type ({types}), an ID, or module:id. Run without a selector to list everything.");
        }

        if (parsed.Has("--trace") && matches.All(definition => definition.Type == DefinitionTypes.CombatBehavior))
        {
            output.BehaviorDiagnostics(rules, matches);
        }
        else
        {
            output.Definitions(rules, matches);
        }
        return GoldboxCli.Ok;
    }

    /// <summary>Every expression in a definition with its checked type, by JSON path.</summary>
    public static IEnumerable<(string Path, string Text, string Type)> Expressions(RuleSet rules, Definition definition)
    {
        foreach (ExpressionSite site in definition.Expressions)
        {
            if (rules.TryExpression(definition, site.JsonPath, out CompiledExpression? compiled))
            {
                yield return (site.JsonPath, site.Text, Core.Expressions.ExprTypes.Name(compiled!.Type));
            }
        }
    }
}
