using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using RustyTales.Core.Authoring;

namespace RustyTales.Cli;

/// <summary>
/// Manages an on-demand browser-streamed <c>rusty dev --output stream --live-debug</c>
/// playtest session for <see cref="WorkbenchServer"/>, exposing start, attach, stop,
/// status discovery and live-debug command forwarding (with clean shutdown on exit).
/// </summary>
internal sealed class PlaytestSession : IDisposable
{
    internal sealed record SessionInfo(
        string Id,
        string Url,
        int Port,
        int? Pid,
        string? Instance,
        string? Label,
        bool ManagedByWorkbench);

    internal sealed record StartOptions(
        string? BindHost,
        int? Port,
        string? Instance,
        bool AutoStartCampaign,
        ulong Seed,
        string? AttachUrl);

    internal sealed record CommandResult(
        bool Ok,
        string? Output,
        string? Error,
        JsonNode? State,
        string? LiveStateJson);

    private readonly string _modulePath;
    private readonly string _moduleId;
    private readonly string _moduleKind;
    private readonly string _workbenchUrl;
    private readonly int _workbenchPort;
    private readonly string? _gameProject;
    private readonly string? _workspaceRoot;
    private readonly object _lock = new();

    private SessionInfo? _active;
    private bool _disposed;

    public PlaytestSession(
        string modulePath,
        string moduleId,
        string moduleKind,
        string workbenchUrl,
        int workbenchPort,
        string? gameProject = null)
    {
        _modulePath = Path.GetFullPath(modulePath);
        _moduleId = moduleId;
        _moduleKind = moduleKind;
        _workbenchUrl = workbenchUrl.TrimEnd('/');
        _workbenchPort = workbenchPort;
        _gameProject = ResolveGameProject(gameProject, _modulePath);
        _workspaceRoot = ResolveAuthoringWorkspace(_modulePath);
    }

    public string? GameProject => _gameProject;

    public string? WorkspaceRoot => _workspaceRoot;

    public object GetStatus(bool probeLiveState, out string? liveStateJson)
    {
        liveStateJson = null;
        List<SessionInfo> discovered = DiscoverRunningSessions();
        SessionInfo? active;
        lock (_lock)
        {
            if (_active is not null)
            {
                SessionInfo? match = discovered.FirstOrDefault(s =>
                    (!string.IsNullOrEmpty(_active.Id) && string.Equals(s.Id, _active.Id, StringComparison.Ordinal))
                    || (_active.Port > 0 && s.Port == _active.Port)
                    || string.Equals(s.Url.TrimEnd('/'), _active.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    _active = match with { ManagedByWorkbench = _active.ManagedByWorkbench };
                }
                else if (_active.ManagedByWorkbench)
                {
                    _active = null;
                }
            }

            active = _active;
        }

        JsonNode? liveStateNode = null;
        if (active is not null && probeLiveState)
        {
            (JsonNode? parsed, string? normalized) = ProbeState(active);
            liveStateNode = parsed;
            liveStateJson = normalized;
        }

        return new
        {
            ok = true,
            available = _gameProject is not null,
            running = active is not null,
            managedByWorkbench = active?.ManagedByWorkbench ?? false,
            session = active,
            sessions = discovered,
            gameProject = _gameProject,
            workspaceRoot = _workspaceRoot,
            moduleId = _moduleId,
            moduleKind = _moduleKind,
            isCampaign = string.Equals(_moduleKind, "campaign", StringComparison.OrdinalIgnoreCase),
            state = liveStateNode,
        };
    }

    public (bool Ok, object Payload, string? LiveStateJson) Start(StartOptions options)
    {
        List<SessionInfo> discovered = DiscoverRunningSessions();

        // Attach to an already-running streamed session without spawning a new one.
        if (!string.IsNullOrWhiteSpace(options.AttachUrl))
        {
            string normalizedUrl = options.AttachUrl.Trim().TrimEnd('/');
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            {
                return (false, new { ok = false, error = $"'{options.AttachUrl}' is not a valid http:// or https:// session URL." }, null);
            }

            SessionInfo? existing = discovered.FirstOrDefault(s =>
                string.Equals(s.Url.TrimEnd('/'), normalizedUrl, StringComparison.OrdinalIgnoreCase)
                || (uri.Port > 0 && s.Port == uri.Port));
            SessionInfo attached = existing is not null
                ? existing with { ManagedByWorkbench = false }
                : new SessionInfo($"attached-{uri.Port}", normalizedUrl, uri.Port, null, null, "attached", false);

            lock (_lock)
            {
                _active = attached;
            }

            _ = RunLiveDebugRaw(attached, $"tales.workbench {_workbenchUrl}", timeoutMs: 4000);
            (JsonNode? attachedState, string? attachedLiveJson) = ProbeState(attached);
            return (true, new
            {
                ok = true,
                session = attached,
                startedMessage = $"Attached to {attached.Url}",
                state = attachedState,
            }, attachedLiveJson);
        }

        // Reuse an already-running managed session if it is still alive.
        lock (_lock)
        {
            if (_active is not null)
            {
                SessionInfo? stillRunning = discovered.FirstOrDefault(s =>
                    (!string.IsNullOrEmpty(_active.Id) && string.Equals(s.Id, _active.Id, StringComparison.Ordinal))
                    || (_active.Port > 0 && s.Port == _active.Port));
                if (stillRunning is not null)
                {
                    _active = stillRunning with { ManagedByWorkbench = _active.ManagedByWorkbench };
                    (JsonNode? existingState, string? existingLiveJson) = ProbeState(_active);
                    return (true, new
                    {
                        ok = true,
                        session = _active,
                        reused = true,
                        startedMessage = $"Playtest session already running at {_active.Url}",
                        state = existingState,
                    }, existingLiveJson);
                }

                _active = null;
            }
        }

        if (_gameProject is null)
        {
            return (false, new
            {
                ok = false,
                error = "Could not locate src/RustyTales.Game/RustyTales.Game.csproj. Pass --game-project <path> to `tales workbench`.",
            }, null);
        }

        string instance = string.IsNullOrWhiteSpace(options.Instance)
            ? $"workbench-{_workbenchPort}"
            : options.Instance.Trim();
        List<string> args =
        [
            "dev", "start",
            "--project", _gameProject,
            "--output", "stream",
            "--audio-output", "stream",
            "--live-debug",
            "--instance", instance,
            "--label", $"workbench:{_moduleId}",
        ];
        if (!string.IsNullOrWhiteSpace(options.BindHost))
        {
            args.Add("--bind-host");
            args.Add(options.BindHost.Trim());
        }

        if (options.Port is int requestedPort and > 0)
        {
            args.Add("--port");
            args.Add(requestedPort.ToString(CultureInfo.InvariantCulture));
        }

        Dictionary<string, string>? env = null;
        if (_workspaceRoot is not null)
        {
            string existingWs = Environment.GetEnvironmentVariable("TALES_WORKSPACES") ?? "";
            string combined = string.IsNullOrWhiteSpace(existingWs)
                ? _workspaceRoot
                : $"{_workspaceRoot}{Path.PathSeparator}{existingWs}";
            env = new Dictionary<string, string> { ["TALES_WORKSPACES"] = combined };
        }

        (int exitCode, string stdout, string stderr) = RunProcess(
            ContentPacker.FindRusty(),
            args,
            workingDirectory: Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(_gameProject))) ?? Environment.CurrentDirectory,
            environment: env,
            timeoutMs: 90_000);

        if (exitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stderr}\n{stdout}";
            return (false, new
            {
                ok = false,
                error = $"rusty dev start failed (exit {exitCode}): {detail.Trim()}",
            }, null);
        }

        SessionInfo? launched = ParseStartedSession(stdout, instance, $"workbench:{_moduleId}");
        if (launched is null)
        {
            return (false, new
            {
                ok = false,
                error = $"rusty dev start returned unexpected output: {stdout.Trim()}",
            }, null);
        }

        lock (_lock)
        {
            _active = launched;
        }

        // Point the streamed game back to this Workbench URL for F9 / live sync.
        _ = RunLiveDebugRaw(launched, $"tales.workbench {_workbenchUrl}", timeoutMs: 8000);

        string startedMessage = $"Streamed game running at {launched.Url}";
        if (options.AutoStartCampaign && string.Equals(_moduleKind, "campaign", StringComparison.OrdinalIgnoreCase))
        {
            (int startCode, string startOut, string startErr) = RunLiveDebugRaw(
                launched,
                $"tales.start {_moduleId} {options.Seed.ToString(CultureInfo.InvariantCulture)}",
                timeoutMs: 15_000);
            if (startCode == 0 && !string.IsNullOrWhiteSpace(startOut))
            {
                startedMessage = startOut.Trim();
            }
            else if (startCode != 0)
            {
                startedMessage = $"{startedMessage} (auto-start note: {(string.IsNullOrWhiteSpace(startErr) ? startOut : startErr).Trim()})";
            }
        }

        (JsonNode? stateNode, string? liveJson) = ProbeState(launched);
        return (true, new
        {
            ok = true,
            session = launched,
            startedMessage,
            state = stateNode,
        }, liveJson);
    }

    public object Stop()
    {
        SessionInfo? target;
        lock (_lock)
        {
            target = _active;
            _active = null;
        }

        if (target is null)
        {
            return new { ok = true, stopped = false, message = "No playtest session is active." };
        }

        string stopSelector = !string.IsNullOrEmpty(target.Id) && !target.Id.StartsWith("attached-", StringComparison.Ordinal)
            ? target.Id
            : target.Port.ToString(CultureInfo.InvariantCulture);

        (int exitCode, string stdout, string stderr) = RunProcess(
            ContentPacker.FindRusty(),
            ["dev", "stop", stopSelector],
            workingDirectory: Environment.CurrentDirectory,
            environment: null,
            timeoutMs: 15_000);

        return new
        {
            ok = exitCode == 0,
            stopped = exitCode == 0,
            session = target,
            message = exitCode == 0
                ? $"Stopped playtest session {stopSelector}."
                : $"rusty dev stop {stopSelector} exited {exitCode}: {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim()}",
        };
    }

    public CommandResult ExecuteCommand(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return new CommandResult(false, null, "Provide a live-debug command line (for example `tales.goto hall 1 1 north`).", null, null);
        }

        SessionInfo? active;
        lock (_lock)
        {
            active = _active;
        }

        if (active is null)
        {
            return new CommandResult(false, null, "No active playtest session. Launch or attach a streamed session first.", null, null);
        }

        string trimmed = commandLine.Trim();
        (int exitCode, string stdout, string stderr) = RunLiveDebugRaw(active, trimmed, timeoutMs: 15_000);
        if (exitCode != 0)
        {
            string err = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stderr}\n{stdout}";
            return new CommandResult(false, null, string.IsNullOrWhiteSpace(err) ? $"rusty-live-debug exited {exitCode}." : err.Trim(), null, null);
        }

        JsonNode? stateNode;
        string? liveJson;
        if (string.Equals(trimmed, "tales.state", StringComparison.OrdinalIgnoreCase))
        {
            (stateNode, liveJson) = ParseLiveStateOutput(stdout);
        }
        else
        {
            (stateNode, liveJson) = ProbeState(active);
        }

        return new CommandResult(true, stdout.Trim(), null, stateNode, liveJson);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SessionInfo? managed;
        lock (_lock)
        {
            managed = _active is { ManagedByWorkbench: true } ? _active : null;
        }

        if (managed is not null)
        {
            try
            {
                _ = Stop();
            }
            catch
            {
            }
        }
    }

    private (JsonNode? StateNode, string? LiveStateJson) ProbeState(SessionInfo session)
    {
        (int exitCode, string stdout, _) = RunLiveDebugRaw(session, "tales.state", timeoutMs: 5000);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            return (null, null);
        }

        return ParseLiveStateOutput(stdout);
    }

    private (JsonNode? StateNode, string? LiveStateJson) ParseLiveStateOutput(string stdout)
    {
        string? jsonSlice = ExtractJsonPayload(stdout);
        if (jsonSlice is null)
        {
            return (null, null);
        }

        try
        {
            JsonNode? parsed = JsonNode.Parse(jsonSlice);
            if (parsed is not JsonObject obj)
            {
                return (null, null);
            }

            JsonObject? pos = obj["position"] as JsonObject;
            JsonObject? combat = obj["combat"] as JsonObject;
            JsonObject liveState = new()
            {
                ["ok"] = true,
                ["connected"] = true,
                ["screen"] = obj["screen"]?.GetValue<string>(),
                ["module"] = _moduleId,
                ["campaign"] = obj["campaign"]?.GetValue<string>(),
                ["area"] = pos?["area"]?.GetValue<string>(),
                ["x"] = pos?["x"]?.DeepClone(),
                ["y"] = pos?["y"]?.DeepClone(),
                ["facing"] = pos?["facing"]?.GetValue<string>(),
                ["activeEvent"] = obj["pendingMenu"]?.GetValue<string>()
                    ?? obj["pendingCombat"]?.GetValue<string>()
                    ?? obj["pendingShop"]?.GetValue<string>(),
                ["activeActor"] = combat?["activeActor"]?.GetValue<string>(),
            };
            return (obj, liveState.ToJsonString());
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunLiveDebugRaw(SessionInfo session, string command, int timeoutMs)
    {
        string origin = LocalOriginFor(session);
        return RunProcess(
            ContentPacker.FindLiveDebug(),
            ["--origin", origin, "--command", command],
            workingDirectory: Environment.CurrentDirectory,
            environment: null,
            timeoutMs: timeoutMs);
    }

    private static string LocalOriginFor(SessionInfo session)
    {
        if (session.Port > 0)
        {
            return $"http://127.0.0.1:{session.Port}";
        }

        return session.Url.TrimEnd('/');
    }

    private static List<SessionInfo> DiscoverRunningSessions()
    {
        (int exitCode, string stdout, _) = RunProcess(
            ContentPacker.FindRusty(),
            ["dev", "list", "--json"],
            workingDirectory: Environment.CurrentDirectory,
            environment: null,
            timeoutMs: 5000);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            return [];
        }

        string? jsonSlice = ExtractJsonPayload(stdout);
        if (jsonSlice is null)
        {
            return [];
        }

        List<SessionInfo> results = [];
        try
        {
            using JsonDocument doc = JsonDocument.Parse(jsonSlice);
            JsonElement array = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("sessions", out JsonElement nested) && nested.ValueKind == JsonValueKind.Array
                    ? nested
                    : default;
            if (array.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                // Skip ended/expired records if any appear.
                if (item.TryGetProperty("endedReason", out JsonElement ended) && ended.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ended.GetString()))
                {
                    continue;
                }

                string id = GetString(item, "id") ?? "";
                string url = GetString(item, "url") ?? "";
                int port = GetInt(item, "port") ?? TryPortFromUrl(url);
                if (string.IsNullOrWhiteSpace(url) && port > 0)
                {
                    url = $"http://127.0.0.1:{port}";
                }

                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                results.Add(new SessionInfo(
                    id,
                    url.TrimEnd('/'),
                    port,
                    GetInt(item, "pid"),
                    GetString(item, "instance"),
                    GetString(item, "label"),
                    ManagedByWorkbench: false));
            }
        }
        catch (JsonException)
        {
        }

        return results;
    }

    private static SessionInfo? ParseStartedSession(string stdout, string fallbackInstance, string fallbackLabel)
    {
        string? jsonSlice = ExtractJsonPayload(stdout);
        if (jsonSlice is null)
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(jsonSlice);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            JsonElement root = doc.RootElement;
            string id = GetString(root, "id") ?? "";
            string url = GetString(root, "url") ?? "";
            int port = GetInt(root, "port") ?? TryPortFromUrl(url);
            if (string.IsNullOrWhiteSpace(url) && port > 0)
            {
                url = $"http://127.0.0.1:{port}";
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            return new SessionInfo(
                id,
                url.TrimEnd('/'),
                port,
                GetInt(root, "pid"),
                GetString(root, "instance") ?? fallbackInstance,
                GetString(root, "label") ?? fallbackLabel,
                ManagedByWorkbench: true);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ResolveGameProject(string? explicitProject, string modulePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitProject))
        {
            string full = Path.GetFullPath(explicitProject);
            return File.Exists(full) ? full : null;
        }

        string[] startDirs =
        [
            Environment.CurrentDirectory,
            Directory.Exists(modulePath) ? modulePath : Path.GetDirectoryName(modulePath) ?? "",
            AppContext.BaseDirectory,
        ];

        foreach (string start in startDirs)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            DirectoryInfo? cursor = new(start);
            for (int depth = 0; depth < 8 && cursor is not null; depth++, cursor = cursor.Parent)
            {
                string candidate = Path.Combine(cursor.FullName, "src", "RustyTales.Game", "RustyTales.Game.csproj");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? ResolveAuthoringWorkspace(string modulePath)
    {
        string probeDir = Directory.Exists(modulePath) ? modulePath : Path.GetDirectoryName(modulePath) ?? modulePath;
        DirectoryInfo? cursor = new(probeDir);
        for (int depth = 0; depth < 4 && cursor is not null; depth++, cursor = cursor.Parent)
        {
            Workspace? ws = Workspace.Find(cursor.FullName, []);
            if (ws is not null)
            {
                return ws.RootDirectory;
            }
        }

        return null;
    }

    private static string? ExtractJsonPayload(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        int objStart = trimmed.IndexOf('{');
        int arrStart = trimmed.IndexOf('[');
        int start = (objStart, arrStart) switch
        {
            (-1, -1) => -1,
            (-1, var a) => a,
            (var o, -1) => o,
            (var o, var a) => Math.Min(o, a),
        };
        if (start < 0)
        {
            return null;
        }

        char close = trimmed[start] == '{' ? '}' : ']';
        int end = trimmed.LastIndexOf(close);
        return end > start ? trimmed[start..(end + 1)] : null;
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static int? GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int value) ? value : null;

    private static int TryPortFromUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Port > 0 ? uri.Port : 0;

    private static (int ExitCode, string Stdout, string Stderr) RunProcess(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        Dictionary<string, string>? environment,
        int timeoutMs)
    {
        ProcessStartInfo start = new(fileName)
        {
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                start.Environment[key] = value;
            }
        }

        try
        {
            using Process process = Process.Start(start)!;
            Task<string> outTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return (-1, "", $"Command `{fileName} {string.Join(' ', args)}` timed out after {timeoutMs / 1000}s.");
            }

            return (process.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            return (-1, "", $"Could not run `{fileName}`: {exception.Message}");
        }
    }
}
