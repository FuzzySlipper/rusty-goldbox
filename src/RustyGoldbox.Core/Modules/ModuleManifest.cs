namespace RustyGoldbox.Core.Modules;

/// <summary>One entry of a manifest's <c>requires</c> list.</summary>
/// <param name="Id">The required module's ID.</param>
/// <param name="Range">Acceptable versions.</param>
/// <param name="Index">Position in the <c>requires</c> array, for diagnostics.</param>
/// <param name="Releases">Where the required module is published, when the entry says.</param>
public sealed record ModuleRequirement(string Id, VersionRange Range, int Index, ReleaseSource? Releases = null);

/// <summary>A module's checked <c>module.json</c>.</summary>
public sealed record ModuleManifest(
    ModuleSource Source,
    int Format,
    string Id,
    ModuleKind Kind,
    ModuleVersion Version,
    string Title,
    IReadOnlyList<ModuleRequirement> Requires,
    string Provenance,
    ReleaseSource? Releases = null)
{
    public string ManifestPath => Source.ManifestPath;
}
