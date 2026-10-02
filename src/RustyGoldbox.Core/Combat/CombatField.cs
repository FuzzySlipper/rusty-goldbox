using System.Text.Json;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Core.Combat;

/// <summary>A cell on a combat field: X across from the first side's edge, Y down.</summary>
public readonly record struct Cell(int X, int Y);

/// <summary>
/// A combat definition's field: a grid of cells, and how distance is counted
/// on it ("chebyshev": a diagonal step is 1; "manhattan": only straight steps).
/// The first side starts on the left edge and the second on the right, rank
/// after rank inward; further sides fill in after them.
/// </summary>
public sealed class CombatField
{
    private CombatField(int width, int height, bool diagonal)
    {
        Width = width;
        Height = height;
        Diagonal = diagonal;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Whether a diagonal step counts as one (chebyshev) rather than not existing (manhattan).</summary>
    public bool Diagonal { get; }

    /// <summary>The combat definition's field, or null when its fights have no positions.</summary>
    public static CombatField? Of(Definition combat)
    {
        if (!combat.Json.TryGetProperty("field", out JsonElement field))
        {
            return null;
        }

        bool diagonal = !field.TryGetProperty("metric", out JsonElement metric) || metric.GetString() == "chebyshev";
        return new CombatField(field.GetProperty("width").GetInt32(), field.GetProperty("height").GetInt32(), diagonal);
    }

    public int Distance(Cell a, Cell b)
    {
        int across = Math.Abs(a.X - b.X);
        int down = Math.Abs(a.Y - b.Y);
        return Diagonal ? Math.Max(across, down) : across + down;
    }

    public bool Contains(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

    /// <summary>The cells one step from <paramref name="cell"/>, straight steps first.</summary>
    public IEnumerable<Cell> Neighbours(Cell cell)
    {
        (int, int)[] steps = Diagonal
            ? [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)]
            : [(1, 0), (-1, 0), (0, 1), (0, -1)];
        return steps.Select(step => new Cell(cell.X + step.Item1, cell.Y + step.Item2)).Where(Contains);
    }

    /// <summary>
    /// Starting cells: side 0 from the left edge, side 1 from the right, each
    /// filling a column from the middle outward before moving one column in.
    /// </summary>
    public IReadOnlyList<Cell> Deploy(int side, int count)
    {
        List<int> rows = Enumerable.Range(0, Height).OrderBy(row => Math.Abs(row * 2 - (Height - 1))).ThenBy(row => row).ToList();
        List<Cell> cells = [];
        for (int index = 0; index < count; index++)
        {
            int column = index / Height;
            int x = side % 2 == 0 ? column + (side / 2) : Width - 1 - column - (side / 2);
            cells.Add(new Cell(Math.Clamp(x, 0, Width - 1), rows[index % Height]));
        }

        return cells;
    }
}
