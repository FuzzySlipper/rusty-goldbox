namespace RustyGoldbox.Game.Presentation;

/// <summary>
/// Where the view panel is, as fractions of the window measured from its top
/// left. The DOM panels report it (the <c>layout</c> action); the scene's
/// camera draws there.
/// </summary>
internal readonly record struct ViewWindow(float X, float Y, float Width, float Height)
{
    /// <summary>The whole window, until the panels report where the view is.</summary>
    public static readonly ViewWindow Whole = new(0, 0, 1, 1);
}
