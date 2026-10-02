using RustyGoldbox.Core.Expressions;

namespace RustyGoldbox.Core.Definitions;

/// <summary>Which creatures an expression may read.</summary>
[Flags]
public enum Roots
{
    None = 0,
    Self = 1,
    Target = 2,

    /// <summary>use.&lt;parameter&gt;: the parameters an action was used with.</summary>
    Use = 4,

    /// <summary>check.roll, check.total, check.target, check.margin: the check being resolved.</summary>
    Check = 8,

    /// <summary>campaign.var.&lt;name&gt;: campaign variables.</summary>
    Campaign = 16,

    /// <summary>class.level: the creature's level in the class a modifier belongs to.</summary>
    Class = 32,

    /// <summary>outer.roll, outer.total, outer.target, outer.margin: the check a nested check was made during.</summary>
    Outer = 64,

    /// <summary>condition.&lt;value&gt;: a value the condition was applied with, inside the condition's own definition.</summary>
    Condition = 128,
}

/// <summary>What a definition field accepts. <see cref="Describe"/> is the text <c>goldbox schema</c> shows.</summary>
public abstract record FieldKind
{
    public abstract string Describe();
}

public sealed record TextKind : FieldKind
{
    public override string Describe() => "text";
}

public sealed record BooleanKind : FieldKind
{
    public override string Describe() => "true or false";
}

public sealed record IntegerKind : FieldKind
{
    public override string Describe() => "whole number";
}

public sealed record NumberKind : FieldKind
{
    public override string Describe() => "number";
}

public sealed record EnumKind(IReadOnlyList<string> Values) : FieldKind
{
    public override string Describe() => "one of " + string.Join(", ", Values.Select(value => $"\"{value}\""));
}

/// <summary>An expression string. <see cref="Expected"/> null means any type (it is inferred).</summary>
public sealed record ExpressionKind(ExprType? Expected, Roots Roots) : FieldKind
{
    public override string Describe()
    {
        string type = Expected is null ? "any type" : ExprTypes.Name(Expected.Value);
        List<string> roots = [];
        if (Roots.HasFlag(Roots.Self))
        {
            roots.Add("self");
        }

        if (Roots.HasFlag(Roots.Target))
        {
            roots.Add("target");
        }

        if (Roots.HasFlag(Roots.Use))
        {
            roots.Add("use");
        }

        if (Roots.HasFlag(Roots.Check))
        {
            roots.Add("check");
        }

        if (Roots.HasFlag(Roots.Campaign))
        {
            roots.Add("campaign.var");
        }

        if (Roots.HasFlag(Roots.Class))
        {
            roots.Add("class.level");
        }

        if (Roots.HasFlag(Roots.Outer))
        {
            roots.Add("outer");
        }

        if (Roots.HasFlag(Roots.Condition))
        {
            roots.Add("condition.<value>");
        }

        string reads = roots.Count == 0 ? "no reads" : "may read " + string.Join(", ", roots);
        return $"expression ({type}; {reads})";
    }
}

/// <summary>
/// A reference to another definition: <c>id</c> in the same module or
/// <c>module:id</c> in a required one. With <paramref name="AssetKind"/>, it
/// must be an asset of that kind.
/// </summary>
public sealed record ReferenceKind(string DefinitionType, string? AssetKind = null) : FieldKind
{
    public override string Describe() => AssetKind is null
        ? $"reference to a {DefinitionType} (\"id\" or \"module:id\")"
        : $"reference to a {AssetKind} asset (\"id\" or \"module:id\")";
}

/// <summary>The ID of a stat: an attribute or a derived value.</summary>
public sealed record StatKind(bool AttributesOnly) : FieldKind
{
    public override string Describe() => AttributesOnly ? "attribute ID" : "stat ID (an attribute or derived value)";
}

public sealed record ListKind(FieldKind Item, int? Count = null) : FieldKind
{
    public override string Describe()
    {
        string count = Count is null ? "" : $" of exactly {Count}";
        return $"array{count} of {Item.Describe()}";
    }
}

/// <summary>An object whose keys are references or stat IDs.</summary>
public sealed record MapKind(FieldKind Key, FieldKind Value) : FieldKind
{
    public override string Describe() => $"object mapping {Key.Describe()} to {Value.Describe()}";
}

public sealed record ObjectKind(IReadOnlyList<Field> Fields) : FieldKind
{
    public override string Describe()
    {
        return "object { " + string.Join(", ", Fields.Select(field => field.Required ? field.Name : field.Name + "?")) + " }";
    }
}

/// <summary>
/// A stat or check modifier: <c>{ "stat": id, "value": expr }</c> or
/// <c>{ "check": ref, "value": expr }</c>. Its value may read <see cref="Roots"/>.
/// </summary>
public sealed record ModifierKind(Roots Roots = Roots.Self) : FieldKind
{
    public override string Describe()
    {
        string reads = "self" + (Roots.HasFlag(Roots.Class) ? ", class.level" : "") + (Roots.HasFlag(Roots.Condition) ? ", condition.<value>" : "");
        return $"modifier {{ \"stat\": stat ID, \"check\": check reference or \"track\": track reference (raises its maximum), \"value\": number expression (may read {reads}), \"against\"?: for a check, a boolean that may also read target; the modifier applies only when it holds }}";
    }
}

/// <summary>
/// An operation: <c>{ "op": name, ...its fields }</c>. Its expressions may read
/// <see cref="Roots"/>; inside a check's outcomes they may also read check.
/// </summary>
public sealed record OperationKind(Roots Roots) : FieldKind
{
    public override string Describe() => "operation { \"op\": name, ...fields } (see `goldbox schema operations`)";
}

/// <summary>
/// A use of an action: <c>{ "action": ref, "name"?: text, "from_item"?: item kind, ...parameter: expression }</c>.
/// Parameters the use doesn't give come from an equipped item of the from_item kind.
/// </summary>
public sealed record UseKind : FieldKind
{
    public override string Describe()
    {
        return "use { \"action\": action reference, \"name\"?: text, \"from_item\"?: item kind, <parameter>: number expression (may read self, target), ... }";
    }
}

/// <summary>Table rows: arrays of key values followed by the result value.</summary>
public sealed record TableRowsKind : FieldKind
{
    public override string Describe()
    {
        return "array of rows; each row is [key, ..., value]. Number keys are a whole number, \"a-b\" (inclusive range) or \"a+\" (a or more)";
    }
}

/// <summary>One field of a definition type or object.</summary>
public sealed record Field(string Name, FieldKind Kind, bool Required, string Description);
