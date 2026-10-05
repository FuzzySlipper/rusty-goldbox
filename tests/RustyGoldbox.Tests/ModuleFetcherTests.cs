using System.Text;
using System.Text.Json;
using RustyGoldbox.Cli;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

/// <summary>Fetching published modules: version choice, requirements across sources, identity checks and the install record.</summary>
public sealed class ModuleFetcherTests
{
    [Fact]
    public void ACampaignFetchesItsRequirementsFromTheirOwnSourcesAndSkipsWhatIsHere()
    {
        using TempModules scratch = new();
        string library = Path.Combine(scratch.Root, "library");
        Directory.CreateDirectory(library);
        FakeGitHub hub = new();
        hub.Release("alice/tale", "tale-v0.1.0",
            Module("tale", "0.1.0", ModuleKind.Campaign, [("rules", ">=0.1.0 <1.0.0", "github:carol/rules"), ("art", "*", "github:bob/art"), ("tale-extras", "*", null)]),
            Module("tale-extras", "0.1.0", ModuleKind.Extension, [("rules", "*", "github:carol/rules")]));
        hub.Release("carol/rules", "rules-v0.1.0", Module("rules", "0.1.0", ModuleKind.Ruleset, []));
        hub.Release("carol/rules", "rules-v0.2.0", Module("rules", "0.2.0", ModuleKind.Ruleset, []));
        hub.Release("carol/rules", "rules-v1.0.0", Module("rules", "1.0.0", ModuleKind.Ruleset, []));
        hub.Release("bob/art", "art-v0.1.0", Module("art", "0.1.0", ModuleKind.Assets, []));

        Assert.True(ReleaseSource.TryParse("github:alice/tale", out ReleaseSource? tale));
        FetchResult result = hub.Fetcher().Get(tale!, null, null, [("art", new ModuleVersion(0, 1, 0))], library);

        Assert.True(result.Succeeded, string.Join("\n", result.Problems));
        Assert.Equal(["tale 0.1.0", "rules 0.2.0", "tale-extras 0.1.0"], result.Installed.Select(module => $"{module.Module.Id} {module.Module.Version}"));
        Assert.Contains("art 0.1.0", result.Present);
        Assert.Contains("rules 0.2.0", result.Present);
        Assert.True(File.Exists(Path.Combine(library, "rules-0.2.0.rpak")));
        Assert.False(File.Exists(Path.Combine(library, "rules-1.0.0.rpak")));
        Dictionary<string, InstalledSources.Entry> record = InstalledSources.Read(library);
        Assert.Equal("github:carol/rules", record["rules"].Releases);
        Assert.Equal("github:alice/tale", record["tale-extras"].Releases);
        Assert.Equal("identity-of-rules-0.2.0", record["rules"].Versions["0.2.0"]);
    }

    [Fact]
    public void FetchingAnInstalledModuleAgainFinishesItsMissingRequirements()
    {
        using TempModules scratch = new();
        string library = Path.Combine(scratch.Root, "library");
        Directory.CreateDirectory(library);
        FakeGitHub hub = new();
        hub.Release("alice/tale", "tale-v0.1.0", Module("tale", "0.1.0", ModuleKind.Campaign, [("tale-extras", "*", null)]));
        hub.Release("alice/tale", "tale-extras-v0.1.0", Module("tale-extras", "0.1.0", ModuleKind.Extension, []));

        Assert.True(ReleaseSource.TryParse("github:alice/tale", out ReleaseSource? tale));
        FetchResult result = hub.Fetcher().Get(tale!, "tale", null, [("tale", new ModuleVersion(0, 1, 0))], library);

        Assert.True(result.Succeeded, string.Join("\n", result.Problems));
        Assert.Equal("tale-extras", Assert.Single(result.Installed).Module.Id);
    }

    [Fact]
    public void AContainerThatDoesNotMatchItsIndexIsNotInstalled()
    {
        using TempModules scratch = new();
        string library = Path.Combine(scratch.Root, "library");
        Directory.CreateDirectory(library);
        FakeGitHub hub = new();
        hub.Release("carol/rules", "rules-v0.1.0", Module("rules", "0.1.0", ModuleKind.Ruleset, []));
        hub.Tamper("rules-0.1.0.rpak");

        Assert.True(ReleaseSource.TryParse("github:carol/rules", out ReleaseSource? rules));
        FetchResult result = hub.Fetcher().Get(rules!, null, null, [], library);

        Assert.Empty(result.Installed);
        Assert.Contains("doesn't match its index", Assert.Single(result.Problems), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(library, "*.rpak", SearchOption.AllDirectories));
    }

    [Fact]
    public void ManifestsAcceptOnlyGitHubOrHttpsIndexSources()
    {
        Assert.True(ReleaseSource.TryParse("github:alice/blackapple", out _));
        Assert.True(ReleaseSource.TryParse("https://example.org/goldbox/module-index.json", out _));
        Assert.False(ReleaseSource.TryParse("http://example.org/module-index.json", out _));
        Assert.False(ReleaseSource.TryParse("github:alice", out _));
        Assert.False(ReleaseSource.TryParse("/home/alice/modules", out _));
    }

    private static ReleasedModule Module(string id, string version, ModuleKind kind, (string Id, string Range, string? Releases)[] requires)
    {
        Assert.True(ModuleVersion.TryParse(version, out ModuleVersion parsed));
        List<ModuleRequirement> requirements = requires.Select((requirement, index) =>
        {
            Assert.True(VersionRange.TryParse(requirement.Range, out VersionRange? range));
            ReleaseSource? releases = null;
            Assert.True(requirement.Releases is null || ReleaseSource.TryParse(requirement.Releases, out releases));
            return new ModuleRequirement(requirement.Id, range!, index, releases);
        }).ToList();
        return new ReleasedModule(id, parsed, kind, id, "Test fixture.", requirements, InstalledModules.FileName(id, parsed), $"identity-of-{id}-{version}");
    }

    /// <summary>GitHub's release list and asset downloads, served from memory. A "container" holds its own identity as text.</summary>
    private sealed class FakeGitHub
    {
        private readonly Dictionary<string, List<(string Tag, ReleasedModule[] Modules)>> _repositories = [];
        private readonly Dictionary<string, byte[]> _assets = [];

        public void Release(string repository, string tag, params ReleasedModule[] modules)
        {
            if (!_repositories.TryGetValue(repository, out List<(string, ReleasedModule[])>? releases))
            {
                releases = [];
                _repositories[repository] = releases;
            }

            releases.Add((tag, modules));
            _assets[$"https://github.com/{repository}/releases/download/{tag}/{ReleaseIndex.FileName}"] = Encoding.UTF8.GetBytes(ReleaseIndex.Write(modules));
            foreach (ReleasedModule module in modules)
            {
                _assets[$"https://github.com/{repository}/releases/download/{tag}/{module.File}"] = Encoding.UTF8.GetBytes(module.Identity);
            }
        }

        public void Tamper(string file)
        {
            foreach (string url in _assets.Keys.Where(url => url.EndsWith("/" + file, StringComparison.Ordinal)).ToList())
            {
                _assets[url] = Encoding.UTF8.GetBytes("something else");
            }
        }

        public ModuleFetcher Fetcher() => new(Read, Download, path => File.ReadAllText(path));

        private (byte[]? Body, string? Failure) Read(Uri url, IReadOnlyDictionary<string, string> headers)
        {
            string text = url.ToString();
            const string api = "https://api.github.com/repos/";
            if (text.StartsWith(api, StringComparison.Ordinal))
            {
                string repository = text[api.Length..text.IndexOf("/releases", StringComparison.Ordinal)];
                if (!_repositories.TryGetValue(repository, out List<(string Tag, ReleasedModule[] Modules)>? releases))
                {
                    return (null, "404 Not Found");
                }

                var list = releases.Select(release => new
                {
                    tag_name = release.Tag,
                    draft = false,
                    assets = release.Modules.Select(module => module.File).Append(ReleaseIndex.FileName).Select(name => new
                    {
                        name,
                        browser_download_url = $"https://github.com/{repository}/releases/download/{release.Tag}/{name}",
                    }),
                });
                return (JsonSerializer.SerializeToUtf8Bytes(list), null);
            }

            return _assets.TryGetValue(text, out byte[]? body) ? (body, null) : (null, "404 Not Found");
        }

        private string? Download(Uri url, string path)
        {
            (byte[]? body, string? failure) = Read(url, new Dictionary<string, string>());
            if (body is not null)
            {
                File.WriteAllBytes(path, body);
            }

            return failure;
        }
    }
}

/// <summary>`module release --output` then `module get` from those files, through real Engine containers.</summary>
[Collection(nameof(ModuleLibraryVariable))]
public sealed class ModuleReleaseAndGetTests
{
    [Fact]
    public void ReleasedModulesInstallWithTheirRequirementsAndAreFoundOnceThere()
    {
        using TempModules scratch = new();
        string releases = Path.Combine(scratch.Root, "releases");
        foreach (string id in new[] { "classic", "placeholder-art", "sample-crypt" })
        {
            (int code, string printed) = Run(scratch, "module", "release", Path.Combine(Rules.RepositoryRoot, "modules", id),
                "--output", Path.Combine(releases, id), "--modules", Path.Combine(Rules.RepositoryRoot, "modules"));
            Assert.True(code == GoldboxCli.Ok, printed);
        }

        string library = Path.Combine(scratch.Root, "library");
        string? previous = Environment.GetEnvironmentVariable(InstalledModules.DirectoryVariable);
        Environment.SetEnvironmentVariable(InstalledModules.DirectoryVariable, library);
        try
        {
            (int code, string printed) = Run(scratch, "module", "get", releases, "--json");
            Assert.True(code == GoldboxCli.Ok, printed);
            using JsonDocument json = JsonDocument.Parse(printed);
            Assert.Equal(["sample-crypt", "classic", "placeholder-art"], json.RootElement.GetProperty("installed").EnumerateArray().Select(module => module.GetProperty("id").GetString()));

            // Everything is now here, so a second get installs nothing.
            (int againCode, string again) = Run(scratch, "module", "get", releases, "--json");
            Assert.True(againCode == GoldboxCli.Ok, again);
            using JsonDocument second = JsonDocument.Parse(again);
            Assert.Equal(0, second.RootElement.GetProperty("installed").GetArrayLength());
            Assert.Contains("sample-crypt 0.1.0", second.RootElement.GetProperty("present").EnumerateArray().Select(entry => entry.GetString()));

            (int validateCode, string validated) = Run(scratch, "module", "validate", Path.Combine(library, "sample-crypt-0.1.0.rpak"), "--modules", library);
            Assert.True(validateCode == GoldboxCli.Ok, validated);
        }
        finally
        {
            Environment.SetEnvironmentVariable(InstalledModules.DirectoryVariable, previous);
        }
    }

    private static (int Code, string Output) Run(TempModules scratch, params string[] args)
    {
        using StringWriter output = new();
        int code = GoldboxCli.Run(args, output, scratch.Root);
        return (code, output.ToString());
    }
}

/// <summary>Updated modules install beside the old ones; saves keep the versions they were made with.</summary>
[Collection(nameof(ModuleLibraryVariable))]
public sealed class ModuleUpdateTests
{
    [Fact]
    public void ASaveKeepsItsRequiredVersionBesideANewerOne()
    {
        using TempModules scratch = new();
        foreach (string module in new[] { "classic", "placeholder-art", "sample-crypt" })
        {
            CopyDirectory(Path.Combine(Rules.RepositoryRoot, "modules", module), Path.Combine(scratch.Root, "modules", module));
        }

        scratch.Write("goldbox.json", """{ "modules": ["modules"] }""");
        string campaign = Path.Combine(scratch.Root, "modules", "sample-crypt");
        CampaignTests.WriteParty(scratch, Path.Combine(scratch.Root, "modules", "classic"));
        (int saveCode, string saved) = CampaignTests.Run(scratch, "play", "--campaign", campaign, "--party", "ada.json,brom.json", "--script", CampaignTests.Script("crypt-early-stairs.script"), "--save", "game.json");
        Assert.True(saveCode == GoldboxCli.Ok, saved);

        // A newer classic with different content arrives beside the old one.
        string newer = Path.Combine(scratch.Root, "modules", "classic-next");
        CopyDirectory(Path.Combine(scratch.Root, "modules", "classic"), newer);
        string manifest = Path.Combine(newer, "module.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"0.1.0\"", "\"0.1.1\"", StringComparison.Ordinal));
        string rat = Path.Combine(newer, "monsters", "giant_rat.json");
        File.WriteAllText(rat, File.ReadAllText(rat).Replace("\"xp\": 7", "\"xp\": 8", StringComparison.Ordinal));

        (int loadCode, string loaded) = CampaignTests.Run(scratch, "play", "--campaign", campaign, "--load", "game.json", "--script", CampaignTests.Script("crypt-early-stairs.script"));
        Assert.True(loadCode == GoldboxCli.Ok, loaded);

        (int depsCode, string deps) = CampaignTests.Run(scratch, "module", "deps", campaign, "--json");
        Assert.True(depsCode == GoldboxCli.Ok, deps);
        Assert.Contains("0.1.1", deps, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdatesListAndInstallNewerVersionsAndRemoveDropsOne()
    {
        using TempModules scratch = new();
        string source = Path.Combine(scratch.Root, "classic-source");
        CopyDirectory(Path.Combine(Rules.RepositoryRoot, "modules", "classic"), source);
        string releases = Path.Combine(scratch.Root, "releases");
        (int firstCode, string first) = CampaignTests.Run(scratch, "module", "release", source, "--output", Path.Combine(releases, "v010"));
        Assert.True(firstCode == GoldboxCli.Ok, first);

        string library = Path.Combine(scratch.Root, "library");
        string? previous = Environment.GetEnvironmentVariable(InstalledModules.DirectoryVariable);
        Environment.SetEnvironmentVariable(InstalledModules.DirectoryVariable, library);
        try
        {
            (int getCode, string got) = CampaignTests.Run(scratch, "module", "get", releases);
            Assert.True(getCode == GoldboxCli.Ok, got);
            (int quietCode, string quiet) = CampaignTests.Run(scratch, "module", "updates");
            Assert.True(quietCode == GoldboxCli.Ok, quiet);
            Assert.Contains("up to date", quiet, StringComparison.Ordinal);

            string manifest = Path.Combine(source, "module.json");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"0.1.0\"", "\"0.1.1\"", StringComparison.Ordinal));
            (int secondCode, string second) = CampaignTests.Run(scratch, "module", "release", source, "--output", Path.Combine(releases, "v011"));
            Assert.True(secondCode == GoldboxCli.Ok, second);

            (int listCode, string listed) = CampaignTests.Run(scratch, "module", "updates");
            Assert.True(listCode == GoldboxCli.Ok, listed);
            Assert.Contains("classic: 0.1.0 installed, 0.1.1 available", listed, StringComparison.Ordinal);

            (int installCode, string installed) = CampaignTests.Run(scratch, "module", "updates", "--install");
            Assert.True(installCode == GoldboxCli.Ok, installed);
            Assert.True(File.Exists(Path.Combine(library, "classic-0.1.0.rpak")));
            Assert.True(File.Exists(Path.Combine(library, "classic-0.1.1.rpak")));
            Assert.Equal(["0.1.0", "0.1.1"], InstalledSources.Read(library)["classic"].Versions.Keys.Order());

            (int removeCode, string removed) = CampaignTests.Run(scratch, "module", "remove", "classic@0.1.0");
            Assert.True(removeCode == GoldboxCli.Ok, removed);
            Assert.False(File.Exists(Path.Combine(library, "classic-0.1.0.rpak")));
            Assert.Equal(["0.1.1"], InstalledSources.Read(library)["classic"].Versions.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable(InstalledModules.DirectoryVariable, previous);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
