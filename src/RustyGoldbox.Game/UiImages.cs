using Rusty.Engine;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Game;

/// <summary>
/// Module images (portraits, icons) granted to the DOM panels: each asset's
/// PNG opened once from its module's bundle as an Engine UI image, whose URL
/// the projection carries for an img element. The Engine keeps the bytes, so
/// the bundle is released at once; the images last until the product ends.
/// </summary>
internal sealed class UiImages(IEngineContext engine, ModuleLibrary library) : IDisposable
{
    private readonly Dictionary<string, UiImage?> _images = [];

    /// <summary>The URL the panels show the asset at, or null when its module can't be opened.</summary>
    public string? Url(ModuleSet set, Definition asset)
    {
        ModuleSource source = set.LoadOrder.First(loaded => loaded.Manifest.Id == asset.Module).Manifest.Source;
        string key = $"{asset.QualifiedId}@{source.Identity}";
        if (!_images.TryGetValue(key, out UiImage? image))
        {
            string file = asset.Json.GetProperty("file").GetString()!;
            image = library.ReadModule(source.Location, bundle =>
            {
                using ContentReference reference = bundle.OpenReference(file);
                return engine.Ui.OpenImage(new UiImageRequest(reference));
            });
            _images[key] = image;
        }

        return image?.Url();
    }

    public void Dispose()
    {
        foreach (UiImage? image in _images.Values)
        {
            image?.Dispose();
        }

        _images.Clear();
    }
}
