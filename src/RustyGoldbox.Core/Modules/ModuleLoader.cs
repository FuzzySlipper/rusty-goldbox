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

        ModuleCatalog catalog = ModuleCatalog.Scan(directories, root.Directory, diagnostics);
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

        return new ModuleSet(root, directories, order, rules, diagnostics);
    }
}
