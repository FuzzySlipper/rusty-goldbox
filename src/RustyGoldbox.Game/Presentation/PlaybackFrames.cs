using Rusty.Engine;

namespace RustyGoldbox.Game.Presentation;

/// <summary>
/// Advances sprite playbacks and says when one moved to another frame. The
/// renderer shows a playback's frame as of the latest published snapshot, so
/// a scene republishes when this reports a change; animations then show at
/// their own rate rather than only when something else publishes.
/// </summary>
internal sealed class PlaybackFrames(IGraphicsService graphics)
{
    private readonly Dictionary<SpritePlayback, uint> _shown = [];

    /// <summary>Advances <paramref name="playback"/>; true when its frame differs from the one last published.</summary>
    public bool Advance(SpritePlayback playback)
    {
        uint frame = graphics.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(playback)).Readout.FrameId;
        if (_shown.TryGetValue(playback, out uint shown) && shown == frame)
        {
            return false;
        }

        _shown[playback] = frame;
        return true;
    }

    /// <summary>Forgets every playback; call when a new snapshot replaces what was shown.</summary>
    public void Published() => _shown.Clear();
}
