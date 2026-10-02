using Rusty.Engine;

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

    /// <summary>Modules whose manifests have errors, or containers that don't open, so they can't be used.</summary>
    public IReadOnlyList<string> Unreadable => _unreadable;

    /// <param name="available">Every module source that may be required; the root is skipped.</param>
    /// <param name="searched">Where <paramref name="available"/> came from, for messages.</param>
    /// <param name="howToAdd">How to make a missing module available, for messages.</param>
    /// <param name="unreadable">Candidates that couldn't be opened at all, named in the same messages.</param>
    public static ModuleCatalog Read(IEnumerable<ModuleSource> available, ModuleManifest root, IReadOnlyList<string> searched, string howToAdd, IEnumerable<string> unreadable)
    {
        ModuleCatalog catalog = new(searched, howToAdd);
        catalog._unreadable.AddRange(unreadable);
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
    /// The modules in a set of search directories: each directory itself if
    /// it holds a <c>module.json</c>, each immediate subdirectory that does,
    /// and each installed container (<c>.rpak</c>) directly in it when
    /// <paramref name="content"/> is given. Containers open into <paramref name="opened"/>,
    /// which the caller disposes; one that doesn't open goes to
    /// <paramref name="unreadable"/>.
    /// </summary>
    /// <param name="root">The module being loaded, which is not opened again.</param>
    public static List<ModuleSource> Sources(
        IReadOnlyList<string> searchDirectories,
        string root,
        IContentService? content,
        List<ProductContentBundle> opened,
        List<string> unreadable,
        List<ModuleDiagnostic> diagnostics)
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

            // Without the content service containers can't be read; like other
            // modules nobody may require, they are not this load's problem.
            IEnumerable<string> containers = content is null ? [] : InstalledModules.In(searchDirectory, diagnostics).Where(path => path != root);
            foreach (string container in containers)
            {
                try
                {
                    ProductContentBundle bundle = ProductContentBundle.OpenContainer(content!, container);
                    opened.Add(bundle);
                    sources.Add(new BundleModuleSource(bundle));
                }
                catch (EngineCallException exception)
                {
                    unreadable.Add($"{container} ({exception.Message})");
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
