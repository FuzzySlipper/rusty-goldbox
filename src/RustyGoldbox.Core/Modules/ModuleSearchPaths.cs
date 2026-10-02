using System.Text.Json;

namespace RustyGoldbox.Core.Modules;

/// <summary>
/// Finds the directories that hold a module's dependencies: explicit
/// <c>--modules</c> directories, then the <c>modules</c> list of the nearest
/// <c>goldbox.json</c> workspace file. With neither, the module's parent
/// directory (its siblings) is searched.
/// </summary>
public static class ModuleSearchPaths
{
    public const string WorkspaceFileName = "goldbox.json";

    public static List<string> Find(string moduleDirectory, IReadOnlyList<string> explicitDirectories, List<ModuleDiagnostic> diagnostics)
    {
        List<string> directories = [];
        foreach (string directory in explicitDirectories)
        {
            Add(directories, Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), null, null, diagnostics);
        }

        string start = Path.GetFullPath(moduleDirectory);
        string? workspace = FindWorkspaceFile(start);
        if (workspace is not null)
        {
            ReadWorkspace(workspace, directories, diagnostics);
        }
        else if (explicitDirectories.Count == 0)
        {
            string? parent = Path.GetDirectoryName(start);
            if (parent is not null)
            {
                Add(directories, parent, null, null, diagnostics);
            }
        }

        return directories;
    }

    /// <summary>
    /// Where <c>module new</c> puts a module by default: the first
    /// <c>modules</c> directory of the nearest <c>goldbox.json</c>, or the
    /// working directory when there is none.
    /// </summary>
    public static string DefaultNewModuleParent(string workingDirectory, List<ModuleDiagnostic> diagnostics)
    {
        string? workspace = FindWorkspaceFile(Path.GetFullPath(workingDirectory));
        if (workspace is null)
        {
            return workingDirectory;
        }

        List<string> directories = [];
        ReadWorkspace(workspace, directories, diagnostics);
        return directories.Count > 0 ? directories[0] : workingDirectory;
    }

    private static string? FindWorkspaceFile(string start)
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, WorkspaceFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void ReadWorkspace(string path, List<string> directories, List<ModuleDiagnostic> diagnostics)
    {
        using JsonDocument? document = JsonFiles.Parse(path, null, diagnostics);
        if (document is null)
        {
            return;
        }

        const string Shape = "{ \"modules\": [\"modules\"] } (directories relative to goldbox.json)";
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(new ModuleDiagnostic("workspace.type", $"goldbox.json must be an object like {Shape}.", File: path, JsonPath: "$"));
            return;
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name != "modules")
            {
                diagnostics.Add(new ModuleDiagnostic(
                    "workspace.unknown-field",
                    $"'{property.Name}' is not a goldbox.json field. The only field is \"modules\": {Shape}.",
                    File: path,
                    JsonPath: $"$.{property.Name}"));
            }
        }

        if (!root.TryGetProperty("modules", out JsonElement modules) || modules.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(new ModuleDiagnostic("workspace.modules", $"goldbox.json needs a \"modules\" array: {Shape}.", File: path, JsonPath: "$.modules"));
            return;
        }

        string baseDirectory = Path.GetDirectoryName(path)!;
        int index = 0;
        foreach (JsonElement entry in modules.EnumerateArray())
        {
            string at = $"$.modules[{index}]";
            if (entry.ValueKind != JsonValueKind.String || entry.GetString()!.Length == 0)
            {
                diagnostics.Add(new ModuleDiagnostic("workspace.modules", $"Each \"modules\" entry must be a directory path string: {Shape}.", File: path, JsonPath: at));
            }
            else
            {
                string directory = Path.GetFullPath(Path.Combine(baseDirectory, entry.GetString()!));
                Add(directories, Path.TrimEndingDirectorySeparator(directory), path, at, diagnostics);
            }

            index++;
        }
    }

    private static void Add(List<string> directories, string directory, string? source, string? jsonPath, List<ModuleDiagnostic> diagnostics)
    {
        if (!Directory.Exists(directory))
        {
            string from = source is null ? "--modules" : WorkspaceFileName;
            diagnostics.Add(new ModuleDiagnostic(
                "search.not-found",
                $"The module search directory {directory} (from {from}) does not exist. Fix the path or remove it.",
                File: source,
                JsonPath: jsonPath));
            return;
        }

        if (!directories.Contains(directory))
        {
            directories.Add(directory);
        }
    }
}
