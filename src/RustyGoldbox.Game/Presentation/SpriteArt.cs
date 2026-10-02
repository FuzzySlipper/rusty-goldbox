using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Game.Presentation;

/// <summary>
/// A sprite asset admitted to the Engine: an atlas of its frames sized in
/// world units (a cell is 1), the pivot its anchor stands on, and its
/// animations as playback frames. Figures made from it are billboards turned
/// to the camera around the vertical axis; <see cref="Scale"/> flips one to
/// face the other way, since the art faces only one.
/// </summary>
internal sealed class SpriteArt : IDisposable
{
    private readonly IGraphicsService _graphics;
    private readonly Dictionary<string, (SpritePlaybackFrame[] Frames, bool Loop)> _animations = [];

    private SpriteArt(IGraphicsService graphics, SpriteAtlas atlas, Vector2 pivot, bool facesRight)
    {
        _graphics = graphics;
        Atlas = atlas;
        Pivot = pivot;
        FacesRight = facesRight;
    }

    public SpriteAtlas Atlas { get; }

    /// <summary>The anchor as a fraction of the frame, from its lower left.</summary>
    public Vector2 Pivot { get; }

    public bool FacesRight { get; }

    public IReadOnlyCollection<string> Animations => _animations.Keys;

    /// <summary>Admits a checked sprite asset's frames from its <paramref name="texture"/>.</summary>
    public static SpriteArt Admit(IGraphicsService graphics, RenderResource texture, Definition asset, (int Width, int Height) image)
    {
        JsonElement json = asset.Json;
        int frameWidth = json.GetProperty("frame_size")[0].GetInt32();
        int frameHeight = json.GetProperty("frame_size")[1].GetInt32();
        int columns = image.Width / frameWidth;
        int count = json.TryGetProperty("frame_count", out JsonElement declared) ? declared.GetInt32() : columns * (image.Height / frameHeight);
        float height = (float)json.GetProperty("height").GetDouble();
        Vector2 size = new(height * frameWidth / frameHeight, height);

        SpriteAtlasFrame[] frames = new SpriteAtlasFrame[count];
        for (int index = 0; index < count; index++)
        {
            int x = index % columns * frameWidth;
            int y = index / columns * frameHeight;
            frames[index] = new SpriteAtlasFrame(
                (uint)index,
                new Vector2((float)x / image.Width, (float)y / image.Height),
                new Vector2((float)(x + frameWidth) / image.Width, (float)(y + frameHeight) / image.Height),
                true,
                size);
        }

        // The anchor pixel's bottom edge stands on the floor, at its centre across.
        Vector2 pivot = json.TryGetProperty("anchor", out JsonElement anchor)
            ? new Vector2((anchor[0].GetInt32() + 0.5f) / frameWidth, 1 - ((anchor[1].GetInt32() + 1f) / frameHeight))
            : new Vector2(0.5f, 0);
        SpriteArt art = new(graphics, graphics.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture, frames)), pivot, json.GetProperty("faces").GetString() == "right");
        if (json.TryGetProperty("animations", out JsonElement animations))
        {
            foreach (JsonProperty animation in animations.EnumerateObject())
            {
                double seconds = 1 / animation.Value.GetProperty("fps").GetDouble();
                SpritePlaybackFrame[] played = animation.Value.GetProperty("frames").EnumerateArray()
                    .Select(frame => new SpritePlaybackFrame((uint)frame.GetInt32(), seconds))
                    .ToArray();
                bool loop = !animation.Value.TryGetProperty("loop", out JsonElement repeat) || repeat.GetBoolean();
                art._animations[animation.Name] = (played, loop);
            }
        }

        return art;
    }

    /// <summary>
    /// A new figure showing frame 0, standing on its anchor. Cylindrical
    /// figures turn to the camera around the vertical axis (for the level
    /// camera); spherical ones face it fully, so they stay upright under a
    /// camera looking down (combat).
    /// </summary>
    public Appearance CreateFigure(BillboardMode billboard = BillboardMode.Cylindrical)
    {
        return _graphics.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(
            Atlas, 0, Pivot, Vector2.One, billboard, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1, 1, 1, 1)));
    }

    /// <summary>
    /// Starts <paramref name="animation"/> on a figure, or returns null when
    /// the sprite has no such animation. The product advances it in its
    /// updates (<c>AdvanceSpritePlayback</c>) and disposes it.
    /// </summary>
    public SpritePlayback? Play(Appearance figure, string animation)
    {
        if (!_animations.TryGetValue(animation, out (SpritePlaybackFrame[] Frames, bool Loop) played))
        {
            return null;
        }

        SpritePlayback playback = _graphics.CreateSpritePlayback(new SpritePlaybackCreateRequest(
            figure, Atlas, played.Frames, ReadOnlyMemory<SpritePlaybackMarker>.Empty,
            played.Loop ? SpritePlaybackLoopMode.Loop : SpritePlaybackLoopMode.OneShot, 1));
        _graphics.ControlSpritePlayback(new SpritePlaybackControlRequest(playback, SpritePlaybackControl.Start));
        return playback;
    }

    /// <summary>The transform scale that faces a figure right or left: a negative X scale mirrors it.</summary>
    public Vector3 Scale(bool faceRight) => new(faceRight == FacesRight ? 1 : -1, 1, 1);

    public void Dispose() => Atlas.Dispose();
}
