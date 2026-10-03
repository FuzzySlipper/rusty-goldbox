using System.Globalization;
using System.Text;

namespace RustyGoldbox.Core.Campaigns;

/// <summary>What is on one side of a cell.</summary>
public enum Edge
{
    Open,
    Wall,
    Door,

    /// <summary>A secret door: it remains hidden and blocks movement until discovered.</summary>
    Secret,
}

public enum Facing
{
    North,
    East,
    South,
    West,
}

/// <summary>A canonical edge of an area's cell grid. Shared edges have one key from either side.</summary>
public readonly record struct AreaEdge(int X, int Y, Facing Facing)
{
    public AreaEdge Canonical => Facing switch
    {
        Facing.South => new AreaEdge(X, Y + 1, Facing.North),
        Facing.East => new AreaEdge(X + 1, Y, Facing.West),
        _ => this,
    };

    public string Key
    {
        get
        {
            AreaEdge edge = Canonical;
            return $"{edge.X},{edge.Y},{Facings.Name(edge.Facing)}";
        }
    }

    public static bool TryParse(string text, out AreaEdge edge)
    {
        edge = default;
        string[] parts = text.Split(',', StringSplitOptions.None);
        return parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            && Facings.TryParse(parts[2], out Facing facing)
            && (edge = new AreaEdge(x, y, facing)).Key == text;
    }
}

public static class Facings
{
    public static IReadOnlyList<string> Names { get; } = ["north", "east", "south", "west"];

    public static bool TryParse(string text, out Facing facing)
    {
        int index = Names.ToList().IndexOf(text);
        facing = (Facing)Math.Max(index, 0);
        return index >= 0;
    }

    public static string Name(Facing facing) => Names[(int)facing];

    public static Facing Turn(Facing facing, int quarterTurnsClockwise) => (Facing)(((int)facing + quarterTurnsClockwise % 4 + 4) % 4);

    public static (int Dx, int Dy) Step(Facing facing)
    {
        return facing switch
        {
            Facing.North => (0, -1),
            Facing.East => (1, 0),
            Facing.South => (0, 1),
            _ => (-1, 0),
        };
    }
}

/// <summary>
/// An area's grid of cells with walls on cell edges, read from rows of text:
/// <c>+</c> corners, horizontal edges of two characters (<c>--</c> wall,
/// two spaces open, <c>DD</c> door, <c>SS</c> secret door) and vertical
/// edges of one (<c>|</c>, space, <c>D</c>, <c>S</c>). Cell interiors are two
/// characters the map ignores. x runs west to east, y north to south, from 0.
/// </summary>
public sealed class AreaMap
{
    public const string FormatDescription =
        "rows of text: corners '+', horizontal edges of two characters ('--' wall, '  ' open, 'DD' door, 'SS' secret door), " +
        "vertical edges of one ('|' wall, ' ' open, 'D' door, 'S' secret door), cell interiors of two characters (ignored). " +
        "Rows alternate between edge rows (\"+--+  +\") and cell rows (\"|  D  |\"), starting and ending with an edge row.";

    private readonly Edge[,] _horizontal;
    private readonly Edge[,] _vertical;

    private AreaMap(int width, int height)
    {
        Width = width;
        Height = height;
        _horizontal = new Edge[width, height + 1];
        _vertical = new Edge[width + 1, height];
    }

    public int Width { get; }

    public int Height { get; }

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Whether a cell-side coordinate addresses an edge in this map, including the outer boundary.</summary>
    public bool ContainsEdge(int x, int y, Facing facing) => facing switch
    {
        Facing.North => x >= 0 && x < Width && y >= 0 && y <= Height,
        Facing.South => Contains(x, y),
        Facing.West => x >= 0 && x <= Width && y >= 0 && y < Height,
        _ => Contains(x, y),
    };

    /// <summary>The edge on the <paramref name="facing"/> side of cell (x, y).</summary>
    public Edge EdgeOf(int x, int y, Facing facing)
    {
        return facing switch
        {
            Facing.North => _horizontal[x, y],
            Facing.South => _horizontal[x, y + 1],
            Facing.West => _vertical[x, y],
            _ => _vertical[x + 1, y],
        };
    }

    /// <summary>Copies the map while replacing effective edge kinds, used for player-discovered or opened edges.</summary>
    public AreaMap WithEdges(Func<int, int, Facing, Edge, Edge> replace)
    {
        AreaMap copy = new(Width, Height);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                foreach (Facing facing in Enum.GetValues<Facing>())
                {
                    copy.SetEdge(x, y, facing, replace(x, y, facing, EdgeOf(x, y, facing)));
                }
            }
        }

        return copy;
    }

    /// <summary>Parses map rows; problems are (row, column, message) with 1-based columns.</summary>
    public static AreaMap? Parse(IReadOnlyList<string> rows, List<(int Row, int Column, string Message)> problems)
    {
        if (rows.Count < 3 || rows.Count % 2 == 0)
        {
            problems.Add((0, 1, $"A map needs an odd number of rows, at least 3 (edge, cell, edge); it has {rows.Count}."));
            return null;
        }

        int length = rows[0].Length;
        if (length < 4 || (length - 1) % 3 != 0)
        {
            problems.Add((0, 1, $"Each row must be 1 + 3 × width characters long (\"+--+\" is one cell); row 0 is {length}."));
            return null;
        }

        for (int row = 1; row < rows.Count; row++)
        {
            if (rows[row].Length != length)
            {
                problems.Add((row, 1, $"Every row must be as long as row 0 ({length} characters); this one is {rows[row].Length}."));
            }
        }

        if (problems.Count > 0)
        {
            return null;
        }

        AreaMap map = new((length - 1) / 3, (rows.Count - 1) / 2);
        for (int row = 0; row < rows.Count; row++)
        {
            string text = rows[row];
            if (row % 2 == 0)
            {
                ParseEdgeRow(map, text, row, problems);
            }
            else
            {
                ParseCellRow(map, text, row, problems);
            }
        }

        return problems.Count > 0 ? null : map;
    }

    private static void ParseEdgeRow(AreaMap map, string text, int row, List<(int, int, string)> problems)
    {
        int y = row / 2;
        for (int x = 0; x <= map.Width; x++)
        {
            int corner = x * 3;
            if (text[corner] != '+')
            {
                problems.Add((row, corner + 1, $"Expected a corner '+' here but found '{text[corner]}'."));
            }

            if (x == map.Width)
            {
                break;
            }

            string edge = text.Substring(corner + 1, 2);
            Edge? parsed = edge switch
            {
                "--" => Edge.Wall,
                "  " => Edge.Open,
                "DD" => Edge.Door,
                "SS" => Edge.Secret,
                _ => null,
            };
            if (parsed is null)
            {
                problems.Add((row, corner + 2, $"'{edge}' is not a horizontal edge. Use '--' wall, '  ' open, 'DD' door or 'SS' secret door."));
            }
            else
            {
                map._horizontal[x, y] = parsed.Value;
            }
        }
    }

    private static void ParseCellRow(AreaMap map, string text, int row, List<(int, int, string)> problems)
    {
        int y = row / 2;
        for (int x = 0; x <= map.Width; x++)
        {
            int column = x * 3;
            Edge? parsed = text[column] switch
            {
                '|' => Edge.Wall,
                ' ' => Edge.Open,
                'D' => Edge.Door,
                'S' => Edge.Secret,
                _ => null,
            };
            if (parsed is null)
            {
                problems.Add((row, column + 1, $"'{text[column]}' is not a vertical edge. Use '|' wall, ' ' open, 'D' door or 'S' secret door."));
            }
            else
            {
                map._vertical[x, y] = parsed.Value;
            }
        }
    }

    private void SetEdge(int x, int y, Facing facing, Edge edge)
    {
        switch (facing)
        {
            case Facing.North:
                _horizontal[x, y] = edge;
                break;
            case Facing.South:
                _horizontal[x, y + 1] = edge;
                break;
            case Facing.West:
                _vertical[x, y] = edge;
                break;
            default:
                _vertical[x + 1, y] = edge;
                break;
        }
    }

    /// <summary>
    /// Draws the map in the same notation, with a two-character marker inside
    /// cells that have one. With <paramref name="playerView"/>, secret doors draw as walls.
    /// </summary>
    public string Render(IReadOnlyDictionary<(int X, int Y), string> markers, bool playerView = false)
    {
        StringBuilder text = new();
        for (int y = 0; y <= Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                text.Append('+');
                text.Append(Horizontal(_horizontal[x, y], playerView));
            }

            text.Append('+').Append('\n');
            if (y == Height)
            {
                break;
            }

            for (int x = 0; x <= Width; x++)
            {
                text.Append(Vertical(_vertical[x, y], playerView));
                if (x < Width)
                {
                    string marker = markers.TryGetValue((x, y), out string? found) ? found : "  ";
                    text.Append(marker.PadRight(2)[..2]);
                }
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static string Horizontal(Edge edge, bool playerView) => edge switch
    {
        Edge.Wall => "--",
        Edge.Door => "DD",
        Edge.Secret => playerView ? "--" : "SS",
        _ => "  ",
    };

    private static char Vertical(Edge edge, bool playerView) => edge switch
    {
        Edge.Wall => '|',
        Edge.Door => 'D',
        Edge.Secret => playerView ? '|' : 'S',
        _ => ' ',
    };
}
