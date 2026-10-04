using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Persistence;

namespace RustyGoldbox.Game;

/// <summary>
/// The player's own settings, kept between runs in Engine persistence apart
/// from the save slots: music and sound volume, the picked skin, the
/// interface scale and the player's layout. The file is the player's, so loading takes what is
/// sound and turns the rest into notes.
/// </summary>
internal static class PlayerSettings
{
    private const string Scope = "goldbox-settings";
    private const string Key = "player";

    /// <exception cref="PersistenceStorageException">The store can't be written.</exception>
    /// <exception cref="EngineCallException">The host has no persistence root.</exception>
    public static void Save(IEngineContext engine, GameSession session)
    {
        JsonObject settings = new()
        {
            ["music"] = session.MusicVolume,
            ["sound"] = session.SoundVolume,
            ["skin"] = session.PickedSkin?.Choice.Id,
            ["ui_scale"] = session.UiScale,
            ["layout"] = session.LayoutPicked is { } layout ? new JsonObject(layout.Select(part => KeyValuePair.Create(part.Key, (JsonNode?)part.Value))) : null,
        };
        using ProductStateStore<byte[]> store = new(engine, Scope, new Utf8Codec());
        store.Save(Key, System.Text.Encoding.UTF8.GetBytes(settings.ToJsonString()));
    }

    /// <summary>Applies the saved settings, if any, through the session's own checks; what doesn't fit becomes a note.</summary>
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
            // No persistence root: nothing was kept.
            return;
        }
        catch (PersistenceStorageException exception)
        {
            session.Notes.Add($"Can't read the player settings: {exception.Message}");
            return;
        }

        if (bytes is null)
        {
            return;
        }

        // Each setter clears the notes, so keep what was there and gather each setter's.
        List<string> earlier = [.. session.Notes];
        List<string> problems = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            foreach (string bus in new[] { "music", "sound" })
            {
                if (root.TryGetProperty(bus, out JsonElement volume) && volume.ValueKind == JsonValueKind.Number)
                {
                    session.SetVolume(bus, (float)volume.GetDouble());
                    problems.AddRange(session.Notes);
                }
            }

            if (root.TryGetProperty("skin", out JsonElement skin) && skin.ValueKind == JsonValueKind.String)
            {
                session.PickSkin(skin.GetString());
                problems.AddRange(session.Notes);
            }

            if (root.TryGetProperty("ui_scale", out JsonElement scale) && scale.ValueKind == JsonValueKind.Number)
            {
                session.SetUiScale(scale.GetDouble());
                problems.AddRange(session.Notes);
            }

            if (root.TryGetProperty("layout", out JsonElement layout) && layout.ValueKind == JsonValueKind.Object)
            {
                session.SetLayout(layout.EnumerateObject()
                    .Where(part => part.Value.ValueKind == JsonValueKind.Number)
                    .ToDictionary(part => part.Name, part => part.Value.GetDouble()));
                problems.AddRange(session.Notes);
            }
        }
        catch (JsonException exception)
        {
            problems.Add($"they aren't JSON ({exception.Message})");
        }

        session.Notes.Clear();
        session.Notes.AddRange(earlier);
        session.Notes.AddRange(problems.Select(problem => $"Ignored part of the player settings: {problem}"));
    }

    /// <summary>The bytes are the settings' JSON text.</summary>
    private sealed class Utf8Codec : IProductStateCodec<byte[]>
    {
        public void Encode(in byte[] state, IBufferWriter<byte> destination) => destination.Write(state);

        public byte[] Decode(ReadOnlySpan<byte> payload) => payload.ToArray();
    }
}
