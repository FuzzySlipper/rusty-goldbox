using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Core.Authoring;

/// <summary>A runtime module source listed by an author's workspace.</summary>
public sealed record WorkspaceModule(string Entry, string Path, ModuleManifest? Manifest)
{
    public string? Id => Manifest?.Id;

    public string? Kind => Manifest is null ? null : ModuleKinds.Name(Manifest.Kind);

    public string? Version => Manifest?.Version.ToString();
}

/// <summary>The discoverable contents of an authoring workspace.</summary>
public sealed record WorkspaceInspection(
    Workspace Workspace,
    IReadOnlyList<WorkspaceModule> AuthoredModules,
    IReadOnlyList<WorkspaceDirectory> EditableDirectories);

/// <summary>Reads a workspace and discovers its authored module manifests.</summary>
public static class WorkspaceInspector
{
    public static WorkspaceInspection? Read(string path, List<ModuleDiagnostic> diagnostics)
    {
        Workspace? workspace = Workspace.Find(path, diagnostics);
        return workspace is null ? null : Inspect(workspace, diagnostics);
    }

    public static WorkspaceInspection Inspect(Workspace workspace, List<ModuleDiagnostic> diagnostics)
    {
        foreach (WorkspacePath module in workspace.ModulePaths)
        {
            if (!Directory.Exists(module.FullPath))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    "search.not-found",
                    $"The module search directory {module.FullPath} (from {Workspace.FileName}) does not exist. Fix the path or remove it.",
                    File: workspace.ManifestPath,
                    JsonPath: module.JsonPath));
            }
        }

        List<WorkspaceModule> authored = [];
        if (workspace.Authoring is not null)
        {
            foreach (WorkspacePath module in workspace.Authoring.ModulePaths)
            {
                if (!Directory.Exists(module.FullPath))
                {
                    diagnostics.Add(new ModuleDiagnostic(
                        "workspace.authoring.module-not-found",
                        $"The authored module directory {module.FullPath} (from {Workspace.FileName}) does not exist. Add its module source or remove it from authoring.modules.",
                        File: workspace.ManifestPath,
                        JsonPath: module.JsonPath));
                    authored.Add(new WorkspaceModule(module.Entry, module.FullPath, null));
                    continue;
                }

                string manifest = Path.Combine(module.FullPath, ManifestReader.FileName);
                if (!File.Exists(manifest))
                {
                    diagnostics.Add(new ModuleDiagnostic(
                        "workspace.authoring.module",
                        $"Authored module path {module.FullPath} has no {ManifestReader.FileName}. Point authoring.modules at each runtime module directory; keep canon, prompts and source art outside it.",
                        File: workspace.ManifestPath,
                        JsonPath: module.JsonPath));
                    authored.Add(new WorkspaceModule(module.Entry, module.FullPath, null));
                    continue;
                }

                authored.Add(new WorkspaceModule(module.Entry, module.FullPath, ManifestReader.Read(module.FullPath, diagnostics)));
            }
        }

        return new WorkspaceInspection(workspace, authored, workspace.EditableDirectories());
    }
}
