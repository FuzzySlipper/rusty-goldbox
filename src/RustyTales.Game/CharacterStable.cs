using Rusty.Engine;
using Rusty.Engine.Persistence;
using RustyTales.Core.Characters;

namespace RustyTales.Game;

/// <summary>
/// Persists the player's ruleset-bound character stable in Engine persistence
/// via Core's <see cref="CharacterStableStore"/>, so characters rolled or
/// exported from one campaign can be imported into any other campaign sharing
/// the same baseline ruleset.
/// </summary>
internal static class CharacterStable
{
    public const string Scope = CharacterStableStore.Scope;
    public const string Key = CharacterStableStore.Key;

    /// <summary>Saves the session's stable entries to Engine persistence, ignoring hosts with no persistence root.</summary>
    public static void Save(IEngineContext engine, GameSession session)
    {
        try
        {
            using EngineStateStore store = new(engine, Scope);
            CharacterStableStore.Save(store, session.Stable);
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
        List<string> warnings = [];
        List<StableEntry> entries;
        try
        {
            using EngineStateStore store = new(engine, Scope);
            if (store.Load(Key) is null)
            {
                return;
            }

            entries = CharacterStableStore.Load(store, warnings);
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

        session.Stable.Clear();
        session.Stable.AddRange(entries);
        session.Notes.AddRange(warnings);
    }
}
