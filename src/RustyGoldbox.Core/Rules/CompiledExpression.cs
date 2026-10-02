using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Rules;

/// <summary>A parsed and type-checked expression, with the tables its <c>table()</c> calls name.</summary>
public sealed class CompiledExpression(string text, Expr root, ExprType type, IReadOnlyDictionary<Expr, CompiledTable> tables)
{
    public string Text { get; } = text;

    public Expr Root { get; } = root;

    public ExprType Type { get; } = type;

    internal IReadOnlyDictionary<Expr, CompiledTable> Tables { get; } = tables;
}

/// <summary>A stat: an attribute or a derived value.</summary>
public sealed record Stat(string Id, Definition Definition, ExprType Type)
{
    public bool IsAttribute => Definition.Type == DefinitionTypes.Attribute;
}

/// <summary>A modifier from a race, condition or item: adds to a stat or to a check's roll.</summary>
/// <param name="Path">Where the modifier is in its definition, for messages.</param>
public sealed record Modifier(string? Stat, Definition? Check, CompiledExpression Value, string Path, CompiledExpression? Against = null);
