using Rusty.Engine;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Game;

/// <summary>A campaign module the product can start, found in a content bundle.</summary>
internal sealed record CampaignChoice(string Bundle, string Id, string Title, ModuleVersion Version);

/// <summary>
/// The modules the product ships: one Engine content bundle per module
/// directory. Each call opens the bundles afresh, so under <c>rusty dev</c> it
/// sees bundle edits without a restart.
/// </summary>
/// <param name="open">Opens every bundle the product has; the library disposes them.</param>
internal sealed class ModuleLibrary(Func<List<ProductContentBundle>> open)
{
    private const string HowToAdd =
        "Put the module's directory under modules/; the Game project declares every directory there as a content bundle.";

    /// <summary>Every bundle holding a valid campaign manifest. Other bundles' problems are left for their own load.</summary>
    public List<CampaignChoice> Campaigns()
    {
        List<CampaignChoice> campaigns = [];
        WithBundles(sources =>
        {
            foreach (ModuleSource source in sources)
            {
                ModuleManifest? manifest = ManifestReader.Read(source, []);
                if (manifest is { Kind: ModuleKind.Campaign })
                {
                    campaigns.Add(new CampaignChoice(source.Location, manifest.Id, manifest.Title, manifest.Version));
                }
            }
        });
        return campaigns;
    }

    /// <summary>Loads the module in <paramref name="bundle"/> and its requirements from the other bundles.</summary>
    public ModuleSet Load(string bundle)
    {
        ModuleSet? set = null;
        WithBundles(sources =>
        {
            ModuleSource root = sources.FirstOrDefault(source => source.Location == bundle)
                ?? throw new InvalidOperationException($"There is no content bundle '{bundle}'.");
            set = ModuleLoader.Load(root, sources, sources.Select(source => source.Location).ToList(), HowToAdd);
        });
        return set!;
    }

    /// <summary>The bundle holding version <paramref name="version"/> of module <paramref name="id"/>, for finding a save's campaign.</summary>
    public string? BundleOf(string id, string version)
    {
        string? found = null;
        WithBundles(sources =>
        {
            found = sources.FirstOrDefault(source => ManifestReader.Read(source, []) is { } manifest
                && manifest.Id == id
                && manifest.Version.ToString() == version)?.Location;
        });
        return found;
    }

    /// <summary>Opens every bundle for the length of <paramref name="work"/>; loaded modules keep what they read.</summary>
    private void WithBundles(Action<List<ModuleSource>> work)
    {
        List<ProductContentBundle> bundles = open();
        try
        {
            work(bundles.Select(bundle => (ModuleSource)new BundleModuleSource(bundle)).ToList());
        }
        finally
        {
            foreach (ProductContentBundle bundle in bundles)
            {
                bundle.Dispose();
            }
        }
    }
}
