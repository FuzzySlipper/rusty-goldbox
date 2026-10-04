using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Game.Presentation;

/// <summary>
/// An asset in a picture slot, shown over the view window: an Engine atlas
/// of its frames at their pixel size (an image is one frame, a sheet every
/// frame), a sprite fitted to the window, and the frames of the first
/// animation a sheet has, which play while it shows. Every visual media goes
/// through here, so a new one is shown in one place.
/// </summary>
internal sealed class PictureArt : IDisposable
{
    private readonly IGraphicsService _graphics;
    private readonly SpritePlaybackFrame[]? _animation;
    private readonly bool _loop;

    private PictureArt(IGraphicsService graphics, SpriteAtlas atlas, Appearance sprite, SpritePlaybackFrame[]? animation, bool loop)
    {
        _graphics = graphics;
        Atlas = atlas;
        Sprite = sprite;
        _animation = animation;
        _loop = loop;
    }

    public SpriteAtlas Atlas { get; }

    /// <summary>The sprite filling the view window, showing the first frame until played.</summary>
    public Appearance Sprite { get; }

    public bool Animated => _animation is not null;

    /// <summary>Admits a checked picture asset's frames from its <paramref name="texture"/>.</summary>
    public static PictureArt Admit(IGraphicsService graphics, RenderResource texture, Definition asset, (int Width, int Height) image)
    {
        JsonElement json = asset.Json;
        (int frameWidth, int frameHeight) = image;
        int count = 1;
        if (Media.MediaOf(asset) == "sheet")
        {
            frameWidth = json.GetProperty("frame_size")[0].GetInt32();
            frameHeight = json.GetProperty("frame_size")[1].GetInt32();
            int cells = image.Width / frameWidth * (image.Height / frameHeight);
            count = json.TryGetProperty("frame_count", out JsonElement declared) ? declared.GetInt32() : cells;
        }

        int columns = image.Width / frameWidth;
        SpriteAtlasFrame[] frames = new SpriteAtlasFrame[count];
        for (int index = 0; index < count; index++)
        {
            int x = index % columns * frameWidth;
            int y = index / columns * frameHeight;
            frames[index] = TextureSampling.Frame(
                asset,
                (uint)index,
                x,
                y,
                frameWidth,
                frameHeight,
                image,
                new Vector2(frameWidth, frameHeight));
        }

        SpriteAtlas atlas = graphics.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture, frames));
        Appearance sprite = graphics.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(
            atlas, 0, new Vector2(0.5f, 0.5f), Vector2.One, BillboardMode.None, SpriteSizeMode.Pixel, 100, SpriteDepthPolicy.DepthTestOff, new Color(1, 1, 1, 1)));

        // Sprite placement is a rectangle within the camera's viewport: this fills the view window.
        graphics.SetSpriteViewport(new SpriteViewportUpdateRequest(
            sprite, true, Vector2.Zero, Vector2.One, new Vector2(0.5f, 0.5f), SpriteViewportFit.Contain));

        SpritePlaybackFrame[]? animation = null;
        bool loop = true;
        if (json.TryGetProperty("animations", out JsonElement animations) && animations.EnumerateObject().FirstOrDefault() is { Value.ValueKind: JsonValueKind.Object } first)
        {
            double seconds = 1 / first.Value.GetProperty("fps").GetDouble();
            animation = first.Value.GetProperty("frames").EnumerateArray().Select(frame => new SpritePlaybackFrame((uint)frame.GetInt32(), seconds)).ToArray();
            loop = !first.Value.TryGetProperty("loop", out JsonElement repeat) || repeat.GetBoolean();
        }

        return new PictureArt(graphics, atlas, sprite, animation, loop);
    }

    /// <summary>
    /// Starts the first animation from its beginning, or returns null for a
    /// still picture. The caller advances it in its updates and disposes it.
    /// </summary>
    public SpritePlayback? Play()
    {
        if (_animation is null)
        {
            return null;
        }

        SpritePlayback playback = _graphics.CreateSpritePlayback(new SpritePlaybackCreateRequest(
            Sprite, Atlas, _animation, ReadOnlyMemory<SpritePlaybackMarker>.Empty,
            _loop ? SpritePlaybackLoopMode.Loop : SpritePlaybackLoopMode.OneShot, 1));
        _graphics.ControlSpritePlayback(new SpritePlaybackControlRequest(playback, SpritePlaybackControl.Start));
        return playback;
    }

    /// <summary>Releases the sprite and atlas; it must no longer be published.</summary>
    public void Dispose()
    {
        Sprite.Dispose();
        Atlas.Dispose();
    }
}
