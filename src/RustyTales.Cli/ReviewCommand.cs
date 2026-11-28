using System.Text.Json;
using RustyTales.Core.Authoring;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;
using RustyTales.Core.Rules;

namespace RustyTales.Cli;

/// <summary>
/// <c>tales review</c>: reads, sets, resolves, and clears Workbench review pins
/// with resolved module/file/JSON-path provenance so authoring agents can act
/// directly on human feedback without prose ambiguity.
/// </summary>
internal static class ReviewCommand
{
    public const string Usage =
        "Usage:\n"
        + "  tales review list <module-path> [--status open|needs-fix|question|approved|stale|resolved|all] [--kind area|cell|door|event|asset|definition] [--reviews <file>] [--modules <dir>]... [--extension <id>]... [--json]\n"
        + "  tales review show <module-path> <pin-id-or-target> [--reviews <file>] [--modules <dir>]... [--extension <id>]... [--json]\n"
        + "  tales review set <module-path> --kind <kind> --target <target> --status <needs-fix|approved|question|resolved> [--note <text>] [--author <name>] [--reviews <file>] [--modules <dir>]... [--extension <id>]... [--json]\n"
        + "  tales review resolve <module-path> <pin-id-or-target> [--note <resolution>] [--reviews <file>] [--modules <dir>]... [--extension <id>]... [--json]\n"
        + "  tales review clear <module-path> [--status resolved|all] [--reviews <file>] [--modules <dir>]... [--extension <id>]... [--json]";

    public static int Run(IReadOnlyList<string> args, Output output, string workingDirectory)
    {
        if (args.Count == 0 || args[0] is "--help" or "-h")
        {
            output.Line(Usage);
            return args.Count == 0 ? TalesCli.Usage : TalesCli.Ok;
        }

        IEnumerable<string> rest = args.Skip(1);
        return args[0] switch
        {
            "list" => List(rest, output, workingDirectory),
            "show" => Show(rest, output, workingDirectory),
            "set" => Set(rest, output, workingDirectory),
            "resolve" => Resolve(rest, output, workingDirectory),
            "clear" => Clear(rest, output, workingDirectory),
            _ => output.UsageError($"Unknown review command '{args[0]}'. Review commands are list, show, set, resolve and clear.\n{Usage}"),
        };
    }

    private static int List(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--status", "--kind", "--reviews", "--puppets", "--modules", "--extension"], []);
        if (error is null && parsed.Positionals.Count != 1)
        {
            error = "Usage: tales review list <module-path> [--status open|needs-fix|question|approved|stale|resolved|all] [--kind <kind>] [--reviews <file>] [--puppets <dir>] [--json]";
        }

        string? status = parsed.Single("--status");
        if (error is null && status is not null && status is not ("all" or "open" or "stale") && !ReviewStatuses.IsValid(status))
        {
            error = $"Unknown --status '{status}'. Use all, open, stale, {string.Join(", ", ReviewStatuses.All)}.";
        }

        string? kind = parsed.Single("--kind");
        if (error is null && kind is not null && !ReviewTargetKinds.IsValid(kind))
        {
            error = $"Unknown --kind '{kind}'. Use one of: {string.Join(", ", ReviewTargetKinds.All)}.";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        (ModuleSet set, ModuleGraph graph, ReviewStore store) = LoadContext(parsed.Positionals[0], parsed, workingDirectory);
        List<ResolvedReviewPin> all = ReviewResolver.ResolveAll(store.List(), set, graph);
        List<ResolvedReviewPin> filtered = FilterPins(all, status, kind);
        ReviewCounts counts = ReviewResolver.Summarize(all);

        string? explicitPuppets = parsed.Single("--puppets") is string p ? Path.GetFullPath(p, workingDirectory) : null;
        string moduleFull = Path.GetFullPath(parsed.Positionals[0], workingDirectory);
        List<string> puppetWorkspaces = PuppetBridge.DiscoverWorkspaces(moduleFull, explicitPuppets);
        bool openPuppetOnly = status is null or "open" or "needs-fix" or "question";
        List<PuppetBridge.PuppetFlag> puppetFlags = [];
        foreach (string ws in puppetWorkspaces)
        {
            puppetFlags.AddRange(PuppetBridge.ReadFlags(ws, openOnly: openPuppetOnly));
        }

        if (output.Json)
        {
            output.Line(JsonSerializer.Serialize(new
            {
                ok = true,
                module = graph.Module.Id,
                storePath = store.FilePath,
                counts,
                pins = filtered,
                puppetWorkspaces,
                puppetFlags,
            }, ReviewStore.JsonOptions));
            return TalesCli.Ok;
        }

        output.Line($"Review queue for {graph.Module.Title} ({graph.Module.Id}) — {counts.Open} open ({counts.NeedsFix} needs-fix, {counts.Question} question), {counts.Approved} approved, {counts.Stale} stale, {counts.Resolved} resolved");
        output.Line($"Store: {store.FilePath}");
        if (filtered.Count == 0 && puppetFlags.Count == 0)
        {
            output.Line("No matching review pins.");
            return TalesCli.Ok;
        }

        foreach (ResolvedReviewPin pin in filtered)
        {
            string statusTag = pin.Stale ? "approved:STALE" : pin.Status;
            string where = string.Join(" ", new[] { pin.File, pin.JsonPath }.Where(part => !string.IsNullOrWhiteSpace(part)));
            output.Line($"  {pin.Id} [{statusTag}] {pin.TargetKind} {pin.Target} — {pin.Summary}");
            if (where.Length > 0)
            {
                output.Line($"      at {where}");
            }

            if (pin.Note.Length > 0)
            {
                output.Line($"      note: {pin.Note}");
            }

            if (!string.IsNullOrWhiteSpace(pin.Resolution))
            {
                output.Line($"      resolution: {pin.Resolution}");
            }
        }

        if (puppetFlags.Count > 0)
        {
            output.Line($"Puppet review flags ({puppetFlags.Count}):");
            foreach (PuppetBridge.PuppetFlag pf in puppetFlags)
            {
                output.Line($"  [{(pf.Resolved ? "resolved" : "open")}] {pf.CharacterId} {pf.Id} ({pf.Animation}@{pf.Frame} {pf.Tag}) — {pf.Note}");
                if (!string.IsNullOrWhiteSpace(pf.CropPath))
                {
                    output.Line($"      crop: {pf.CropPath}");
                }
            }
        }

        return TalesCli.Ok;
    }

    private static int Show(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--reviews", "--modules", "--extension"], []);
        if (error is null && parsed.Positionals.Count != 2)
        {
            error = "Usage: tales review show <module-path> <pin-id-or-target> [--reviews <file>] [--json]";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        (ModuleSet set, ModuleGraph graph, ReviewStore store) = LoadContext(parsed.Positionals[0], parsed, workingDirectory);
        string selector = parsed.Positionals[1];
        ReviewPin? raw = store.Find(selector);
        if (raw is null)
        {
            return output.Problems([new ModuleDiagnostic("review.not-found", $"No review pin matching '{selector}' was found in {store.FilePath}.", graph.Module.Id, store.FilePath)]);
        }

        ResolvedReviewPin pin = ReviewResolver.Resolve(raw, set, graph);
        JsonElement? targetJson = FindTargetJson(pin, set);

        if (output.Json)
        {
            output.Line(JsonSerializer.Serialize(new
            {
                ok = true,
                storePath = store.FilePath,
                pin,
                definition = targetJson,
            }, ReviewStore.JsonOptions));
            return TalesCli.Ok;
        }

        output.Line($"{pin.Id} [{(pin.Stale ? "approved:STALE" : pin.Status)}] {pin.TargetKind} {pin.Target}");
        output.Line($"  Summary:  {pin.Summary}");
        output.Line($"  Location: {pin.File ?? "(unresolved)"} {pin.JsonPath ?? ""}".TrimEnd());
        output.Line($"  Author:   {pin.Author} · updated {pin.UpdatedAt}");
        if (pin.Note.Length > 0)
        {
            output.Line($"  Note:     {pin.Note}");
        }

        if (!string.IsNullOrWhiteSpace(pin.Resolution))
        {
            output.Line($"  Resolved: {pin.Resolution} ({pin.ResolvedAt})");
        }

        if (pin.Stale)
        {
            output.Line($"  Warning:  Target changed since approval (reviewed {pin.ReviewedRevision}, current {pin.CurrentRevision}).");
        }

        return TalesCli.Ok;
    }

    private static int Set(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--kind", "--target", "--status", "--note", "--author", "--reviews", "--modules", "--extension"], []);
        if (error is null && (parsed.Positionals.Count != 1 || parsed.Single("--kind") is null || parsed.Single("--target") is null || parsed.Single("--status") is null))
        {
            error = "Usage: tales review set <module-path> --kind <kind> --target <target> --status <needs-fix|approved|question|resolved> [--note <text>] [--author <name>] [--reviews <file>] [--json]";
        }

        string? kind = parsed.Single("--kind");
        string? target = parsed.Single("--target");
        string? status = parsed.Single("--status");
        if (error is null && !ReviewTargetKinds.IsValid(kind))
        {
            error = $"Unknown --kind '{kind}'. Use one of: {string.Join(", ", ReviewTargetKinds.All)}.";
        }

        if (error is null && !ReviewStatuses.IsValid(status))
        {
            error = $"Unknown --status '{status}'. Use one of: {string.Join(", ", ReviewStatuses.All)}.";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        (ModuleSet set, ModuleGraph graph, ReviewStore store) = LoadContext(parsed.Positionals[0], parsed, workingDirectory);
        ReviewResolver.TargetMeta meta = ReviewResolver.DescribeTarget(kind!, target!, set, graph);
        ReviewPin saved = store.Upsert(
            kind!,
            target!,
            status!,
            parsed.Single("--note") ?? "",
            parsed.Single("--author") ?? "human",
            meta.Revision,
            meta.AreaId,
            meta.Cell,
            meta.DoorId,
            meta.EventId);
        ResolvedReviewPin resolved = ReviewResolver.Resolve(saved, set, graph);
        ReviewCounts counts = ReviewResolver.Summarize(ReviewResolver.ResolveAll(store.List(), set, graph));

        if (output.Json)
        {
            output.Line(JsonSerializer.Serialize(new { ok = true, storePath = store.FilePath, pin = resolved, counts }, ReviewStore.JsonOptions));
            return TalesCli.Ok;
        }

        output.Line($"Saved {resolved.Id} [{resolved.Status}] {resolved.TargetKind} {resolved.Target} ({resolved.Summary})");
        if (!resolved.Exists)
        {
            output.Line("  Warning: target does not currently resolve in this module set.");
        }

        return TalesCli.Ok;
    }

    private static int Resolve(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--note", "--reviews", "--modules", "--extension"], []);
        if (error is null && parsed.Positionals.Count != 2)
        {
            error = "Usage: tales review resolve <module-path> <pin-id-or-target> [--note <resolution>] [--reviews <file>] [--json]";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        (ModuleSet set, ModuleGraph graph, ReviewStore store) = LoadContext(parsed.Positionals[0], parsed, workingDirectory);
        string selector = parsed.Positionals[1];
        ReviewPin? existing = store.Find(selector);
        if (existing is null)
        {
            return output.Problems([new ModuleDiagnostic("review.not-found", $"No review pin matching '{selector}' was found in {store.FilePath}.", graph.Module.Id, store.FilePath)]);
        }

        ReviewResolver.TargetMeta meta = ReviewResolver.DescribeTarget(existing.TargetKind, existing.Target, set, graph);
        ReviewPin resolvedRaw = store.Resolve(selector, parsed.Single("--note"), meta.Revision)!;
        ResolvedReviewPin pin = ReviewResolver.Resolve(resolvedRaw, set, graph);
        ReviewCounts counts = ReviewResolver.Summarize(ReviewResolver.ResolveAll(store.List(), set, graph));

        if (output.Json)
        {
            output.Line(JsonSerializer.Serialize(new { ok = true, storePath = store.FilePath, pin, counts }, ReviewStore.JsonOptions));
            return TalesCli.Ok;
        }

        output.Line($"Resolved {pin.Id} ({pin.TargetKind} {pin.Target}){(pin.Resolution is string r ? $": {r}" : "")}");
        return TalesCli.Ok;
    }

    private static int Clear(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--status", "--reviews", "--modules", "--extension"], []);
        if (error is null && parsed.Positionals.Count != 1)
        {
            error = "Usage: tales review clear <module-path> [--status resolved|all] [--reviews <file>] [--json]";
        }

        string status = parsed.Single("--status") ?? ReviewStatuses.Resolved;
        if (error is null && status != "all" && !ReviewStatuses.IsValid(status))
        {
            error = $"Unknown --status '{status}'. Use all or one of: {string.Join(", ", ReviewStatuses.All)}.";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        (_, ModuleGraph graph, ReviewStore store) = LoadContext(parsed.Positionals[0], parsed, workingDirectory);
        int cleared = store.Clear(status);

        if (output.Json)
        {
            output.Line(JsonSerializer.Serialize(new { ok = true, module = graph.Module.Id, storePath = store.FilePath, cleared, status }, ReviewStore.JsonOptions));
            return TalesCli.Ok;
        }

        output.Line($"Cleared {cleared} {(status == "all" ? "" : status + " ")}review pin{(cleared == 1 ? "" : "s")} from {store.FilePath}.");
        return TalesCli.Ok;
    }

    internal static List<ResolvedReviewPin> FilterPins(IReadOnlyList<ResolvedReviewPin> pins, string? status, string? kind)
    {
        return pins.Where(pin =>
        {
            if (kind is not null && pin.TargetKind != kind)
            {
                return false;
            }

            return status switch
            {
                null or "all" => true,
                "open" => ReviewStatuses.IsOpen(pin.Status),
                "stale" => pin.Stale,
                _ => pin.Status == status,
            };
        }).ToList();
    }

    private static (ModuleSet Set, ModuleGraph Graph, ReviewStore Store) LoadContext(string moduleArg, Arguments parsed, string workingDirectory)
    {
        string modulePath = Path.GetFullPath(moduleArg, workingDirectory);
        List<string> searchDirectories = parsed.All("--modules")
            .Select(directory => Path.GetFullPath(directory, workingDirectory))
            .ToList();
        ModuleSet set = ModuleSets.Load(modulePath, searchDirectories, ModuleSets.Extensions(parsed));
        ModuleGraph graph = ModuleGraphBuilder.Build(set);
        string reviewFile = parsed.Single("--reviews") is string custom
            ? Path.GetFullPath(custom, workingDirectory)
            : ReviewStore.DefaultPathFor(modulePath, graph.Module.Id);
        return (set, graph, new ReviewStore(reviewFile, graph.Module.Id));
    }

    private static JsonElement? FindTargetJson(ResolvedReviewPin pin, ModuleSet set)
    {
        if (set.Rules is not RuleSet rules)
        {
            return null;
        }

        Definition? def = pin.TargetKind switch
        {
            ReviewTargetKinds.Area => rules.Find(DefinitionTypes.Area, pin.Target, out _),
            ReviewTargetKinds.Cell or ReviewTargetKinds.Door when pin.AreaId is not null => rules.Find(DefinitionTypes.Area, pin.AreaId, out _),
            ReviewTargetKinds.Event => rules.Find(DefinitionTypes.Event, pin.Target, out _),
            ReviewTargetKinds.Asset => rules.Find(DefinitionTypes.Asset, pin.Target, out _),
            ReviewTargetKinds.Definition => rules.Definitions.FirstOrDefault(d =>
                $"{d.Type.Name}:{d.QualifiedId}" == pin.Target || d.QualifiedId == pin.Target),
            _ => null,
        };
        return def?.Json;
    }
}
