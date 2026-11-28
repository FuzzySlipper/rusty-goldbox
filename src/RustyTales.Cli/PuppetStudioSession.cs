using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace RustyTales.Cli;

/// <summary>
/// Manages an on-demand embedded <c>puppet-studio serve</c> HTTP process for
/// <see cref="WorkbenchServer"/>, exposing workspace discovery, start/stop lifecycle,
/// one-click tight-union sheet export into the active module, and guaranteed teardown.
/// </summary>
internal sealed class PuppetStudioSession : IDisposable
{
    private readonly string _modulePath;
    private readonly string? _explicitWorkspace;
    private readonly object _lock = new();
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    private Process? _process;
    private string? _activeWorkspace;
    private string? _activeUrl;
    private int _activePort;
    private bool _disposed;

    public PuppetStudioSession(string modulePath, string? explicitWorkspace = null)
    {
        _modulePath = Path.GetFullPath(modulePath);
        _explicitWorkspace = string.IsNullOrWhiteSpace(explicitWorkspace) ? null : Path.GetFullPath(explicitWorkspace);
    }

    public object GetStatus()
    {
        string? executable = PuppetBridge.FindExecutable(_modulePath);
        List<string> workspaces = PuppetBridge.DiscoverWorkspaces(_modulePath, _explicitWorkspace);
        if (workspaces.Count == 0 && executable is not null)
        {
            // Offer puppet-studio's bundled examples/poselock-trio as a ready-to-explore fallback workspace when present.
            string? repoRoot = Path.GetDirectoryName(Path.GetDirectoryName(executable));
            if (repoRoot is not null)
            {
                string exampleWs = Path.Combine(repoRoot, "examples", "poselock-trio");
                if (Directory.Exists(exampleWs) && PuppetBridge.ListCharacterDirs(exampleWs).Count > 0)
                {
                    workspaces.Add(exampleWs);
                }
            }
        }

        bool running;
        string? url;
        int port;
        string? workspace;
        lock (_lock)
        {
            if (_process is not null && _process.HasExited)
            {
                CleanupProcessLocked();
            }

            running = _process is not null;
            url = _activeUrl;
            port = _activePort;
            workspace = _activeWorkspace;
        }

        string? inspectWs = workspace ?? workspaces.FirstOrDefault();
        List<PuppetBridge.PuppetCharacterSummary> characters = inspectWs is not null
            ? PuppetBridge.ListCharacterDirs(inspectWs).Select(PuppetBridge.ReadCharacterSummary).ToList()
            : [];

        return new
        {
            ok = true,
            available = executable is not null,
            executable,
            running,
            url,
            port,
            workspace = inspectWs,
            workspaces,
            moduleDir = Directory.Exists(_modulePath) ? _modulePath : null,
            openFlagCount = characters.Sum(c => c.OpenFlags),
            characters,
        };
    }

    public (bool Ok, string? Error, object Status) Start(string? requestedWorkspace = null)
    {
        string? executable = PuppetBridge.FindExecutable(_modulePath);
        if (executable is null)
        {
            return (false, "puppet-studio executable was not found. Install puppet-studio on PATH or set PUPPET_STUDIO_BIN.", GetStatus());
        }

        string? workspace = !string.IsNullOrWhiteSpace(requestedWorkspace)
            ? Path.GetFullPath(requestedWorkspace)
            : _explicitWorkspace ?? PuppetBridge.DiscoverWorkspaces(_modulePath, null).FirstOrDefault();

        if (workspace is null)
        {
            string? repoRoot = Path.GetDirectoryName(Path.GetDirectoryName(executable));
            if (repoRoot is not null)
            {
                string exampleWs = Path.Combine(repoRoot, "examples", "poselock-trio");
                if (Directory.Exists(exampleWs))
                {
                    workspace = exampleWs;
                }
            }
        }

        if (workspace is null || !Directory.Exists(workspace))
        {
            return (false, "No puppet workspace directory found. Pass --puppets <dir> or enter a directory containing puppet character folders.", GetStatus());
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return (false, "Workbench is shutting down.", GetStatus());
            }

            if (_process is not null && !_process.HasExited && string.Equals(_activeWorkspace, workspace, StringComparison.Ordinal))
            {
                return (true, null, GetStatusLocked(executable, workspace));
            }

            StopLocked();
        }

        int port = PickFreePort();
        string url = $"http://127.0.0.1:{port}";

        ProcessStartInfo psi = new()
        {
            FileName = executable,
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("serve");
        psi.ArgumentList.Add(workspace);
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add("127.0.0.1");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Process proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to launch '{executable}'.");
        // Drain stdout/stderr asynchronously so buffers never block the child HTTP server.
        List<string> stderrLines = [];
        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                lock (stderrLines)
                {
                    if (stderrLines.Count < 20)
                    {
                        stderrLines.Add(e.Data);
                    }
                }
            }
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        bool healthy = WaitForHealth(url, proc, TimeSpan.FromSeconds(15));
        if (!healthy)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore kill races.
            }

            proc.Dispose();
            string errDetail;
            lock (stderrLines)
            {
                errDetail = string.Join(" | ", stderrLines);
            }

            return (false, $"puppet-studio serve failed to become healthy at {url}.{(string.IsNullOrWhiteSpace(errDetail) ? "" : " " + errDetail)}", GetStatus());
        }

        lock (_lock)
        {
            if (_disposed)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                proc.Dispose();
                return (false, "Workbench shut down during startup.", GetStatus());
            }

            _process = proc;
            _activeWorkspace = workspace;
            _activeUrl = url;
            _activePort = port;
        }

        return (true, null, GetStatus());
    }

    public object Stop()
    {
        lock (_lock)
        {
            StopLocked();
        }

        return GetStatus();
    }

    public PuppetBridge.ExportSheetResult ExportCharacter(
        string characterId,
        string? workspaceOverride = null,
        string? clips = null,
        int? targetHeight = 160,
        double worldHeight = 1.0,
        string faces = "right",
        string sampling = "linear",
        bool rebake = false)
    {
        if (!Directory.Exists(_modulePath) || !File.Exists(Path.Combine(_modulePath, "module.json")))
        {
            throw new InvalidOperationException("Export requires an editable module directory (not a read-only .rpak container).");
        }

        string? workspace;
        lock (_lock)
        {
            workspace = workspaceOverride ?? _activeWorkspace;
        }

        workspace ??= _explicitWorkspace ?? PuppetBridge.DiscoverWorkspaces(_modulePath, null).FirstOrDefault();
        if (workspace is null)
        {
            throw new InvalidOperationException("No active puppet workspace.");
        }

        string charDir = Path.GetFullPath(Path.Combine(workspace, characterId));
        if (!PuppetBridge.IsCharacterDir(charDir))
        {
            throw new InvalidOperationException($"Character '{characterId}' was not found in '{workspace}'.");
        }

        return PuppetBridge.ExportSheet(new PuppetBridge.ExportSheetOptions(
            CharacterDir: charDir,
            ModuleDir: _modulePath,
            AssetId: characterId,
            Clips: PuppetBridge.ParseClipSpecs(clips),
            TargetHeight: targetHeight,
            WorldHeight: worldHeight,
            Faces: faces,
            Sampling: sampling,
            Rebake: rebake));
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            StopLocked();
        }
    }

    private object GetStatusLocked(string executable, string workspace) => new
    {
        ok = true,
        available = true,
        executable,
        running = _process is not null && !_process.HasExited,
        url = _activeUrl,
        port = _activePort,
        workspace,
        workspaces = PuppetBridge.DiscoverWorkspaces(_modulePath, _explicitWorkspace),
        moduleDir = Directory.Exists(_modulePath) ? _modulePath : null,
        characters = PuppetBridge.ListCharacterDirs(workspace).Select(PuppetBridge.ReadCharacterSummary).ToList(),
    };

    private void StopLocked()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch
        {
            // Ignore shutdown races.
        }

        CleanupProcessLocked();
    }

    private void CleanupProcessLocked()
    {
        try
        {
            _process?.Dispose();
        }
        catch
        {
        }

        _process = null;
        _activeWorkspace = null;
        _activeUrl = null;
        _activePort = 0;
    }

    private static bool WaitForHealth(string baseUrl, Process proc, TimeSpan timeout)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (proc.HasExited)
            {
                return false;
            }

            try
            {
                using HttpResponseMessage resp = HealthClient.GetAsync($"{baseUrl}/api/health").GetAwaiter().GetResult();
                if (resp.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // Still starting up.
            }

            Thread.Sleep(150);
        }

        return false;
    }

    private static int PickFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
