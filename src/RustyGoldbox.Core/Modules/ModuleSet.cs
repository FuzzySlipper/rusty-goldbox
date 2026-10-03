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
        IReadOnlyList<string> searched,
        IReadOnlyList<LoadedModule> loadOrder,
        RuleSet? rules,
        IReadOnlyList<ModuleDiagnostic> diagnostics)
    {
        Root = root;
        Searched = searched;
        LoadOrder = loadOrder;
        Rules = rules;
        Diagnostics = diagnostics;
    }

    /// <summary>The module that was asked for, when its manifest is valid.</summary>
    public ModuleManifest? Root { get; }

    /// <summary>Where requirements were looked for: search directories, or the bundles a product offered.</summary>
    public IReadOnlyList<string> Searched { get; }

    /// <summary>Resolved modules, each after everything it requires: the root after its requirements, then the added extensions after theirs.</summary>
    public IReadOnlyList<LoadedModule> LoadOrder { get; }

    /// <summary>IDs of the extensions added to the set though nothing in it requires them (a user's own class book), in the order given.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>Every definition of the set, checked; null when earlier problems stopped the checks.</summary>
    public RuleSet? Rules { get; }

    public IReadOnlyList<ModuleDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;
}
