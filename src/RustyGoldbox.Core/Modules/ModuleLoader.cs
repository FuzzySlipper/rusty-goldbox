using Rusty.Engine;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Modules;

/// <summary>One module a save was made under: its ID, version and content identity.</summary>
public sealed record SavedModule(string Id, string Version, string Identity);

/// <summary>Loads a module and everything it requires, checking all of it.</summary>
public static class ModuleLoader
{
    /// <param name="modulePath">The module directory, or an installed module container (<c>.rpak</c>), to load.</param>
    /// <param name="searchDirectories">Explicit <c>--modules</c> directories; may be empty.</param>
    /// <param name="content">
    /// The Engine content service, used inside a host callback to open module
    /// containers: the module path when it is one, and every <c>.rpak</c> in
    /// the search directories. Without it only module directories are searched.
    /// </param>
    /// <param name="extensions">IDs of extension modules to add to the set, found where requirements are.</param>
    /// <param name="saved">The modules a save was made under; each is loaded at exactly that version and content, even beside newer ones.</param>
    public static ModuleSet Load(string modulePath, IReadOnlyList<string> searchDirectories, IContentService? content = null, IReadOnlyList<string>? extensions = null, IReadOnlyList<SavedModule>? saved = null)
    {
        // "house/" and "house" are the same module; the parent lookup for
        // sibling search needs the form without the trailing separator.
        modulePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modulePath));
        List<ModuleDiagnostic> diagnostics = [];
        List<ProductContentBundle> opened = [];
        try
        {
            ModuleManifest? root = InstalledModules.IsContainer(modulePath)
                ? ReadContainer(modulePath, content, opened, diagnostics)
                : ManifestReader.Read(modulePath, diagnostics);
            List<string> directories = ModuleSearchPaths.Find(modulePath, searchDirectories, diagnostics);
            if (root is null)
            {
                return new ModuleSet(null, directories, [], null, diagnostics);
            }

            List<string> unreadable = [];
            List<ModuleSource> available = AsSaved(ModuleCatalog.Sources(directories, modulePath, content, opened, unreadable, diagnostics), saved);
            string howToAdd = "Add the directory that holds it (a module directory or an installed .rpak) with --modules <dir> or to the \"modules\" list in goldbox.json.";
            ModuleCatalog catalog = ModuleCatalog.Read(available, root, directories, howToAdd, unreadable);
            return Load(root, catalog, directories, extensions ?? [], diagnostics);
        }
        finally
        {
            // Loading copies what it reads; only identities outlive it, and those are copied too.
            foreach (ProductContentBundle bundle in opened)
            {
                bundle.Dispose();
            }
        }
    }

    /// <summary>
    /// <paramref name="available"/> without the other versions or copies of
    /// any module <paramref name="saved"/> names, when the exact one it was
    /// made under is there, so resolving picks it rather than the newest
    /// installed version. When it isn't there, every copy stays, and reading
    /// the save names what differs.
    /// </summary>
    public static List<ModuleSource> AsSaved(IEnumerable<ModuleSource> available, IReadOnlyList<SavedModule>? saved)
    {
        List<(ModuleSource Source, ModuleManifest? Manifest)> sources = available.Select(source => (source, ManifestReader.Read(source, []))).ToList();
        if (saved is null || saved.Count == 0)
        {
            return sources.Select(entry => entry.Source).ToList();
        }

        bool Exact((ModuleSource Source, ModuleManifest? Manifest) entry, SavedModule module) =>
            entry.Manifest is ModuleManifest manifest && manifest.Id == module.Id && manifest.Version.ToString() == module.Version && entry.Source.Identity == module.Identity;
        Dictionary<string, SavedModule> pinned = saved
            .Where(module => sources.Any(entry => Exact(entry, module)))
            .GroupBy(module => module.Id)
            .ToDictionary(group => group.Key, group => group.First());
        return sources
            .Where(entry => entry.Manifest is not ModuleManifest manifest || !pinned.TryGetValue(manifest.Id, out SavedModule? module) || Exact(entry, module))
            .Select(entry => entry.Source)
            .ToList();
    }

    private static ModuleManifest? ReadContainer(string path, IContentService? content, List<ProductContentBundle> opened, List<ModuleDiagnostic> diagnostics)
    {
        if (content is null)
        {
            diagnostics.Add(new ModuleDiagnostic("container.open", "Opening a module container needs the Engine content service; load it from inside a host callback.", File: path));
            return null;
        }

        try
        {
            ProductContentBundle bundle = ProductContentBundle.OpenContainer(content, path);
            opened.Add(bundle);
            return ManifestReader.Read(new BundleModuleSource(bundle), diagnostics);
        }
        catch (EngineCallException exception)
        {
            diagnostics.Add(new ModuleDiagnostic("container.open", $"Can't open the module container: {exception.Message} Pack it again with `goldbox module pack`.", File: path));
            return null;
        }
    }

    /// <summary>
    /// Loads <paramref name="root"/>, picking its requirements from
    /// <paramref name="available"/> (which may include the root itself).
    /// </summary>
    /// <param name="searched">Where the sources came from, named in messages about missing modules.</param>
    /// <param name="howToAdd">How to make a missing module available, for the same messages.</param>
    /// <param name="extensions">IDs of extension modules to add to the set from <paramref name="available"/>.</param>
    public static ModuleSet Load(ModuleSource root, IReadOnlyList<ModuleSource> available, IReadOnlyList<string> searched, string howToAdd, IReadOnlyList<string>? extensions = null)
    {
        List<ModuleDiagnostic> diagnostics = [];
        ModuleManifest? manifest = ManifestReader.Read(root, diagnostics);
        if (manifest is null)
        {
            return new ModuleSet(null, searched, [], null, diagnostics);
        }

        return Load(manifest, ModuleCatalog.Read(available, manifest, searched, howToAdd, []), searched, extensions ?? [], diagnostics);
    }

    private static ModuleSet Load(ModuleManifest root, ModuleCatalog catalog, IReadOnlyList<string> searched, IReadOnlyList<string> extensions, List<ModuleDiagnostic> diagnostics)
    {
        ModuleResolver resolver = new(catalog, diagnostics);
        List<LoadedModule> order = resolver.Resolve(root, extensions);
        List<Definition> definitions = [];
        foreach (LoadedModule loaded in order)
        {
            definitions.AddRange(DefinitionFiles.Read(loaded.Manifest, diagnostics));
        }

        // Cross-definition checks run once every file reads cleanly; otherwise
        // one broken file would surface again as every reference to it.
        RuleSet? rules = null;
        if (diagnostics.Count == 0)
        {
            rules = RuleSetBuilder.Build(order, definitions, diagnostics);
        }

        ModuleSet set = new(root, searched, order, rules, diagnostics) { Extensions = resolver.Added.Select(added => added.Id).ToList() };
        if (set.IsValid)
        {
            Characters.NpcFile.Check(set, diagnostics);
        }

        return set;
    }
}
