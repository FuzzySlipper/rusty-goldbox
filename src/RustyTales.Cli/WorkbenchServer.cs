using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using RustyTales.Core.Authoring;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;
using RustyTales.Core.Rules;

namespace RustyTales.Cli;

/// <summary>
/// Lightweight local HTTP server backing <c>tales workbench</c>: serves the
/// embedded diagnostic SPA and live JSON/asset endpoints directly from
/// <see cref="RustyTales.Core"/>, with on-demand disk reload.
/// </summary>
internal sealed class WorkbenchServer : IDisposable
{
    private readonly string _modulePath;
    private readonly IReadOnlyList<string> _searchDirectories;
    private readonly IReadOnlyList<string> _extensions;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stateLock = new();
    private Task? _loop;

    private ModuleSet _set;
    private ModuleGraph _graph;

    public WorkbenchServer(
        string modulePath,
        IReadOnlyList<string> searchDirectories,
        IReadOnlyList<string> extensions,
        string host = "127.0.0.1",
        int port = 4173)
    {
        _modulePath = Path.GetFullPath(modulePath);
        _searchDirectories = searchDirectories.ToList();
        _extensions = extensions.ToList();

        (_set, _graph) = LoadNow();

        int actualPort = port == 0 ? PickFreePort() : port;
        string prefixHost = host is "0.0.0.0" or "*" ? "+" : host;
        string displayHost = host is "0.0.0.0" or "*" or "+" ? "127.0.0.1" : host;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{prefixHost}:{actualPort}/");
        _listener.Start();

        Port = actualPort;
        Url = $"http://{displayHost}:{actualPort}/";
    }

    public int Port { get; }

    public string Url { get; }

    public ModuleGraph CurrentGraph
    {
        get
        {
            lock (_stateLock)
            {
                return _graph;
            }
        }
    }

    public void Start()
    {
        _loop ??= Task.Run(() => ServeLoopAsync(_cts.Token));
    }

    public ModuleGraph Reload()
    {
        (ModuleSet set, ModuleGraph graph) = LoadNow();
        lock (_stateLock)
        {
            _set = set;
            _graph = graph;
        }

        return graph;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
    }

    private (ModuleSet Set, ModuleGraph Graph) LoadNow()
    {
        ModuleSet set = ModuleSets.Load(_modulePath, _searchDirectories, _extensions);
        return (set, ModuleGraphBuilder.Build(set));
    }

    private async Task ServeLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequest(context), token);
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        try
        {
            string path = request.Url?.AbsolutePath ?? "/";
            if (path.StartsWith("/api/", StringComparison.Ordinal))
            {
                response.Headers["Access-Control-Allow-Origin"] = "*";
                response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
                if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }
            }

            if (path is "/" or "/index.html")
            {
                WriteEmbedded(response, "index.html", "text/html; charset=utf-8");
                return;
            }

            if (path == "/workbench.css")
            {
                WriteEmbedded(response, "workbench.css", "text/css; charset=utf-8");
                return;
            }

            if (path == "/workbench.js")
            {
                WriteEmbedded(response, "workbench.js", "application/javascript; charset=utf-8");
                return;
            }

            if (path == "/api/health")
            {
                ModuleGraph graph = CurrentGraph;
                WriteJson(response, 200, new
                {
                    ok = true,
                    module = graph.Module.Id,
                    version = graph.Module.Version,
                    kind = graph.Module.Kind,
                    title = graph.Module.Title,
                    isValid = graph.Module.IsValid,
                    areas = graph.Counts.Areas,
                    events = graph.Counts.Events,
                    scenes = graph.Counts.Scenes,
                    issues = graph.Counts.Issues,
                });
                return;
            }

            if (path == "/api/graph")
            {
                bool reload = string.Equals(request.QueryString["reload"], "1", StringComparison.Ordinal)
                    || string.Equals(request.QueryString["reload"], "true", StringComparison.OrdinalIgnoreCase);
                ModuleGraph graph = reload ? Reload() : CurrentGraph;
                WriteRawJson(response, 200, JsonSerializer.Serialize(graph, ModuleGraphBuilder.JsonOptions));
                return;
            }

            if (path == "/api/reload" && request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                ModuleGraph graph = Reload();
                WriteRawJson(response, 200, JsonSerializer.Serialize(graph, ModuleGraphBuilder.JsonOptions));
                return;
            }

            if (path == "/api/definition")
            {
                HandleDefinition(request, response);
                return;
            }

            if (path.StartsWith("/api/asset/", StringComparison.Ordinal))
            {
                HandleAsset(path["/api/asset/".Length..], response);
                return;
            }

            WriteJson(response, 404, new { ok = false, error = $"Unknown endpoint '{path}'." });
        }
        catch (Exception exception)
        {
            try
            {
                WriteJson(response, 500, new { ok = false, error = exception.Message });
            }
            catch
            {
            }
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch
            {
            }
        }
    }

    private void HandleDefinition(HttpListenerRequest request, HttpListenerResponse response)
    {
        string? id = request.QueryString["id"];
        string? type = request.QueryString["type"];
        if (string.IsNullOrWhiteSpace(id))
        {
            WriteJson(response, 400, new { ok = false, error = "Query parameter 'id' is required." });
            return;
        }

        ModuleSet set;
        lock (_stateLock)
        {
            set = _set;
        }

        if (set.Rules is not RuleSet rules)
        {
            WriteJson(response, 404, new { ok = false, error = "Module set has no loaded rules due to validation errors." });
            return;
        }

        Definition? found = rules.Definitions.FirstOrDefault(def =>
            (def.QualifiedId == id || def.Id == id)
            && (string.IsNullOrWhiteSpace(type) || def.Type.Name == type));
        if (found is null)
        {
            WriteJson(response, 404, new { ok = false, error = $"Definition '{id}' was not found." });
            return;
        }

        (string module, string file, string jsonPath) = found.DiagnosticLocation("$");
        var expressions = InspectCommand.Expressions(rules, found)
            .Select(entry => new { path = entry.Path, text = entry.Text, type = entry.Type })
            .ToList();
        var references = found.References
            .Select(site => new { path = site.JsonPath, text = site.Text })
            .ToList();

        WriteJson(response, 200, new
        {
            ok = true,
            id = found.QualifiedId,
            localId = found.Id,
            type = found.Type.Name,
            name = found.Name,
            module,
            file,
            jsonPath,
            json = found.Json,
            expressions,
            references,
        });
    }

    private void HandleAsset(string tail, HttpListenerResponse response)
    {
        string[] parts = tail.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            WriteJson(response, 400, new { ok = false, error = "Expected /api/asset/<module>/<asset-id>." });
            return;
        }

        string moduleId = Uri.UnescapeDataString(parts[0]);
        string assetId = Uri.UnescapeDataString(parts[1]);

        ModuleSet set;
        lock (_stateLock)
        {
            set = _set;
        }

        if (set.Rules is not RuleSet rules)
        {
            WriteJson(response, 404, new { ok = false, error = "Module set has no loaded rules." });
            return;
        }

        Definition? asset = rules.Find(DefinitionTypes.Asset, $"{moduleId}:{assetId}", out _);
        if (asset is null)
        {
            WriteJson(response, 404, new { ok = false, error = $"Asset '{moduleId}:{assetId}' was not found." });
            return;
        }

        ModuleManifest? manifest = set.LoadOrder.Select(m => m.Manifest).FirstOrDefault(m => m.Id == asset.Module);
        if (manifest is null)
        {
            WriteJson(response, 404, new { ok = false, error = $"Module '{asset.Module}' was not found." });
            return;
        }

        string relativeFile = asset.Json.GetProperty("file").GetString()!;
        byte[] bytes;
        try
        {
            bytes = manifest.Source.Read(relativeFile);
        }
        catch (Exception exception) when (exception is IOException or FileNotFoundException)
        {
            WriteJson(response, 404, new { ok = false, error = exception.Message });
            return;
        }

        response.StatusCode = 200;
        response.ContentType = ContentTypeFor(relativeFile);
        response.Headers["Cache-Control"] = "no-cache";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".ogg" or ".oga" or ".opus" => "audio/ogg",
        ".wav" => "audio/wav",
        ".flac" => "audio/flac",
        ".json" => "application/json; charset=utf-8",
        _ => "application/octet-stream",
    };

    private static void WriteEmbedded(HttpListenerResponse response, string fileName, string contentType)
    {
        Assembly assembly = typeof(WorkbenchServer).Assembly;
        string resourceName = $"RustyTales.Cli.Workbench.{fileName}";
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            WriteJson(response, 500, new { ok = false, error = $"Missing embedded Workbench asset '{resourceName}'." });
            return;
        }

        using MemoryStream copy = new();
        stream.CopyTo(copy);
        byte[] bytes = copy.ToArray();
        response.StatusCode = 200;
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-cache";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteJson(HttpListenerResponse response, int statusCode, object payload)
    {
        WriteRawJson(response, statusCode, JsonSerializer.Serialize(payload, ModuleGraphBuilder.JsonOptions));
    }

    private static void WriteRawJson(HttpListenerResponse response, int statusCode, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static int PickFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
