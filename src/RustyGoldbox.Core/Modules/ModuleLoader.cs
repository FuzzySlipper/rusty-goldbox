using Rusty.Engine;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Modules;

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
    public static ModuleSet Load(string modulePath, IReadOnlyList<string> searchDirectories, IContentService? content = null)
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
            List<ModuleSource> available = ModuleCatalog.Sources(directories, modulePath, content, opened, unreadable, diagnostics);
            string howToAdd = "Add the directory that holds it (a module directory or an installed .rpak) with --modules <dir> or to the \"modules\" list in goldbox.json.";
            ModuleCatalog catalog = ModuleCatalog.Read(available, root, directories, howToAdd, unreadable);
            return Load(root, catalog, directories, diagnostics);
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
    public static ModuleSet Load(ModuleSource root, IReadOnlyList<ModuleSource> available, IReadOnlyList<string> searched, string howToAdd)
    {
        List<ModuleDiagnostic> diagnostics = [];
        ModuleManifest? manifest = ManifestReader.Read(root, diagnostics);
        if (manifest is null)
        {
            return new ModuleSet(null, searched, [], null, diagnostics);
        }

        return Load(manifest, ModuleCatalog.Read(available, manifest, searched, howToAdd, []), searched, diagnostics);
    }

    private static ModuleSet Load(ModuleManifest root, ModuleCatalog catalog, IReadOnlyList<string> searched, List<ModuleDiagnostic> diagnostics)
    {
        List<LoadedModule> order = new ModuleResolver(catalog, diagnostics).Resolve(root);
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

        return new ModuleSet(root, searched, order, rules, diagnostics);
    }
}
