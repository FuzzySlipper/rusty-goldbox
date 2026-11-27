using System.Buffers;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using RustyTales.Core.Characters;

namespace RustyTales.Game;

/// <summary>
/// Persists the player's ruleset-bound character stable in Engine persistence
/// (apart from campaign save slots), so characters rolled or exported from one
/// campaign can be imported into any other campaign sharing the same baseline
/// ruleset.
/// </summary>
internal static class CharacterStable
{
    public const string Scope = "tales-stable";
    public const string Key = "roster";

    /// <summary>Saves the session's stable entries to Engine persistence, ignoring hosts with no persistence root.</summary>
    public static void Save(IEngineContext engine, GameSession session)
    {
        try
        {
            using ProductStateStore<byte[]> store = new(engine, Scope, new Utf8Codec());
            store.Save(Key, CharacterRoster.Write(session.Stable));
        }
        catch (EngineCallException)
        {
            // No persistence root: keep the stable in memory for this session.
        }
        catch (PersistenceStorageException exception)
        {
            session.Notes.Add($"Can't save the character stable: {exception.Message}");
        }
    }

    /// <summary>Loads the player's character stable into <paramref name="session"/>; unreadable entries become warnings.</summary>
    public static void Load(IEngineContext engine, GameSession session)
    {
        byte[]? bytes;
        try
        {
            using ProductStateStore<byte[]> store = new(engine, Scope, new Utf8Codec());
            ProductStateLoad<byte[]> load = store.Load(Key);
            bytes = load.Present ? load.State : null;
        }
        catch (EngineCallException)
        {
            return;
        }
        catch (PersistenceStorageException exception)
        {
            session.Notes.Add($"Can't read the character stable: {exception.Message}");
            return;
        }

        if (bytes is null)
        {
            return;
        }

        List<string> warnings = [];
        List<StableEntry> entries = CharacterRoster.Read(bytes, warnings);
        session.Stable.Clear();
        session.Stable.AddRange(entries);
        session.Notes.AddRange(warnings);
    }

    private sealed class Utf8Codec : IProductStateCodec<byte[]>
    {
        public void Encode(in byte[] state, IBufferWriter<byte> destination) => destination.Write(state);

        public byte[] Decode(ReadOnlySpan<byte> payload) => payload.ToArray();
    }
}
