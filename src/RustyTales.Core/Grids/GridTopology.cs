namespace RustyTales.Core.Grids;

/// <summary>Canonical 2D grid coordinate: X west-to-east, Y north-to-south, from 0.</summary>
public readonly record struct GridCell(int X, int Y);

/// <summary>Canonical spatial traits of a single grid cell.</summary>
/// <param name="Passable">Whether creatures or the party can occupy this cell.</param>
/// <param name="MoveCost">Movement cost to enter this cell (1 on normal open ground).</param>
/// <param name="BlocksSight">Whether this cell blocks line of sight through it.</param>
/// <param name="IsDoor">Whether this passable cell acts as a doorway/threshold.</param>
/// <param name="Label">Optional human-readable terrain or tile label (for tooltips and diagnostics).</param>
public readonly record struct CellTraits(
    bool Passable,
    int MoveCost = 1,
    bool BlocksSight = false,
    bool IsDoor = false,
    string? Label = null);

/// <summary>
/// Minimal canonical read-only spatial grid interface shared by edge-wall dungeon maps
/// (<see cref="Campaigns.AreaMap"/>), multi-layer 2D tilemaps (<see cref="Campaigns.ResolvedTilemap"/>),
/// and tactical battlefields (<see cref="Combat.CombatField"/>).
/// </summary>
public interface IGridTopology
{
    int Width { get; }

    int Height { get; }

    /// <summary>Whether diagonal steps are adjacent (Chebyshev) rather than orthogonal-only (Manhattan).</summary>
    bool AllowsDiagonal { get; }

    bool Contains(GridCell cell);

    CellTraits TraitsAt(GridCell cell);

    /// <summary>
    /// Whether stepping from <paramref name="from"/> to adjacent cell <paramref name="to"/> is permitted
    /// by this grid's topology (shared edges on edge-wall maps; cell passability on cell grids).
    /// </summary>
    bool CanStep(GridCell from, GridCell to);

    /// <summary>
    /// Whether the boundary between orthogonally adjacent cells <paramref name="from"/> and <paramref name="to"/>
    /// blocks line of sight (e.g. a wall or closed door on an edge-wall map).
    /// </summary>
    bool BlocksSightBetween(GridCell from, GridCell to);
}
