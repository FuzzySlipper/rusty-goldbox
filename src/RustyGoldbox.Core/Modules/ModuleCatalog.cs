namespace RustyGoldbox.Core.Modules;

/// <summary>The modules a load can pick requirements from, by ID.</summary>
internal sealed class ModuleCatalog
{
    private readonly Dictionary<string, List<ModuleManifest>> _byId = [];
    private readonly List<string> _unreadable = [];

    private ModuleCatalog(IReadOnlyList<string> searched, string howToAdd)
    {
        Searched = searched;
        HowToAdd = howToAdd;
    }

    /// <summary>Where modules were looked for, for messages.</summary>
    public IReadOnlyList<string> Searched { get; }

    /// <summary>How to make a missing module available, for messages.</summary>
    public string HowToAdd { get; }

    /// <summary>Modules whose manifests have errors, so they can't be used.</summary>
    public IReadOnlyList<string> Unreadable => _unreadable;

    /// <param name="available">Every module source that may be required; the root is skipped.</param>
    /// <param name="searched">Where <paramref name="available"/> came from, for messages.</param>
    /// <param name="howToAdd">How to make a missing module available, for messages.</param>
    public static ModuleCatalog Read(IEnumerable<ModuleSource> available, ModuleManifest root, IReadOnlyList<string> searched, string howToAdd)
    {
        ModuleCatalog catalog = new(searched, howToAdd);
        HashSet<string> seen = [root.Source.Location];
        foreach (ModuleSource source in available)
        {
            if (seen.Add(source.Location))
            {
                catalog.Add(source);
            }
        }

        return catalog;
    }

    /// <summary>
    /// The module directories in a set of search directories: each directory
    /// itself if it holds a <c>module.json</c>, and each of its immediate
    /// subdirectories that does.
    /// </summary>
    public static List<ModuleSource> Directories(IReadOnlyList<string> searchDirectories, List<ModuleDiagnostic> diagnostics)
    {
        List<ModuleSource> sources = [];
        foreach (string searchDirectory in searchDirectories)
        {
            IEnumerable<string> children = DirectoryListing.List(searchDirectory, directories: true, null, diagnostics) ?? [];
            foreach (string directory in children.Prepend(searchDirectory))
            {
                if (File.Exists(Path.Combine(directory, ManifestReader.FileName)))
                {
                    sources.Add(new DirectoryModuleSource(directory));
                }
            }
        }

        return sources;
    }

    public IReadOnlyList<ModuleManifest> Find(string id)
    {
        return _byId.TryGetValue(id, out List<ModuleManifest>? candidates) ? candidates : [];
    }

    private void Add(ModuleSource source)
    {
        // Problems in modules nobody requires are not this load's problems;
        // they are reported when that module is validated itself.
        List<ModuleDiagnostic> ignored = [];
        ModuleManifest? manifest = ManifestReader.Read(source, ignored);
        if (manifest is null)
        {
            _unreadable.Add(source.Location);
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
