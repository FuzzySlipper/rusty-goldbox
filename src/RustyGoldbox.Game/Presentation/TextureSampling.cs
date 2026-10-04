using System.Numerics;
using Rusty.Engine;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Game.Presentation;

/// <summary>
/// Translates an asset's authored sampling policy into the existing Engine
/// texture request and cropped UVs. Linear filtering needs a half-pixel inset
/// whenever a frame is cut from a larger image, otherwise its filter footprint
/// can include the adjacent frame.
/// </summary>
internal static class TextureSampling
{
    public static TextureFilter Filter(Definition asset)
    {
        return Media.SamplingOf(asset) == "linear" ? TextureFilter.Linear : TextureFilter.Nearest;
    }

    public static SpriteAtlasFrame Frame(
        Definition asset,
        uint frameId,
        int x,
        int y,
        int width,
        int height,
        (int Width, int Height) image,
        Vector2 size)
    {
        (Vector2 min, Vector2 max) = Coordinates(asset, x, y, width, height, image);
        return new SpriteAtlasFrame(frameId, min, max, true, size);
    }

    public static UvRect Region(Definition asset, int x, int y, int width, int height, (int Width, int Height) image)
    {
        (Vector2 min, Vector2 max) = Coordinates(asset, x, y, width, height, image);
        return new UvRect(min.X, min.Y, max.X, max.Y);
    }

    private static (Vector2 Min, Vector2 Max) Coordinates(
        Definition asset,
        int x,
        int y,
        int width,
        int height,
        (int Width, int Height) image)
    {
        // A full image has no neighboring texels to bleed into it. For a
        // cropped frame or named region, keep the linear filter footprint
        // within the region by moving each edge half a source pixel inward.
        bool linear = Media.SamplingOf(asset) == "linear";
        bool croppedX = x > 0 || width < image.Width;
        bool croppedY = y > 0 || height < image.Height;
        float insetX = croppedX && linear && width > 1 ? 0.5f : 0;
        float insetY = croppedY && linear && height > 1 ? 0.5f : 0;
        return (
            new Vector2((x + insetX) / image.Width, (y + insetY) / image.Height),
            new Vector2((x + width - insetX) / image.Width, (y + height - insetY) / image.Height));
    }
}
