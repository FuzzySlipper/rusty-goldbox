using System.Buffers;
using System.Globalization;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using RustyTales.Core.Modules;
using RustyTales.Core.Persistence;
using RustyTales.Core.Rules;

namespace RustyTales.Game;

/// <summary>Adapts Rusty Engine's deterministic <see cref="IRandomService"/> to Core's <see cref="ITalesRandom"/>.</summary>
internal sealed class EngineRandom(IRandomService random) : ITalesRandom
{
    public ITalesRandomStream CreateScoped(ulong seed, string scope) =>
        new Stream(random, random.CreateScoped(new ScopedRngCreateRequest(seed, scope)));

    public uint DrawKeyed(ulong seed, string scope, string key, uint exclusiveMaximum) =>
        checked((uint)random.DrawKeyed(new KeyedRngRequest(seed, scope, key, 0, (int)exclusiveMaximum - 1)).Value);

    private sealed class Stream(IRandomService random, Rng stream) : ITalesRandomStream
    {
        public uint NextBounded(uint exclusiveMaximum) =>
            checked((uint)random.NextBoundedU32(new ScopedRngBoundedRequest(stream, exclusiveMaximum)).Value);

        public void Dispose() => stream.Dispose();
    }
}

/// <summary>Adapts Rusty Engine's <see cref="ProductStateStore{T}"/> to Core's <see cref="IStateStore"/>.</summary>
internal sealed class EngineStateStore(IEngineContext engine, string scope) : IStateStore
{
    private readonly ProductStateStore<byte[]> _store = new(engine, scope, new Utf8Codec());

    public byte[]? Load(string key)
    {
        ProductStateLoad<byte[]> load = _store.Load(key);
        return load.Present ? load.State : null;
    }

    public void Save(string key, byte[] payload) => _store.Save(key, payload);

    public void Dispose() => _store.Dispose();

    private sealed class Utf8Codec : IProductStateCodec<byte[]>
    {
        public void Encode(in byte[] state, IBufferWriter<byte> destination) => destination.Write(state);

        public byte[] Decode(ReadOnlySpan<byte> payload) => payload.ToArray();
    }
}

/// <summary>
/// A module in an Engine content bundle: a build bundle opened with
/// <c>Content.OpenBundle</c>, or a container opened with <c>ProductContentBundle.OpenContainer</c>.
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
            throw new IOException(exception.Message, exception);
        }
    }

    public override string PathOf(string relativePath) => $"{Location}/{relativePath}";

    private static string Hex(ContentSha256 identity) =>
        string.Create(CultureInfo.InvariantCulture, $"{identity.Word0:x16}{identity.Word1:x16}{identity.Word2:x16}{identity.Word3:x16}");
}

/// <summary>Adapts Rusty Engine's <see cref="IContentService"/> to Core's <see cref="IModuleContainerOpener"/>.</summary>
public sealed class EngineContainerOpener(IContentService content) : IModuleContainerOpener
{
    public OpenedModuleContainer Open(string path)
    {
        try
        {
            ProductContentBundle bundle = ProductContentBundle.OpenContainer(content, path);
            return new OpenedModuleContainer(new BundleModuleSource(bundle), bundle);
        }
        catch (EngineCallException exception)
        {
            throw new IOException(exception.Message, exception);
        }
    }

    /// <summary>Opens every <c>.rpak</c> container in <paramref name="directory"/> into <paramref name="opened"/>.</summary>
    public static void OpenAll(IContentService content, string directory, List<ProductContentBundle> opened, List<string> problems)
    {
        List<ModuleDiagnostic> listing = [];
        List<string> containers = InstalledModules.In(directory, listing);
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
