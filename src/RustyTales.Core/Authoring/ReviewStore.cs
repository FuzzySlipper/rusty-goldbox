using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;
using RustyTales.Core.Rules;

namespace RustyTales.Core.Authoring;

/// <summary>Allowed statuses for a Workbench review pin.</summary>
public static class ReviewStatuses
{
    public const string NeedsFix = "needs-fix";
    public const string Approved = "approved";
    public const string Question = "question";
    public const string Resolved = "resolved";

    public static IReadOnlyList<string> All { get; } = [NeedsFix, Approved, Question, Resolved];

    public static bool IsValid(string? status) =>
        status is NeedsFix or Approved or Question or Resolved;

    public static bool IsOpen(string status) => status is NeedsFix or Question;
}

/// <summary>Addressable target categories for Workbench review pins.</summary>
public static class ReviewTargetKinds
{
    public const string Area = "area";
    public const string Cell = "cell";
    public const string Door = "door";
    public const string Event = "event";
    public const string Asset = "asset";
    public const string Definition = "definition";

    public static IReadOnlyList<string> All { get; } = [Area, Cell, Door, Event, Asset, Definition];

    public static bool IsValid(string? kind) =>
        kind is Area or Cell or Door or Event or Asset or Definition;
}

/// <summary>A persisted human or agent review pin stored in the workspace sidecar file.</summary>
public sealed record ReviewPin(
    string Id,
    string TargetKind,
    string Target,
    string Status,
    string Note,
    string? Resolution,
    string Author,
    string? ReviewedRevision,
    string CreatedAt,
    string UpdatedAt,
    string? ResolvedAt = null,
    string? AreaId = null,
    int[]? Cell = null,
    string? DoorId = null,
    string? EventId = null);

/// <summary>A review pin enriched with live provenance and stale-approval detection from the loaded module set.</summary>
public sealed record ResolvedReviewPin(
    string Id,
    string TargetKind,
    string Target,
    string Status,
    bool Stale,
    bool Exists,
    string Note,
    string? Resolution,
    string Author,
    string? ReviewedRevision,
    string? CurrentRevision,
    string? Module,
    string? File,
    string? JsonPath,
    string Summary,
    string CreatedAt,
    string UpdatedAt,
    string? ResolvedAt,
    string? AreaId,
    int[]? Cell,
    string? DoorId,
    string? EventId);

/// <summary>Summary counts for a module's review pin queue.</summary>
public sealed record ReviewCounts(
    int Total,
    int Open,
    int NeedsFix,
    int Question,
    int Approved,
    int Stale,
    int Resolved);

/// <summary>On-disk JSON envelope for a module's review sidecar file.</summary>
public sealed record ReviewDocument(
    int Format,
    string Module,
    int NextSequence,
    List<ReviewPin> Pins);

/// <summary>
/// Atomic JSON sidecar persistence for Workbench review pins. Always stores
/// outside the module directory (under <c>.tales/reviews/&lt;id&gt;.json</c>)
/// so review annotations never alter a module's content identity or exports.
/// </summary>
public sealed class ReviewStore
{
    public const int CurrentFormat = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly object _gate = new();

    public ReviewStore(string path, string moduleId)
    {
        FilePath = Path.GetFullPath(path);
        ModuleId = moduleId;
    }

    public string FilePath { get; }

    public string ModuleId { get; }

    /// <summary>
    /// Computes the default sidecar path for a module path: under the nearest
    /// <c>tales.json</c> workspace's <c>.tales/reviews/&lt;moduleId&gt;.json</c>,
    /// or beside the module directory when outside a workspace.
    /// </summary>
    public static string DefaultPathFor(string modulePath, string moduleId)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modulePath));
        string? manifest = Workspace.FindManifest(full);
        string root = manifest is not null
            ? Path.GetDirectoryName(manifest)!
            : Path.GetDirectoryName(full) ?? full;
        return Path.Combine(root, ".tales", "reviews", $"{moduleId}.json");
    }

    public List<ReviewPin> List()
    {
        lock (_gate)
        {
            return LoadDocument().Pins;
        }
    }

    public ReviewPin? Find(string idOrTarget)
    {
        lock (_gate)
        {
            ReviewDocument doc = LoadDocument();
            return doc.Pins.FirstOrDefault(p =>
                string.Equals(p.Id, idOrTarget, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Target, idOrTarget, StringComparison.Ordinal));
        }
    }

    public ReviewPin Upsert(
        string targetKind,
        string target,
        string status,
        string note,
        string author,
        string? reviewedRevision,
        string? areaId = null,
        int[]? cell = null,
        string? doorId = null,
        string? eventId = null)
    {
        if (!ReviewTargetKinds.IsValid(targetKind))
        {
            throw new ArgumentException($"Unknown target kind '{targetKind}'. Expected one of: {string.Join(", ", ReviewTargetKinds.All)}.");
        }

        if (!ReviewStatuses.IsValid(status))
        {
            throw new ArgumentException($"Unknown review status '{status}'. Expected one of: {string.Join(", ", ReviewStatuses.All)}.");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("Target must not be empty.");
        }

        string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        string cleanNote = note.Trim();
        string cleanAuthor = string.IsNullOrWhiteSpace(author) ? "human" : author.Trim();

        lock (_gate)
        {
            ReviewDocument doc = LoadDocument();
            int existingIndex = doc.Pins.FindIndex(p =>
                p.TargetKind == targetKind && string.Equals(p.Target, target, StringComparison.Ordinal));

            ReviewPin updated;
            if (existingIndex >= 0)
            {
                ReviewPin prev = doc.Pins[existingIndex];
                updated = prev with
                {
                    Status = status,
                    Note = cleanNote,
                    Author = cleanAuthor,
                    ReviewedRevision = reviewedRevision ?? prev.ReviewedRevision,
                    UpdatedAt = now,
                    ResolvedAt = status == ReviewStatuses.Resolved ? now : null,
                    Resolution = status == ReviewStatuses.Resolved ? prev.Resolution : null,
                    AreaId = areaId ?? prev.AreaId,
                    Cell = cell ?? prev.Cell,
                    DoorId = doorId ?? prev.DoorId,
                    EventId = eventId ?? prev.EventId,
                };
                doc.Pins[existingIndex] = updated;
            }
            else
            {
                int seq = Math.Max(1, doc.NextSequence);
                string id = $"rev-{seq:D3}";
                while (doc.Pins.Any(p => p.Id == id))
                {
                    seq++;
                    id = $"rev-{seq:D3}";
                }

                updated = new ReviewPin(
                    id,
                    targetKind,
                    target,
                    status,
                    cleanNote,
                    null,
                    cleanAuthor,
                    reviewedRevision,
                    now,
                    now,
                    status == ReviewStatuses.Resolved ? now : null,
                    areaId,
                    cell,
                    doorId,
                    eventId);
                doc.Pins.Add(updated);
                doc = doc with { NextSequence = seq + 1 };
            }

            SaveDocument(doc);
            return updated;
        }
    }

    public ReviewPin? Resolve(string idOrTarget, string? resolution, string? currentRevision = null)
    {
        string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        lock (_gate)
        {
            ReviewDocument doc = LoadDocument();
            int idx = doc.Pins.FindIndex(p =>
                string.Equals(p.Id, idOrTarget, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Target, idOrTarget, StringComparison.Ordinal));
            if (idx < 0)
            {
                return null;
            }

            ReviewPin prev = doc.Pins[idx];
            ReviewPin resolved = prev with
            {
                Status = ReviewStatuses.Resolved,
                Resolution = string.IsNullOrWhiteSpace(resolution) ? prev.Resolution : resolution.Trim(),
                ReviewedRevision = currentRevision ?? prev.ReviewedRevision,
                UpdatedAt = now,
                ResolvedAt = now,
            };
            doc.Pins[idx] = resolved;
            SaveDocument(doc);
            return resolved;
        }
    }

    public bool Delete(string idOrTarget)
    {
        lock (_gate)
        {
            ReviewDocument doc = LoadDocument();
            int removed = doc.Pins.RemoveAll(p =>
                string.Equals(p.Id, idOrTarget, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Target, idOrTarget, StringComparison.Ordinal));
            if (removed == 0)
            {
                return false;
            }

            SaveDocument(doc);
            return true;
        }
    }

    public int Clear(string? statusFilter = ReviewStatuses.Resolved)
    {
        lock (_gate)
        {
            ReviewDocument doc = LoadDocument();
            int removed = string.IsNullOrWhiteSpace(statusFilter) || statusFilter == "all"
                ? RemoveAll(doc.Pins)
                : doc.Pins.RemoveAll(p => p.Status == statusFilter);
            if (removed > 0)
            {
                SaveDocument(doc);
            }

            return removed;
        }
    }

    private static int RemoveAll(List<ReviewPin> pins)
    {
        int count = pins.Count;
        pins.Clear();
        return count;
    }

    private ReviewDocument LoadDocument()
    {
        if (!File.Exists(FilePath))
        {
            return new ReviewDocument(CurrentFormat, ModuleId, 1, []);
        }

        string text = File.ReadAllText(FilePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ReviewDocument(CurrentFormat, ModuleId, 1, []);
        }

        ReviewDocument? doc = JsonSerializer.Deserialize<ReviewDocument>(text, JsonOptions);
        return doc is null
            ? new ReviewDocument(CurrentFormat, ModuleId, 1, [])
            : doc with { Pins = doc.Pins ?? [] };
    }

    private void SaveDocument(ReviewDocument doc)
    {
        string? dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(doc, JsonOptions);
        string temp = $"{FilePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, json, Encoding.UTF8);
        File.Move(temp, FilePath, overwrite: true);
    }
}

/// <summary>
/// Resolves persisted <see cref="ReviewPin"/> records against a loaded
/// <see cref="ModuleSet"/> and <see cref="ModuleGraph"/> to attach exact file
/// provenance, JSON paths, summaries, and content-hash stale detection.
/// </summary>
public static class ReviewResolver
{
    public sealed record TargetMeta(
        bool Exists,
        string? Module,
        string? File,
        string? JsonPath,
        string Summary,
        string? Revision,
        string? AreaId = null,
        int[]? Cell = null,
        string? DoorId = null,
        string? EventId = null);

    public static List<ResolvedReviewPin> ResolveAll(IReadOnlyList<ReviewPin> pins, ModuleSet set, ModuleGraph graph)
    {
        return pins
            .Select(pin => Resolve(pin, set, graph))
            .OrderBy(pin => SortRank(pin))
            .ThenByDescending(pin => pin.UpdatedAt, StringComparer.Ordinal)
            .ToList();
    }

    public static ResolvedReviewPin Resolve(ReviewPin pin, ModuleSet set, ModuleGraph graph)
    {
        TargetMeta meta = DescribeTarget(pin.TargetKind, pin.Target, set, graph);
        bool stale = pin.Status == ReviewStatuses.Approved
            && meta.Exists
            && pin.ReviewedRevision is not null
            && meta.Revision is not null
            && !string.Equals(pin.ReviewedRevision, meta.Revision, StringComparison.Ordinal);

        return new ResolvedReviewPin(
            pin.Id,
            pin.TargetKind,
            pin.Target,
            pin.Status,
            stale,
            meta.Exists,
            pin.Note,
            pin.Resolution,
            pin.Author,
            pin.ReviewedRevision,
            meta.Revision,
            meta.Module,
            meta.File,
            meta.JsonPath,
            meta.Summary,
            pin.CreatedAt,
            pin.UpdatedAt,
            pin.ResolvedAt,
            meta.AreaId ?? pin.AreaId,
            meta.Cell ?? pin.Cell,
            meta.DoorId ?? pin.DoorId,
            meta.EventId ?? pin.EventId);
    }

    public static ReviewCounts Summarize(IReadOnlyList<ResolvedReviewPin> pins)
    {
        int needsFix = 0;
        int question = 0;
        int approved = 0;
        int stale = 0;
        int resolved = 0;

        foreach (ResolvedReviewPin pin in pins)
        {
            switch (pin.Status)
            {
                case ReviewStatuses.NeedsFix:
                    needsFix++;
                    break;
                case ReviewStatuses.Question:
                    question++;
                    break;
                case ReviewStatuses.Approved:
                    if (pin.Stale)
                    {
                        stale++;
                    }
                    else
                    {
                        approved++;
                    }

                    break;
                case ReviewStatuses.Resolved:
                    resolved++;
                    break;
            }
        }

        return new ReviewCounts(pins.Count, needsFix + question, needsFix, question, approved, stale, resolved);
    }

    public static TargetMeta DescribeTarget(string targetKind, string target, ModuleSet set, ModuleGraph graph)
    {
        if (set.Rules is not RuleSet rules)
        {
            return new TargetMeta(false, null, null, null, $"{targetKind} {target} (module not loaded)", null);
        }

        return targetKind switch
        {
            ReviewTargetKinds.Area => ResolveArea(target, rules, graph),
            ReviewTargetKinds.Cell => ResolveCell(target, rules, graph),
            ReviewTargetKinds.Door => ResolveDoor(target, rules, graph),
            ReviewTargetKinds.Event => ResolveEvent(target, rules, graph),
            ReviewTargetKinds.Asset => ResolveAsset(target, set, rules),
            ReviewTargetKinds.Definition => ResolveDefinition(target, rules),
            _ => new TargetMeta(false, null, null, null, $"Unknown target '{target}'", null),
        };
    }

    private static TargetMeta ResolveArea(string target, RuleSet rules, ModuleGraph graph)
    {
        Definition? areaDef = FindByQualifiedOrLocal(rules, DefinitionTypes.Area, target);
        if (areaDef is null)
        {
            return new TargetMeta(false, null, null, null, $"Missing area '{target}'", null, AreaId: target);
        }

        (string module, string file, string path) = areaDef.DiagnosticLocation("$");
        GraphArea? area = graph.Areas.FirstOrDefault(a => a.Id == areaDef.QualifiedId);
        string summary = area is null
            ? $"Area {areaDef.Name} ({areaDef.QualifiedId})"
            : $"Area {area.Name} ({area.Width}×{area.Height}, {area.Entries.Count} entries)";
        return new TargetMeta(true, module, file, path, summary, HashText(areaDef.Json.GetRawText()), AreaId: areaDef.QualifiedId);
    }

    private static TargetMeta ResolveCell(string target, RuleSet rules, ModuleGraph graph)
    {
        if (!TryParseCellTarget(target, out string areaKey, out int x, out int y))
        {
            return new TargetMeta(false, null, null, null, $"Malformed cell target '{target}' (expected <area>@[x,y])", null);
        }

        Definition? areaDef = FindByQualifiedOrLocal(rules, DefinitionTypes.Area, areaKey);
        if (areaDef is null)
        {
            return new TargetMeta(false, null, null, null, $"Missing area '{areaKey}' for cell [{x}, {y}]", null, AreaId: areaKey, Cell: [x, y]);
        }

        (string module, string file, string rootPath) = areaDef.DiagnosticLocation("$");
        string jsonPath = rootPath;
        string hashPayload = $"{areaDef.QualifiedId}@[{x},{y}]";

        if (areaDef.Json.TryGetProperty("cells", out JsonElement cellsEl))
        {
            int idx = 0;
            foreach (JsonElement cellEl in cellsEl.EnumerateArray())
            {
                JsonElement at = cellEl.GetProperty("at");
                if (at[0].GetInt32() == x && at[1].GetInt32() == y)
                {
                    jsonPath = areaDef.DiagnosticPath($"$.cells[{idx}]");
                    hashPayload += "|" + cellEl.GetRawText();
                    break;
                }

                idx++;
            }
        }

        GraphArea? graphArea = graph.Areas.FirstOrDefault(a => a.Id == areaDef.QualifiedId);
        GraphCell? graphCell = graphArea?.Cells.FirstOrDefault(c => c.X == x && c.Y == y);
        bool inBounds = graphArea is not null && x >= 0 && y >= 0 && x < graphArea.Width && y < graphArea.Height;
        if (graphCell is not null)
        {
            hashPayload += $"|reach:{graphCell.Reachability}|evt:{graphCell.EventId}";
        }

        string summary = graphCell is not null && graphCell.EventId is not null
            ? $"{areaDef.Name} [{x}, {y}] · {graphCell.EventKind}: {graphCell.EventSummary}"
            : $"{areaDef.Name} [{x}, {y}] ({graphCell?.Reachability ?? "cell"})";

        return new TargetMeta(
            inBounds,
            module,
            file,
            jsonPath,
            summary,
            HashText(hashPayload),
            AreaId: areaDef.QualifiedId,
            Cell: [x, y],
            EventId: graphCell?.EventId);
    }

    private static TargetMeta ResolveDoor(string target, RuleSet rules, ModuleGraph graph)
    {
        int hash = target.IndexOf('#');
        if (hash <= 0 || hash >= target.Length - 1)
        {
            return new TargetMeta(false, null, null, null, $"Malformed door target '{target}' (expected <area>#<doorId>)", null);
        }

        string areaKey = target[..hash];
        string doorId = target[(hash + 1)..];
        Definition? areaDef = FindByQualifiedOrLocal(rules, DefinitionTypes.Area, areaKey);
        if (areaDef is null)
        {
            return new TargetMeta(false, null, null, null, $"Missing area '{areaKey}' for door '{doorId}'", null, AreaId: areaKey, DoorId: doorId);
        }

        (string module, string file, string rootPath) = areaDef.DiagnosticLocation("$");
        string jsonPath = rootPath;
        string? rawDoor = null;
        if (areaDef.Json.TryGetProperty("doors", out JsonElement doorsEl))
        {
            int idx = 0;
            foreach (JsonElement doorEl in doorsEl.EnumerateArray())
            {
                if (doorEl.TryGetProperty("id", out JsonElement idEl) && idEl.GetString() == doorId)
                {
                    jsonPath = areaDef.DiagnosticPath($"$.doors[{idx}]");
                    rawDoor = doorEl.GetRawText();
                    break;
                }

                idx++;
            }
        }

        GraphArea? graphArea = graph.Areas.FirstOrDefault(a => a.Id == areaDef.QualifiedId);
        GraphDoor? graphDoor = graphArea?.Doors.FirstOrDefault(d => d.Id == doorId);
        if (graphDoor is null || rawDoor is null)
        {
            return new TargetMeta(false, module, file, jsonPath, $"Missing door '{doorId}' in {areaDef.Name}", null, AreaId: areaDef.QualifiedId, DoorId: doorId);
        }

        string summary = $"Door '{doorId}' in {areaDef.Name} [{graphDoor.X}, {graphDoor.Y}] ({(graphDoor.Locked ? "locked" : "unlocked")})";
        return new TargetMeta(
            true,
            module,
            file,
            jsonPath,
            summary,
            HashText(rawDoor),
            AreaId: areaDef.QualifiedId,
            Cell: [graphDoor.X, graphDoor.Y],
            DoorId: doorId,
            EventId: graphDoor.UnlockEvent);
    }

    private static TargetMeta ResolveEvent(string target, RuleSet rules, ModuleGraph graph)
    {
        Definition? evtDef = FindByQualifiedOrLocal(rules, DefinitionTypes.Event, target);
        if (evtDef is null)
        {
            return new TargetMeta(false, null, null, null, $"Missing event '{target}'", null, EventId: target);
        }

        (string module, string file, string path) = evtDef.DiagnosticLocation("$");
        GraphEvent? ge = graph.Events.FirstOrDefault(e => e.Id == evtDef.QualifiedId);
        string summary = ge is not null
            ? $"{ge.Kind}: {ge.Summary}{(ge.SceneId is not null ? $" (scene {ge.SceneId}/{ge.SceneNode})" : "")}"
            : $"Event {evtDef.QualifiedId}";
        GraphEventTrigger? firstCell = ge?.Triggers.FirstOrDefault(t => t.AreaId is not null && t.X is not null && t.Y is not null);
        return new TargetMeta(
            true,
            module,
            file,
            path,
            summary,
            HashText(evtDef.Json.GetRawText()),
            AreaId: firstCell?.AreaId,
            Cell: firstCell is { X: int cx, Y: int cy } ? [cx, cy] : null,
            EventId: evtDef.QualifiedId);
    }

    private static TargetMeta ResolveAsset(string target, ModuleSet set, RuleSet rules)
    {
        Definition? assetDef = FindByQualifiedOrLocal(rules, DefinitionTypes.Asset, target);
        if (assetDef is null)
        {
            return new TargetMeta(false, null, null, null, $"Missing asset '{target}'", null);
        }

        (string module, string file, string path) = assetDef.DiagnosticLocation("$");
        string relativeFile = assetDef.Json.GetProperty("file").GetString()!;
        string media = assetDef.Json.GetProperty("media").GetString()!;

        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(Encoding.UTF8.GetBytes(assetDef.Json.GetRawText()));

        ModuleManifest? manifest = set.LoadOrder.Select(m => m.Manifest).FirstOrDefault(m => m.Id == assetDef.Module);
        if (manifest is not null && manifest.Source.Contains(relativeFile))
        {
            try
            {
                hasher.AppendData(manifest.Source.Read(relativeFile));
            }
            catch (IOException)
            {
            }
        }

        string rev = Convert.ToHexString(hasher.GetHashAndReset())[..16].ToLowerInvariant();
        return new TargetMeta(true, module, file, path, $"Asset {assetDef.QualifiedId} ({media}: {relativeFile})", rev);
    }

    private static TargetMeta ResolveDefinition(string target, RuleSet rules)
    {
        // Format: <type>:<module>:<id> or <module>:<id>
        string? typeName = null;
        string lookup = target;
        string[] parts = target.Split(':');
        if (parts.Length == 3 && DefinitionTypes.Find(parts[0]) is DefinitionType dt)
        {
            typeName = dt.Name;
            lookup = $"{parts[1]}:{parts[2]}";
        }

        Definition? found = rules.Definitions.FirstOrDefault(def =>
            (def.QualifiedId == lookup || def.Id == lookup)
            && (typeName is null || def.Type.Name == typeName));
        if (found is null)
        {
            return new TargetMeta(false, null, null, null, $"Missing definition '{target}'", null);
        }

        (string module, string file, string path) = found.DiagnosticLocation("$");
        return new TargetMeta(
            true,
            module,
            file,
            path,
            $"{found.Type.Name} {found.Name} ({found.QualifiedId})",
            HashText(found.Json.GetRawText()));
    }

    public static bool TryParseCellTarget(string target, out string areaId, out int x, out int y)
    {
        areaId = "";
        x = 0;
        y = 0;
        int at = target.IndexOf("@[", StringComparison.Ordinal);
        if (at <= 0 || !target.EndsWith(']'))
        {
            return false;
        }

        areaId = target[..at];
        string coords = target[(at + 2)..^1];
        string[] parts = coords.Split(',');
        return parts.Length == 2
            && int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
            && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
    }

    private static Definition? FindByQualifiedOrLocal(RuleSet rules, DefinitionType type, string reference) =>
        rules.Find(type, reference, out _);

    private static int SortRank(ResolvedReviewPin pin) => pin.Status switch
    {
        ReviewStatuses.NeedsFix => 0,
        ReviewStatuses.Question => 1,
        ReviewStatuses.Approved when pin.Stale => 2,
        ReviewStatuses.Approved => 3,
        _ => 4,
    };

    private static string HashText(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
