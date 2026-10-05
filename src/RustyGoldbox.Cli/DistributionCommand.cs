using System.Diagnostics;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Cli;

/// <summary>
/// <c>goldbox module release</c> and <c>goldbox module get</c>: publish a
/// module with its <c>module-index.json</c>, and fetch one with everything
/// it requires into the module library.
/// </summary>
internal sealed record ModuleUpdate(string Id, ModuleVersion Installed, ModuleVersion Available, ReleaseSource From);

internal static class DistributionCommand
{
    public const string ReleaseUsage = "Usage: goldbox module release <module-dir> [--output <dir> | --repo <owner>/<repo>] [--modules <dir>]...";
    public const string GetUsage = "Usage: goldbox module get <source> [--id <id>] [--version <range>] [--modules <dir>]...";

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(30) };

    public const string UpdatesUsage = "Usage: goldbox module updates [--install]";
    public const string RemoveUsage = "Usage: goldbox module remove <id>@<version>";

    /// <summary>
    /// Checks where each fetched module was published for a newer version;
    /// with --install, fetches each newest version (and anything new it
    /// requires) beside the installed ones, so existing saves keep loading.
    /// </summary>
    public static int Updates(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, [], ["--install"]);
        if (error is null && parsed.Positionals.Count != 0)
        {
            error = UpdatesUsage;
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        string library = InstalledModules.DefaultDirectory();
        Dictionary<string, InstalledSources.Entry> record = InstalledSources.Read(library);
        List<string> searched = ModuleSearchPaths.Find(workingDirectory, [], []);
        searched.Add(library);
        List<string> problems = [];
        List<ModuleUpdate> updates = [];
        List<FetchedModule> installed = [];
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            ModuleFetcher fetcher = new(Read, Download, container => IdentityOf(engine.Content, container));
            foreach ((string id, InstalledSources.Entry entry) in record.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                if (!ReleaseSource.TryParseArgument(entry.Releases, workingDirectory, out ReleaseSource? source))
                {
                    problems.Add($"{InstalledSources.FileName} records '{entry.Releases}' for {id}, which isn't a release source; fetch it again with `goldbox module get`.");
                    continue;
                }

                ModuleVersion newestHere = entry.Versions.Keys
                    .Select(text => ModuleVersion.TryParse(text, out ModuleVersion version) ? version : default)
                    .Max();
                ReleasedModule? newest = fetcher.Published(source!, problems)
                    .Where(module => module.Id == id && module.Version > newestHere)
                    .MaxBy(module => module.Version);
                if (newest is null)
                {
                    continue;
                }

                updates.Add(new ModuleUpdate(id, newestHere, newest.Version, source!));
                if (parsed.Has("--install"))
                {
                    VersionRange.TryParse(newest.Version.ToString(), out VersionRange? exact);
                    FetchResult result = fetcher.Get(source!, id, exact, InstalledModules.Present(searched, engine.Content), library);
                    installed.AddRange(result.Installed);
                    problems.AddRange(result.Problems);
                }
            }
        });
        return output.Updates(updates, installed, problems, library);
    }

    /// <summary>Removes one installed version from the module library and its record.</summary>
    public static int Remove(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, [], []);
        string[] parts = error is null && parsed.Positionals.Count == 1 ? parsed.Positionals[0].Split('@') : [];
        if (error is null && (parts.Length != 2 || !ModuleIds.IsValid(parts[0]) || !ModuleVersion.TryParse(parts[1], out _)))
        {
            error = RemoveUsage;
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        ModuleVersion.TryParse(parts[1], out ModuleVersion version);
        string library = InstalledModules.DefaultDirectory();
        string container = Path.Combine(library, InstalledModules.FileName(parts[0], version));
        if (!File.Exists(container))
        {
            return output.Problems([new ModuleDiagnostic("remove.missing", $"{parts[0]} {version} isn't installed in {library}.", parts[0], container)]);
        }

        File.Delete(container);
        InstalledSources.Forget(library, parts[0], version);
        return output.Removed(parts[0], version, container);
    }

    public static int Release(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--output", "--repo", "--modules"], []);
        if (error is null && (parsed.Positionals.Count != 1 || (parsed.Single("--output") is not null && parsed.Single("--repo") is not null)))
        {
            error = ReleaseUsage;
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parsed.Positionals[0], workingDirectory));
        ModuleSet set = ModuleSets.Load(path, parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList());
        if (set.Root is null || !set.IsValid)
        {
            return output.ModuleErrors(set);
        }

        ModuleManifest root = set.Root;
        string? repository = parsed.Single("--repo")
            ?? (root.Releases is { Owner: string owner, Repository: string name } ? $"{owner}/{name}" : null);
        string? requested = parsed.Single("--output");
        if (requested is null && repository is null)
        {
            return output.UsageError($"{root.Id} has no \"releases\": \"github:<owner>/<repo>\" in its module.json. Add one, pass --repo <owner>/<repo>, or write the release files with --output <dir>.");
        }

        string directory = requested is not null
            ? Path.GetFullPath(requested, workingDirectory)
            : Path.Combine(Path.GetTempPath(), $"goldbox-release-{root.Id}-{root.Version}-{Environment.ProcessId}");
        if (Path.GetFullPath(directory).StartsWith(path + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return output.UsageError($"The release files would be written inside the module ({directory}). Choose an --output outside it.");
        }

        Directory.CreateDirectory(directory);
        ReleasedModule released = ReleaseIndex.Describe(root, root.Source.Identity);
        string container = Path.Combine(directory, released.File);
        string index = Path.Combine(directory, ReleaseIndex.FileName);
        if (ContentPacker.Pack(path, container) is string failure)
        {
            return output.Problems([new ModuleDiagnostic("release.pack", failure, root.Id, container)]);
        }

        File.WriteAllText(index, ReleaseIndex.Write([released]));
        if (repository is null)
        {
            return output.Released(released, [container, index], null);
        }

        string tag = $"{root.Id}-v{root.Version}";
        string? published = Publish(repository, tag, $"{root.Title} {root.Version}", root.Provenance, [container, index], out string? ghFailure);
        Directory.Delete(directory, recursive: true);
        return published is null
            ? output.Problems([new ModuleDiagnostic("release.publish", $"Publishing {tag} to {repository} failed: {ghFailure}", root.Id, root.ManifestPath)])
            : output.Released(released, [], published);
    }

    public static int Get(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--id", "--version", "--modules"], []);
        if (error is null && parsed.Positionals.Count != 1)
        {
            error = GetUsage;
        }

        ReleaseSource? source = null;
        if (error is null && !ReleaseSource.TryParseArgument(parsed.Positionals[0], workingDirectory, out source))
        {
            error = $"'{parsed.Positionals[0]}' is not a release source. Use {ReleaseSource.FormatDescription}, or a directory holding {ReleaseIndex.FileName}.";
        }

        string? id = parsed.Single("--id");
        if (error is null && id is not null && !ModuleIds.IsValid(id))
        {
            error = $"--id '{id}' is not a module ID. Use {ModuleIds.FormatDescription}.";
        }

        VersionRange? range = null;
        if (error is null && parsed.Single("--version") is string rangeText && !VersionRange.TryParse(rangeText, out range))
        {
            error = $"--version '{rangeText}' is not a version range. Use {VersionRange.FormatDescription}.";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        List<ModuleDiagnostic> diagnostics = [];
        string library = InstalledModules.DefaultDirectory();
        List<string> searched = ModuleSearchPaths.Find(workingDirectory, parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList(), diagnostics);
        searched.Add(library);
        Directory.CreateDirectory(library);

        using EngineTestHost host = EngineTestHost.Create();
        FetchResult result = host.Call(engine =>
        {
            ModuleFetcher fetcher = new(Read, Download, container => IdentityOf(engine.Content, container));
            return fetcher.Get(source!, id, range, InstalledModules.Present(searched, engine.Content), library);
        });
        return output.Fetched(result, library);
    }

    private static (byte[]? Body, string? Failure) Read(Uri url, IReadOnlyDictionary<string, string> headers)
    {
        try
        {
            using HttpRequestMessage request = Request(url, headers);
            using HttpResponseMessage response = Http.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                return (null, $"{(int)response.StatusCode} {response.ReasonPhrase}{RateLimit(response)}");
            }

            using Stream body = response.Content.ReadAsStream();
            using MemoryStream copy = new();
            body.CopyTo(copy);
            return (copy.ToArray(), null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return (null, exception.Message);
        }
    }

    private static string? Download(Uri url, string path)
    {
        try
        {
            using HttpRequestMessage request = Request(url, new Dictionary<string, string> { ["User-Agent"] = "rusty-goldbox" });
            using HttpResponseMessage response = Http.Send(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return $"{(int)response.StatusCode} {response.ReasonPhrase}";
            }

            using Stream body = response.Content.ReadAsStream();
            using FileStream file = File.Create(path);
            body.CopyTo(file);
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    private static HttpRequestMessage Request(Uri url, IReadOnlyDictionary<string, string> headers)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        foreach ((string name, string value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        // A token lifts GitHub's unauthenticated rate limit and reaches private repositories.
        if (url.Host == "api.github.com" && Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } token)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        return request;
    }

    private static string RateLimit(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string>? remaining) && remaining.FirstOrDefault() == "0"
            ? " (GitHub's rate limit for unauthenticated requests is used up; set GITHUB_TOKEN or wait an hour)"
            : "";
    }

    private static string? IdentityOf(IContentService content, string container)
    {
        try
        {
            using ProductContentBundle bundle = ProductContentBundle.OpenContainer(content, container);
            return new BundleModuleSource(bundle).Identity;
        }
        catch (EngineCallException)
        {
            return null;
        }
    }

    /// <summary>Creates the release through <c>gh</c>, or adds the files to an existing one; returns its URL.</summary>
    private static string? Publish(string repository, string tag, string title, string notes, IReadOnlyList<string> files, out string? failure)
    {
        bool exists = Gh(["release", "view", tag, "--repo", repository, "--json", "url", "--jq", ".url"], out _, out _);
        bool done = exists
            ? Gh(["release", "upload", tag, .. files, "--repo", repository, "--clobber"], out _, out failure)
            : Gh(["release", "create", tag, .. files, "--repo", repository, "--title", title, "--notes", notes], out _, out failure);
        if (!done)
        {
            return null;
        }

        return Gh(["release", "view", tag, "--repo", repository, "--json", "url", "--jq", ".url"], out string url, out failure) ? url.Trim() : null;
    }

    private static bool Gh(IReadOnlyList<string> arguments, out string printed, out string? failure)
    {
        ProcessStartInfo start = new("gh", arguments) { RedirectStandardOutput = true, RedirectStandardError = true };
        try
        {
            using Process process = Process.Start(start)!;
            Task<string> errors = process.StandardError.ReadToEndAsync();
            printed = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            failure = process.ExitCode == 0 ? null : errors.Result.Trim();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            printed = "";
            failure = $"Can't run gh ({exception.Message}). Install the GitHub CLI and run `gh auth login`, or use --output to write the release files.";
            return false;
        }
    }
}
