using System.Buffers;
using Rusty.Engine;
using Rusty.Engine.Persistence;

namespace RustyGoldbox.Game;

/// <summary>
/// Who sat where in a hosted game, beside its save slot: the same slot name
/// in Engine persistence, holding <see cref="PartyTable.ToJson"/> (or no
/// seats for a game saved alone). Open inside a host callback and dispose
/// before it ends.
/// </summary>
internal sealed class SeatSlots : IDisposable
{
    public const string Scope = "goldbox-seats";

    private readonly ProductStateStore<byte[]> _store;

    public SeatSlots(IEngineContext engine)
    {
        _store = new ProductStateStore<byte[]>(engine, Scope, new Bytes());
    }

    public void Write(string slot, string json) => _store.Save(slot, System.Text.Encoding.UTF8.GetBytes(json));

    /// <summary>The slot's seats JSON, or null when none was saved beside it.</summary>
    public byte[]? Read(string slot)
    {
        ProductStateLoad<byte[]> load = _store.Load(slot);
        return load.Present ? load.State : null;
    }

    public void Dispose() => _store.Dispose();

    private sealed class Bytes : IProductStateCodec<byte[]>
    {
        public void Encode(in byte[] state, IBufferWriter<byte> destination) => destination.Write(state);

        public byte[] Decode(ReadOnlySpan<byte> payload) => payload.ToArray();
    }
}
