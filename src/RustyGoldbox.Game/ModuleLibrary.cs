using Rusty.Engine;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Game;

/// <summary>A campaign module the product can start, found in a content bundle.</summary>
/// <param name="Extensions">Extensions the player may add: installed ones built on the campaign's ruleset that it doesn't already require.</param>
internal sealed record CampaignChoice(string Bundle, string Id, string Title, ModuleVersion Version, string Identity, IReadOnlyList<ExtensionChoice> Extensions);

/// <summary>A skin the player may pick, from an installed assets module.</summary>
internal sealed record SkinChoice(string Bundle, string Id, string Name);

/// <summary>An extension module the player may add to a campaign's module set.</summary>
internal sealed record ExtensionChoice(string Id, string Title, ModuleVersion Version);

/// <summary>
/// The modules the product can load: its own content bundles (one per module
/// directory), modules installed as containers in the module library, and
/// any development workspaces' modules (<see cref="WorkspaceModules"/>).
/// Each call opens them afresh, so under <c>rusty dev</c> it sees bundle
/// edits and newly installed modules without a restart.
/// </summary>
/// <param name="open">
/// Opens every bundle and container the product has, describing any that
/// don't open in the list it is given; the library disposes what it returns.
/// </param>
/// <param name="prepare">
/// Runs before each campaign listing (the title screen and its Refresh), for
/// work too slow for every open, such as packing edited workspace modules.
/// </param>
internal sealed class ModuleLibrary(Func<List<string>, List<ProductContentBundle>> open, Action<List<string>>? prepare = null)
{
    private static readonly string HowToAdd =
        $"Install it with `goldbox module pack <dir> --install` or `goldbox workspace install <workspace>` (into {InstalledModules.DefaultDirectory()}), or put its directory under modules/ and rebuild.";

    /// <summary>
    /// Every bundle holding a valid campaign manifest. Containers that don't
    /// open are described in <paramref name="problems"/>; other bundles'
    /// problems are left for their own load.
    /// </summary>
    public List<CampaignChoice> Campaigns(List<string> problems)
    {
        prepare?.Invoke(problems);
        List<CampaignChoice> campaigns = [];
        WithBundles(problems, sources =>
        {
            List<(ModuleSource Source, ModuleManifest Manifest)> manifests = sources
                .Select(source => (source, ManifestReader.Read(source, [])))
                .Where(entry => entry.Item2 is not null)
                .Select(entry => (entry.source, entry.Item2!))
                .ToList();
            HashSet<string> rulesets = manifests.Where(entry => entry.Manifest.Kind == ModuleKind.Ruleset).Select(entry => entry.Manifest.Id).ToHashSet();
            foreach ((ModuleSource source, ModuleManifest manifest) in manifests)
            {
                // A campaign the product ships and also has installed, with the
                // same content, is one campaign; list its first copy.
                if (manifest.Kind == ModuleKind.Campaign
                    && !campaigns.Any(campaign => campaign.Id == manifest.Id && campaign.Version == manifest.Version && campaign.Identity == source.Identity))
                {
                    List<string> ruleset = manifest.Requires.Select(requirement => requirement.Id).Where(rulesets.Contains).ToList();
                    List<ExtensionChoice> extensions = manifests
                        .Select(entry => entry.Manifest)
                        .Where(extension => extension.Kind == ModuleKind.Extension
                            && manifest.Requires.All(requirement => requirement.Id != extension.Id)
                            && extension.Requires.Any(requirement => ruleset.Contains(requirement.Id)))
                        .GroupBy(extension => extension.Id)
                        .Select(versions => versions.MaxBy(extension => extension.Version)!)
                        .Select(extension => new ExtensionChoice(extension.Id, extension.Title, extension.Version))
                        .ToList();
                    campaigns.Add(new CampaignChoice(source.Location, manifest.Id, manifest.Title, manifest.Version, source.Identity, extensions));
                }
            }
        });
        return campaigns;
    }

    /// <summary>
    /// Every skin in a valid assets module, by qualified ID, first copy of
    /// each. A campaign's own skin comes with the campaign instead.
    /// </summary>
    public List<SkinChoice> Skins()
    {
        List<SkinChoice> skins = [];
        WithBundles([], sources =>
        {
            foreach (ModuleSource source in sources)
            {
                if (ManifestReader.Read(source, []) is not { Kind: ModuleKind.Assets })
                {
                    continue;
                }

                ModuleSet set = ModuleLoader.Load(source, sources, sources.Select(each => each.Location).ToList(), HowToAdd);
                foreach (Definition skin in set.Rules?.OfType(DefinitionTypes.Skin).Where(skin => skin.Module == set.Root!.Id) ?? [])
                {
                    if (skins.All(known => known.Id != skin.QualifiedId))
                    {
                        skins.Add(new SkinChoice(source.Location, skin.QualifiedId, skin.Json.GetProperty("name").GetString()!));
                    }
                }
            }
        });
        return skins;
    }

    /// <summary>Loads the module in <paramref name="bundle"/> and its requirements from the other bundles, with the extensions added.</summary>
    public ModuleSet Load(string bundle, IReadOnlyList<string> extensions)
    {
        ModuleSet? set = null;
        WithBundles([], sources =>
        {
            ModuleSource root = sources.FirstOrDefault(source => source.Location == bundle)
                ?? throw new InvalidOperationException($"There is no content bundle '{bundle}'.");
            set = ModuleLoader.Load(root, sources, sources.Select(source => source.Location).ToList(), HowToAdd, extensions);
        });
        return set!;
    }

    /// <summary>The bundle holding the module a save names: its ID, version and content identity.</summary>
    public string? BundleOf(string id, string version, string identity)
    {
        string? found = null;
        WithBundles([], sources =>
        {
            found = sources.FirstOrDefault(source => source.Identity == identity
                && ManifestReader.Read(source, []) is { } manifest
                && manifest.Id == id
                && manifest.Version.ToString() == version)?.Location;
        });
        return found;
    }

    /// <summary>
    /// Runs <paramref name="read"/> on the bundle or container a loaded
    /// module came from (its source location), for art the presentation
    /// admits after loading. Null when it is no longer there.
    /// </summary>
    public T? ReadModule<T>(string location, Func<ProductContentBundle, T> read)
        where T : class
    {
        T? result = null;
        List<ProductContentBundle> bundles = open([]);
        try
        {
            if (bundles.FirstOrDefault(bundle => bundle.Id == location) is ProductContentBundle found)
            {
                result = read(found);
            }
        }
        finally
        {
            foreach (ProductContentBundle bundle in bundles)
            {
                bundle.Dispose();
            }
        }

        return result;
    }

    /// <summary>Opens every bundle for the length of <paramref name="work"/>; loaded modules keep what they read.</summary>
    private void WithBundles(List<string> problems, Action<List<ModuleSource>> work)
    {
        List<ProductContentBundle> bundles = open(problems);
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
