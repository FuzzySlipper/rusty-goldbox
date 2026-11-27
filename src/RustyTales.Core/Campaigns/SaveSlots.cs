using RustyTales.Core.Modules;
using RustyTales.Core.Persistence;

namespace RustyTales.Core.Campaigns;

/// <summary>
/// Named save slots over an <see cref="IStateStore"/>. Each slot holds the save
/// JSON that <see cref="SaveFile"/> writes, so a save made by the Game loads in
/// <c>tales play --store</c> and the other way round.
/// </summary>
public sealed class SaveSlots(IStateStore store) : IDisposable
{
    /// <summary>The persistence scope every save slot is in.</summary>
    public const string Scope = "tales-saves";

    public const string NameDescription = "lowercase letters, digits and single hyphens, starting with a letter, for example \"slot-1\"";

    /// <summary>Slot names follow module ID rules, so they are always plain persistence keys.</summary>
    public static bool IsValidName(string slot) => ModuleIds.IsValid(slot);

    /// <summary>How a slot is named in diagnostics.</summary>
    public static string Location(string slot) => $"save slot '{slot}'";

    public void Write(string slot, string json)
    {
        store.Save(slot, System.Text.Encoding.UTF8.GetBytes(json));
    }

    /// <summary>The slot's save JSON, or null when the slot is empty.</summary>
    public byte[]? Read(string slot) => store.Load(slot);

    public void Dispose() => store.Dispose();
}
