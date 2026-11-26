namespace RustyTales.Core.Definitions;

/// <summary>
/// A kind of definition a module file can declare with its <c>type</c> field:
/// its fields, which module kinds may define it, and a valid example.
/// </summary>
public sealed record DefinitionType(
    string Name,
    string Description,
    IReadOnlyList<Field> Fields,
    string Example)
{
    /// <summary>Fields every definition has, before the type's own fields.</summary>
    public static IReadOnlyList<Field> CommonFields { get; } =
    [
        new("type", new TextKind(), true, "The definition type, for example \"class\"."),
        new("id", new TextKind(), true, $"Local ID: {DefinitionIds.FormatDescription}. Other modules refer to it as \"module:id\"."),
    ];

    /// <summary>Optional inheritance field accepted on every non-patch definition.</summary>
    public static Field ExtendsField { get; } = new(
        "extends",
        new TextKind(),
        false,
        "Optional ID (\"id\" or \"module:id\") of another definition of the same type to inherit top-level fields from. Fields set here replace the parent's; set an optional field to null to remove it.");

    /// <summary>All fields accepted on a definition of <paramref name="type"/>.</summary>
    public static IReadOnlyList<Field> FieldsFor(DefinitionType type)
    {
        return type == DefinitionTypes.Patch
            ? [.. CommonFields, .. type.Fields]
            : [.. CommonFields, ExtendsField, .. type.Fields];
    }
}

public static class DefinitionIds
{
    public const string FormatDescription =
        "lowercase letters, digits and underscores, starting with a letter, for example \"fighter\" or \"save_spell\"";

    public static bool IsValid(string id)
    {
        if (id.Length == 0 || id[0] is < 'a' or > 'z')
        {
            return false;
        }

        return id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');
    }
}
