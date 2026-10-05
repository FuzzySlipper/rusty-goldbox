using System.Text.Json;

namespace RustyGoldbox.Core.Modules;

/// <summary>A module <see cref="ModuleFetcher"/> installed.</summary>
public sealed record FetchedModule(ReleasedModule Module, ReleaseSource From, string Container);

/// <summary>What <see cref="ModuleFetcher.Get"/> did.</summary>
/// <param name="Present">Requirements already met by modules on this machine, as "id version".</param>
public sealed record FetchResult(IReadOnlyList<FetchedModule> Installed, IReadOnlyList<string> Present, IReadOnlyList<string> Problems)
{
    public bool Succeeded => Problems.Count == 0;
}

/// <summary>
/// Fetches a module from where it is published, and every module it requires
/// that isn't already here, into a module library. The caller supplies how to
/// read a URL, download one to a file and read a container's content
/// identity, so the CLI and the Game share the rules.
/// </summary>
/// <param name="read">GETs a URL with the given headers: its body, or why it failed.</param>
/// <param name="download">Downloads a URL to a file: null, or why it failed.</param>
/// <param name="identityOf">A container's Engine content identity, or null when it isn't a usable container.</param>
public sealed class ModuleFetcher(
    Func<Uri, IReadOnlyDictionary<string, string>, (byte[]? Body, string? Failure)> read,
    Func<Uri, string, string?> download,
    Func<string, string?> identityOf)
{
    private static readonly Dictionary<string, string> GitHubHeaders = new()
    {
        ["Accept"] = "application/vnd.github+json",
        ["User-Agent"] = "rusty-goldbox",
    };

    private readonly Dictionary<string, List<Offer>> _offers = [];

    /// <param name="id">The module to fetch; may be left out when the source offers one module, or one campaign.</param>
    /// <param name="range">Acceptable versions; any version when null.</param>
    /// <param name="available">Modules already on this machine, as (id, version).</param>
    /// <param name="library">The module library directory to install into.</param>
    public FetchResult Get(ReleaseSource source, string? id, VersionRange? range, IEnumerable<(string Id, ModuleVersion Version)> available, string library)
    {
        List<FetchedModule> installed = [];
        List<string> present = [];
        List<string> problems = [];
        List<(string Id, ModuleVersion Version)> here = [.. available];
        Queue<(ReleaseSource Source, string? Id, VersionRange? Range, string Why)> wanted = new();
        wanted.Enqueue((source, id, range, $"requested from {source}"));
        while (wanted.Count > 0)
        {
            (ReleaseSource from, string? wantedId, VersionRange? wantedRange, string why) = wanted.Dequeue();
            List<Offer> offers = Offers(from, problems);
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

            if (Install(offer, from, library, problems) is not FetchedModule fetched)
            {
                continue;
            }

            installed.Add(fetched);
            here.Add((fetched.Module.Id, fetched.Module.Version));
            foreach (ModuleRequirement requirement in fetched.Module.Requires)
            {
                // A requirement without its own source is looked for where its requirer came from.
                wanted.Enqueue((requirement.Releases ?? from, requirement.Id, requirement.Range, $"required by {fetched.Module.Id} {fetched.Module.Version}"));
            }
        }

        return new FetchResult(installed, present, problems);
    }

    /// <summary>Every module the releases of <paramref name="source"/> offer.</summary>
    public IReadOnlyList<ReleasedModule> Published(ReleaseSource source, List<string> problems)
    {
        return Offers(source, problems).Select(offer => offer.Module).ToList();
    }

    private FetchedModule? Install(Offer offer, ReleaseSource from, string library, List<string> problems)
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
            return null;
        }

        string? failure = offer.Container is Uri url ? download(url, temporary) : Copy(offer.Path!, temporary);
        if (failure is not null)
        {
            problems.Add($"Downloading {module.Id} {module.Version} from {from} failed: {failure}");
            return null;
        }

        string? identity = identityOf(temporary);
        if (identity != module.Identity)
        {
            File.Delete(temporary);
            problems.Add(identity is null
                ? $"{module.File} from {from} isn't a usable module container, so it wasn't installed."
                : $"{module.File} from {from} doesn't match its index (content {identity}, index says {module.Identity}), so it wasn't installed. Ask the publisher to re-release it.");
            return null;
        }

        File.Move(temporary, target, overwrite: true);
        InstalledSources.Record(library, module, from);
        return new FetchedModule(module, from, target);
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

    /// <summary>Every module every release of a source offers, read once per source.</summary>
    private List<Offer> Offers(ReleaseSource source, List<string> problems)
    {
        if (_offers.TryGetValue(source.Text, out List<Offer>? known))
        {
            return known;
        }

        List<Offer> offers = source switch
        {
            { Directory: string directory } => LocalOffers(directory, problems),
            { Index: Uri index } => IndexOffers(index, file => new Uri(index, file), problems),
            _ => GitHubOffers(source, problems),
        };
        if (offers.Count == 0 && problems.Count == 0)
        {
            problems.Add($"{source} has no releases with a {ReleaseIndex.FileName}. Publish one with `goldbox module release`.");
        }

        _offers[source.Text] = offers;
        return offers;
    }

    private static List<Offer> LocalOffers(string directory, List<string> problems)
    {
        List<Offer> offers = [];
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

        return offers;
    }

    private List<Offer> IndexOffers(Uri index, Func<string, Uri?> container, List<string> problems)
    {
        (byte[]? body, string? failure) = read(index, GitHubHeaders);
        if (body is null)
        {
            problems.Add($"Can't read {index}: {failure}");
            return [];
        }

        List<Offer> offers = [];
        foreach (ReleasedModule module in ReleaseIndex.Read(body, index.ToString(), problems))
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

        return offers;
    }

    private List<Offer> GitHubOffers(ReleaseSource source, List<string> problems)
    {
        Uri releases = new($"https://api.github.com/repos/{source.Owner}/{source.Repository}/releases?per_page=100");
        (byte[]? body, string? failure) = read(releases, GitHubHeaders);
        if (body is null)
        {
            problems.Add($"Can't list the releases of {source}: {failure}");
            return [];
        }

        List<Offer> offers = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
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
                    offers.AddRange(IndexOffers(index, file => assets.GetValueOrDefault(file), problems));
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            problems.Add($"GitHub's release list for {source} wasn't in the expected shape: {exception.Message}");
        }

        return offers;
    }

    private sealed record Offer(ReleasedModule Module, Uri? Container, string? Path);
}
