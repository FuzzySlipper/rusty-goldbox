using System.Text.Json;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Cli;

/// <summary><c>goldbox map render</c>: an area as text, with entries and triggers marked.</summary>
internal static class MapCommand
{
    private const string Usage = "Usage: goldbox map render <area> --module <path> [--player] [--modules <dir>]...";

    public static int Run(IReadOnlyList<string> args, Output output, string workingDirectory)
    {
        if (args.Count == 0 || args[0] != "render")
        {
            return output.UsageError(Usage);
        }

        (Arguments parsed, string? error) = Arguments.Parse(args.Skip(1), ["--module", "--modules"], ["--player"]);
        if (error is null && (parsed.Positionals.Count != 1 || parsed.Single("--module") is null))
        {
            error = Usage;
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        ModuleSet set = ModuleSets.Load(
            Path.GetFullPath(parsed.Single("--module")!, workingDirectory),
            parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList());
        if (set.Rules is null || !set.IsValid)
        {
            return output.ModuleErrors(set);
        }

        Definition? area = set.Rules.Find(DefinitionTypes.Area, parsed.Positionals[0], out string? problem);
        if (area is null)
        {
            return output.UsageError(problem!);
        }

        AreaMap map = AreaMap.Parse(area.Json.GetProperty("map").EnumerateArray().Select(row => row.GetString()!).ToList(), [])!;
        Dictionary<(int X, int Y), string> markers = [];
        List<string> legend = [];
        if (area.Json.TryGetProperty("cells", out JsonElement cells))
        {
            foreach (JsonElement cell in cells.EnumerateArray())
            {
                (int x, int y) = (cell.GetProperty("at")[0].GetInt32(), cell.GetProperty("at")[1].GetInt32());
                List<string> notes = [];
                string mark = "  ";
                if (cell.TryGetProperty("event", out JsonElement evt))
                {
                    bool once = cell.TryGetProperty("once", out JsonElement onceFlag) && onceFlag.GetBoolean();
                    string facing = cell.TryGetProperty("facing", out JsonElement facingFlag) ? facingFlag.GetString()! : "";
                    mark = "!" + (facing.Length > 0 ? Arrow(facing) : once ? '1' : ' ');
                    notes.Add($"event {evt.GetString()}{(once ? ", once" : "")}{(facing.Length > 0 ? $", facing {facing}" : "")}");
                }

                if (cell.TryGetProperty("zone", out JsonElement zone))
                {
                    notes.Add($"zone {zone.GetString()}");
                }

                if (cell.TryGetProperty("backdrop", out JsonElement backdrop))
                {
                    notes.Add($"backdrop {backdrop.GetString()}");
                }

                markers[(x, y)] = mark;
                legend.Add($"[{x}, {y}] {string.Join("; ", notes)}");
            }
        }

        foreach (JsonProperty entry in area.Json.GetProperty("entries").EnumerateObject())
        {
            (int x, int y) = (entry.Value.GetProperty("at")[0].GetInt32(), entry.Value.GetProperty("at")[1].GetInt32());
            string facing = entry.Value.GetProperty("facing").GetString()!;
            markers[(x, y)] = $"{Arrow(facing)}{char.ToUpperInvariant(entry.Name[0])}";
            legend.Add($"[{x}, {y}] entry {entry.Name}, facing {facing}");
        }

        string rendered = map.Render(markers, parsed.Has("--player"));
        if (output.Json)
        {
            output.WriteJson(new { ok = true, area = area.QualifiedId, name = area.Name, width = map.Width, height = map.Height, rows = rendered.TrimEnd('\n').Split('\n'), legend });
            return GoldboxCli.Ok;
        }

        output.Line($"{area.Name} ({area.QualifiedId}), {map.Width} x {map.Height}; x runs east, y south, from 0");
        output.Line(rendered.TrimEnd('\n'));
        output.Line("Legend: '--' '|' wall, 'DD' 'D' door, 'SS' 'S' secret door; !1 once-only event, !<arrow> event when facing that way, <arrow><letter> entry.");
        foreach (string line in legend)
        {
            output.Line($"  {line}");
        }

        return GoldboxCli.Ok;
    }

    private static char Arrow(string facing) => facing switch
    {
        "north" => '^',
        "east" => '>',
        "south" => 'v',
        _ => '<',
    };
}
