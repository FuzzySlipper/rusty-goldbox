using RustyTales.Core.Persistence;

namespace RustyTales.Core.Characters;

/// <summary>
/// Reads and writes the player's ruleset-bound character stable over an
/// <see cref="IStateStore"/>, shared cleanly between the Game, CLI and web hosts.
/// </summary>
public static class CharacterStableStore
{
    public const string Scope = "tales-stable";
    public const string Key = "roster";

    public static List<StableEntry> Load(IStateStore store, List<string> warnings)
    {
        byte[]? bytes = store.Load(Key);
        return bytes is null ? [] : CharacterRoster.Read(bytes, warnings);
    }

    public static void Save(IStateStore store, IReadOnlyList<StableEntry> entries)
    {
        store.Save(Key, CharacterRoster.Write(entries));
    }
}
