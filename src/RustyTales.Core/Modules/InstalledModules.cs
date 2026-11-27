namespace RustyTales.Core.Modules;

/// <summary>
/// Modules installed as Engine content containers (<c>.rpak</c> files packed
/// by <c>tales module pack</c>), each installed and replaced on its own.
/// </summary>
public static class InstalledModules
{
    public const string Extension = ".rpak";

    /// <summary>Overrides <see cref="DefaultDirectory"/> for the Game and <c>module pack --install</c>.</summary>
    public const string DirectoryVariable = "TALES_MODULE_LIBRARY";

    /// <summary>
    /// Where the Game finds installed modules: <c>$TALES_MODULE_LIBRARY</c>,
    /// else <c>$XDG_DATA_HOME/rusty-tales/modules</c> (when that is an
    /// absolute path, as XDG requires), else <c>~/.local/share/rusty-tales/modules</c>.
    /// </summary>
    public static string DefaultDirectory()
    {
        if (Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } library)
        {
            return Path.GetFullPath(library);
        }

        string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(data, "rusty-tales", "modules");
    }

    /// <summary>The installed file name for a module version, so versions sit side by side.</summary>
    public static string FileName(string id, ModuleVersion version) => $"{id}-{version}{Extension}";

    public static bool IsContainer(string path) => path.EndsWith(Extension, StringComparison.Ordinal);

    /// <summary>
    /// The containers directly in <paramref name="directory"/>, in ordinal
    /// order; none when it doesn't exist. A directory that can't be read is
    /// reported in <paramref name="diagnostics"/>.
    /// </summary>
    public static List<string> In(string directory, List<ModuleDiagnostic> diagnostics)
    {
        return Directory.Exists(directory)
            ? DirectoryListing.List(directory, directories: false, null, diagnostics, "*" + Extension) ?? []
            : [];
    }

    /// <summary>
    /// The modules in <paramref name="directories"/> (module directories and
    /// installed containers when <paramref name="containers"/> is supplied), as (id, version).
    /// </summary>
    public static List<(string Id, ModuleVersion Version)> Present(IReadOnlyList<string> directories, IModuleContainerOpener? containers = null)
    {
        List<IDisposable> opened = [];
        try
        {
            return ModuleCatalog.Sources(directories, "", containers, opened, [], [])
                .Select(source => ManifestReader.Read(source, []))
                .OfType<ModuleManifest>()
                .Select(manifest => (manifest.Id, manifest.Version))
                .ToList();
        }
        finally
        {
            opened.ForEach(bundle => bundle.Dispose());
        }
    }
}
