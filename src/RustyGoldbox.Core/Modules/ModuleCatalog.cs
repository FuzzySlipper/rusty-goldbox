namespace RustyGoldbox.Core.Modules;

/// <summary>
/// The modules available in a set of search directories: each directory
/// itself if it holds a <c>module.json</c>, and each of its immediate
/// subdirectories that does.
/// </summary>
internal sealed class ModuleCatalog
{
    private readonly Dictionary<string, List<ModuleManifest>> _byId = [];
    private readonly List<string> _unreadable = [];

    private ModuleCatalog(IReadOnlyList<string> searchDirectories)
    {
        SearchDirectories = searchDirectories;
    }

    public IReadOnlyList<string> SearchDirectories { get; }

    /// <summary>Module directories whose manifests have errors, so they can't be used.</summary>
    public IReadOnlyList<string> Unreadable => _unreadable;

    public static ModuleCatalog Scan(IReadOnlyList<string> searchDirectories, string excludedDirectory, List<ModuleDiagnostic> diagnostics)
    {
        ModuleCatalog catalog = new(searchDirectories);
        HashSet<string> seen = [excludedDirectory];
        foreach (string searchDirectory in searchDirectories)
        {
            catalog.TryAdd(searchDirectory, seen);
            foreach (string child in DirectoryListing.List(searchDirectory, directories: true, null, diagnostics) ?? [])
            {
                catalog.TryAdd(child, seen);
            }
        }

        return catalog;
    }

    public IReadOnlyList<ModuleManifest> Find(string id)
    {
        return _byId.TryGetValue(id, out List<ModuleManifest>? candidates) ? candidates : [];
    }

    private void TryAdd(string directory, HashSet<string> seen)
    {
        if (!File.Exists(Path.Combine(directory, ManifestReader.FileName)) || !seen.Add(directory))
        {
            return;
        }

        // Problems in modules nobody requires are not this load's problems;
        // they are reported when that module is validated itself.
        List<ModuleDiagnostic> ignored = [];
        ModuleManifest? manifest = ManifestReader.Read(directory, ignored);
        if (manifest is null)
        {
            _unreadable.Add(directory);
            return;
        }

        if (!_byId.TryGetValue(manifest.Id, out List<ModuleManifest>? candidates))
        {
            candidates = [];
            _byId[manifest.Id] = candidates;
        }

        candidates.Add(manifest);
    }
}
