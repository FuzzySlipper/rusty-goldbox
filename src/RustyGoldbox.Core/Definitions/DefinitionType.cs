namespace RustyGoldbox.Core.Definitions;

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
