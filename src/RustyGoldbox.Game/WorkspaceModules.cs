using Rusty.Engine;
using RustyGoldbox.Core.Authoring;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Game;

/// <summary>
/// Authoring workspaces the Game plays straight from source while you edit
/// them: the directories named by <c>$GOLDBOX_WORKSPACES</c> (separated like
/// <c>PATH</c>). Each title-screen listing builds them and repacks a module
/// whose content changed into the workspace's <c>.goldbox/game/</c>; every
/// module open then uses those containers in place of installed copies of
/// the same modules. Unset, the Game sees only its own and installed modules.
/// </summary>
internal sealed class WorkspaceModules
{
    public const string Variable = "GOLDBOX_WORKSPACES";

    private readonly List<string> _roots;

    public WorkspaceModules(IEnumerable<string> roots)
    {
        _roots = roots.Select(root => Path.GetFullPath(root)).ToList();
    }

    public static WorkspaceModules FromEnvironment()
    {
        string listed = Environment.GetEnvironmentVariable(Variable) ?? "";
        return new WorkspaceModules(listed.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// Builds each workspace and packs each authored module whose staged
    /// content differs from its packed container. Problems become title
    /// notes naming the workspace; a workspace that doesn't build keeps its
    /// last packed modules.
    /// </summary>
    public void Prepare(IContentService content, List<string> problems)
    {
        foreach (string root in _roots)
        {
            List<ModuleDiagnostic> found = [];
            Workspace? workspace = Workspace.Find(root, found);
            if (workspace is null || found.Count > 0)
            {
                problems.Add($"{Variable} names {root}, which isn't an authoring workspace: {Describe(found)}");
                continue;
            }

            WorkspaceBuildResult result = WorkspaceBuilder.Build(workspace, content);
            if (!result.IsValid)
            {
                problems.Add($"Workspace {root} doesn't build, so its last packed modules are used: {Describe(result.Diagnostics)} Run `goldbox workspace build {root}` for every error.");
                continue;
            }

            string packed = PackedDirectory(workspace.RootDirectory);
            Directory.CreateDirectory(packed);
            HashSet<string> current = [];
            foreach (WorkspaceBuiltModule module in result.Modules)
            {
                string target = Path.Combine(packed, InstalledModules.FileName(module.Manifest.Id, module.Manifest.Version));
                current.Add(target);
                if (!Matches(content, target, new DirectoryModuleSource(module.StagedDirectory).Identity))
                {
                    File.Delete(target);
                    ProductContentBundle.PackContainer(content, module.StagedDirectory, target, false);
                }
            }

            // A module the workspace no longer authors stops being offered.
            foreach (string stale in InstalledModules.In(packed, []).Where(path => !current.Contains(path)))
            {
                File.Delete(stale);
            }
        }
    }

    /// <summary>
    /// Adds each workspace's packed modules to <paramref name="modules"/>,
    /// disposing any other bundle that holds a module with the same ID, so
    /// the workspace copy is the one played.
    /// </summary>
    public void Open(IContentService content, List<ProductContentBundle> modules, List<string> problems)
    {
        if (_roots.Count == 0)
        {
            return;
        }

        List<ProductContentBundle> authored = [];
        foreach (string root in _roots)
        {
            InstalledModules.Open(content, PackedDirectory(root), authored, problems);
        }

        HashSet<string> ids = authored.Select(ModuleId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (ProductContentBundle replaced in modules.Where(bundle => ModuleId(bundle) is string id && ids.Contains(id)).ToList())
        {
            modules.Remove(replaced);
            replaced.Dispose();
        }

        modules.AddRange(authored);
    }

    private static string PackedDirectory(string root) => Path.Combine(root, ".goldbox", "game");

    private static bool Matches(IContentService content, string container, string identity)
    {
        if (!File.Exists(container))
        {
            return false;
        }

        try
        {
            using ProductContentBundle bundle = ProductContentBundle.OpenContainer(content, container);
            return new BundleModuleSource(bundle).Identity == identity;
        }
        catch (EngineCallException)
        {
            return false;
        }
    }

    private static string? ModuleId(ProductContentBundle bundle) => ManifestReader.Read(new BundleModuleSource(bundle), [])?.Id;

    private static string Describe(IEnumerable<ModuleDiagnostic> diagnostics)
    {
        ModuleDiagnostic[] all = diagnostics.ToArray();
        return all.Length == 0
            ? "no goldbox.json with an authoring section was found."
            : $"{all[0].Message}{(all.Length > 1 ? $" (and {all.Length - 1} more)" : "")}";
    }
}
