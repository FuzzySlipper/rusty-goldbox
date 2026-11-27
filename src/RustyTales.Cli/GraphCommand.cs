using System.Text.Json;
using RustyTales.Core.Authoring;
using RustyTales.Core.Modules;

namespace RustyTales.Cli;

/// <summary>
/// <c>tales module graph</c>: prints the navigation, map, event/scene flow, and
/// structural reachability diagnostics for a module set (same JSON payload as
/// <c>tales workbench</c>).
/// </summary>
internal static class GraphCommand
{
    public const string Usage =
        "Usage: tales module graph <path> [--modules <dir>]... [--extension <id>]... [--json]";

    public static int Run(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--modules", "--extension"], []);
        if (error is null && parsed.Positionals.Count != 1)
        {
            error = Usage;
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        string path = Path.GetFullPath(parsed.Positionals[0], workingDirectory);
        List<string> searchDirectories = parsed.All("--modules")
            .Select(directory => Path.GetFullPath(directory, workingDirectory))
            .ToList();
        ModuleSet set = ModuleSets.Load(path, searchDirectories, ModuleSets.Extensions(parsed));
        ModuleGraph graph = ModuleGraphBuilder.Build(set);

        if (output.Json)
        {
            output.Line(JsonSerializer.Serialize(graph, ModuleGraphBuilder.JsonOptions));
            return set.IsValid ? TalesCli.Ok : TalesCli.Invalid;
        }

        output.Line($"{graph.Module.Title} ({graph.Module.Id} {graph.Module.Version}, {graph.Module.Kind})");
        if (graph.Campaign is GraphCampaignInfo campaign)
        {
            output.Line($"Campaign: {campaign.Name} ({campaign.Id}) — start {campaign.StartArea} [{campaign.StartEntry}], party {campaign.PartyMin}–{campaign.PartyMax}{(campaign.IntroEvent is string intro ? $", intro {intro}" : "")}");
        }

        output.Line($"Summary: {graph.Counts.Areas} areas, {graph.Counts.Events} events ({graph.Counts.Scenes} scenes), {graph.Counts.Doors} locked/declared doors, {graph.Counts.SecretDoors} secret doors, {graph.Counts.Assets} assets, {graph.Counts.Issues} issues.");

        if (graph.Areas.Count > 0)
        {
            output.Line("Areas:");
            foreach (GraphArea area in graph.Areas)
            {
                int eventCells = area.Cells.Count(cell => cell.EventId is not null);
                int unreachableCells = area.Cells.Count(cell => cell.Reachability == "unreachable");
                List<string> flags = [];
                if (area.IsStartArea)
                {
                    flags.Add("start");
                }

                if (!area.Reachable)
                {
                    flags.Add("UNREACHABLE");
                }

                if (area.HasEndEvent)
                {
                    flags.Add("end");
                }

                if (area.Doors.Count > 0)
                {
                    flags.Add($"{area.Doors.Count} doors");
                }

                if (unreachableCells > 0)
                {
                    flags.Add($"{unreachableCells} walled cells");
                }

                string flagText = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";
                output.Line($"  {area.Id} \"{area.Name}\" ({area.Width}×{area.Height}, {area.Entries.Count} entries, {eventCells} cell events){flagText}");
            }
        }

        if (graph.NavigationLinks.Count > 0)
        {
            output.Line("Navigation & Key Links:");
            foreach (GraphNavLink link in graph.NavigationLinks)
            {
                output.Line($"  [{link.Kind}] {link.FromArea} -> {link.ToArea}: {link.Label}{(link.ViaEvent is string via ? $" (via {via})" : "")}");
            }
        }

        if (graph.Issues.Count > 0)
        {
            output.Line("Issues:");
            foreach (GraphIssue issue in graph.Issues)
            {
                string where = string.Join(" ", new[] { issue.File, issue.JsonPath }.Where(part => part is not null));
                output.Line($"  [{issue.Severity}:{issue.Code}] {(where.Length > 0 ? where + ": " : "")}{issue.Message}");
            }
        }
        else
        {
            output.Line("No graph or validation issues found.");
        }

        return set.IsValid ? TalesCli.Ok : TalesCli.Invalid;
    }
}
