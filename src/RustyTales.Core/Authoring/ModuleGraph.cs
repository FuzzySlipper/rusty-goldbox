using System.Text.Json;
using System.Text.Json.Serialization;
using RustyTales.Core.Campaigns;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;
using RustyTales.Core.Rules;

namespace RustyTales.Core.Authoring;

/// <summary>
/// A host-agnostic diagnostic graph of a loaded <see cref="ModuleSet"/>:
/// campaign entry, areas with full 2D wall/door/cell topology, area-to-area
/// teleports, key-to-door dependencies, event/scene flow graphs, asset usage,
/// and structural reachability/dead-end issues. Shared by <c>tales module graph</c>
/// and the <c>tales workbench</c> HTTP server.
/// </summary>
public sealed record ModuleGraph(
    GraphModuleInfo Module,
    IReadOnlyList<GraphDependencyInfo> Dependencies,
    IReadOnlyList<string> Extensions,
    GraphCampaignInfo? Campaign,
    GraphCounts Counts,
    IReadOnlyList<GraphArea> Areas,
    IReadOnlyList<GraphNavLink> NavigationLinks,
    IReadOnlyList<GraphEvent> Events,
    IReadOnlyList<GraphEventEdge> EventEdges,
    IReadOnlyList<GraphAsset> Assets,
    IReadOnlyList<GraphIssue> Issues);

public sealed record GraphModuleInfo(
    string Id,
    string Version,
    string Kind,
    string Title,
    string Location,
    string Identity,
    bool IsValid);

public sealed record GraphDependencyInfo(
    string Id,
    string Version,
    string Kind,
    string Title,
    string Location);

public sealed record GraphCampaignInfo(
    string Id,
    string LocalId,
    string Name,
    string File,
    string StartArea,
    string StartEntry,
    string? IntroEvent,
    int PartyMin,
    int PartyMax,
    bool HasStartingParty,
    string? CombatPresentation,
    string? Skin);

public sealed record GraphCounts(
    int Areas,
    int Events,
    int Scenes,
    int Doors,
    int SecretDoors,
    int Encounters,
    int Monsters,
    int Items,
    int Npcs,
    int Variables,
    int Assets,
    int Figures,
    int Issues);

public sealed record GraphArea(
    string Id,
    string LocalId,
    string Name,
    string Module,
    string File,
    int Width,
    int Height,
    string? WallSet,
    bool IsStartArea,
    bool Reachable,
    bool HasEndEvent,
    int OutgoingAreaCount,
    IReadOnlyList<IReadOnlyList<string>> HorizontalEdges,
    IReadOnlyList<IReadOnlyList<string>> VerticalEdges,
    IReadOnlyList<GraphEntry> Entries,
    IReadOnlyList<GraphDoor> Doors,
    IReadOnlyList<GraphCell> Cells);

public sealed record GraphEntry(
    string Name,
    int X,
    int Y,
    string Facing,
    bool IsCampaignStart,
    bool Reachable,
    int IncomingTeleports);

public sealed record GraphDoor(
    string Id,
    int X,
    int Y,
    string Facing,
    bool Locked,
    string? KeyItem,
    string? PickCheck,
    string? ForceCheck,
    string? UnlockEvent,
    IReadOnlyList<string> OpenedByEvents,
    IReadOnlyList<string> KeySourceEvents,
    bool Reachable);

public sealed record GraphCell(
    int X,
    int Y,
    string? Zone,
    string? EventId,
    string? EventKind,
    string? EventSummary,
    string? Facing,
    bool Once,
    string? Backdrop,
    string? PropSprite,
    string? PropHidden,
    string Reachability);

public sealed record GraphNavLink(
    string Kind,
    string FromArea,
    string ToArea,
    string? ToEntry,
    string? ViaEvent,
    string? DoorId,
    string? ItemId,
    string Label,
    IReadOnlyList<int[]> SourceCells);

public sealed record GraphEvent(
    string Id,
    string LocalId,
    string Module,
    string File,
    string JsonPath,
    string Kind,
    string Summary,
    string? SceneId,
    string? SceneNode,
    string? Picture,
    string? Sound,
    string? Music,
    string? Encounter,
    string? TeleportArea,
    string? TeleportEntry,
    string? OpenDoor,
    bool Reachable,
    IReadOnlyList<GraphEventTrigger> Triggers);

public sealed record GraphEventTrigger(
    string Kind,
    string? AreaId,
    int? X,
    int? Y,
    string? DoorId,
    string? Facing,
    bool Once);

public sealed record GraphEventEdge(
    string From,
    string To,
    string Kind,
    string Label,
    string? Guard);

public sealed record GraphAsset(
    string Id,
    string LocalId,
    string Module,
    string File,
    string Media,
    string Sampling,
    IReadOnlyList<string> Tags,
    int[]? FrameSize,
    int? FrameCount,
    IReadOnlyList<string> UsedBy);

public sealed record GraphIssue(
    string Severity,
    string Code,
    string Message,
    string? Module = null,
    string? File = null,
    string? JsonPath = null,
    string? AreaId = null,
    int[]? Cell = null,
    string? EventId = null,
    string? DoorId = null);

/// <summary>Builds a <see cref="ModuleGraph"/> from a loaded <see cref="ModuleSet"/>.</summary>
public static class ModuleGraphBuilder
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static ModuleGraph Build(ModuleSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        ModuleManifest? root = set.Root;
        GraphModuleInfo moduleInfo = root is null
            ? new GraphModuleInfo("unknown", "0.0.0", "unknown", "Unloaded module", "", "", false)
            : new GraphModuleInfo(
                root.Id,
                root.Version.ToString(),
                ModuleKinds.Name(root.Kind),
                root.Title,
                root.Source.Location,
                root.Source.Identity,
                set.IsValid);

        List<GraphDependencyInfo> deps = set.LoadOrder
            .Select(loaded => loaded.Manifest)
            .Where(manifest => root is null || manifest.Id != root.Id)
            .Select(manifest => new GraphDependencyInfo(
                manifest.Id,
                manifest.Version.ToString(),
                ModuleKinds.Name(manifest.Kind),
                manifest.Title,
                manifest.Source.Location))
            .ToList();

        List<GraphIssue> issues = [];
        foreach (ModuleDiagnostic diagnostic in set.Diagnostics)
        {
            issues.Add(new GraphIssue(
                "error",
                diagnostic.Rule,
                diagnostic.Message,
                diagnostic.Module,
                diagnostic.File,
                diagnostic.JsonPath));
        }

        if (set.Rules is not RuleSet rules)
        {
            return new ModuleGraph(
                moduleInfo,
                deps,
                set.Extensions,
                null,
                new GraphCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, issues.Count),
                [],
                [],
                [],
                [],
                [],
                issues);
        }

        Definition? campaignDef = root is null
            ? rules.OfType(DefinitionTypes.Campaign).FirstOrDefault()
            : rules.OfType(DefinitionTypes.Campaign).FirstOrDefault(def => def.Module == root.Id)
              ?? rules.OfType(DefinitionTypes.Campaign).FirstOrDefault();

        GraphCampaignInfo? campaignInfo = null;
        string? startAreaId = null;
        string? startEntryName = null;
        string? introEventId = null;

        if (campaignDef is not null)
        {
            Definition startArea = rules.Reference(campaignDef, "$.start.area");
            startAreaId = startArea.QualifiedId;
            startEntryName = campaignDef.Json.GetProperty("start").GetProperty("entry").GetString()!;
            introEventId = TryRef(rules, campaignDef, "$.intro")?.QualifiedId;
            JsonElement party = campaignDef.Json.GetProperty("party");
            campaignInfo = new GraphCampaignInfo(
                campaignDef.QualifiedId,
                campaignDef.Id,
                campaignDef.Name,
                campaignDef.File,
                startAreaId,
                startEntryName,
                introEventId,
                party.GetProperty("min").GetInt32(),
                party.GetProperty("max").GetInt32(),
                campaignDef.Json.TryGetProperty("starting_party", out _),
                TryString(campaignDef.Json, "combat_presentation"),
                TryRef(rules, campaignDef, "$.skin")?.QualifiedId);
        }

        // Index all events and outgoing event edges.
        List<Definition> eventDefs = rules.OfType(DefinitionTypes.Event).ToList();
        Dictionary<string, Definition> eventsById = eventDefs.ToDictionary(def => def.QualifiedId, StringComparer.Ordinal);
        Dictionary<string, List<GraphEventEdge>> outEdges = eventDefs.ToDictionary(def => def.QualifiedId, _ => new List<GraphEventEdge>(), StringComparer.Ordinal);
        List<GraphEventEdge> allEventEdges = [];

        Dictionary<string, (string TargetAreaId, string TargetEntry)> teleports = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> openEventsByDoorId = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> itemSourcesByItemId = new(StringComparer.Ordinal);
        Dictionary<string, List<GraphEventTrigger>> triggersByEvent = eventDefs.ToDictionary(def => def.QualifiedId, _ => new List<GraphEventTrigger>(), StringComparer.Ordinal);

        if (introEventId is not null && triggersByEvent.TryGetValue(introEventId, out List<GraphEventTrigger>? introTriggers))
        {
            introTriggers.Add(new GraphEventTrigger("campaign_intro", startAreaId, null, null, null, null, true));
        }

        foreach (Definition evt in eventDefs)
        {
            string kind = evt.Json.GetProperty("kind").GetString()!;
            void AddEdge(string targetId, string edgeKind, string label, string? guard = null)
            {
                GraphEventEdge edge = new(evt.QualifiedId, targetId, edgeKind, label, guard);
                outEdges[evt.QualifiedId].Add(edge);
                allEventEdges.Add(edge);
            }

            if (TryRef(rules, evt, "$.next") is Definition nextDef)
            {
                AddEdge(nextDef.QualifiedId, "next", "next");
            }

            switch (kind)
            {
                case "menu" when evt.Json.TryGetProperty("options", out JsonElement options):
                {
                    int idx = 0;
                    foreach (JsonElement opt in options.EnumerateArray())
                    {
                        if (TryRef(rules, evt, $"$.options[{idx}].next") is Definition optNext)
                        {
                            string label = TryString(opt, "label") ?? $"Option {idx + 1}";
                            string? guard = TryString(opt, "when");
                            AddEdge(optNext.QualifiedId, "choice", label, guard);
                        }

                        idx++;
                    }

                    break;
                }
                case "branch":
                {
                    if (evt.Json.TryGetProperty("branches", out JsonElement branches))
                    {
                        int idx = 0;
                        foreach (JsonElement br in branches.EnumerateArray())
                        {
                            if (TryRef(rules, evt, $"$.branches[{idx}].next") is Definition brNext)
                            {
                                string? guard = TryString(br, "when");
                                AddEdge(brNext.QualifiedId, "branch", guard ?? $"branch {idx + 1}", guard);
                            }

                            idx++;
                        }
                    }

                    if (TryRef(rules, evt, "$.otherwise") is Definition otherwiseDef)
                    {
                        AddEdge(otherwiseDef.QualifiedId, "otherwise", "otherwise");
                    }

                    break;
                }
                case "check":
                {
                    if (TryRef(rules, evt, "$.on_success") is Definition succ)
                    {
                        AddEdge(succ.QualifiedId, "success", "on_success");
                    }

                    if (TryRef(rules, evt, "$.on_failure") is Definition fail)
                    {
                        AddEdge(fail.QualifiedId, "failure", "on_failure");
                    }

                    break;
                }
                case "combat":
                {
                    if (TryRef(rules, evt, "$.on_win") is Definition win)
                    {
                        AddEdge(win.QualifiedId, "win", "on_win");
                    }

                    if (TryRef(rules, evt, "$.on_lose") is Definition lose)
                    {
                        AddEdge(lose.QualifiedId, "lose", "on_lose");
                    }

                    if (TryRef(rules, evt, "$.on_draw") is Definition draw)
                    {
                        AddEdge(draw.QualifiedId, "draw", "on_draw");
                    }

                    if (TryRef(rules, evt, "$.on_flee") is Definition flee)
                    {
                        AddEdge(flee.QualifiedId, "flee", "on_flee");
                    }

                    break;
                }
                case "take" or "join" or "dismiss":
                {
                    if (TryRef(rules, evt, "$.on_refused") is Definition refused)
                    {
                        AddEdge(refused.QualifiedId, "refused", "on_refused");
                    }

                    break;
                }
                case "rest":
                {
                    if (TryRef(rules, evt, "$.wandering.event") is Definition wandering)
                    {
                        AddEdge(wandering.QualifiedId, "wandering", "wandering", TryString(evt.Json.GetProperty("wandering"), "when"));
                    }

                    break;
                }
                case "teleport":
                {
                    if (TryRef(rules, evt, "$.area") is Definition destArea && TryString(evt.Json, "entry") is string destEntry)
                    {
                        teleports[evt.QualifiedId] = (destArea.QualifiedId, destEntry);
                    }

                    break;
                }
                case "open":
                {
                    if (TryString(evt.Json, "door") is string doorId)
                    {
                        AddToMap(openEventsByDoorId, doorId, evt.QualifiedId);
                    }

                    break;
                }
                case "give":
                {
                    if (TryRef(rules, evt, "$.item") is Definition givenItem)
                    {
                        AddToMap(itemSourcesByItemId, givenItem.QualifiedId, evt.QualifiedId);
                    }

                    break;
                }
                case "treasure":
                {
                    if (evt.Json.TryGetProperty("items", out JsonElement itemsEl))
                    {
                        for (int i = 0; i < itemsEl.GetArrayLength(); i++)
                        {
                            if (TryRef(rules, evt, $"$.items[{i}]") is Definition treasureItem)
                            {
                                AddToMap(itemSourcesByItemId, treasureItem.QualifiedId, evt.QualifiedId);
                            }
                        }
                    }

                    break;
                }
                case "shop":
                {
                    if (evt.Json.TryGetProperty("items", out JsonElement shopItems))
                    {
                        for (int i = 0; i < shopItems.GetArrayLength(); i++)
                        {
                            if (TryRef(rules, evt, $"$.items[{i}].item") is Definition shopItem)
                            {
                                AddToMap(itemSourcesByItemId, shopItem.QualifiedId, evt.QualifiedId);
                            }
                        }
                    }

                    break;
                }
            }
        }

        // Parse areas into mutable builders so we can compute cross-area reachability cleanly.
        List<Definition> areaDefs = rules.OfType(DefinitionTypes.Area).ToList();
        Dictionary<string, AreaAnalysis> areaAnalyses = new(StringComparer.Ordinal);
        foreach (Definition areaDef in areaDefs)
        {
            AreaAnalysis analysis = ParseArea(rules, areaDef, startAreaId, startEntryName, eventsById, triggersByEvent);
            areaAnalyses[areaDef.QualifiedId] = analysis;
        }

        // Iterative fixed-point reachability across areas, entries, walkable cells, doors, and event chains.
        HashSet<string> reachableAreas = new(StringComparer.Ordinal);
        HashSet<(string AreaId, string EntryName)> reachableEntries = [];
        HashSet<string> reachableEvents = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> eventAreas = eventDefs.ToDictionary(def => def.QualifiedId, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        if (startAreaId is not null && areaAnalyses.ContainsKey(startAreaId))
        {
            reachableAreas.Add(startAreaId);
            if (startEntryName is not null)
            {
                reachableEntries.Add((startAreaId, startEntryName));
            }
        }

        if (introEventId is not null && eventsById.ContainsKey(introEventId))
        {
            reachableEvents.Add(introEventId);
            if (startAreaId is not null)
            {
                eventAreas[introEventId].Add(startAreaId);
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;

            // 1. Walk each reachable area from its reachable entries to mark cell/door events reachable.
            foreach (string areaId in reachableAreas.ToList())
            {
                AreaAnalysis area = areaAnalyses[areaId];
                List<(int X, int Y)> seeds = area.Entries
                    .Where(entry => reachableEntries.Contains((areaId, entry.Name)))
                    .Select(entry => (entry.X, entry.Y))
                    .ToList();
                if (seeds.Count == 0)
                {
                    continue;
                }

                HashSet<(int X, int Y)> allWalkable = WalkArea(area, seeds, allowLockedDoors: true, allowSecretDoors: true);
                foreach (CellInfo cell in area.Cells.Values)
                {
                    if (cell.EventId is not null && allWalkable.Contains((cell.X, cell.Y)))
                    {
                        if (reachableEvents.Add(cell.EventId))
                        {
                            changed = true;
                        }

                        if (eventAreas[cell.EventId].Add(areaId))
                        {
                            changed = true;
                        }
                    }
                }

                foreach (DoorInfo door in area.Doors)
                {
                    (int nx, int ny) = Step(door.X, door.Y, door.Facing);
                    bool doorTouched = allWalkable.Contains((door.X, door.Y)) || allWalkable.Contains((nx, ny));
                    if (doorTouched && door.UnlockEvent is not null)
                    {
                        if (reachableEvents.Add(door.UnlockEvent))
                        {
                            changed = true;
                        }

                        if (eventAreas[door.UnlockEvent].Add(areaId))
                        {
                            changed = true;
                        }
                    }
                }
            }

            // 2. Propagate along event edges and teleports.
            foreach (string evtId in reachableEvents.ToList())
            {
                HashSet<string> sourceAreas = eventAreas[evtId];
                foreach (GraphEventEdge edge in outEdges[evtId])
                {
                    if (reachableEvents.Add(edge.To))
                    {
                        changed = true;
                    }

                    foreach (string a in sourceAreas)
                    {
                        if (eventAreas[edge.To].Add(a))
                        {
                            changed = true;
                        }
                    }
                }

                if (teleports.TryGetValue(evtId, out (string TargetArea, string TargetEntry) dest) && areaAnalyses.ContainsKey(dest.TargetArea))
                {
                    if (reachableAreas.Add(dest.TargetArea))
                    {
                        changed = true;
                    }

                    if (reachableEntries.Add((dest.TargetArea, dest.TargetEntry)))
                    {
                        changed = true;
                    }

                    // If a teleport chains to a `next` event, that next event runs in the destination area.
                    foreach (GraphEventEdge edge in outEdges[evtId])
                    {
                        if (eventAreas[edge.To].Add(dest.TargetArea))
                        {
                            changed = true;
                        }
                    }
                }
            }
        }

        // If there is no campaign definition (e.g. standalone ruleset or partial module), treat all areas/events as unflagged for campaign reachability.
        bool hasCampaign = campaignDef is not null;

        // Build Navigation Links (area -> area teleports, open events -> doors, key sources -> locked doors).
        List<GraphNavLink> navLinks = [];
        Dictionary<(string AreaId, string EntryName), int> incomingTeleportsByEntry = [];
        Dictionary<string, HashSet<string>> outgoingAreasByArea = areaAnalyses.Keys.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        Dictionary<string, bool> areaHasEnd = areaAnalyses.Keys.ToDictionary(id => id, _ => false, StringComparer.Ordinal);

        foreach (Definition evt in eventDefs)
        {
            if (evt.Json.GetProperty("kind").GetString() == "end")
            {
                foreach (string a in eventAreas[evt.QualifiedId])
                {
                    areaHasEnd[a] = true;
                }
            }
        }

        // Map each event back to the area cells that can trigger it (transitively within the event graph).
        Dictionary<string, List<(string AreaId, int X, int Y)>> cellSourcesByEvent = FindEventSourceCells(areaAnalyses, outEdges, eventDefs);

        foreach ((string teleportEventId, (string destArea, string destEntry)) in teleports)
        {
            if (!areaAnalyses.ContainsKey(destArea))
            {
                continue;
            }

            List<(string AreaId, int X, int Y)> sources = cellSourcesByEvent.TryGetValue(teleportEventId, out List<(string AreaId, int X, int Y)>? found)
                ? found
                : [];
            IEnumerable<string> fromAreas = sources.Count > 0
                ? sources.Select(s => s.AreaId).Distinct(StringComparer.Ordinal)
                : eventAreas[teleportEventId].Count > 0
                    ? eventAreas[teleportEventId]
                    : startAreaId is not null ? [startAreaId] : [];

            foreach (string fromArea in fromAreas)
            {
                List<int[]> cells = sources.Where(s => s.AreaId == fromArea).Select(s => new[] { s.X, s.Y }).ToList();
                string destName = areaAnalyses[destArea].Name;
                navLinks.Add(new GraphNavLink(
                    "teleport",
                    fromArea,
                    destArea,
                    destEntry,
                    teleportEventId,
                    null,
                    null,
                    $"→ {destName} ({destEntry})",
                    cells));
                incomingTeleportsByEntry[(destArea, destEntry)] = incomingTeleportsByEntry.GetValueOrDefault((destArea, destEntry)) + 1;
                if (fromArea != destArea)
                {
                    outgoingAreasByArea[fromArea].Add(destArea);
                }
            }
        }

        // Build final GraphArea records and check area/door/cell issues.
        List<GraphArea> graphAreas = [];
        int totalDoors = 0;
        int totalSecrets = 0;

        foreach (AreaAnalysis area in areaAnalyses.Values.OrderBy(a => !a.IsStartArea).ThenBy(a => a.Id, StringComparer.Ordinal))
        {
            bool areaReachable = !hasCampaign || reachableAreas.Contains(area.Id);
            List<(int X, int Y)> seedPoints = area.Entries
                .Where(e => !hasCampaign || reachableEntries.Contains((area.Id, e.Name)))
                .Select(e => (e.X, e.Y))
                .ToList();
            if (seedPoints.Count == 0 && !hasCampaign && area.Entries.Count > 0)
            {
                seedPoints.Add((area.Entries[0].X, area.Entries[0].Y));
            }

            HashSet<(int X, int Y)> openCells = seedPoints.Count > 0 ? WalkArea(area, seedPoints, allowLockedDoors: false, allowSecretDoors: false) : [];
            HashSet<(int X, int Y)> lockedCells = seedPoints.Count > 0 ? WalkArea(area, seedPoints, allowLockedDoors: true, allowSecretDoors: false) : [];
            HashSet<(int X, int Y)> secretCells = seedPoints.Count > 0 ? WalkArea(area, seedPoints, allowLockedDoors: true, allowSecretDoors: true) : [];

            totalSecrets += CountSecretEdges(area.Map);
            totalDoors += area.Doors.Count;

            List<GraphEntry> entries = area.Entries.Select(entry => new GraphEntry(
                entry.Name,
                entry.X,
                entry.Y,
                Facings.Name(entry.Facing),
                entry.IsCampaignStart,
                !hasCampaign || reachableEntries.Contains((area.Id, entry.Name)),
                incomingTeleportsByEntry.GetValueOrDefault((area.Id, entry.Name))))
                .ToList();

            List<GraphDoor> doors = [];
            foreach (DoorInfo door in area.Doors)
            {
                (int nx, int ny) = Step(door.X, door.Y, door.Facing);
                bool doorReachable = areaReachable && (secretCells.Contains((door.X, door.Y)) || secretCells.Contains((nx, ny)));
                List<string> openedBy = openEventsByDoorId.TryGetValue(door.Id, out List<string>? openers)
                    ? openers.Where(id => eventAreas[id].Contains(area.Id) || !hasCampaign).Distinct(StringComparer.Ordinal).ToList()
                    : [];
                List<string> keySources = door.KeyItem is not null && itemSourcesByItemId.TryGetValue(door.KeyItem, out List<string>? sources)
                    ? sources.Distinct(StringComparer.Ordinal).ToList()
                    : [];

                doors.Add(new GraphDoor(
                    door.Id,
                    door.X,
                    door.Y,
                    Facings.Name(door.Facing),
                    door.Locked,
                    door.KeyItem,
                    door.PickCheck,
                    door.ForceCheck,
                    door.UnlockEvent,
                    openedBy,
                    keySources,
                    doorReachable));

                if (door.KeyItem is not null)
                {
                    foreach (string srcEvent in keySources)
                    {
                        foreach (string srcArea in eventAreas[srcEvent].DefaultIfEmpty(area.Id))
                        {
                            navLinks.Add(new GraphNavLink(
                                "key_source",
                                srcArea,
                                area.Id,
                                null,
                                srcEvent,
                                door.Id,
                                door.KeyItem,
                                $"Key {ShortId(door.KeyItem)} → door {door.Id}",
                                []));
                        }
                    }

                    bool hasAnyReachableKey = keySources.Any(src => !hasCampaign || reachableEvents.Contains(src));
                    bool hasOtherUnlock = door.PickCheck is not null || door.ForceCheck is not null || openedBy.Any(op => !hasCampaign || reachableEvents.Contains(op));
                    if (hasCampaign && door.Locked && doorReachable && !hasAnyReachableKey && !hasOtherUnlock)
                    {
                        issues.Add(new GraphIssue(
                            "warning",
                            "locked_door_no_key_source",
                            $"Locked door '{door.Id}' in {area.Name} requires key '{door.KeyItem}', but no reachable give/treasure/shop event grants it (and no pick/force/open unlock exists).",
                            area.Module,
                            area.File,
                            "$.doors",
                            area.Id,
                            [door.X, door.Y],
                            DoorId: door.Id));
                    }
                }
            }

            List<GraphCell> cells = [];
            for (int y = 0; y < area.Map.Height; y++)
            {
                for (int x = 0; x < area.Map.Width; x++)
                {
                    string reachability = !areaReachable
                        ? "unreachable"
                        : openCells.Contains((x, y))
                            ? "open"
                            : lockedCells.Contains((x, y))
                                ? "locked"
                                : secretCells.Contains((x, y))
                                    ? "secret"
                                    : "unreachable";

                    area.Cells.TryGetValue((x, y), out CellInfo? info);
                    cells.Add(new GraphCell(
                        x,
                        y,
                        info?.Zone,
                        info?.EventId,
                        info?.EventKind,
                        info?.EventSummary,
                        info?.Facing,
                        info?.Once ?? false,
                        info?.Backdrop,
                        info?.PropSprite,
                        info?.PropHidden,
                        reachability));

                    if (hasCampaign && areaReachable && info?.EventId is not null)
                    {
                        if (reachability == "unreachable")
                        {
                            issues.Add(new GraphIssue(
                                "warning",
                                "unreachable_cell_event",
                                $"Cell [{x}, {y}] in {area.Name} triggers '{info.EventId}' but is walled off from every reachable entry in the area.",
                                area.Module,
                                area.File,
                                "$.cells",
                                area.Id,
                                [x, y],
                                info.EventId));
                        }
                        else if (reachability == "secret")
                        {
                            issues.Add(new GraphIssue(
                                "info",
                                "secret_only_event",
                                $"Cell [{x}, {y}] in {area.Name} ('{info.EventId}') is only reachable through a secret door.",
                                area.Module,
                                area.File,
                                "$.cells",
                                area.Id,
                                [x, y],
                                info.EventId));
                        }
                    }
                }
            }

            if (hasCampaign && !areaReachable)
            {
                issues.Add(new GraphIssue(
                    "warning",
                    "unreachable_area",
                    $"Area '{area.Name}' ({area.Id}) cannot be reached from campaign start '{startAreaId}' via teleports.",
                    area.Module,
                    area.File,
                    "$",
                    area.Id));
            }
            else if (hasCampaign && areaReachable && areaAnalyses.Count > 1 && outgoingAreasByArea[area.Id].Count == 0 && !areaHasEnd[area.Id])
            {
                issues.Add(new GraphIssue(
                    "warning",
                    "dead_end_area",
                    $"Area '{area.Name}' ({area.Id}) has no outgoing teleport to another area and no reachable end event.",
                    area.Module,
                    area.File,
                    "$",
                    area.Id));
            }

            graphAreas.Add(new GraphArea(
                area.Id,
                area.LocalId,
                area.Name,
                area.Module,
                area.File,
                area.Map.Width,
                area.Map.Height,
                area.WallSet,
                area.IsStartArea,
                areaReachable,
                areaHasEnd[area.Id],
                outgoingAreasByArea[area.Id].Count,
                ExtractHorizontalEdges(area.Map),
                ExtractVerticalEdges(area.Map),
                entries,
                doors,
                cells));
        }

        // Build GraphEvent list and flag unreachable events.
        HashSet<string> sceneIds = new(StringComparer.Ordinal);
        List<GraphEvent> graphEvents = [];
        foreach (Definition evt in eventDefs.OrderBy(def => def.QualifiedId, StringComparer.Ordinal))
        {
            string kind = evt.Json.GetProperty("kind").GetString()!;
            (string? sceneId, string? sceneNode) = DetectSceneOrigin(evt);
            if (sceneId is not null)
            {
                sceneIds.Add($"{evt.Module}:{sceneId}");
            }

            bool isReachable = !hasCampaign || reachableEvents.Contains(evt.QualifiedId);
            if (hasCampaign && !isReachable && (root is null || evt.Module == root.Id))
            {
                issues.Add(new GraphIssue(
                    "warning",
                    "unreachable_event",
                    $"Event '{evt.QualifiedId}' ({kind}) is never reached from campaign intro, any reachable cell trigger, or any door event.",
                    evt.Module,
                    evt.File,
                    evt.DiagnosticLocation("$").JsonPath,
                    EventId: evt.QualifiedId));
            }

            teleports.TryGetValue(evt.QualifiedId, out (string TargetArea, string TargetEntry) tp);
            graphEvents.Add(new GraphEvent(
                evt.QualifiedId,
                evt.Id,
                evt.Module,
                evt.File,
                evt.DiagnosticLocation("$").JsonPath,
                kind,
                SummarizeEvent(rules, evt, kind),
                sceneId,
                sceneNode,
                TryRef(rules, evt, "$.picture")?.QualifiedId,
                TryRef(rules, evt, "$.sound")?.QualifiedId,
                TryRef(rules, evt, "$.music")?.QualifiedId,
                TryRef(rules, evt, "$.encounter")?.QualifiedId,
                tp.TargetArea,
                tp.TargetEntry,
                kind == "open" ? TryString(evt.Json, "door") : null,
                isReachable,
                triggersByEvent[evt.QualifiedId]));
        }

        // Build Asset usage index.
        Dictionary<string, HashSet<string>> assetUsage = rules.OfType(DefinitionTypes.Asset)
            .ToDictionary(def => def.QualifiedId, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (Definition def in rules.Definitions)
        {
            foreach (ReferenceSite site in def.References)
            {
                if (site.Kind is ReferenceKind { DefinitionType: "asset" })
                {
                    if (TryRef(rules, def, site.JsonPath) is Definition asset && assetUsage.TryGetValue(asset.QualifiedId, out HashSet<string>? users))
                    {
                        users.Add($"{def.Type.Name}:{def.QualifiedId}");
                    }
                }
            }
        }

        List<GraphAsset> graphAssets = rules.OfType(DefinitionTypes.Asset)
            .OrderBy(def => def.QualifiedId, StringComparer.Ordinal)
            .Select(asset => new GraphAsset(
                asset.QualifiedId,
                asset.Id,
                asset.Module,
                asset.Json.GetProperty("file").GetString()!,
                asset.Json.GetProperty("media").GetString()!,
                TryString(asset.Json, "sampling") ?? "nearest",
                asset.Json.TryGetProperty("tags", out JsonElement tags)
                    ? tags.EnumerateArray().Select(t => t.GetString()!).ToList()
                    : [],
                asset.Json.TryGetProperty("frame_size", out JsonElement fs)
                    ? [fs[0].GetInt32(), fs[1].GetInt32()]
                    : null,
                asset.Json.TryGetProperty("frame_count", out JsonElement fc) ? fc.GetInt32() : null,
                assetUsage[asset.QualifiedId].OrderBy(u => u, StringComparer.Ordinal).ToList()))
            .ToList();

        GraphCounts counts = new(
            graphAreas.Count,
            graphEvents.Count,
            sceneIds.Count,
            totalDoors,
            totalSecrets,
            rules.OfType(DefinitionTypes.Encounter).Count(),
            rules.OfType(DefinitionTypes.Monster).Count(),
            rules.OfType(DefinitionTypes.Item).Count(),
            rules.OfType(DefinitionTypes.Npc).Count(),
            rules.OfType(DefinitionTypes.Variable).Count(),
            graphAssets.Count,
            rules.OfType(DefinitionTypes.Figure).Count(),
            issues.Count);

        return new ModuleGraph(
            moduleInfo,
            deps,
            set.Extensions,
            campaignInfo,
            counts,
            graphAreas,
            navLinks,
            graphEvents,
            allEventEdges,
            graphAssets,
            issues);
    }

    private static AreaAnalysis ParseArea(
        RuleSet rules,
        Definition areaDef,
        string? startAreaId,
        string? startEntryName,
        Dictionary<string, Definition> eventsById,
        Dictionary<string, List<GraphEventTrigger>> triggersByEvent)
    {
        List<string> rows = areaDef.Json.GetProperty("map").EnumerateArray().Select(row => row.GetString()!).ToList();
        AreaMap map = AreaMap.Parse(rows, [])!;
        string? wallSet = TryRef(rules, areaDef, "$.wall_set")?.QualifiedId;

        List<EntryInfo> entries = [];
        foreach (JsonProperty prop in areaDef.Json.GetProperty("entries").EnumerateObject())
        {
            JsonElement at = prop.Value.GetProperty("at");
            Facings.TryParse(prop.Value.GetProperty("facing").GetString()!, out Facing facing);
            bool isStart = areaDef.QualifiedId == startAreaId && prop.Name == startEntryName;
            entries.Add(new EntryInfo(prop.Name, at[0].GetInt32(), at[1].GetInt32(), facing, isStart));
        }

        List<DoorInfo> doors = [];
        HashSet<string> lockedEdgeKeys = new(StringComparer.Ordinal);
        if (areaDef.Json.TryGetProperty("doors", out JsonElement doorsEl))
        {
            int idx = 0;
            foreach (JsonElement doorEl in doorsEl.EnumerateArray())
            {
                string id = doorEl.GetProperty("id").GetString()!;
                JsonElement at = doorEl.GetProperty("at");
                int x = at[0].GetInt32();
                int y = at[1].GetInt32();
                Facings.TryParse(doorEl.GetProperty("facing").GetString()!, out Facing facing);
                bool locked = !doorEl.TryGetProperty("locked", out JsonElement lockedEl) || lockedEl.GetBoolean();
                string? keyItem = TryRef(rules, areaDef, $"$.doors[{idx}].key")?.QualifiedId;
                string? pickCheck = TryRef(rules, areaDef, $"$.doors[{idx}].pick")?.QualifiedId;
                string? forceCheck = TryRef(rules, areaDef, $"$.doors[{idx}].force")?.QualifiedId;
                string? unlockEvent = TryRef(rules, areaDef, $"$.doors[{idx}].event")?.QualifiedId;

                if (locked)
                {
                    lockedEdgeKeys.Add(new AreaEdge(x, y, facing).Key);
                }

                if (unlockEvent is not null && triggersByEvent.TryGetValue(unlockEvent, out List<GraphEventTrigger>? doorTriggers))
                {
                    doorTriggers.Add(new GraphEventTrigger("door", areaDef.QualifiedId, x, y, id, Facings.Name(facing), false));
                }

                doors.Add(new DoorInfo(id, x, y, facing, locked, keyItem, pickCheck, forceCheck, unlockEvent));
                idx++;
            }
        }

        Dictionary<(int X, int Y), CellInfo> cells = [];
        if (areaDef.Json.TryGetProperty("cells", out JsonElement cellsEl))
        {
            int idx = 0;
            foreach (JsonElement cellEl in cellsEl.EnumerateArray())
            {
                JsonElement at = cellEl.GetProperty("at");
                int x = at[0].GetInt32();
                int y = at[1].GetInt32();
                Definition? evtDef = TryRef(rules, areaDef, $"$.cells[{idx}].event");
                string? evtKind = evtDef is not null ? evtDef.Json.GetProperty("kind").GetString() : null;
                string? evtSummary = evtDef is not null && evtKind is not null ? SummarizeEvent(rules, evtDef, evtKind) : null;
                string? facing = TryString(cellEl, "facing");
                bool once = cellEl.TryGetProperty("once", out JsonElement onceEl) && onceEl.GetBoolean();
                string? backdrop = TryRef(rules, areaDef, $"$.cells[{idx}].backdrop")?.QualifiedId;
                string? propSprite = TryRef(rules, areaDef, $"$.cells[{idx}].prop.sprite")?.QualifiedId;
                string? propHidden = cellEl.TryGetProperty("prop", out JsonElement propEl) ? TryString(propEl, "hidden") : null;

                if (evtDef is not null && triggersByEvent.TryGetValue(evtDef.QualifiedId, out List<GraphEventTrigger>? cellTriggers))
                {
                    cellTriggers.Add(new GraphEventTrigger("cell", areaDef.QualifiedId, x, y, null, facing, once));
                }

                cells[(x, y)] = new CellInfo(x, y, TryString(cellEl, "zone"), evtDef?.QualifiedId, evtKind, evtSummary, facing, once, backdrop, propSprite, propHidden);
                idx++;
            }
        }

        return new AreaAnalysis(areaDef.QualifiedId, areaDef.Id, areaDef.Name, areaDef.Module, areaDef.File, wallSet, areaDef.QualifiedId == startAreaId, map, entries, doors, lockedEdgeKeys, cells);
    }

    private static HashSet<(int X, int Y)> WalkArea(AreaAnalysis area, IReadOnlyList<(int X, int Y)> seeds, bool allowLockedDoors, bool allowSecretDoors)
    {
        HashSet<(int X, int Y)> visited = [];
        Queue<(int X, int Y)> queue = new();
        foreach ((int x, int y) in seeds)
        {
            if (area.Map.Contains(x, y) && visited.Add((x, y)))
            {
                queue.Enqueue((x, y));
            }
        }

        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            foreach (Facing facing in Enum.GetValues<Facing>())
            {
                Edge edge = area.Map.EdgeOf(x, y, facing);
                bool passable = edge switch
                {
                    Edge.Open => true,
                    Edge.Door => allowLockedDoors || !area.LockedEdgeKeys.Contains(new AreaEdge(x, y, facing).Key),
                    Edge.Secret => allowSecretDoors,
                    _ => false,
                };
                if (!passable)
                {
                    continue;
                }

                (int nx, int ny) = Step(x, y, facing);
                if (area.Map.Contains(nx, ny) && visited.Add((nx, ny)))
                {
                    queue.Enqueue((nx, ny));
                }
            }
        }

        return visited;
    }

    private static Dictionary<string, List<(string AreaId, int X, int Y)>> FindEventSourceCells(
        Dictionary<string, AreaAnalysis> areas,
        Dictionary<string, List<GraphEventEdge>> outEdges,
        List<Definition> events)
    {
        Dictionary<string, List<(string AreaId, int X, int Y)>> result = [];
        foreach (AreaAnalysis area in areas.Values)
        {
            foreach (CellInfo cell in area.Cells.Values)
            {
                if (cell.EventId is null)
                {
                    continue;
                }

                HashSet<string> seen = [cell.EventId];
                Queue<string> queue = new();
                queue.Enqueue(cell.EventId);
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();
                    if (!result.TryGetValue(current, out List<(string AreaId, int X, int Y)>? list))
                    {
                        list = [];
                        result[current] = list;
                    }

                    list.Add((area.Id, cell.X, cell.Y));
                    if (outEdges.TryGetValue(current, out List<GraphEventEdge>? edges))
                    {
                        foreach (GraphEventEdge edge in edges)
                        {
                            if (seen.Add(edge.To))
                            {
                                queue.Enqueue(edge.To);
                            }
                        }
                    }
                }
            }
        }

        return result;
    }

    private static (string? SceneId, string? SceneNode) DetectSceneOrigin(Definition evt)
    {
        if (evt.FieldOrigins is null || !evt.FieldOrigins.TryGetValue("$", out FieldOrigin? rootOrigin))
        {
            return (null, null);
        }

        string? exact = rootOrigin.ExactPath;
        int nodesMarker = exact is null ? -1 : exact.IndexOf(".nodes.", StringComparison.Ordinal);
        if (exact is null || nodesMarker < 0)
        {
            return (null, null);
        }

        string remainder = exact[(nodesMarker + ".nodes.".Length)..];
        int dot = remainder.IndexOf('.');
        string nodeName = dot < 0 ? remainder : remainder[..dot];
        int sep = evt.Id.IndexOf("__", StringComparison.Ordinal);
        string sceneId = sep < 0 ? evt.Id : evt.Id[..sep];
        return (sceneId, nodeName);
    }

    private static string SummarizeEvent(RuleSet rules, Definition evt, string kind)
    {
        return kind switch
        {
            "text" => Truncate(TryString(evt.Json, "text") ?? "Text", 72),
            "menu" => $"Menu: {Truncate(TryString(evt.Json, "text") ?? evt.Id, 60)}",
            "combat" => $"Combat: {TryRef(rules, evt, "$.encounter")?.Name ?? TryString(evt.Json, "encounter") ?? evt.Id}",
            "teleport" => $"Teleport → {TryRef(rules, evt, "$.area")?.Name ?? TryString(evt.Json, "area")} ({TryString(evt.Json, "entry")})",
            "branch" => $"Branch ({(evt.Json.TryGetProperty("branches", out JsonElement b) ? b.GetArrayLength() : 0)} rules)",
            "check" => $"Check: {TryRef(rules, evt, "$.check")?.Name ?? TryString(evt.Json, "check")}",
            "open" => $"Open door '{TryString(evt.Json, "door")}'",
            "set" => $"Set {TryString(evt.Json, "variable")} = {TryString(evt.Json, "value")}",
            "give" => $"Give {TryRef(rules, evt, "$.item")?.Name ?? TryString(evt.Json, "item")}",
            "take" => $"Take {TryRef(rules, evt, "$.item")?.Name ?? TryString(evt.Json, "item")}",
            "treasure" => $"Treasure: {TryString(evt.Json, "amount") ?? "items"} {ShortId(TryString(evt.Json, "currency"))}",
            "experience" => $"XP: {TryString(evt.Json, "amount")}",
            "shop" => $"Shop: {Truncate(TryString(evt.Json, "text") ?? evt.Id, 56)}",
            "temple" => $"Temple: {Truncate(TryString(evt.Json, "text") ?? evt.Id, 56)}",
            "training" => $"Trainer: {Truncate(TryString(evt.Json, "text") ?? evt.Id, 56)}",
            "rest" => $"Rest: {Truncate(TryString(evt.Json, "text") ?? evt.Id, 56)}",
            "join" => $"Join NPC: {TryRef(rules, evt, "$.npc")?.Name ?? TryString(evt.Json, "npc")}",
            "dismiss" => $"Dismiss NPC: {TryRef(rules, evt, "$.npc")?.Name ?? TryString(evt.Json, "npc")}",
            "end" => $"END: {Truncate(TryString(evt.Json, "text") ?? "Adventure ends", 64)}",
            _ => $"{kind}: {evt.Id}",
        };
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExtractHorizontalEdges(AreaMap map)
    {
        List<IReadOnlyList<string>> rows = [];
        for (int y = 0; y <= map.Height; y++)
        {
            List<string> row = [];
            for (int x = 0; x < map.Width; x++)
            {
                Edge edge = y < map.Height ? map.EdgeOf(x, y, Facing.North) : map.EdgeOf(x, map.Height - 1, Facing.South);
                row.Add(EdgeName(edge));
            }

            rows.Add(row);
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExtractVerticalEdges(AreaMap map)
    {
        List<IReadOnlyList<string>> rows = [];
        for (int y = 0; y < map.Height; y++)
        {
            List<string> row = [];
            for (int x = 0; x <= map.Width; x++)
            {
                Edge edge = x < map.Width ? map.EdgeOf(x, y, Facing.West) : map.EdgeOf(map.Width - 1, y, Facing.East);
                row.Add(EdgeName(edge));
            }

            rows.Add(row);
        }

        return rows;
    }

    private static int CountSecretEdges(AreaMap map)
    {
        HashSet<string> secrets = new(StringComparer.Ordinal);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                foreach (Facing facing in Enum.GetValues<Facing>())
                {
                    if (map.EdgeOf(x, y, facing) == Edge.Secret)
                    {
                        secrets.Add(new AreaEdge(x, y, facing).Key);
                    }
                }
            }
        }

        return secrets.Count;
    }

    private static string EdgeName(Edge edge) => edge switch
    {
        Edge.Wall => "wall",
        Edge.Door => "door",
        Edge.Secret => "secret",
        _ => "open",
    };

    private static (int X, int Y) Step(int x, int y, Facing facing)
    {
        (int dx, int dy) = Facings.Step(facing);
        return (x + dx, y + dy);
    }

    private static Definition? TryRef(RuleSet rules, Definition def, string jsonPath)
    {
        try
        {
            return rules.TryReference(def, jsonPath);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static string? TryString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;

    private static string ShortId(string? id) =>
        id is null ? "" : id.Contains(':') ? id.Split(':')[^1] : id;

    private static string Truncate(string text, int maxLength)
    {
        string singleLine = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..(maxLength - 1)] + "…";
    }

    private static void AddToMap(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out List<string>? list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(value);
    }

    private sealed record EntryInfo(string Name, int X, int Y, Facing Facing, bool IsCampaignStart);

    private sealed record DoorInfo(string Id, int X, int Y, Facing Facing, bool Locked, string? KeyItem, string? PickCheck, string? ForceCheck, string? UnlockEvent);

    private sealed record CellInfo(int X, int Y, string? Zone, string? EventId, string? EventKind, string? EventSummary, string? Facing, bool Once, string? Backdrop, string? PropSprite, string? PropHidden);

    private sealed record AreaAnalysis(
        string Id,
        string LocalId,
        string Name,
        string Module,
        string File,
        string? WallSet,
        bool IsStartArea,
        AreaMap Map,
        List<EntryInfo> Entries,
        List<DoorInfo> Doors,
        HashSet<string> LockedEdgeKeys,
        Dictionary<(int X, int Y), CellInfo> Cells);
}
