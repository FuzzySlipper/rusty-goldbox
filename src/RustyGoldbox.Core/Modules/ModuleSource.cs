using System.Security.Cryptography;
using System.Text;

namespace RustyGoldbox.Core.Modules;

/// <summary>
/// Where a module's files come from: a directory (the CLI, authoring) or an
/// Engine content bundle or container (the Game). Paths are module-relative
/// with <c>/</c> separators.
/// </summary>
public abstract class ModuleSource
{
    /// <summary>Where the module is, as a person would look for it: a directory or a bundle name.</summary>
    public abstract string Location { get; }

    /// <summary>
    /// The module's content identity: SHA-256 over each file's relative path,
    /// a zero byte and the file's SHA-256, in path order. It is the Engine's
    /// bundle identity, so a directory and a bundle or container with the
    /// same files have the same identity.
    /// </summary>
    public abstract string Identity { get; }

    /// <summary>
    /// Every file's relative path in ordinal order. Parts that can't be listed
    /// are left out and reported in <paramref name="diagnostics"/>.
    /// </summary>
    public abstract IReadOnlyList<string> ListFiles(string? module, List<ModuleDiagnostic> diagnostics);

    public abstract bool Contains(string relativePath);

    /// <exception cref="IOException">The file can't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file can't be read.</exception>
    public abstract byte[] Read(string relativePath);

    /// <summary>A file's location for diagnostics.</summary>
    public abstract string PathOf(string relativePath);

    public string ManifestPath => PathOf(ManifestReader.FileName);

    /// <summary>The identity formula over (path, file SHA-256) pairs; see <see cref="Identity"/>.</summary>
    protected static string IdentityOf(IEnumerable<(string Path, byte[] Sha256)> files)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach ((string path, byte[] sha256) in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            hash.AppendData([0]);
            hash.AppendData(sha256);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

/// <summary>A module directory on disk.</summary>
public sealed class DirectoryModuleSource : ModuleSource
{
    private string? _identity;

    public DirectoryModuleSource(string directory)
    {
        Directory = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(directory));
    }

    public string Directory { get; }

    public override string Location => Directory;

    /// <summary>Hashes every file under the directory the first time it is asked for.</summary>
    public override string Identity => _identity ??= IdentityOf(
        System.IO.Directory.EnumerateFiles(Directory, "*", SearchOption.AllDirectories)
            .Select(file => (Relative(file), SHA256.HashData(File.ReadAllBytes(file)))));

    public override IReadOnlyList<string> ListFiles(string? module, List<ModuleDiagnostic> diagnostics)
    {
        List<string> files = [];
        List(Directory, files, module, diagnostics);
        return files.Order(StringComparer.Ordinal).ToList();
    }

    public override bool Contains(string relativePath) => File.Exists(PathOf(relativePath));

    public override byte[] Read(string relativePath) => File.ReadAllBytes(PathOf(relativePath));

    public override string PathOf(string relativePath) => System.IO.Path.Combine(Directory, relativePath);

    private void List(string directory, List<string> files, string? module, List<ModuleDiagnostic> diagnostics)
    {
        List<string>? here = DirectoryListing.List(directory, directories: false, module, diagnostics);
        List<string>? children = here is null ? null : DirectoryListing.List(directory, directories: true, module, diagnostics);
        if (here is null || children is null)
        {
            return;
        }

        files.AddRange(here.Select(Relative));
        foreach (string child in children)
        {
            List(child, files, module, diagnostics);
        }
    }

    private string Relative(string path) => System.IO.Path.GetRelativePath(Directory, path).Replace('\\', '/');
}
