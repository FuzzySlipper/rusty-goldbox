using System.Globalization;
using RustyTales.Core.Authoring;

namespace RustyTales.Cli;

/// <summary>
/// <c>tales workbench</c>: starts the local HTTP diagnostic workbench server for
/// a module directory (or container), visualizing navigation flow graphs, 2D
/// area maps, events/scenes, and structural reachability diagnostics.
/// </summary>
internal static class WorkbenchCommand
{
    public const string Usage =
        "Usage: tales workbench <path> [--port <n>] [--host <addr>] [--reviews <file>] [--game-project <csproj>] [--puppets <dir>] [--modules <dir>]... [--extension <id>]... [--once]";

    public static int Run(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--module", "--port", "--host", "--reviews", "--game-project", "--puppets", "--modules", "--extension"], ["--once", "--no-open"]);
        string? target = parsed.Single("--module") ?? (parsed.Positionals.Count == 1 ? parsed.Positionals[0] : null);
        if (error is null && (target is null || (parsed.Single("--module") is not null && parsed.Positionals.Count > 0) || parsed.Positionals.Count > 1))
        {
            error = Usage;
        }

        int port = 4173;
        if (error is null && parsed.Single("--port") is string portText)
        {
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 0 || port > 65535)
            {
                error = $"--port must be a port number from 0 to 65535, but was '{portText}'.";
            }
        }

        string host = parsed.Single("--host") ?? "127.0.0.1";
        if (error is not null)
        {
            return output.UsageError(error);
        }

        string path = Path.GetFullPath(target!, workingDirectory);
        string? reviewsPath = parsed.Single("--reviews") is string customReviews
            ? Path.GetFullPath(customReviews, workingDirectory)
            : null;
        string? gameProject = parsed.Single("--game-project") is string customGameProject
            ? Path.GetFullPath(customGameProject, workingDirectory)
            : null;
        string? puppetsDir = parsed.Single("--puppets") is string customPuppets
            ? Path.GetFullPath(customPuppets, workingDirectory)
            : null;
        List<string> searchDirectories = parsed.All("--modules")
            .Select(directory => Path.GetFullPath(directory, workingDirectory))
            .ToList();
        IReadOnlyList<string> extensions = ModuleSets.Extensions(parsed);

        WorkbenchServer server;
        try
        {
            server = new WorkbenchServer(path, searchDirectories, extensions, host, port, reviewsPath, gameProject, puppetsDir);
        }
        catch (Exception exception)
        {
            return output.UsageError($"Could not start Workbench server on {host}:{port}: {exception.Message}");
        }

        using (server)
        {
            server.Start();
            ModuleGraph graph = server.CurrentGraph;

            if (output.Json)
            {
                output.WriteJson(new
                {
                    ok = true,
                    url = server.Url,
                    port = server.Port,
                    module = graph.Module.Id,
                    version = graph.Module.Version,
                    title = graph.Module.Title,
                    isValid = graph.Module.IsValid,
                    areas = graph.Counts.Areas,
                    events = graph.Counts.Events,
                    scenes = graph.Counts.Scenes,
                    issues = graph.Counts.Issues,
                });
            }
            else
            {
                output.Line($"Rusty Tales Workbench — {graph.Module.Title} ({graph.Module.Id} {graph.Module.Version})");
                output.Line($"  URL:     {server.Url}");
                output.Line($"  Summary: {graph.Counts.Areas} areas, {graph.Counts.Events} events ({graph.Counts.Scenes} scenes), {graph.Counts.Assets} assets, {graph.Counts.Issues} issues");
                if (!parsed.Has("--once"))
                {
                    output.Line("Press Ctrl+C to stop.");
                }
            }

            if (parsed.Has("--once"))
            {
                return TalesCli.Ok;
            }

            using ManualResetEventSlim stop = new(false);
            ConsoleCancelEventHandler handler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                stop.Set();
            };
            Console.CancelKeyPress += handler;
            try
            {
                stop.Wait();
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        }

        return TalesCli.Ok;
    }
}
