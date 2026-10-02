using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

/// <summary>
/// Modules read from Engine content containers (the bundle surface the Game
/// uses) behave exactly like the same modules read from directories.
/// </summary>
public sealed class EngineContentTests
{
    private static readonly string[] SampleSet = ["classic", "placeholder-art", "sample-crypt"];

    [Fact]
    public void DirectoryIdentityIsTheEngineBundleIdentity()
    {
        using TempModules scratch = new();
        using EngineTestHost host = EngineTestHost.Create();
        foreach (string directory in Directory.GetDirectories(Path.Combine(Rules.RepositoryRoot, "modules")))
        {
            string container = Pack(directory, scratch);
            host.Call(engine =>
            {
                using ProductContentBundle bundle = ProductContentBundle.OpenContainer(engine.Content, container);
                Assert.Equal(new DirectoryModuleSource(directory).Identity, new BundleModuleSource(bundle).Identity);
            });
        }
    }

    [Fact]
    public void CampaignFromContainersPlaysAndSavesLikeDirectories()
    {
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        List<string> containers = SampleSet.Select(id => Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch)).ToList();
        List<string> script = File.ReadAllLines(CampaignTests.Script("crypt.script"))
            .Select(line => line.Split('#')[0].Trim())
            .Where(line => line.Length > 0)
            .ToList();

        using EngineTestHost host = EngineTestHost.Create();
        string fromDirectories = host.Call(engine => PlayAndSave(ModuleLoader.Load(CampaignTests.SampleCrypt, []), scratch, script, engine));
        string fromContainers = host.Call(engine =>
        {
            List<ProductContentBundle> bundles = containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList();
            try
            {
                List<ModuleSource> sources = bundles.Select(bundle => (ModuleSource)new BundleModuleSource(bundle)).ToList();
                ModuleSet set = ModuleLoader.Load(sources[^1], sources, containers, "Pack it.");
                Assert.Empty(set.Diagnostics);
                return PlayAndSave(set, scratch, script, engine);
            }
            finally
            {
                bundles.ForEach(bundle => bundle.Dispose());
            }
        });

        // Same moves, rolls and module identities: the saves are identical.
        Assert.Equal(fromDirectories, fromContainers);
        Assert.Contains("\"ended\": true", fromContainers, StringComparison.Ordinal);
    }

    [Fact]
    public void PackedModulesInstallSeparatelyAndPlayLikeTheirDirectories()
    {
        using TempModules scratch = new();
        CampaignTests.WriteParty(scratch, Rules.ClassicPath);
        string library = Path.Combine(scratch.Root, "library");
        foreach (string id in SampleSet)
        {
            (int code, string printed) = CampaignTests.Run(scratch, "module", "pack", Path.Combine(Rules.RepositoryRoot, "modules", id), "--output", Path.Combine(library, $"{id}-0.1.0.rpak"));
            Assert.True(code == 0, printed);
        }

        string[] play = ["play", "--party", "ada.json,brom.json", "--seed", "4", "--script", CampaignTests.Script("crypt.script"), "--json"];
        (int fromSource, string sourceOutput) = CampaignTests.Run(scratch, [play[0], "--campaign", CampaignTests.SampleCrypt, .. play[1..]]);
        (int fromLibrary, string libraryOutput) = CampaignTests.Run(scratch, [play[0], "--campaign", Path.Combine(library, "sample-crypt-0.1.0.rpak"), .. play[1..]]);
        Assert.Equal(0, fromSource);
        Assert.Equal(0, fromLibrary);
        Assert.Equal(sourceOutput, libraryOutput);

        (int depsCode, string deps) = CampaignTests.Run(scratch, "module", "deps", Path.Combine(library, "sample-crypt-0.1.0.rpak"));
        Assert.Equal(0, depsCode);
        Assert.Contains(Path.Combine("library", "classic-0.1.0.rpak"), deps, StringComparison.Ordinal);
    }

    [Fact]
    public void PackRefusesAnInvalidModule()
    {
        using TempModules scratch = new();
        string broken = scratch.Module("broken", "ruleset");
        scratch.Write("broken/classes/x.json", """{ "type": "spellbook", "id": "x" }""");

        (int code, string printed) = CampaignTests.Run(scratch, "module", "pack", broken);

        Assert.Equal(1, code);
        Assert.Contains("definition.type-unknown", printed, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(scratch.Root, "*.rpak"));
    }

    [Fact]
    public void AnInstalledCopyOfASourceModuleIsTheSameModuleUnlessItDiffers()
    {
        using TempModules scratch = new();
        string classic = scratch.Module("classic", "ruleset");
        string house = scratch.Module("house", "extension", requires: TempModules.Require("classic", "*"));
        Pack(classic, scratch, Path.Combine(scratch.Root, "classic-0.1.0.rpak"));

        Assert.Empty(Load(house).Diagnostics);

        scratch.Write("classic/notes.txt", "Edited after packing.");
        ModuleDiagnostic ambiguous = Assert.Single(Load(house).Diagnostics);
        Assert.Equal("resolve.ambiguous", ambiguous.Rule);
        Assert.Contains("classic-0.1.0.rpak", ambiguous.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrokenContainerIsNamedWhenItsModuleIsMissing()
    {
        using TempModules scratch = new();
        string house = scratch.Module("house", "extension", requires: TempModules.Require("classic", "*"));
        scratch.Write("classic-0.1.0.rpak", "not a container");

        ModuleDiagnostic missing = Assert.Single(Load(house).Diagnostics);

        Assert.Equal("resolve.not-found", missing.Rule);
        Assert.Contains("classic-0.1.0.rpak", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PackRefusesToWriteIntoTheModuleItPacks()
    {
        using TempModules scratch = new();
        string classic = scratch.Module("classic", "ruleset");
        using StringWriter printed = new();

        int code = Cli.GoldboxCli.Run(["module", "pack", "."], printed, classic);

        Assert.Equal(2, code);
        Assert.Contains("inside the module it packs", printed.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void AnUnreadableLibraryIsAProblemNotACrash()
    {
        using TempModules scratch = new();
        string library = Path.Combine(scratch.Root, "library");
        Directory.CreateDirectory(library);
        File.SetUnixFileMode(library, UnixFileMode.None);
        try
        {
            using EngineTestHost host = EngineTestHost.Create();
            List<string> problems = [];
            List<ProductContentBundle> opened = [];
            host.Call(engine => InstalledModules.Open(engine.Content, library, opened, problems));

            Assert.Empty(opened);
            Assert.Contains("Can't read the directory", Assert.Single(problems), StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(library, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static ModuleSet Load(string path)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine => ModuleLoader.Load(path, [], engine.Content));
    }

    private static string PlayAndSave(ModuleSet set, TempModules scratch, List<string> script, IEngineContext engine)
    {
        List<ModuleDiagnostic> problems = [];
        List<Character> party = new[] { "ada.json", "brom.json" }
            .Select(file => CharacterFile.Read(Path.Combine(scratch.Root, file), set, problems)!)
            .ToList();
        Assert.Empty(problems);
        Definition campaign = set.Rules!.OfType(DefinitionTypes.Campaign).Single();
        CampaignState state = CampaignRunner.NewState(set.Rules, campaign, party, 3);
        CampaignRunner runner = new(set.Rules, state);
        runner.Begin(engine.Random);
        foreach (string command in script)
        {
            runner.Execute(command, engine.Random);
        }

        return SaveFile.ToJson(state, set);
    }

    /// <summary>Packs a module with <c>goldbox module pack</c> (the pinned pair's <c>rusty pack-content</c>).</summary>
    internal static string Pack(string directory, TempModules scratch, string? output = null)
    {
        output ??= Path.Combine(scratch.Root, Path.GetFileName(directory) + ".rpak");
        (int code, string printed) = CampaignTests.Run(scratch, "module", "pack", directory, "--output", output);
        Assert.True(code == 0, printed);
        return output;
    }
}
