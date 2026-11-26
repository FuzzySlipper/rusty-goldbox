using System.Text.Json;

namespace RustyTales.Core.Definitions;

/// <summary>An expression found in a definition, checked once the whole module set is known.</summary>
public sealed record ExpressionSite(string JsonPath, string Text, ExpressionKind Kind, string? SourceModule = null);

/// <summary>A reference found in a definition: a <see cref="ReferenceKind"/> or a <see cref="StatKind"/>.</summary>
public sealed record ReferenceSite(string JsonPath, string Text, FieldKind Kind, string? SourceModule = null);

/// <summary>Where a field of a composed or lowered definition came from (its authoring module and diagnostic source).</summary>
public sealed record FieldOrigin(
    string Module,
    string File,
    string RootPath,
    string? FieldPrefix = null,
    bool AttributeErrorsToOrigin = false,
    string? ExactPath = null)
{
    public string FormatPath(string localPath)
    {
        if (ExactPath is not null)
        {
            return Definition.CombinePath(RootPath, ExactPath);
        }

        string withPrefix = FieldPrefix is null
            ? localPath
            : localPath == "$"
                ? FieldPrefix
                : localPath.StartsWith('$')
                    ? $"{FieldPrefix}{localPath[1..]}"
                    : localPath;
        return Definition.CombinePath(RootPath, withPrefix);
    }
}

/// <summary>One definition read from a module file.</summary>
public sealed class Definition(
    DefinitionType type,
    string id,
    string module,
    string file,
    JsonElement json,
    IReadOnlyList<ExpressionSite> expressions,
    IReadOnlyList<ReferenceSite> references,
    string rootPath = "$",
    IReadOnlyDictionary<string, FieldOrigin>? fieldOrigins = null)
{
    public DefinitionType Type { get; } = type;

    public string Id { get; } = id;

    public string Module { get; } = module;

    public string File { get; } = file;

    /// <summary>Location of this definition inside <see cref="File"/>: <c>$</c> or <c>$[i]</c>.</summary>
    public string RootPath { get; } = rootPath;

    /// <summary>The definition's JSON, checked against <see cref="Type"/>.</summary>
    public JsonElement Json { get; } = json;

    public IReadOnlyList<ExpressionSite> Expressions { get; } = expressions;

    public IReadOnlyList<ReferenceSite> References { get; } = references;

    /// <summary>Per-field authoring provenance when composed via <c>extends</c> or <c>patch</c>.</summary>
    public IReadOnlyDictionary<string, FieldOrigin>? FieldOrigins { get; } = fieldOrigins;

    public string QualifiedId => $"{Module}:{Id}";

    public string Name => Json.TryGetProperty("name", out JsonElement name) ? name.GetString()! : Id;

    public string ModuleOf(string topLevelField) =>
        FieldOrigins is not null && FieldOrigins.TryGetValue(topLevelField, out FieldOrigin? origin)
            ? origin.Module
            : Module;

    public string DiagnosticPath(string localPath) => CombinePath(RootPath, localPath);

    public (string Module, string File, string JsonPath) DiagnosticLocation(string localPath)
    {
        if (FindOrigin(FieldOrigins, localPath) is FieldOrigin origin && origin.AttributeErrorsToOrigin)
        {
            return (origin.Module, origin.File, origin.FormatPath(localPath));
        }

        return (Module, File, DiagnosticPath(localPath));
    }

    internal static FieldOrigin? FindOrigin(IReadOnlyDictionary<string, FieldOrigin>? origins, string localPath)
    {
        if (origins is null)
        {
            return null;
        }

        if (origins.TryGetValue(localPath, out FieldOrigin? exact))
        {
            return exact;
        }

        if (TopLevelField(localPath) is string field && origins.TryGetValue(field, out FieldOrigin? top))
        {
            return top;
        }

        return origins.TryGetValue("$", out FieldOrigin? root) ? root : null;
    }

    internal static string? TopLevelField(string localPath)
    {
        if (!localPath.StartsWith("$.", StringComparison.Ordinal) || localPath.Length <= 2)
        {
            return null;
        }

        int end = 2;
        while (end < localPath.Length && localPath[end] != '.' && localPath[end] != '[')
        {
            end++;
        }

        return localPath[2..end];
    }

    internal static string CombinePath(string rootPath, string localPath)
    {
        if (rootPath == "$" || !localPath.StartsWith('$'))
        {
            return localPath;
        }

        return $"{rootPath}{localPath[1..]}";
    }
}
