using System.Text.Json;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Core.Combat;

/// <summary>A cell on a combat field: X across from the first side's edge, Y down.</summary>
public readonly record struct Cell(int X, int Y);

/// <summary>A kind of ground the combat field declares: whether creatures can enter it, what entering costs and whether it blocks sight.</summary>
public sealed record Terrain(char Key, string Name, bool Passable, int Cost, bool BlocksSight);

/// <summary>
/// A combat definition's field: a grid of cells, and how distance is counted
/// on it ("chebyshev": a diagonal step is 1; "manhattan": only straight steps).
/// The first side starts on the left edge and the second on the right, rank
/// after rank inward; further sides fill in after them. An encounter may lay
/// the field's terrain over it; every other cell is open ground.
/// </summary>
public sealed class CombatField
{
    /// <summary>The character an encounter's terrain rows use for open ground.</summary>
    public const char Open = '.';

    private readonly Dictionary<Cell, Terrain> _terrain;

    private CombatField(int width, int height, bool diagonal, Dictionary<Cell, Terrain> terrain)
    {
        Width = width;
        Height = height;
        Diagonal = diagonal;
        _terrain = terrain;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Whether a diagonal step counts as one (chebyshev) rather than not existing (manhattan).</summary>
    public bool Diagonal { get; }

    /// <summary>The cells that aren't open ground, and what is on them.</summary>
    public IReadOnlyDictionary<Cell, Terrain> Terrain => _terrain;

    /// <summary>
    /// The combat definition's field, or null when its fights have no
    /// positions, with the encounter's terrain laid over it. Loading checked
    /// that the encounter's rows fit the field and use its terrain.
    /// </summary>
    public static CombatField? Of(Definition combat, Definition? encounter = null)
    {
        if (!combat.Json.TryGetProperty("field", out JsonElement field))
        {
            return null;
        }

        bool diagonal = !field.TryGetProperty("metric", out JsonElement metric) || metric.GetString() == "chebyshev";
        Dictionary<char, Terrain> kinds = Kinds(field);
        Dictionary<Cell, Terrain> terrain = [];
        if (encounter is not null && encounter.Json.TryGetProperty("terrain", out JsonElement rows))
        {
            int y = 0;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                string text = row.GetString()!;
                for (int x = 0; x < text.Length; x++)
                {
                    if (kinds.TryGetValue(text[x], out Terrain? kind))
                    {
                        terrain[new Cell(x, y)] = kind;
                    }
                }

                y++;
            }
        }

        return new CombatField(field.GetProperty("width").GetInt32(), field.GetProperty("height").GetInt32(), diagonal, terrain);
    }

    /// <summary>The terrain kinds a combat definition's field declares, by key.</summary>
    public static Dictionary<char, Terrain> Kinds(JsonElement field)
    {
        Dictionary<char, Terrain> kinds = [];
        if (!field.TryGetProperty("terrain", out JsonElement terrain))
        {
            return kinds;
        }

        foreach (JsonProperty entry in terrain.EnumerateObject())
        {
            JsonElement kind = entry.Value;
            kinds[entry.Name[0]] = new Terrain(
                entry.Name[0],
                kind.GetProperty("name").GetString()!,
                !kind.TryGetProperty("passable", out JsonElement passable) || passable.GetBoolean(),
                kind.TryGetProperty("cost", out JsonElement cost) ? cost.GetInt32() : 1,
                kind.TryGetProperty("blocks_sight", out JsonElement sight) && sight.GetBoolean());
        }

        return kinds;
    }

    public int Distance(Cell a, Cell b)
    {
        int across = Math.Abs(a.X - b.X);
        int down = Math.Abs(a.Y - b.Y);
        return Diagonal ? Math.Max(across, down) : across + down;
    }

    public bool Contains(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

    /// <summary>Whether a creature can stand in the cell.</summary>
    public bool Passable(Cell cell) => !_terrain.TryGetValue(cell, out Terrain? terrain) || terrain.Passable;

    /// <summary>The movement it takes to enter the cell: 1 on open ground.</summary>
    public int Cost(Cell cell) => _terrain.TryGetValue(cell, out Terrain? terrain) ? terrain.Cost : 1;

    /// <summary>The cells one step from <paramref name="cell"/>, straight steps first.</summary>
    public IEnumerable<Cell> Neighbours(Cell cell)
    {
        (int, int)[] steps = Diagonal
            ? [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)]
            : [(1, 0), (-1, 0), (0, 1), (0, -1)];
        return steps.Select(step => new Cell(cell.X + step.Item1, cell.Y + step.Item2)).Where(Contains);
    }

    /// <summary>
    /// Whether nothing between two cells blocks sight: no cell on the straight
    /// line from centre to centre (looked at every half cell), other than the
    /// two ends, has terrain that blocks it. Where the line runs exactly
    /// between cells, it is blocked only if all of them block.
    /// </summary>
    public bool CanSee(Cell from, Cell to)
    {
        if (!_terrain.Values.Any(terrain => terrain.BlocksSight))
        {
            return true;
        }

        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy)) * 2;
        for (int step = 1; step < steps; step++)
        {
            // Points along the line at half-cell spacing, in doubled coordinates so they stay whole.
            int numeratorX = from.X * steps * 2 + dx * step * 2 + steps;
            int numeratorY = from.Y * steps * 2 + dy * step * 2 + steps;
            int denominator = steps * 2;
            List<int> xs = Covering(numeratorX, denominator);
            List<int> ys = Covering(numeratorY, denominator);
            List<Cell> cells = xs.SelectMany(x => ys.Select(y => new Cell(x, y))).Where(cell => cell != from && cell != to).ToList();
            if (cells.Count > 0 && cells.All(BlocksSight))
            {
                return false;
            }
        }

        return true;
    }

    private bool BlocksSight(Cell cell) => _terrain.TryGetValue(cell, out Terrain? terrain) && terrain.BlocksSight;

    /// <summary>The cell indexes a coordinate numerator / denominator lies in: one, or two when it is on the edge between them.</summary>
    private static List<int> Covering(int numerator, int denominator)
    {
        int index = (int)Math.Floor((double)numerator / denominator);
        return numerator % denominator == 0 ? [index - 1, index] : [index];
    }

    /// <summary>
    /// Starting cells: side 0 from the left edge, side 1 from the right, each
    /// filling a column from the middle outward before moving one column in,
    /// passing over cells creatures can't stand in.
    /// </summary>
    public IReadOnlyList<Cell> Deploy(int side, int count)
    {
        List<int> rows = Enumerable.Range(0, Height).OrderBy(row => Math.Abs(row * 2 - (Height - 1))).ThenBy(row => row).ToList();
        List<Cell> open = Enumerable.Range(0, Width)
            .Select(column => side % 2 == 0 ? column + (side / 2) : Width - 1 - column - (side / 2))
            .Where(x => x >= 0 && x < Width)
            .SelectMany(x => rows.Select(y => new Cell(x, y)))
            .Where(Passable)
            .ToList();
        if (open.Count == 0)
        {
            open.Add(new Cell(side % 2 == 0 ? 0 : Width - 1, rows[0]));
        }

        List<Cell> cells = [];
        for (int index = 0; index < count; index++)
        {
            // A field too full for everyone stacks the rest on the last cell.
            cells.Add(open[Math.Min(index, open.Count - 1)]);
        }

        return cells;
    }
}
