using Rusty.Engine;

namespace RustyGoldbox.Core.Modules;

/// <summary>
/// Modules installed as Engine content containers (<c>.rpak</c> files packed
/// by <c>goldbox module pack</c>), each installed and replaced on its own.
/// </summary>
public static class InstalledModules
{
    public const string Extension = ".rpak";

    /// <summary>Overrides <see cref="DefaultDirectory"/> for the Game and <c>module pack --install</c>.</summary>
    public const string DirectoryVariable = "GOLDBOX_MODULE_LIBRARY";

    /// <summary>
    /// Where the Game finds installed modules: <c>$GOLDBOX_MODULE_LIBRARY</c>,
    /// else <c>$XDG_DATA_HOME/rusty-goldbox/modules</c> (when that is an
    /// absolute path, as XDG requires), else <c>~/.local/share/rusty-goldbox/modules</c>.
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
        return Path.Combine(data, "rusty-goldbox", "modules");
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
    /// Opens every container in <paramref name="directory"/> and adds it to
    /// <paramref name="opened"/>; the caller disposes them. A container that
    /// doesn't open is left out and described in <paramref name="problems"/>.
    /// Call inside a host callback.
    /// </summary>
    public static void Open(IContentService content, string directory, List<ProductContentBundle> opened, List<string> problems)
    {
        List<ModuleDiagnostic> listing = [];
        List<string> containers = In(directory, listing);
        problems.AddRange(listing.Select(diagnostic => $"{diagnostic.File}: {diagnostic.Message}"));
        foreach (string path in containers)
        {
            try
            {
                opened.Add(ProductContentBundle.OpenContainer(content, path));
            }
            catch (EngineCallException exception)
            {
                problems.Add($"{path} isn't a usable module container: {exception.Message}");
            }
        }
    }
}
