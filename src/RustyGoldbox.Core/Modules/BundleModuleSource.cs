using System.Globalization;
using Rusty.Engine;

namespace RustyGoldbox.Core.Modules;

/// <summary>
/// A module in an Engine content bundle: a build bundle the Game opened with
/// <c>Content.OpenBundle</c>, or a container opened with
/// <c>ProductContentBundle.OpenContainer</c>. The caller keeps the bundle
/// open while the module loads and disposes it afterwards; the identity and
/// file list are copied when the source is made.
/// </summary>
public sealed class BundleModuleSource : ModuleSource
{
    private readonly ProductContentBundle _bundle;
    private readonly List<string> _files;
    private readonly HashSet<string> _contains;

    public BundleModuleSource(ProductContentBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        _bundle = bundle;
        _files = [];
        foreach (ContentReferenceInfo entry in bundle.Entries.Span)
        {
            _files.Add(entry.Path);
        }

        _files.Sort(StringComparer.Ordinal);
        _contains = new HashSet<string>(_files, StringComparer.Ordinal);
        Location = bundle.Id;
        Identity = Hex(bundle.Identity);
    }

    public override string Location { get; }

    public override string Identity { get; }

    public override IReadOnlyList<string> ListFiles(string? module, List<ModuleDiagnostic> diagnostics) => _files;

    public override bool Contains(string relativePath) => _contains.Contains(relativePath);

    public override byte[] Read(string relativePath)
    {
        if (!_contains.Contains(relativePath))
        {
            throw new FileNotFoundException($"{Location} has no file {relativePath}.");
        }

        try
        {
            return _bundle.ReadBytes(relativePath).ToArray();
        }
        catch (EngineCallException exception)
        {
            // A compressed container file that no longer decompresses is found here.
            throw new IOException(exception.Message, exception);
        }
    }

    public override string PathOf(string relativePath) => $"{Location}/{relativePath}";

    /// <summary>The Engine's identity words are the digest's bytes, big-endian, in order.</summary>
    private static string Hex(ContentSha256 identity)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{identity.Word0:x16}{identity.Word1:x16}{identity.Word2:x16}{identity.Word3:x16}");
    }
}
