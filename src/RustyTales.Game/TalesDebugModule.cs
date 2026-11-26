using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using RustyTales.Core.Campaigns;
using RustyTales.Core.Characters;
using RustyTales.Core.Combat;
using RustyTales.Core.Definitions;
using RustyTales.Core.Rules;

namespace RustyTales.Game;

/// <summary>
/// Live-debug console shortcuts registered with the Engine's compiled debug
/// catalog (<c>rusty-live-debug</c>), for quickly jumping into campaigns,
/// areas, cells, events and fights while testing or polishing presentation.
/// </summary>
public sealed class TalesDebugModule : IDebugCommandModule
{
    private readonly GameSession _session;
    private readonly IEngineContext _engine;
    private readonly Action _publish;

    internal TalesDebugModule(GameSession session, IEngineContext engine, Action publish)
    {
        _session = session;
        _engine = engine;
        _publish = publish;
    }

    [DebugCommand("tales.help", Description = "List Rusty Tales dev console shortcuts.")]
    public DebugCommandResult Help()
    {
        return DebugCommandResult.Success(string.Join('\n',
        [
            "tales.state                            Show screen, position, party, areas and events as JSON",
            "tales.campaigns                        List available campaigns",
            "tales.start [campaign] [seed]          Open a campaign, auto-roll a valid party and enter play",
            "tales.play <command>                   Run an ordinary play command (forward, choose 1, leave, ...)",
            "tales.goto <area> <x> <y> [facing]     Teleport to an area, cell and facing (north|east|south|west)",
            "tales.tp <x> <y> [facing]              Teleport within the current area",
            "tales.event <event>                    Trigger an authored event chain immediately",
            "tales.combat-view <mode>               Switch combat view: tactical, first_person, side_view or auto",
            "tales.heal                             Restore every party member's tracks and clear conditions",
            "tales.quit                             Return to the title screen",
        ]));
    }

    [DebugCommand("tales.campaigns", Description = "List installed and bundled campaigns.")]
    public DebugCommandResult Campaigns()
    {
        _session.Refresh();
        JsonArray list = new(_session.Campaigns.Select(choice => (JsonNode)new JsonObject
        {
            ["id"] = choice.Id,
            ["bundle"] = choice.Bundle,
            ["title"] = choice.Title,
            ["version"] = choice.Version.ToString(),
        }).ToArray());
        return DebugCommandResult.Success(list.ToJsonString());
    }

    [DebugCommand("tales.state", Description = "Inspect the current Rusty Tales session state as JSON.")]
    public DebugCommandResult State()
    {
        JsonObject readout = new()
        {
            ["screen"] = _session.Screen.ToString().ToLowerInvariant(),
            ["campaign"] = _session.Campaign?.QualifiedId,
            ["seed"] = _session.Seed.ToString(CultureInfo.InvariantCulture),
        };
        if (_session.Runner is CampaignRunner runner && _session.Set?.Rules is RuleSet rules)
        {
            CampaignState state = runner.State;
            readout["position"] = new JsonObject
            {
                ["area"] = state.Area.QualifiedId,
                ["x"] = state.X,
                ["y"] = state.Y,
                ["facing"] = Facings.Name(state.Facing),
            };
            readout["party"] = new JsonArray(state.Party.Select(member => (JsonNode)member.Name).ToArray());
            readout["pendingMenu"] = state.PendingMenu?.QualifiedId;
            readout["pendingShop"] = state.PendingShop?.QualifiedId;
            readout["pendingTemple"] = state.PendingTemple?.QualifiedId;
            readout["pendingTraining"] = state.PendingTraining?.QualifiedId;
            readout["pendingCombat"] = state.PendingCombat?.Event.QualifiedId;
            readout["areas"] = new JsonArray(rules.OfType(DefinitionTypes.Area).Select(area => (JsonNode)area.Id).ToArray());
            readout["events"] = new JsonArray(rules.OfType(DefinitionTypes.Event).Select(evt => (JsonNode)evt.Id).ToArray());
        }

        if (_session.Combat is CombatObservation combat)
        {
            readout["combat"] = new JsonObject
            {
                ["phase"] = combat.Phase.ToString(),
                ["round"] = combat.Round,
                ["activeActor"] = combat.ActiveActorId,
            };
        }

        return DebugCommandResult.Success(readout.ToJsonString());
    }

    [DebugCommand("tales.start", Description = "Quickstart a campaign by id/bundle (or default first) with an auto-rolled party.")]
    public DebugCommandResult Start(string campaign = "", string seed = "1")
    {
        if (!ulong.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedSeed))
        {
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"'{seed}' is not a valid seed.");
        }

        _session.Refresh();
        CampaignChoice? choice = string.IsNullOrWhiteSpace(campaign)
            ? _session.Campaigns.FirstOrDefault()
            : _session.Campaigns.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, campaign, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Bundle, campaign, StringComparison.OrdinalIgnoreCase));
        if (choice is null)
        {
            string offered = string.Join(", ", _session.Campaigns.Select(candidate => candidate.Id));
            return DebugCommandResult.Failure(DebugCommandStatus.Failed, $"Campaign '{campaign}' not found. Available: {offered}");
        }

        _session.Open(choice.Bundle, parsedSeed);
        if (_session.Screen != Screen.Party || _session.Set?.Rules is not RuleSet rules || _session.Campaign is not Definition openCampaign)
        {
            return DebugCommandResult.Failure(DebugCommandStatus.Failed, string.Join(" ", _session.Notes));
        }

        if (!_session.HasFixedStartingParty)
        {
            int needed = Math.Max(1, openCampaign.Json.GetProperty("party").GetProperty("min").GetInt32());
            string? race = rules.OfType(DefinitionTypes.Race).FirstOrDefault()?.QualifiedId;
            string? characterClass = rules.OfType(DefinitionTypes.Class).FirstOrDefault()?.QualifiedId;
            string[] names = ["Ada", "Brom", "Cora", "Dain", "Elin", "Finn"];

            for (int member = 0; member < needed; member++)
            {
                string name = member < names.Length ? names[member] : $"Hero {member + 1}";
                int before = _session.Party.Count;
                for (int attempt = 0; attempt < 64 && _session.Party.Count == before; attempt++)
                {
                    _session.Roll(_engine, name, race, characterClass);
                }

                if (_session.Party.Count == before)
                {
                    return DebugCommandResult.Failure(DebugCommandStatus.Failed, $"Could not auto-roll party member {name}: {string.Join(" ", _session.Notes)}");
                }
            }
        }

        _session.Begin(_engine);
        if (_session.Screen is not (Screen.Play or Screen.Combat))
        {
            return DebugCommandResult.Failure(DebugCommandStatus.Failed, string.Join(" ", _session.Notes));
        }

        _publish();
        return DebugCommandResult.Success($"Started {openCampaign.Name} (seed {parsedSeed}) at {_session.Runner!.State.Area.Id} [{_session.Runner.State.X}, {_session.Runner.State.Y}] facing {Facings.Name(_session.Runner.State.Facing)}.");
    }

    [DebugCommand("tales.play", Description = "Execute an ordinary play command in the running campaign.")]
    public DebugCommandResult Play(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Provide a play command, e.g. `tales.play forward`.");
        }

        if (_session.Screen != Screen.Play)
        {
            return DebugCommandResult.Failure(DebugCommandStatus.Failed, $"Not on the play screen (currently {_session.Screen}).");
        }

        _session.Execute(_engine, command);
        _publish();
        string note = _session.Notes.Count > 0 ? $" ({string.Join(" ", _session.Notes)})" : "";
        CampaignState state = _session.Runner!.State;
        return DebugCommandResult.Success($"{_session.Screen}: {state.Area.Id} [{state.X}, {state.Y}] {Facings.Name(state.Facing)}{note}");
    }

    [DebugCommand("tales.goto", Description = "Teleport to a named area, cell [x, y] and optional facing.")]
    public DebugCommandResult Goto(string area, int x, int y, string facing = "")
    {
        if (_session.Screen != Screen.Play || _session.Set?.Rules is not RuleSet rules || _session.Runner is null)
        {
            return DebugCommandResult.Failure(DebugCommandStatus.Failed, "Start a campaign before using tales.goto.");
        }

        if (rules.Find(DefinitionTypes.Area, area, out string? problem) is not Definition targetArea)
        {
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, problem!);
        }

        Facing targetFacing = _session.Runner.State.Facing;
        if (!string.IsNullOrWhiteSpace(facing) && !Facings.TryParse(facing.Trim().ToLowerInvariant(), out targetFacing))
        {
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"'{facing}' is not a facing (north, east, south, west).");
        }

        if (!_session.Teleport(targetArea, x, y, targetFacing))
        {
            return DebugCommandResult.Failure(DebugCommandStatus.Failed, string.Join(" ", _session.Notes));
        }

        _publish();
        return DebugCommandResult.Success($"Moved to {targetArea.Id} [{x}, {y}] facing {Facings.Name(targetFacing)}.");
    }

    [DebugCommand("tales.tp", Description = "Teleport within the current area to cell [x, y] and optional facing.")]
    public DebugCommandResult TeleportLocal(int x, int y, string facing = "")
    {
        if (_session.Screen != Screen.Play || _session.Runner is null)
        {
            return DebugCommandResult.Failure(DebugCommandStatus.Failed, "Start a campaign before using tales.tp.");
        }

        return Goto(_session.Runner.State.Area.QualifiedId, x, y, facing);
    }

    [DebugCommand("tales.event", Description = "Trigger an authored event by local or qualified ID.")]
    public DebugCommandResult Event(string eventId)
    {
        if (_session.Screen != Screen.Play || _session.Set?.Rules is not RuleSet rules)
        {
            return DebugCommandResult.Failure(DebugCommandStatus.Failed, "Start a campaign before triggering an event.");
        }

        if (rules.Find(DefinitionTypes.Event, eventId, out string? problem) is not Definition evt)
        {
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, problem!);
        }

        if (!_session.TriggerEvent(_engine, evt))
        {
            return DebugCommandResult.Failure(DebugCommandStatus.Failed, string.Join(" ", _session.Notes));
        }

        _publish();
        return DebugCommandResult.Success($"Triggered {evt.QualifiedId} (screen: {_session.Screen}).");
    }

    [DebugCommand("tales.combat-view", Description = "Override combat presentation mode: tactical, first_person, side_view or auto.")]
    public DebugCommandResult CombatView(string mode)
    {
        string normalized = mode.Trim().ToLowerInvariant();
        if (normalized is "auto" or "default" or "")
        {
            _session.CombatPresentationOverride = null;
            _publish();
            return DebugCommandResult.Success("Combat presentation restored to authored default.");
        }

        if (normalized is not ("tactical" or "first_person" or "side_view"))
        {
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"'{mode}' is not a combat view. Use tactical, first_person, side_view or auto.");
        }

        _session.CombatPresentationOverride = normalized;
        _publish();
        return DebugCommandResult.Success($"Combat presentation set to {normalized}.");
    }

    [DebugCommand("tales.heal", Description = "Restore all party tracks to maximum and clear conditions.")]
    public DebugCommandResult Heal()
    {
        if (!_session.HealParty())
        {
            return DebugCommandResult.Failure(DebugCommandStatus.Failed, string.Join(" ", _session.Notes));
        }

        _publish();
        return DebugCommandResult.Success("Healed party.");
    }

    [DebugCommand("tales.quit", Description = "Return to the title screen.")]
    public DebugCommandResult Quit()
    {
        _session.Quit();
        _publish();
        return DebugCommandResult.Success("Returned to title screen.");
    }
}
