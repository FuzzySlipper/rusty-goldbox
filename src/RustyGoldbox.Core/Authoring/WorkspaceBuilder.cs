using Rusty.Engine;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Core.Authoring;

/// <summary>A module dependency that a workspace build reported.</summary>
/// <param name="RequiredBy">The module whose manifest named the dependency.</param>
/// <param name="Id">The required module ID, when it could be identified.</param>
/// <param name="Range">The required version range, when it could be identified.</param>
/// <param name="ResolvedVersion">The version selected by the loader, or null for an unresolved dependency.</param>
public sealed record WorkspaceDependency(
    string? RequiredBy,
    string? Id,
    string? Range,
    string? ResolvedVersion,
    string Rule,
    string? JsonPath,
    string Message);

/// <summary>One authored module copied into the clean staging tree.</summary>
public sealed record WorkspaceBuiltModule(
    ModuleManifest Manifest,
    string SourceDirectory,
    string StagedDirectory,
    IReadOnlyList<string> IncludedFiles,
    IReadOnlyList<WorkspaceDependency> Dependencies);

/// <summary>The result of validating authored modules and preparing their runtime files.</summary>
public sealed record WorkspaceBuildResult(
    Workspace Workspace,
    IReadOnlyList<WorkspaceBuiltModule> Modules,
    IReadOnlyList<WorkspaceDependency> UnresolvedDependencies,
    IReadOnlyList<ModuleDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;
}

/// <summary>
/// Validates the explicit runtime module sources in an authoring workspace and
/// copies them to a clean staging tree. Authoring roots are deliberately outside
/// this operation: only files under an entry in <c>authoring.modules</c> become
/// runtime content, including the licence and provenance files kept with a module.
/// </summary>
public static class WorkspaceBuilder
{
    private const string PathOverlapRule = "workspace.build.path-overlap";

    /// <summary>
    /// Validates all sources before cleaning staging. The optional Engine content
    /// service lets the normal module loader resolve installed containers as
    /// dependencies while the CLI runs inside its tool host.
    /// </summary>
    public static WorkspaceBuildResult Build(Workspace workspace, IContentService? content = null)
    {
        List<ModuleDiagnostic> diagnostics = [];
        List<WorkspaceDependency> unresolved = [];
        List<WorkspaceBuiltModule> modules = [];

        if (workspace.Authoring is not AuthoringWorkspace authoring)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "workspace.authoring.required",
                $"{Workspace.FileName} has no authoring object. Add authoring.modules, authoring.staging and authoring.exports before running workspace build.",
                File: workspace.ManifestPath,
                JsonPath: "$.authoring"));
            return new WorkspaceBuildResult(workspace, modules, unresolved, diagnostics);
        }

        ValidateGeneratedRoots(workspace, authoring, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new WorkspaceBuildResult(workspace, modules, unresolved, diagnostics);
        }

        WorkspaceInspection inspection = WorkspaceInspector.Inspect(workspace, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new WorkspaceBuildResult(workspace, modules, unresolved, diagnostics);
        }

        Dictionary<string, WorkspaceModule> byId = new(StringComparer.Ordinal);
        foreach ((WorkspaceModule authored, int index) in inspection.AuthoredModules.Select((module, index) => (module, index)))
        {
            if (authored.Manifest is null)
            {
                continue;
            }

            if (!byId.TryAdd(authored.Manifest.Id, authored))
            {
                WorkspaceModule first = byId[authored.Manifest.Id];
                diagnostics.Add(new ModuleDiagnostic(
                    "workspace.build.duplicate-module",
                    $"Authored module ID '{authored.Manifest.Id}' is listed more than once ({first.Path} and {authored.Path}). Give each staged module a distinct ID or remove the duplicate entry.",
                    Module: authored.Manifest.Id,
                    File: workspace.ManifestPath,
                    JsonPath: $"$.authoring.modules[{index}]"));
            }
        }

        if (diagnostics.Count > 0)
        {
            return new WorkspaceBuildResult(workspace, modules, unresolved, diagnostics);
        }

        // ModuleLoader adds the workspace's top-level modules list itself. The
        // explicit authoring paths are the additional catalog needed when one
        // authored module requires another authored module.
        IReadOnlyList<string> authoredDirectories = authoring.ModuleDirectories;
        List<(WorkspaceModule Module, ModuleSet Set)> validated = [];
        foreach (WorkspaceModule authored in inspection.AuthoredModules)
        {
            ModuleManifest manifest = authored.Manifest!;
            ModuleSet set = ModuleLoader.Load(authored.Path, authoredDirectories, content);
            if (!set.IsValid)
            {
                diagnostics.AddRange(set.Diagnostics);
                unresolved.AddRange(ResolveDependencies(set));
                continue;
            }

            validated.Add((authored, set));
        }

        if (diagnostics.Count > 0)
        {
            return new WorkspaceBuildResult(workspace, modules, unresolved, diagnostics);
        }

        if (!PrepareStagingRoot(authoring.StagingDirectory, workspace.ManifestPath, diagnostics))
        {
            return new WorkspaceBuildResult(workspace, modules, unresolved, diagnostics);
        }

        try
        {
            foreach ((WorkspaceModule authored, ModuleSet set) in validated)
            {
                ModuleManifest manifest = authored.Manifest!;
                string staged = Path.Combine(authoring.StagingDirectory, manifest.Id);
                IReadOnlyList<string> included = CopyModule(authored.Path, staged);
                modules.Add(new WorkspaceBuiltModule(
                    manifest,
                    authored.Path,
                    staged,
                    included,
                    ResolvedDependencies(manifest, set)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "workspace.build.staging",
                $"Can't prepare generated staging content: {exception.Message} Source modules and editable roots were left in place; fix the staging directory and run workspace build again.",
                File: workspace.ManifestPath,
                JsonPath: "$.authoring.staging"));
            modules.Clear();
        }

        return new WorkspaceBuildResult(workspace, modules, unresolved, diagnostics);
    }

    private static void ValidateGeneratedRoots(Workspace workspace, AuthoringWorkspace authoring, List<ModuleDiagnostic> diagnostics)
    {
        List<(string Label, string Path, string JsonPath)> protectedRoots = [];
        protectedRoots.AddRange(workspace.ModulePaths.Select(path => ("module search directory", path.FullPath, path.JsonPath)));
        protectedRoots.AddRange(authoring.ModulePaths.Select(path => ("authored module source", path.FullPath, path.JsonPath)));
        protectedRoots.AddRange(workspace.EditableDirectories().Select(directory => ("editable root", directory.Path, $"$.authoring.{directory.Name.Replace('/', '.')}")));

        CheckAgainst(authoring.Staging, protectedRoots, workspace, diagnostics);
        CheckAgainst(authoring.Exports, protectedRoots, workspace, diagnostics);
        if (Overlaps(authoring.Staging.FullPath, authoring.Exports.FullPath))
        {
            diagnostics.Add(new ModuleDiagnostic(
                PathOverlapRule,
                $"Generated staging {authoring.Staging.FullPath} and exports {authoring.Exports.FullPath} overlap. Give authoring.staging and authoring.exports separate directories before either generated root is cleaned.",
                File: workspace.ManifestPath,
                JsonPath: "$.authoring.exports"));
        }
    }

    private static void CheckAgainst(
        WorkspacePath generated,
        IReadOnlyList<(string Label, string Path, string JsonPath)> protectedRoots,
        Workspace workspace,
        List<ModuleDiagnostic> diagnostics)
    {
        foreach ((string label, string path, string jsonPath) in protectedRoots)
        {
            if (!Overlaps(generated.FullPath, path))
            {
                continue;
            }

            diagnostics.Add(new ModuleDiagnostic(
                PathOverlapRule,
                $"Generated directory {generated.FullPath} overlaps or contains the {label} {path}. Move {generated.Entry} outside protected module and editable roots before the build can clean it.",
                File: workspace.ManifestPath,
                JsonPath: generated.JsonPath));
            break;
        }
    }

    private static bool Overlaps(string left, string right)
    {
        left = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        right = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return IsSameOrChild(left, right, comparison) || IsSameOrChild(right, left, comparison);
    }

    private static bool IsSameOrChild(string parent, string candidate, StringComparison comparison)
    {
        if (string.Equals(parent, candidate, comparison))
        {
            return true;
        }

        string prefix = parent.EndsWith(Path.DirectorySeparatorChar)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }

    private static bool PrepareStagingRoot(string staging, string manifestPath, List<ModuleDiagnostic> diagnostics)
    {
        try
        {
            if (File.Exists(staging))
            {
                diagnostics.Add(new ModuleDiagnostic(
                    "workspace.build.staging",
                    $"The staging output {staging} is a file. Move it or remove it so the generated staging directory can be rebuilt.",
                    File: manifestPath,
                    JsonPath: "$.authoring.staging"));
                return false;
            }

            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ModuleDiagnostic(
                "workspace.build.staging",
                $"Can't clean or create generated staging directory {staging}: {exception.Message} Fix its permissions or choose another authoring.staging path.",
                File: manifestPath,
                JsonPath: "$.authoring.staging"));
            return false;
        }
    }

    private static IReadOnlyList<string> CopyModule(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        List<string> included = [];
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            included.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
        }

        return included;
    }

    private static IReadOnlyList<WorkspaceDependency> ResolvedDependencies(ModuleManifest root, ModuleSet set)
    {
        LoadedModule? loaded = set.LoadOrder.FirstOrDefault(module => module.Manifest.Id == root.Id);
        return loaded is null
            ? []
            : loaded.Requires.Select(requirement => new WorkspaceDependency(
                root.Id,
                requirement.Id,
                requirement.Range.Text,
                requirement.Version.ToString(),
                "resolve.ok",
                null,
                $"Resolved to {requirement.Id} {requirement.Version}."))
                .ToList();
    }

    private static IReadOnlyList<WorkspaceDependency> ResolveDependencies(ModuleSet set)
    {
        List<WorkspaceDependency> dependencies = [];
        foreach (ModuleDiagnostic diagnostic in set.Diagnostics.Where(diagnostic => diagnostic.Rule.StartsWith("resolve.", StringComparison.Ordinal)))
        {
            ModuleManifest? owner = OwningManifest(set, diagnostic);
            ModuleRequirement? requirement = owner is null ? null : RequirementAt(owner, diagnostic.JsonPath);
            dependencies.Add(new WorkspaceDependency(
                owner?.Id ?? set.Root?.Id,
                requirement?.Id,
                requirement?.Range.Text,
                null,
                diagnostic.Rule,
                diagnostic.JsonPath,
                diagnostic.Message));
        }

        return dependencies;
    }

    private static ModuleManifest? OwningManifest(ModuleSet set, ModuleDiagnostic diagnostic)
    {
        if (diagnostic.Module is string module)
        {
            ModuleManifest? byId = set.LoadOrder
                .Select(loaded => loaded.Manifest)
                .FirstOrDefault(manifest => manifest.Id == module);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (diagnostic.File is string file)
        {
            string fullFile = Path.GetFullPath(file);
            ModuleManifest? byFile = set.LoadOrder
                .Select(loaded => loaded.Manifest)
                .FirstOrDefault(manifest => string.Equals(Path.GetFullPath(manifest.ManifestPath), fullFile, StringComparison.OrdinalIgnoreCase));
            if (byFile is not null)
            {
                return byFile;
            }
        }

        return set.Root;
    }

    private static ModuleRequirement? RequirementAt(ModuleManifest root, string? jsonPath)
    {
        if (jsonPath is null)
        {
            return null;
        }

        const string Prefix = "$.requires[";
        if (!jsonPath.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        int end = jsonPath.IndexOf(']', Prefix.Length);
        return end < 0 || !int.TryParse(jsonPath.AsSpan(Prefix.Length, end - Prefix.Length), out int index)
            ? null
            : root.Requires.FirstOrDefault(requirement => requirement.Index == index);
    }
}
