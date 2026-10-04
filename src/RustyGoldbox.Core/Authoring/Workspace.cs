using System.Text.Json;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Core.Authoring;

/// <summary>
/// The path of one entry in <c>goldbox.json</c>, retaining its source JSON
/// path for actionable diagnostics.
/// </summary>
public sealed record WorkspacePath(string Entry, string FullPath, string JsonPath);

/// <summary>
/// The optional authoring part of a workspace manifest. Module entries point
/// at runtime module source directories; staging and exports are generated
/// locations and are not module search paths.
/// </summary>
public sealed record AuthoringWorkspace(
    IReadOnlyList<WorkspacePath> ModulePaths,
    WorkspacePath Staging,
    WorkspacePath Exports)
{
    public IReadOnlyList<string> ModuleDirectories => ModulePaths.Select(path => path.FullPath).ToList();

    public string StagingDirectory => Staging.FullPath;

    public string ExportsDirectory => Exports.FullPath;
}

/// <summary>
/// The shared <c>goldbox.json</c> workspace contract. The top-level
/// <c>modules</c> list remains the dependency search path used by existing
/// module commands. <c>authoring</c> is optional so standalone modules and
/// modules-only workspace files remain valid.
/// </summary>
public sealed class Workspace
{
    public const string FileName = "goldbox.json";

    public static readonly IReadOnlyList<string> EditableDirectoryNames =
    [
        "canon",
        "art/references",
        "art/accepted",
        "art/rejected",
        "prompts",
        "scripts",
    ];

    private Workspace(
        string rootDirectory,
        string manifestPath,
        IReadOnlyList<WorkspacePath> modulePaths,
        AuthoringWorkspace? authoring)
    {
        RootDirectory = rootDirectory;
        ManifestPath = manifestPath;
        ModulePaths = modulePaths;
        Authoring = authoring;
    }

    public string RootDirectory { get; }

    public string ManifestPath { get; }

    /// <summary>Raw top-level entries and their resolved paths.</summary>
    public IReadOnlyList<WorkspacePath> ModulePaths { get; }

    /// <summary>The top-level module search directories.</summary>
    public IReadOnlyList<string> ModuleDirectories => ModulePaths.Select(path => path.FullPath).ToList();

    /// <summary>The optional authoring contract.</summary>
    public AuthoringWorkspace? Authoring { get; }

    /// <summary>Finds the nearest workspace manifest from a file or directory path.</summary>
    public static string? FindManifest(string start)
    {
        string fullStart = Path.GetFullPath(start);
        DirectoryInfo directory = File.Exists(fullStart)
            ? new FileInfo(fullStart).Directory!
            : new DirectoryInfo(fullStart);
        for (DirectoryInfo? current = directory; current is not null; current = current.Parent)
        {
            string candidate = Path.Combine(current.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Reads a workspace directory or its <c>goldbox.json</c>.</summary>
    public static Workspace? Read(string path, List<ModuleDiagnostic> diagnostics)
    {
        string manifestPath = GetManifestPath(path);
        if (!File.Exists(manifestPath))
        {
            diagnostics.Add(new ModuleDiagnostic(
                "workspace.not-found",
                $"No {FileName} was found at {manifestPath}. Run `goldbox workspace new <dir>` or pass a path inside an existing workspace.",
                File: manifestPath,
                JsonPath: "$"));
            return null;
        }

        using JsonDocument? document = JsonFiles.Parse(manifestPath, null, diagnostics);
        if (document is null)
        {
            return null;
        }

        JsonElement root = document.RootElement;
        const string Shape = "{ \"modules\": [\"modules\"], \"authoring\": { \"modules\": [\"modules/my-campaign\"], \"staging\": \".goldbox/staged\", \"exports\": \"exports\" } }";
        if (root.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "workspace.type",
                $"{FileName} must be an object like {Shape}.",
                File: manifestPath,
                JsonPath: "$"));
            return null;
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name is not ("modules" or "authoring"))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    "workspace.unknown-field",
                    $"'{property.Name}' is not a {FileName} field. Use \"modules\" and optional \"authoring\": {Shape}.",
                    File: manifestPath,
                    JsonPath: $"$.{property.Name}"));
            }
        }

        string rootDirectory = Path.GetDirectoryName(manifestPath)!;
        List<WorkspacePath> modulePaths = ReadPaths(
            root,
            "modules",
            manifestPath,
            rootDirectory,
            required: true,
            "workspace.modules",
            diagnostics);

        AuthoringWorkspace? authoring = null;
        if (root.TryGetProperty("authoring", out JsonElement authoringElement))
        {
            authoring = ReadAuthoring(authoringElement, manifestPath, rootDirectory, diagnostics);
        }

        return new Workspace(rootDirectory, manifestPath, modulePaths, authoring);
    }

    /// <summary>Reads the nearest workspace from a file or directory path.</summary>
    public static Workspace? Find(string start, List<ModuleDiagnostic> diagnostics)
    {
        string? manifest = FindManifest(start);
        if (manifest is null)
        {
            string fullStart = Path.GetFullPath(start);
            string searchRoot = File.Exists(fullStart)
                ? Path.GetDirectoryName(fullStart)!
                : fullStart;
            diagnostics.Add(new ModuleDiagnostic(
                "workspace.not-found",
                $"No {FileName} was found at or above {fullStart}. Run `goldbox workspace new <dir>` to create one.",
                File: Path.Combine(searchRoot, FileName),
                JsonPath: "$"));
            return null;
        }

        return Read(manifest, diagnostics);
    }

    public IReadOnlyList<WorkspaceDirectory> EditableDirectories()
    {
        return EditableDirectoryNames
            .Select(name => new WorkspaceDirectory(name, Path.GetFullPath(Path.Combine(RootDirectory, name)), Directory.Exists(Path.Combine(RootDirectory, name))))
            .ToList();
    }

    private static AuthoringWorkspace? ReadAuthoring(
        JsonElement authoring,
        string manifestPath,
        string rootDirectory,
        List<ModuleDiagnostic> diagnostics)
    {
        const string Shape = "{ \"modules\": [\"modules/my-campaign\"], \"staging\": \".goldbox/staged\", \"exports\": \"exports\" }";
        if (authoring.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "workspace.authoring.type",
                $"'authoring' must be an object like {Shape}.",
                File: manifestPath,
                JsonPath: "$.authoring"));
            return null;
        }

        foreach (JsonProperty property in authoring.EnumerateObject())
        {
            if (property.Name is not ("modules" or "staging" or "exports"))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    "workspace.authoring.unknown-field",
                    $"'{property.Name}' is not an authoring field. Use \"modules\", \"staging\" and \"exports\": {Shape}.",
                    File: manifestPath,
                    JsonPath: $"$.authoring.{property.Name}"));
            }
        }

        List<WorkspacePath> modulePaths = ReadPaths(
            authoring,
            "modules",
            manifestPath,
            rootDirectory,
            required: true,
            "workspace.authoring.modules",
            diagnostics,
            prefix: "$.authoring");
        WorkspacePath staging = ReadPath(
            authoring,
            "staging",
            manifestPath,
            rootDirectory,
            required: true,
            "workspace.authoring.staging",
            diagnostics,
            prefix: "$.authoring")
            ?? new WorkspacePath(".goldbox/staged", Path.GetFullPath(Path.Combine(rootDirectory, ".goldbox/staged")), "$.authoring.staging");
        WorkspacePath exports = ReadPath(
            authoring,
            "exports",
            manifestPath,
            rootDirectory,
            required: true,
            "workspace.authoring.exports",
            diagnostics,
            prefix: "$.authoring")
            ?? new WorkspacePath("exports", Path.GetFullPath(Path.Combine(rootDirectory, "exports")), "$.authoring.exports");
        return new AuthoringWorkspace(modulePaths, staging, exports);
    }

    private static List<WorkspacePath> ReadPaths(
        JsonElement parent,
        string propertyName,
        string manifestPath,
        string rootDirectory,
        bool required,
        string rule,
        List<ModuleDiagnostic> diagnostics,
        string prefix = "$")
    {
        string jsonPath = $"{prefix}.{propertyName}";
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            if (required)
            {
                diagnostics.Add(new ModuleDiagnostic(
                    rule,
                    $"{FileName} needs a \"{propertyName}\" array of directory path strings.",
                    File: manifestPath,
                    JsonPath: jsonPath));
            }

            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(new ModuleDiagnostic(
                rule,
                $"\"{propertyName}\" must be an array of directory path strings.",
                File: manifestPath,
                JsonPath: jsonPath));
            return [];
        }

        List<WorkspacePath> paths = [];
        int index = 0;
        foreach (JsonElement entry in value.EnumerateArray())
        {
            string at = $"{jsonPath}[{index}]";
            WorkspacePath? path = ReadPathValue(entry, at, manifestPath, rootDirectory, rule, diagnostics);
            if (path is not null)
            {
                paths.Add(path);
            }

            index++;
        }

        return paths;
    }

    private static WorkspacePath? ReadPath(
        JsonElement parent,
        string propertyName,
        string manifestPath,
        string rootDirectory,
        bool required,
        string rule,
        List<ModuleDiagnostic> diagnostics,
        string prefix)
    {
        string jsonPath = $"{prefix}.{propertyName}";
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            if (required)
            {
                diagnostics.Add(new ModuleDiagnostic(
                    rule,
                    $"{FileName} needs \"{propertyName}\" as a non-empty directory path string.",
                    File: manifestPath,
                    JsonPath: jsonPath));
            }

            return null;
        }

        return ReadPathValue(value, jsonPath, manifestPath, rootDirectory, rule, diagnostics);
    }

    private static WorkspacePath? ReadPathValue(
        JsonElement value,
        string jsonPath,
        string manifestPath,
        string rootDirectory,
        string rule,
        List<ModuleDiagnostic> diagnostics)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            diagnostics.Add(new ModuleDiagnostic(
                rule,
                "The value must be a non-empty directory path string relative to goldbox.json (or an absolute path).",
                File: manifestPath,
                JsonPath: jsonPath));
            return null;
        }

        string entry = value.GetString()!;
        try
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(rootDirectory, entry)));
            return new WorkspacePath(entry, fullPath, jsonPath);
        }
        catch (ArgumentException exception)
        {
            diagnostics.Add(new ModuleDiagnostic(
                rule,
                $"'{entry}' is not a valid directory path: {exception.Message}",
                File: manifestPath,
                JsonPath: jsonPath));
            return null;
        }
    }

    private static string GetManifestPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return File.Exists(fullPath) || string.Equals(Path.GetFileName(fullPath), FileName, StringComparison.Ordinal)
            ? fullPath
            : Path.Combine(fullPath, FileName);
    }
}

/// <summary>A conventional editable workspace directory and its presence.</summary>
public sealed record WorkspaceDirectory(string Name, string Path, bool Exists);
