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
    private static readonly object PortBindLock = new();

    private readonly string _modulePath;
    private readonly IReadOnlyList<string> _searchDirectories;
    private readonly IReadOnlyList<string> _extensions;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stateLock = new();
    private readonly ReviewStore _reviews;
    private Task? _loop;

    private ModuleSet _set;
    private ModuleGraph _graph;
    private string? _liveStateJson;

    public WorkbenchServer(
        string modulePath,
        IReadOnlyList<string> searchDirectories,
        IReadOnlyList<string> extensions,
        string host = "127.0.0.1",
        int port = 4173,
        string? reviewsPath = null)
    {
        _modulePath = Path.GetFullPath(modulePath);
        _searchDirectories = searchDirectories.ToList();
        _extensions = extensions.ToList();

        (_set, _graph) = LoadNow();
        _reviews = new ReviewStore(
            reviewsPath ?? ReviewStore.DefaultPathFor(_modulePath, _graph.Module.Id),
            _graph.Module.Id);

        string prefixHost = host is "0.0.0.0" or "*" ? "+" : host;
        string displayHost = host is "0.0.0.0" or "*" or "+" ? "127.0.0.1" : host;

        (_listener, Port) = BindListener(prefixHost, port);
        Url = $"http://{displayHost}:{Port}/";
    }

    private static (HttpListener Listener, int Port) BindListener(string prefixHost, int requestedPort)
    {
        if (requestedPort != 0)
        {
            HttpListener fixedListener = new();
            fixedListener.Prefixes.Add($"http://{prefixHost}:{requestedPort}/");
            fixedListener.Start();
            return (fixedListener, requestedPort);
        }

        lock (PortBindLock)
        {
            HttpListenerException? lastError = null;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                int candidate = PickFreePort();
                HttpListener listener = new();
                listener.Prefixes.Add($"http://{prefixHost}:{candidate}/");
                try
                {
                    listener.Start();
                    return (listener, candidate);
                }
                catch (HttpListenerException exception)
                {
                    lastError = exception;
                    try
                    {
                        listener.Close();
                    }
                    catch
                    {
                    }
                }
            }

            throw (Exception?)lastError ?? new InvalidOperationException("Could not bind an ephemeral loopback port.");
        }
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
        catch (Exception exception) when (exception is ObjectDisposedException or HttpListenerException)
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
                response.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
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
                (ModuleSet set, ModuleGraph graph) = Snapshot();
                ReviewCounts reviewCounts = ReviewResolver.Summarize(ReviewResolver.ResolveAll(_reviews.List(), set, graph));
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
                    reviews = reviewCounts,
                });
                return;
            }

            if (path == "/api/reviews" || path.StartsWith("/api/reviews/", StringComparison.Ordinal))
            {
                HandleReviews(request, response, path);
                return;
            }

            if (path == "/api/live-state")
            {
                HandleLiveState(request, response);
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

    private (ModuleSet Set, ModuleGraph Graph) Snapshot()
    {
        lock (_stateLock)
        {
            return (_set, _graph);
        }
    }

    private void HandleReviews(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        (ModuleSet set, ModuleGraph graph) = Snapshot();
        string method = request.HttpMethod.ToUpperInvariant();

        if (path == "/api/reviews" && method == "GET")
        {
            List<ResolvedReviewPin> all = ReviewResolver.ResolveAll(_reviews.List(), set, graph);
            List<ResolvedReviewPin> filtered = ReviewCommand.FilterPins(all, request.QueryString["status"], request.QueryString["kind"]);
            ReviewCounts counts = ReviewResolver.Summarize(all);
            WriteRawJson(response, 200, JsonSerializer.Serialize(new
            {
                ok = true,
                module = graph.Module.Id,
                storePath = _reviews.FilePath,
                counts,
                pins = filtered,
            }, ReviewStore.JsonOptions));
            return;
        }

        if (path == "/api/reviews" && method == "POST")
        {
            using StreamReader reader = new(request.InputStream, request.ContentEncoding);
            string body = reader.ReadToEnd();
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException exception)
            {
                WriteJson(response, 400, new { ok = false, error = $"Invalid JSON: {exception.Message}" });
                return;
            }

            using (doc)
            {
                JsonElement root = doc.RootElement;
                string? kind = root.TryGetProperty("targetKind", out JsonElement kEl) && kEl.ValueKind == JsonValueKind.String ? kEl.GetString() : null;
                string? target = root.TryGetProperty("target", out JsonElement tEl) && tEl.ValueKind == JsonValueKind.String ? tEl.GetString() : null;
                string? status = root.TryGetProperty("status", out JsonElement sEl) && sEl.ValueKind == JsonValueKind.String ? sEl.GetString() : null;
                string note = root.TryGetProperty("note", out JsonElement nEl) && nEl.ValueKind == JsonValueKind.String ? nEl.GetString() ?? "" : "";
                string author = root.TryGetProperty("author", out JsonElement aEl) && aEl.ValueKind == JsonValueKind.String ? aEl.GetString() ?? "human" : "human";

                if (!ReviewTargetKinds.IsValid(kind) || string.IsNullOrWhiteSpace(target) || !ReviewStatuses.IsValid(status))
                {
                    WriteJson(response, 400, new { ok = false, error = "targetKind, target, and valid status (needs-fix, approved, question, resolved) are required." });
                    return;
                }

                ReviewResolver.TargetMeta meta = ReviewResolver.DescribeTarget(kind!, target!, set, graph);
                ReviewPin saved = _reviews.Upsert(kind!, target!, status!, note, author, meta.Revision, meta.AreaId, meta.Cell, meta.DoorId, meta.EventId);
                ResolvedReviewPin resolved = ReviewResolver.Resolve(saved, set, graph);
                ReviewCounts counts = ReviewResolver.Summarize(ReviewResolver.ResolveAll(_reviews.List(), set, graph));
                WriteRawJson(response, 200, JsonSerializer.Serialize(new { ok = true, pin = resolved, counts }, ReviewStore.JsonOptions));
                return;
            }
        }

        if (path == "/api/reviews/clear" && method == "POST")
        {
            string statusFilter = ReviewStatuses.Resolved;
            using (StreamReader reader = new(request.InputStream, request.ContentEncoding))
            {
                string body = reader.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("status", out JsonElement stEl) && stEl.ValueKind == JsonValueKind.String)
                        {
                            statusFilter = stEl.GetString()!;
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }

            int cleared = _reviews.Clear(statusFilter);
            ReviewCounts counts = ReviewResolver.Summarize(ReviewResolver.ResolveAll(_reviews.List(), set, graph));
            WriteRawJson(response, 200, JsonSerializer.Serialize(new { ok = true, cleared, counts }, ReviewStore.JsonOptions));
            return;
        }

        string tail = path["/api/reviews/".Length..];
        if (tail.EndsWith("/resolve", StringComparison.Ordinal) && method == "POST")
        {
            string id = Uri.UnescapeDataString(tail[..^"/resolve".Length]);
            string? resolution = null;
            using (StreamReader reader = new(request.InputStream, request.ContentEncoding))
            {
                string body = reader.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("resolution", out JsonElement rEl) && rEl.ValueKind == JsonValueKind.String)
                        {
                            resolution = rEl.GetString();
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }

            ReviewPin? existing = _reviews.Find(id);
            if (existing is null)
            {
                WriteJson(response, 404, new { ok = false, error = $"Review pin '{id}' was not found." });
                return;
            }

            ReviewResolver.TargetMeta meta = ReviewResolver.DescribeTarget(existing.TargetKind, existing.Target, set, graph);
            ReviewPin resolvedRaw = _reviews.Resolve(id, resolution, meta.Revision)!;
            ResolvedReviewPin pin = ReviewResolver.Resolve(resolvedRaw, set, graph);
            ReviewCounts counts = ReviewResolver.Summarize(ReviewResolver.ResolveAll(_reviews.List(), set, graph));
            WriteRawJson(response, 200, JsonSerializer.Serialize(new { ok = true, pin, counts }, ReviewStore.JsonOptions));
            return;
        }

        if (method == "DELETE" && !tail.Contains('/'))
        {
            string id = Uri.UnescapeDataString(tail);
            bool deleted = _reviews.Delete(id);
            if (!deleted)
            {
                WriteJson(response, 404, new { ok = false, error = $"Review pin '{id}' was not found." });
                return;
            }

            ReviewCounts counts = ReviewResolver.Summarize(ReviewResolver.ResolveAll(_reviews.List(), set, graph));
            WriteRawJson(response, 200, JsonSerializer.Serialize(new { ok = true, deleted = true, counts }, ReviewStore.JsonOptions));
            return;
        }

        WriteJson(response, 405, new { ok = false, error = $"Unsupported review request {method} {path}." });
    }

    private void HandleLiveState(HttpListenerRequest request, HttpListenerResponse response)
    {
        string method = request.HttpMethod.ToUpperInvariant();
        if (method == "GET")
        {
            string? snapshot;
            lock (_stateLock)
            {
                snapshot = _liveStateJson;
            }

            if (snapshot is null)
            {
                WriteJson(response, 200, new { ok = true, connected = false });
                return;
            }

            WriteRawJson(response, 200, snapshot);
            return;
        }

        if (method == "POST")
        {
            using StreamReader reader = new(request.InputStream, request.ContentEncoding);
            string body = reader.ReadToEnd();
            try
            {
                using JsonDocument parsed = JsonDocument.Parse(body);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                {
                    WriteJson(response, 400, new { ok = false, error = "Live state payload must be a JSON object." });
                    return;
                }
            }
            catch (JsonException exception)
            {
                WriteJson(response, 400, new { ok = false, error = $"Invalid JSON: {exception.Message}" });
                return;
            }

            lock (_stateLock)
            {
                _liveStateJson = body;
            }

            WriteJson(response, 200, new { ok = true });
            return;
        }

        WriteJson(response, 405, new { ok = false, error = $"Unsupported method {method} for /api/live-state." });
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
