using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Core.Modules;

/// <summary>A requirement and the version the resolver picked for it.</summary>
public sealed record ResolvedRequirement(string Id, VersionRange Range, ModuleVersion Version);

/// <summary>A module in a resolved set, with what each of its requirements resolved to.</summary>
public sealed record LoadedModule(ModuleManifest Manifest, IReadOnlyList<ResolvedRequirement> Requires);

/// <summary>The result of loading a module and everything it requires.</summary>
public sealed class ModuleSet
{
    internal ModuleSet(
        ModuleManifest? root,
        IReadOnlyList<string> searchDirectories,
        IReadOnlyList<LoadedModule> loadOrder,
        RuleSet? rules,
        IReadOnlyList<ModuleDiagnostic> diagnostics)
    {
        Root = root;
        SearchDirectories = searchDirectories;
        LoadOrder = loadOrder;
        Rules = rules;
        Diagnostics = diagnostics;
    }

    /// <summary>The module that was asked for, when its manifest is valid.</summary>
    public ModuleManifest? Root { get; }

    public IReadOnlyList<string> SearchDirectories { get; }

    /// <summary>Resolved modules, each after everything it requires. The root is last.</summary>
    public IReadOnlyList<LoadedModule> LoadOrder { get; }

    /// <summary>Every definition of the set, checked; null when earlier problems stopped the checks.</summary>
    public RuleSet? Rules { get; }

    public IReadOnlyList<ModuleDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;
}
