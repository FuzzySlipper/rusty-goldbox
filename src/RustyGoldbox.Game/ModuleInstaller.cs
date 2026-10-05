using Rusty.Engine;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Game;

/// <summary>A module a previewed source offers, with what it requires.</summary>
internal sealed record ModuleOffer(string Id, string Version, string Kind, string Title, string Provenance, IReadOnlyList<string> Requires);

/// <summary>A newer version of a fetched module, from where it was fetched.</summary>
internal sealed record ModuleUpdateOffer(string Id, string Installed, string Available, string Source);

/// <summary>An installed module the player may remove.</summary>
internal sealed record InstalledModule(string Id, string Version, string? Source);

/// <summary>
/// Previews, installs, updates and removes modules in the module library
/// from where they are published, over Engine <c>Http</c>. The rules are
/// Core's <see cref="ModuleFetcher"/>; this runs its steps across updates,
/// one transfer at a time, and keeps what the title screen shows.
/// </summary>
internal sealed class ModuleInstaller(ModuleLibrary library)
{
    private const string UserAgent = "rusty-goldbox";

    // GitHub's release downloads sometimes answer 5xx or drop the connection
    // for a moment, especially just after an upload; a few retries make an
    // install dependable.
    private const int Attempts = 3;

    private readonly Queue<(string Activity, Func<IEngineContext, ModuleFetch> Start, Action<FetchResult> Done)> _queued = new();
    private (string Activity, ModuleFetch Fetch, Action<FetchResult> Done)? _current;
    private HttpTransfer? _transfer;
    private int _attempt;

    /// <summary>What is running, for the title screen: "Reading github:…", "Downloading blackapple-art 0.1.0".</summary>
    public string? Activity { get; private set; }

    public ulong Received { get; private set; }

    public ulong Expected { get; private set; }

    public bool Busy => _current is not null || _queued.Count > 0;

    public string? PreviewSource { get; private set; }

    public List<ModuleOffer> Preview { get; } = [];

    public List<ModuleUpdateOffer> Updates { get; } = [];

    public List<string> Messages { get; } = [];

    /// <summary>Set when an install changed the library, so the campaign list is read again.</summary>
    public bool LibraryChanged { get; set; }

    /// <summary>Reads what <paramref name="sourceText"/> publishes, to show before installing.</summary>
    public void PreviewFrom(string sourceText)
    {
        Messages.Clear();
        if (!TrySource(sourceText, out ReleaseSource? source))
        {
            return;
        }

        Preview.Clear();
        PreviewSource = null;
        List<ReleasedModule> published = [];
        Enqueue($"Reading {source}", engine => Fetcher(engine).Published(source!, published), result =>
        {
            Messages.AddRange(result.Problems);
            PreviewSource = source!.Text;
            foreach (ReleasedModule module in published.OrderBy(module => module.Kind != ModuleKind.Campaign).ThenBy(module => module.Id).ThenByDescending(module => module.Version))
            {
                Preview.Add(new ModuleOffer(
                    module.Id,
                    module.Version.ToString(),
                    ModuleKinds.Name(module.Kind),
                    module.Title,
                    module.Provenance,
                    module.Requires.Select(requirement => $"{requirement.Id} {requirement.Range}{(requirement.Releases is null ? "" : $" from {requirement.Releases}")}").ToList()));
            }
        });
    }

    /// <summary>Installs a module from <paramref name="sourceText"/> with everything it requires that isn't here.</summary>
    public void Install(string sourceText, string? id, string? version)
    {
        Messages.Clear();
        if (!TrySource(sourceText, out ReleaseSource? source))
        {
            return;
        }

        VersionRange? range = null;
        if (version is not null && !VersionRange.TryParse(version, out range))
        {
            Messages.Add($"'{version}' is not a module version.");
            return;
        }

        string target = InstalledModules.DefaultDirectory();
        Enqueue($"Installing from {source}", engine => Fetcher(engine).Get(source!, id, range, library.Present(), target), result =>
        {
            Messages.AddRange(result.Installed.Select(module => $"Installed {module.Module.Title} ({module.Module.Id} {module.Module.Version}): {module.Module.Provenance}"));
            Messages.AddRange(result.Problems);
            if (result.Installed.Count == 0 && result.Succeeded)
            {
                Messages.Add("Everything it needs is already installed.");
            }

            LibraryChanged |= result.Installed.Count > 0;
            Updates.RemoveAll(update => result.Installed.Any(module => module.Module.Id == update.Id));
        });
    }

    /// <summary>Checks where each fetched module came from for a newer version.</summary>
    public void CheckUpdates()
    {
        Messages.Clear();
        Updates.Clear();
        Dictionary<string, InstalledSources.Entry> record = InstalledSources.Read(InstalledModules.DefaultDirectory());
        if (record.Count == 0)
        {
            Messages.Add("No installed module was fetched from a published source.");
            return;
        }

        List<(string Id, InstalledSources.Entry Entry, ReleaseSource Source)> checks = [];
        foreach ((string id, InstalledSources.Entry entry) in record.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (ReleaseSource.TryParse(entry.Releases, out ReleaseSource? source))
            {
                checks.Add((id, entry, source!));
            }
        }

        int remaining = checks.Count;
        foreach ((string id, InstalledSources.Entry entry, ReleaseSource source) in checks)
        {
            ModuleVersion newestHere = entry.Versions.Keys.Select(text => ModuleVersion.TryParse(text, out ModuleVersion version) ? version : default).Max();
            List<ReleasedModule> published = [];
            Enqueue($"Checking {source}", engine => Fetcher(engine).Published(source, published), result =>
            {
                Messages.AddRange(result.Problems);
                if (published.Where(module => module.Id == id && module.Version > newestHere).MaxBy(module => module.Version) is ReleasedModule newest)
                {
                    Updates.Add(new ModuleUpdateOffer(id, newestHere.ToString(), newest.Version.ToString(), source.Text));
                }

                if (--remaining == 0 && Updates.Count == 0 && Messages.Count == 0)
                {
                    Messages.Add("Every fetched module is up to date.");
                }
            });
        }
    }

    /// <summary>Removes one installed version through the Engine library and forgets it.</summary>
    public void Remove(IEngineContext engine, string id, string version)
    {
        Messages.Clear();
        if (!ModuleIds.IsValid(id) || !ModuleVersion.TryParse(version, out ModuleVersion parsed))
        {
            Messages.Add($"'{id} {version}' isn't an installed module.");
            return;
        }

        string directory = InstalledModules.DefaultDirectory();
        using HttpLibrary installed = engine.Http.OpenLibrary(new HttpLibraryOpenRequest(directory));
        if (!engine.Http.RemoveLibraryFile(new HttpLibraryFileRequest(installed, InstalledModules.FileName(id, parsed))).Removed)
        {
            Messages.Add($"{id} {version} isn't installed in {directory}.");
            return;
        }

        InstalledSources.Forget(directory, id, parsed);
        Messages.Add($"Removed {id} {version}. A save made with it now names it as missing when loaded.");
        LibraryChanged = true;
    }

    /// <summary>Stops what is running; nothing half-downloaded is left in the library.</summary>
    public void Cancel(IEngineContext engine)
    {
        if (_transfer is not null)
        {
            engine.Http.Cancel(_transfer);
            _transfer.Dispose();
            _transfer = null;
        }

        _current = null;
        _queued.Clear();
        Activity = null;
        Messages.Clear();
        Messages.Add("Cancelled.");
    }

    /// <summary>Advances the running work; true when anything the title screen shows changed.</summary>
    public bool Tick(IEngineContext engine)
    {
        bool changed = false;
        while (true)
        {
            if (_current is null)
            {
                if (_queued.Count == 0)
                {
                    if (Activity is not null)
                    {
                        Activity = null;
                        changed = true;
                    }

                    return changed;
                }

                (string activity, Func<IEngineContext, ModuleFetch> start, Action<FetchResult> done) = _queued.Dequeue();
                Activity = activity;
                _current = (activity, start(engine), done);
                changed = true;
            }

            (string _, ModuleFetch fetch, Action<FetchResult> finished) = _current.Value;
            if (fetch.Pending is not FetchStep step)
            {
                finished(fetch.Result!);
                _current = null;
                changed = true;
                continue;
            }

            if (_transfer is null)
            {
                Start(engine, step);
                return true;
            }

            HttpTransferReadout readout = engine.Http.Read(_transfer);
            if (readout.ReceivedBytes != Received || readout.ExpectedBytes != Expected)
            {
                (Received, Expected) = (readout.ReceivedBytes, readout.ExpectedBytes);
                changed = true;
            }

            if (readout.State == HttpTransferState.Running)
            {
                return changed;
            }

            string? failure = Failure(engine, _transfer, step, readout);
            bool transient = readout.Failure is HttpFailure.Connect or HttpFailure.Interrupted || readout.Status >= 500;
            if (failure is not null && transient && ++_attempt < Attempts)
            {
                _transfer.Dispose();
                _transfer = null;
                continue;
            }

            if (step is ReadStep read && failure is null)
            {
                read.Body = engine.Http.ReadBody(_transfer).ToArray();
            }

            step.Failure = failure;
            _transfer.Dispose();
            _transfer = null;
            _attempt = 0;
            fetch.Resume();
            changed = true;
        }
    }

    private void Start(IEngineContext engine, FetchStep step)
    {
        Received = 0;
        Expected = 0;

        switch (step)
        {
            case ReadStep read:
                Activity = $"Reading {read.Url.Host}{read.Url.AbsolutePath}";
                _transfer = engine.Http.Get(new HttpGetRequest(read.Url.AbsoluteUri, Header(read, "User-Agent") ?? UserAgent, Header(read, "Accept") ?? "", "", ""));
                break;
            case DownloadStep download:
                Activity = $"Downloading {download.Module}";
                using (HttpLibrary downloads = engine.Http.OpenLibrary(new HttpLibraryOpenRequest(download.Directory)))
                {
                    _transfer = engine.Http.Download(new HttpDownloadRequest(downloads, download.FileName, download.Url.AbsoluteUri, UserAgent, "application/octet-stream", ""));
                }

                break;
        }
    }

    /// <summary>Why a finished transfer failed, in words, or null when it succeeded.</summary>
    private static string? Failure(IEngineContext engine, HttpTransfer transfer, FetchStep step, HttpTransferReadout readout)
    {
        return readout.State switch
        {
            HttpTransferState.Cancelled => "cancelled",
            HttpTransferState.Failed when readout.Failure == HttpFailure.Status => $"the server answered {readout.Status}",
            HttpTransferState.Failed => engine.Http.ReadDiagnosticText(transfer) is { Length: > 0 } why ? why : readout.Failure.ToString(),
            _ when step is ReadStep && readout.Status is < 200 or >= 300 => $"the server answered {readout.Status}",
            _ => null,
        };
    }

    private static string? Header(ReadStep step, string name) => step.Headers.TryGetValue(name, out string? value) ? value : null;

    private void Enqueue(string activity, Func<IEngineContext, ModuleFetch> start, Action<FetchResult> done)
    {
        _queued.Enqueue((activity, start, done));
    }

    private ModuleFetcher Fetcher(IEngineContext engine) => new(container => IdentityOf(engine, container));

    private static string? IdentityOf(IEngineContext engine, string container)
    {
        try
        {
            using ProductContentBundle bundle = ProductContentBundle.OpenContainer(engine.Content, container);
            return new BundleModuleSource(bundle).Identity;
        }
        catch (EngineCallException)
        {
            return null;
        }
    }

    private bool TrySource(string text, out ReleaseSource? source)
    {
        // Accept a pasted GitHub page URL as well as github:owner/repo.
        string trimmed = text.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? url) && url.Host == "github.com" && url.Segments.Length >= 3)
        {
            trimmed = $"github:{url.Segments[1].TrimEnd('/')}/{url.Segments[2].TrimEnd('/').Replace(".git", "", StringComparison.Ordinal)}";
        }

        if (ReleaseSource.TryParse(trimmed, out source))
        {
            return true;
        }

        Messages.Add($"'{text}' isn't a module source. Paste a GitHub repository (https://github.com/owner/repo or github:owner/repo) or the https URL of a {ReleaseIndex.FileName}.");
        return false;
    }
}
