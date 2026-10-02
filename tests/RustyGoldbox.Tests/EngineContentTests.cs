using System.Diagnostics;
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

    /// <summary>Packs a module with the pinned pair's own <c>rusty pack-content</c>.</summary>
    internal static string Pack(string directory, TempModules scratch)
    {
        string library = (string)AppContext.GetData("Rusty.Engine.TestHostLibrary")!;
        string rusty = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(library)!, "..", "bin", "rusty"));
        string output = Path.Combine(scratch.Root, Path.GetFileName(directory) + ".rpak");
        using Process process = Process.Start(new ProcessStartInfo(rusty, ["pack-content", directory, "--output", output, "--compress"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        string errors = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, errors);
        return output;
    }
}
