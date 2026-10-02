using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Rules;

/// <summary>
/// Every definition of a loaded module set, with references resolved and
/// expressions checked. Built by <see cref="RuleSetBuilder"/>.
/// </summary>
public sealed class RuleSet
{
    internal RuleSet()
    {
    }

    /// <summary>Definitions in module load order, then file order.</summary>
    public List<Definition> Definitions { get; } = [];

    /// <summary>Attributes and derived values by ID.</summary>
    public Dictionary<string, Stat> Stats { get; } = [];

    /// <summary>Stat names every creature has, whatever the ruleset defines.</summary>
    public static IReadOnlyDictionary<string, ExprType> BuiltInStats { get; } = new Dictionary<string, ExprType>
    {
        ["level"] = ExprType.Number,
        ["class"] = ExprType.Text,
        ["race"] = ExprType.Text,
    };

    internal Dictionary<(string Type, string Module, string Id), Definition> ByKey { get; } = [];

    /// <summary>For each module, the modules its definitions may refer to: itself and its requires.</summary>
    internal Dictionary<string, HashSet<string>> VisibleModules { get; } = [];

    internal Dictionary<(Definition Definition, string Path), CompiledExpression> Expressions { get; } = [];

    internal Dictionary<(Definition Definition, string Path), Definition> References { get; } = [];

    internal Dictionary<Definition, CompiledTable> Tables { get; } = [];

    internal Dictionary<Definition, List<Modifier>> Modifiers { get; } = [];

    public CompiledExpression Expression(Definition definition, string path) => Expressions[(definition, path)];

    public bool TryExpression(Definition definition, string path, out CompiledExpression? expression)
    {
        return Expressions.TryGetValue((definition, path), out expression);
    }

    public Definition Reference(Definition definition, string path) => References[(definition, path)];

    public IReadOnlyList<Modifier> ModifiersOf(Definition definition)
    {
        return Modifiers.TryGetValue(definition, out List<Modifier>? modifiers) ? modifiers : [];
    }

    public IEnumerable<Definition> OfType(DefinitionType type) => Definitions.Where(definition => definition.Type == type);

    /// <summary>
    /// Finds a definition by "module:id", or by bare ID when exactly one
    /// module defines it. Returns null with a reason otherwise.
    /// </summary>
    public Definition? Find(DefinitionType type, string reference, out string? problem)
    {
        problem = null;
        int colon = reference.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            string module = reference[..colon];
            string id = reference[(colon + 1)..];
            if (ByKey.TryGetValue((type.Name, module, id), out Definition? found))
            {
                return found;
            }

            problem = $"There is no {type.Name} '{reference}'. {Known(type)}";
            return null;
        }

        List<Definition> matches = OfType(type).Where(definition => definition.Id == reference).ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        problem = matches.Count == 0
            ? $"There is no {type.Name} '{reference}'. {Known(type)}"
            : $"'{reference}' names a {type.Name} in more than one module ({string.Join(", ", matches.Select(match => match.Module))}); write it as module:id.";
        return null;
    }

    /// <summary>
    /// Checks an expression written against this rule set as if it were in
    /// <paramref name="module"/>, for example one typed into <c>goldbox eval</c>.
    /// </summary>
    /// <exception cref="ExpressionException">The expression doesn't parse or type-check.</exception>
    public CompiledExpression Compile(string text, string module, Roots roots)
    {
        Expr root = Parser.Parse(text);
        ExpressionChecker checker = new(this, module, roots, derived => Stats[derived.Id].Type, _ => false);
        ExprType type = checker.Check(root);
        return new CompiledExpression(text, root, type, checker.Tables);
    }

    public string StatList(bool attributesOnly)
    {
        List<string> ids = Stats.Values
            .Where(stat => !attributesOnly || stat.IsAttribute)
            .Select(stat => stat.Id)
            .Order(StringComparer.Ordinal)
            .ToList();
        string what = attributesOnly ? "Attributes" : "Stats";
        return ids.Count == 0 ? $"No {what.ToLowerInvariant()} are defined." : $"{what}: {string.Join(", ", ids)}.";
    }

    public string Known(DefinitionType type)
    {
        List<string> ids = OfType(type).Select(definition => definition.QualifiedId).ToList();
        return ids.Count == 0 ? $"No {type.Name} definitions are loaded." : $"Known: {string.Join(", ", ids)}.";
    }
}
