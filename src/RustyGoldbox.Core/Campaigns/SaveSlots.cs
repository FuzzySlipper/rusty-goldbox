using System.Buffers;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Core.Campaigns;

/// <summary>
/// Named save slots in Engine persistence. Each slot holds the save JSON
/// that <see cref="SaveFile"/> writes, so a save made by the Game loads in
/// <c>goldbox play --store</c> and the other way round. Open it inside a host
/// callback and dispose it before the callback ends.
/// </summary>
public sealed class SaveSlots : IDisposable
{
    /// <summary>The persistence scope every save slot is in.</summary>
    public const string Scope = "goldbox-saves";

    public const string NameDescription = "lowercase letters, digits and single hyphens, starting with a letter, for example \"slot-1\"";

    private readonly ProductStateStore<byte[]> _store;

    /// <exception cref="PersistenceStorageException">The store can't be opened.</exception>
    /// <exception cref="EngineCallException">The host has no persistence root.</exception>
    public SaveSlots(IEngineContext engine)
    {
        _store = new ProductStateStore<byte[]>(engine, Scope, new Utf8Codec());
    }

    /// <summary>Slot names follow module ID rules, so they are always plain persistence keys.</summary>
    public static bool IsValidName(string slot) => ModuleIds.IsValid(slot);

    /// <summary>How a slot is named in diagnostics.</summary>
    public static string Location(string slot) => $"save slot '{slot}'";

    /// <exception cref="PersistenceStorageException">The slot can't be written.</exception>
    public void Write(string slot, string json)
    {
        _store.Save(slot, System.Text.Encoding.UTF8.GetBytes(json));
    }

    /// <summary>The slot's save JSON, or null when the slot is empty.</summary>
    /// <exception cref="PersistenceStorageException">The slot can't be read.</exception>
    public byte[]? Read(string slot)
    {
        ProductStateLoad<byte[]> load = _store.Load(slot);
        return load.Present ? load.State : null;
    }

    public void Dispose() => _store.Dispose();

    /// <summary>The bytes are the save's JSON text; <see cref="SaveFile"/> checks them on load.</summary>
    private sealed class Utf8Codec : IProductStateCodec<byte[]>
    {
        public void Encode(in byte[] state, IBufferWriter<byte> destination) => destination.Write(state);

        public byte[] Decode(ReadOnlySpan<byte> payload) => payload.ToArray();
    }
}
