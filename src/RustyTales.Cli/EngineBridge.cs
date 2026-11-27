using System.Buffers;
using System.Globalization;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using RustyTales.Core.Modules;
using RustyTales.Core.Persistence;

namespace RustyTales.Cli;

/// <summary>
/// Thin CLI bridge to <see cref="Rusty.Engine"/> strictly for reading packaged
/// <c>.rpak</c> module containers (pending standalone rpak tooling in
/// <c>rusty-engine</c> #11155) and accessing Engine <c>--store</c> save slots.
/// All CLI dice, combat simulation and campaign execution run on pure .NET
/// <see cref="RustyTales.Core.Rules.DeterministicRandom.Shared"/>.
/// </summary>
internal sealed class CliBundleModuleSource : ModuleSource
{
    private readonly ProductContentBundle _bundle;
    private readonly List<string> _files;
    private readonly HashSet<string> _contains;

    public CliBundleModuleSource(ProductContentBundle bundle)
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

internal sealed class CliContainerOpener(IContentService content) : IModuleContainerOpener
{
    public OpenedModuleContainer Open(string path)
    {
        try
        {
            ProductContentBundle bundle = ProductContentBundle.OpenContainer(content, path);
            return new OpenedModuleContainer(new CliBundleModuleSource(bundle), bundle);
        }
        catch (EngineCallException exception)
        {
            throw new IOException(exception.Message, exception);
        }
    }
}

internal sealed class CliEngineStateStore(IEngineContext engine, string scope) : IStateStore
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
