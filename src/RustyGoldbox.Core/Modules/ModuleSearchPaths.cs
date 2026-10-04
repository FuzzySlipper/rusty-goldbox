using RustyGoldbox.Core.Authoring;

namespace RustyGoldbox.Core.Modules;

/// <summary>
/// Finds the directories that hold a module's dependencies: explicit
/// <c>--modules</c> directories, then the <c>modules</c> list of the nearest
/// <c>goldbox.json</c> workspace file. With neither, the module's parent
/// directory (its siblings) is searched.
/// </summary>
public static class ModuleSearchPaths
{
    public const string WorkspaceFileName = Workspace.FileName;

    public static List<string> Find(string moduleDirectory, IReadOnlyList<string> explicitDirectories, List<ModuleDiagnostic> diagnostics)
    {
        List<string> directories = [];
        foreach (string directory in explicitDirectories)
        {
            Add(directories, Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), null, null, diagnostics);
        }

        string start = Path.GetFullPath(moduleDirectory);
        string? workspacePath = Workspace.FindManifest(start);
        if (workspacePath is not null)
        {
            Workspace? workspace = Workspace.Read(workspacePath, diagnostics);
            if (workspace is not null)
            {
                foreach (WorkspacePath module in workspace.ModulePaths)
                {
                    Add(directories, module.FullPath, workspacePath, module.JsonPath, diagnostics);
                }
            }
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
        string? workspacePath = Workspace.FindManifest(Path.GetFullPath(workingDirectory));
        if (workspacePath is null)
        {
            return workingDirectory;
        }

        Workspace? workspace = Workspace.Read(workspacePath, diagnostics);
        if (workspace is null)
        {
            return workingDirectory;
        }

        List<string> directories = [];
        foreach (WorkspacePath module in workspace.ModulePaths)
        {
            Add(directories, module.FullPath, workspacePath, module.JsonPath, diagnostics);
        }

        return directories.Count > 0 ? directories[0] : workingDirectory;
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
