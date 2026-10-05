using System.Text.Json.Nodes;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Game;

/// <summary>
/// What a multiplayer host sends its guests: everything a guest's Game needs
/// to show what the host shows, without running the campaign itself. Play
/// state travels in the save format, which already names the exact module set
/// and is read as untrusted input.
/// </summary>
/// <remarks>
/// <c>{ "screen", "log", "notes", "campaign"?, "party"?, "save"?, "finished"? }</c>:
/// <c>campaign</c> and <c>party</c> on the party screen (the campaign's module
/// identity and the characters made so far, as character files); <c>save</c>
/// once play has begun; <c>finished</c> while a fight that just ended is on
/// screen (the save with that fight still in it, finalized).
/// </remarks>
internal static class GameView
{
    public static JsonObject Of(GameSession session)
    {
        JsonObject view = new()
        {
            ["screen"] = session.Screen.ToString().ToLowerInvariant(),
            ["log"] = new JsonArray(session.Log.Select(line => (JsonNode)line).ToArray()),
            ["notes"] = new JsonArray(session.Notes.Select(note => (JsonNode)note).ToArray()),
        };
        if (session.Table is PartyTable table)
        {
            view["table"] = table.ToJson();
        }

        if (session.Set?.Root is not ModuleManifest root)
        {
            return view;
        }

        // Where each module of the set is published, so a guest without one can fetch it.
        Dictionary<string, InstalledSources.Entry> installed = InstalledSources.Read(InstalledModules.DefaultDirectory());
        view["sources"] = new JsonObject(session.Set.LoadOrder
            .Select(loaded => (loaded.Manifest.Id, Releases: loaded.Manifest.Releases?.Text ?? (installed.TryGetValue(loaded.Manifest.Id, out InstalledSources.Entry? entry) ? entry.Releases : null)))
            .Where(source => source.Releases is not null)
            .Select(source => KeyValuePair.Create(source.Id, (JsonNode?)source.Releases)));

        if (session.Screen == Screen.Party)
        {
            view["campaign"] = new JsonObject
            {
                ["id"] = root.Id,
                ["version"] = root.Version.ToString(),
                ["identity"] = root.Source.Identity,
                ["extensions"] = new JsonArray(session.Set.Extensions.Select(id => (JsonNode)id).ToArray()),
                ["seed"] = session.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            view["party"] = new JsonArray(session.Party.Select(character => JsonNode.Parse(CharacterFile.ToJson(character))).ToArray());
        }

        if (session.Runner is CampaignRunner runner)
        {
            view["save"] = JsonNode.Parse(SaveFile.ToJson(runner.State, session.Set));
            if (session.FinishedFight is PendingCombatState finished)
            {
                view["finished"] = JsonNode.Parse(SaveFile.ToJson(runner.State, session.Set, finished));
            }
        }

        return view;
    }
}
