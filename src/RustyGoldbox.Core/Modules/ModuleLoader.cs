using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Modules;

/// <summary>Loads a module and everything it requires, checking all of it.</summary>
public static class ModuleLoader
{
    /// <param name="modulePath">The module directory to load.</param>
    /// <param name="searchDirectories">Explicit <c>--modules</c> directories; may be empty.</param>
    public static ModuleSet Load(string modulePath, IReadOnlyList<string> searchDirectories)
    {
        // "house/" and "house" are the same module; the parent lookup for
        // sibling search needs the form without the trailing separator.
        modulePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modulePath));
        List<ModuleDiagnostic> diagnostics = [];
        ModuleManifest? root = ManifestReader.Read(modulePath, diagnostics);
        List<string> directories = ModuleSearchPaths.Find(modulePath, searchDirectories, diagnostics);
        if (root is null)
        {
            return new ModuleSet(null, directories, [], null, diagnostics);
        }

        string howToAdd = "Add the directory that holds it with --modules <dir> or to the \"modules\" list in goldbox.json.";
        return Load(root, ModuleCatalog.Directories(directories, diagnostics), directories, howToAdd, diagnostics);
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

        return Load(manifest, available, searched, howToAdd, diagnostics);
    }

    private static ModuleSet Load(ModuleManifest root, IReadOnlyList<ModuleSource> available, IReadOnlyList<string> searched, string howToAdd, List<ModuleDiagnostic> diagnostics)
    {
        ModuleCatalog catalog = ModuleCatalog.Read(available, root, searched, howToAdd);
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
