using System.Text.Json;

namespace RustyGoldbox.Core.Modules;

/// <summary>A module <see cref="ModuleFetcher"/> installed.</summary>
public sealed record FetchedModule(ReleasedModule Module, ReleaseSource From, string Container);

/// <summary>What a fetch did.</summary>
/// <param name="Present">Requirements already met by modules on this machine, as "id version".</param>
public sealed record FetchResult(IReadOnlyList<FetchedModule> Installed, IReadOnlyList<string> Present, IReadOnlyList<string> Problems)
{
    public bool Succeeded => Problems.Count == 0;
}

/// <summary>
/// Network work a fetch is waiting for. The caller does it however its host
/// allows (the CLI at once with .NET, the Game over Engine <c>Http</c> across
/// updates), fills in the result and resumes the fetch.
/// </summary>
public abstract class FetchStep
{
    /// <summary>Why it failed, or null when it succeeded.</summary>
    public string? Failure { get; set; }
}

/// <summary>GET a URL into memory.</summary>
public sealed class ReadStep(Uri url, IReadOnlyDictionary<string, string> headers) : FetchStep
{
    public Uri Url { get; } = url;

    public IReadOnlyDictionary<string, string> Headers { get; } = headers;

    /// <summary>The body, when the GET succeeded.</summary>
    public byte[]? Body { get; set; }
}

/// <summary>Download a URL to <see cref="FileName"/> in <see cref="Directory"/>, replacing any file of that name.</summary>
public sealed class DownloadStep(Uri url, string directory, string fileName, string module) : FetchStep
{
    public Uri Url { get; } = url;

    public string Directory { get; } = directory;

    public string FileName { get; } = fileName;

    /// <summary>The module being downloaded, as "id version", for progress.</summary>
    public string Module { get; } = module;
}

/// <summary>
/// One fetch in progress: run it by doing each <see cref="Pending"/> step and
/// calling <see cref="Resume"/> until <see cref="Result"/> is set.
/// </summary>
public sealed class ModuleFetch
{
    private readonly IEnumerator<FetchStep> _steps;

    internal ModuleFetch(IEnumerable<FetchStep> steps, Func<FetchResult> result)
    {
        _steps = steps.GetEnumerator();
        ResultOf = result;
        Resume();
    }

    /// <summary>The step the fetch waits for, or null once it has finished.</summary>
    public FetchStep? Pending { get; private set; }

    /// <summary>What the fetch did, once it has finished.</summary>
    public FetchResult? Result { get; private set; }

    private Func<FetchResult> ResultOf { get; }

    /// <summary>Continues after <see cref="Pending"/> has been done and its result filled in.</summary>
    public void Resume()
    {
        if (_steps.MoveNext())
        {
            Pending = _steps.Current;
            return;
        }

        Pending = null;
        Result = ResultOf();
        _steps.Dispose();
    }

    /// <summary>Runs the whole fetch with steps done at once, as the CLI does.</summary>
    public FetchResult RunWith(Action<ReadStep> read, Action<DownloadStep> download)
    {
        while (Pending is FetchStep step)
        {
            switch (step)
            {
                case ReadStep reading:
                    read(reading);
                    break;
                case DownloadStep downloading:
                    download(downloading);
                    break;
            }

            Resume();
        }

        return Result!;
    }
}

/// <summary>
/// Fetches a module from where it is published, and every module it requires
/// that isn't already here, into a module library. Network work is handed out
/// as steps, so the CLI and the Game share the rules.
/// </summary>
/// <param name="identityOf">A container's Engine content identity, or null when it isn't a usable container.</param>
public sealed class ModuleFetcher(Func<string, string?> identityOf)
{
    private static readonly Dictionary<string, string> GitHubHeaders = new()
    {
        ["Accept"] = "application/vnd.github+json",
        ["User-Agent"] = "rusty-goldbox",
    };

    private readonly Dictionary<string, List<Offer>> _offers = [];

    /// <summary>Starts fetching a module and what it requires.</summary>
    /// <param name="id">The module to fetch; may be left out when the source offers one module, or one campaign.</param>
    /// <param name="range">Acceptable versions; any version when null.</param>
    /// <param name="available">Modules already on this machine, as (id, version).</param>
    /// <param name="library">The module library directory to install into.</param>
    public ModuleFetch Get(ReleaseSource source, string? id, VersionRange? range, IEnumerable<(string Id, ModuleVersion Version)> available, string library)
    {
        List<FetchedModule> installed = [];
        List<string> present = [];
        List<string> problems = [];
        return new ModuleFetch(Fetch(source, id, range, [.. available], library, installed, present, problems), () => new FetchResult(installed, present, problems));
    }

    /// <summary>Starts reading every module the releases of <paramref name="source"/> offer.</summary>
    public ModuleFetch Published(ReleaseSource source, List<ReleasedModule> modules)
    {
        List<string> problems = [];
        return new ModuleFetch(Listing(source, modules, problems), () => new FetchResult([], [], problems));
    }

    private IEnumerable<FetchStep> Listing(ReleaseSource source, List<ReleasedModule> modules, List<string> problems)
    {
        List<Offer> offers = [];
        foreach (FetchStep step in Offers(source, offers, problems))
        {
            yield return step;
        }

        modules.AddRange(offers.Select(offer => offer.Module));
    }

    private IEnumerable<FetchStep> Fetch(
        ReleaseSource source,
        string? id,
        VersionRange? range,
        List<(string Id, ModuleVersion Version)> here,
        string library,
        List<FetchedModule> installed,
        List<string> present,
        List<string> problems)
    {
        Queue<(ReleaseSource Source, string? Id, VersionRange? Range, string Why)> wanted = new();
        wanted.Enqueue((source, id, range, $"requested from {source}"));
        while (wanted.Count > 0)
        {
            (ReleaseSource from, string? wantedId, VersionRange? wantedRange, string why) = wanted.Dequeue();
            List<Offer> offers = [];
            foreach (FetchStep step in Offers(from, offers, problems))
            {
                yield return step;
            }

            if (offers.Count == 0)
            {
                continue;
            }

            wantedId ??= Pick(from, offers, problems);
            if (wantedId is null)
            {
                continue;
            }

            if (here.Where(module => module.Id == wantedId).Where(module => wantedRange?.Contains(module.Version) ?? true).OrderByDescending(module => module.Version).FirstOrDefault() is { Id: not null } have)
            {
                present.Add($"{have.Id} {have.Version}");

                // Still make sure what it requires is here, so an interrupted fetch can be finished.
                if (offers.FirstOrDefault(candidate => candidate.Module.Id == have.Id && candidate.Module.Version == have.Version) is Offer known)
                {
                    foreach (ModuleRequirement requirement in known.Module.Requires)
                    {
                        wanted.Enqueue((requirement.Releases ?? from, requirement.Id, requirement.Range, $"required by {have.Id} {have.Version}"));
                    }
                }

                continue;
            }

            Offer? offer = offers
                .Where(candidate => candidate.Module.Id == wantedId && (wantedRange?.Contains(candidate.Module.Version) ?? true))
                .OrderByDescending(candidate => candidate.Module.Version)
                .FirstOrDefault();
            if (offer is null)
            {
                string versions = string.Join(", ", offers.Where(candidate => candidate.Module.Id == wantedId).Select(candidate => candidate.Module.Version.ToString()).Distinct());
                problems.Add($"{from} has no release of '{wantedId}'{(wantedRange is null ? "" : $" matching {wantedRange}")} ({why}). {(versions.Length == 0 ? "It doesn't publish that module." : $"It has {versions}.")}");
                continue;
            }

            List<FetchedModule> fetched = [];
            foreach (FetchStep step in Install(offer, from, library, fetched, problems))
            {
                yield return step;
            }

            if (fetched is not [FetchedModule module])
            {
                continue;
            }

            installed.Add(module);
            here.Add((module.Module.Id, module.Module.Version));
            foreach (ModuleRequirement requirement in module.Module.Requires)
            {
                // A requirement without its own source is looked for where its requirer came from.
                wanted.Enqueue((requirement.Releases ?? from, requirement.Id, requirement.Range, $"required by {module.Module.Id} {module.Module.Version}"));
            }
        }
    }

    private IEnumerable<FetchStep> Install(Offer offer, ReleaseSource from, string library, List<FetchedModule> fetched, List<string> problems)
    {
        ReleasedModule module = offer.Module;
        string target = Path.Combine(library, module.File);
        string downloads = Path.Combine(library, ".downloads");
        string temporary = Path.Combine(downloads, module.File);
        try
        {
            Directory.CreateDirectory(downloads);
            File.Delete(temporary);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problems.Add($"Can't prepare {downloads} for {module.Id} {module.Version}: {exception.Message}");
            yield break;
        }

        string? failure;
        if (offer.Container is Uri url)
        {
            DownloadStep download = new(url, downloads, module.File, $"{module.Id} {module.Version}");
            yield return download;
            failure = download.Failure;
        }
        else
        {
            failure = Copy(offer.Path!, temporary);
        }

        if (failure is not null)
        {
            problems.Add($"Downloading {module.Id} {module.Version} from {from} failed: {failure}");
            yield break;
        }

        string? identity = identityOf(temporary);
        if (identity != module.Identity)
        {
            File.Delete(temporary);
            problems.Add(identity is null
                ? $"{module.File} from {from} isn't a usable module container, so it wasn't installed."
                : $"{module.File} from {from} doesn't match its index (content {identity}, index says {module.Identity}), so it wasn't installed. Ask the publisher to re-release it.");
            yield break;
        }

        File.Move(temporary, target, overwrite: true);
        InstalledSources.Record(library, module, from);
        fetched.Add(new FetchedModule(module, from, target));
    }

    private static string? Copy(string from, string to)
    {
        try
        {
            File.Copy(from, to, overwrite: true);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    private static string? Pick(ReleaseSource from, List<Offer> offers, List<string> problems)
    {
        List<string> ids = offers.Select(offer => offer.Module.Id).Distinct().ToList();
        List<string> campaigns = offers.Where(offer => offer.Module.Kind == ModuleKind.Campaign).Select(offer => offer.Module.Id).Distinct().ToList();
        if (ids.Count == 1)
        {
            return ids[0];
        }

        if (campaigns.Count == 1)
        {
            return campaigns[0];
        }

        problems.Add($"{from} publishes {string.Join(", ", ids)}; name the one you want with --id.");
        return null;
    }

    /// <summary>Every module every release of a source offers, read once per source, into <paramref name="offers"/>.</summary>
    private IEnumerable<FetchStep> Offers(ReleaseSource source, List<Offer> offers, List<string> problems)
    {
        if (_offers.TryGetValue(source.Text, out List<Offer>? known))
        {
            offers.AddRange(known);
            yield break;
        }

        int problemsBefore = problems.Count;
        List<Offer> found = [];
        IEnumerable<FetchStep> steps = source switch
        {
            { Directory: string directory } => LocalOffers(directory, found, problems),
            { Index: Uri index } => IndexOffers(index, file => new Uri(index, file), found, problems),
            _ => GitHubOffers(source, found, problems),
        };
        foreach (FetchStep step in steps)
        {
            yield return step;
        }

        if (found.Count == 0 && problems.Count == problemsBefore)
        {
            problems.Add($"{source} has no releases with a {ReleaseIndex.FileName}. Publish one with `goldbox module release`.");
        }

        _offers[source.Text] = found;
        offers.AddRange(found);
    }

    private static IEnumerable<FetchStep> LocalOffers(string directory, List<Offer> offers, List<string> problems)
    {
        IEnumerable<string> folders = Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal).Prepend(directory);
        foreach (string folder in folders)
        {
            string index = Path.Combine(folder, ReleaseIndex.FileName);
            if (File.Exists(index))
            {
                offers.AddRange(ReleaseIndex.Read(File.ReadAllBytes(index), index, problems)
                    .Select(module => new Offer(module, null, Path.Combine(folder, module.File))));
            }
        }

        yield break;
    }

    private static IEnumerable<FetchStep> IndexOffers(Uri index, Func<string, Uri?> container, List<Offer> offers, List<string> problems)
    {
        ReadStep read = new(index, GitHubHeaders);
        yield return read;
        if (read.Body is null)
        {
            problems.Add($"Can't read {index}: {read.Failure}");
            yield break;
        }

        foreach (ReleasedModule module in ReleaseIndex.Read(read.Body, index.ToString(), problems))
        {
            if (container(module.File) is Uri url)
            {
                offers.Add(new Offer(module, url, null));
            }
            else
            {
                problems.Add($"{index} lists {module.File}, but its release has no such file.");
            }
        }
    }

    private static IEnumerable<FetchStep> GitHubOffers(ReleaseSource source, List<Offer> offers, List<string> problems)
    {
        ReadStep list = new(new Uri($"https://api.github.com/repos/{source.Owner}/{source.Repository}/releases?per_page=100"), GitHubHeaders);
        yield return list;
        if (list.Body is null)
        {
            problems.Add($"Can't list the releases of {source}: {list.Failure}");
            yield break;
        }

        List<(Uri Index, Dictionary<string, Uri> Assets)> releases = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(list.Body);
            foreach (JsonElement release in document.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                Dictionary<string, Uri> assets = [];
                foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
                {
                    if (Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out Uri? url))
                    {
                        assets[asset.GetProperty("name").GetString()!] = url;
                    }
                }

                if (assets.TryGetValue(ReleaseIndex.FileName, out Uri? index))
                {
                    releases.Add((index, assets));
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            problems.Add($"GitHub's release list for {source} wasn't in the expected shape: {exception.Message}");
            yield break;
        }

        foreach ((Uri index, Dictionary<string, Uri> assets) in releases)
        {
            foreach (FetchStep step in IndexOffers(index, file => assets.GetValueOrDefault(file), offers, problems))
            {
                yield return step;
            }
        }
    }

    private sealed record Offer(ReleasedModule Module, Uri? Container, string? Path);
}
