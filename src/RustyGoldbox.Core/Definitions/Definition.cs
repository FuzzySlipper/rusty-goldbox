using System.Text.Json;

namespace RustyGoldbox.Core.Definitions;

/// <summary>An expression found in a definition, checked once the whole module set is known.</summary>
public sealed record ExpressionSite(string JsonPath, string Text, ExpressionKind Kind);

/// <summary>A reference found in a definition: a <see cref="ReferenceKind"/> or a <see cref="StatKind"/>.</summary>
public sealed record ReferenceSite(string JsonPath, string Text, FieldKind Kind);

/// <summary>One definition read from a module file.</summary>
public sealed class Definition(
    DefinitionType type,
    string id,
    string module,
    string file,
    JsonElement json,
    IReadOnlyList<ExpressionSite> expressions,
    IReadOnlyList<ReferenceSite> references)
{
    public DefinitionType Type { get; } = type;

    public string Id { get; } = id;

    public string Module { get; } = module;

    public string File { get; } = file;

    /// <summary>The definition's JSON, checked against <see cref="Type"/>.</summary>
    public JsonElement Json { get; } = json;

    public IReadOnlyList<ExpressionSite> Expressions { get; } = expressions;

    public IReadOnlyList<ReferenceSite> References { get; } = references;

    public string QualifiedId => $"{Module}:{Id}";

    public string Name => Json.TryGetProperty("name", out JsonElement name) ? name.GetString()! : Id;
}
