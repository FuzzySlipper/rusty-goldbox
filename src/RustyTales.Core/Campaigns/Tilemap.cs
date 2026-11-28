using System.Text.Json;
using RustyTales.Core.Definitions;
using RustyTales.Core.Rules;

namespace RustyTales.Core.Campaigns;

/// <summary>Auto-tiling rule declared on a <see cref="TileGlyph"/>.</summary>
public sealed record TileAutotileRule(
    string Mode,
    int? BaseFrame,
    IReadOnlyDictionary<string, int> Frames,
    IReadOnlySet<char> ConnectsTo,
    bool OutOfBoundsSame);

/// <summary>One semantic tile entry in a compiled <see cref="TilesetDefinition"/>.</summary>
public sealed record TileGlyph(
    char Glyph,
    string Name,
    int? Tile,
    IReadOnlyList<int> Variants,
    TileAutotileRule? Autotile,
    bool Passable,
    bool Door,
    string? Color);

/// <summary>A validated, compiled tileset definition bound to its sheet asset geometry.</summary>
public sealed class TilesetDefinition
{
    public TilesetDefinition(
        Definition definition,
        Definition sheet,
        int imageWidth,
        int imageHeight,
        int frameWidth,
        int frameHeight,
        int columns,
        int rows,
        int frameCount,
        IReadOnlyDictionary<char, TileGlyph> tiles)
    {
        Definition = definition;
        Sheet = sheet;
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        Columns = columns;
        Rows = rows;
        FrameCount = frameCount;
        Tiles = tiles;
    }

    public Definition Definition { get; }

    public Definition Sheet { get; }

    public int ImageWidth { get; }

    public int ImageHeight { get; }

    public int FrameWidth { get; }

    public int FrameHeight { get; }

    public int Columns { get; }

    public int Rows { get; }

    public int FrameCount { get; }

    public IReadOnlyDictionary<char, TileGlyph> Tiles { get; }
}

/// <summary>One resolved cell on one tilemap layer.</summary>
public readonly record struct ResolvedTile(
    char Glyph,
    int Frame,
    int Bitmask,
    string? AutotileMode,
    bool Passable,
    bool Door);

/// <summary>One resolved 2D layer of a compiled <see cref="ResolvedTilemap"/>.</summary>
public sealed class ResolvedTileLayer
{
    private readonly ResolvedTile[,] _cells;

    public ResolvedTileLayer(int width, int height, IReadOnlyList<string> rawRows, ResolvedTile[,] cells)
    {
        Width = width;
        Height = height;
        RawRows = rawRows;
        _cells = cells;
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<string> RawRows { get; }

    public ResolvedTile At(int x, int y) => _cells[x, y];
}

/// <summary>
/// A compiled, auto-tiled multi-layer 2D tilemap shared by Core validation,
/// <see cref="AreaMap"/> synthesis, Studio Workbench rendering, and Game meshes.
/// </summary>
public sealed class ResolvedTilemap
{
    public ResolvedTilemap(
        TilesetDefinition tileset,
        int width,
        int height,
        double? zoom,
        IReadOnlyList<ResolvedTileLayer> layers)
    {
        Tileset = tileset;
        Width = width;
        Height = height;
        Zoom = zoom;
        Layers = layers;
    }

    public TilesetDefinition Tileset { get; }

    public int Width { get; }

    public int Height { get; }

    public double? Zoom { get; }

    public IReadOnlyList<ResolvedTileLayer> Layers { get; }

    public bool IsPassable(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return false;
        }

        for (int i = 0; i < Layers.Count; i++)
        {
            ResolvedTile tile = Layers[i].At(x, y);
            if (tile.Frame >= 0 && !tile.Passable)
            {
                return false;
            }
        }

        return true;
    }

    public bool IsDoor(int x, int y)
    {
        if (!IsPassable(x, y))
        {
            return false;
        }

        for (int i = 0; i < Layers.Count; i++)
        {
            ResolvedTile tile = Layers[i].At(x, y);
            if (tile.Frame >= 0 && tile.Door)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Synthesizes an <see cref="AreaMap"/> from tile passability and door flags so
    /// tile-first overhead areas reuse Core movement, doors, minimap, saves, and BFS
    /// reachability without a parallel map topology system.
    /// </summary>
    public AreaMap SynthesizeAreaMap()
    {
        return AreaMap.FromPassability(Width, Height, IsPassable, IsDoor);
    }
}

/// <summary>
/// Pure-.NET deterministic auto-tiler supporting:
/// <list type="bullet">
/// <item><description><c>4bit</c>: 16 cardinal masks (N=1, E=2, S=4, W=8).</description></item>
/// <item><description><c>wall_top</c>: 3/4 Ultima/SKALD front wall vs roof/top cap.</description></item>
/// <item><description><c>blob47</c>: 47-tile corner-aware Wang blob bitmasking.</description></item>
/// <item><description>Coordinate-seeded deterministic <c>variants</c>.</description></item>
/// </list>
/// </summary>
public static class AutoTiler
{
    /// <summary>
    /// Maps each of the 47 valid 8-bit Wang blob masks (N=1, NE=2, E=4, SE=8, S=16, SW=32, W=64, NW=128,
    /// where diagonals are set only when both adjacent cardinals are set) to a canonical sequential index 0..46.
    /// </summary>
    public static IReadOnlyDictionary<int, int> Blob47Index { get; } = BuildBlob47Table();

    private static Dictionary<int, int> BuildBlob47Table()
    {
        int[] masks =
        [
            0, 1, 4, 5, 7, 16, 17, 20, 21, 23, 28, 29, 31, 64, 65, 68,
            69, 71, 80, 81, 84, 85, 87, 92, 93, 95, 112, 113, 116, 117, 119, 124,
            125, 127, 193, 197, 199, 209, 213, 215, 221, 223, 241, 245, 247, 253, 255,
        ];
        Dictionary<int, int> map = new(masks.Length);
        for (int i = 0; i < masks.Length; i++)
        {
            map[masks[i]] = i;
        }

        return map;
    }

    public static ResolvedTilemap Resolve(TilesetDefinition tileset, IReadOnlyList<IReadOnlyList<string>> layers, double? zoom)
    {
        int height = layers.Count > 0 ? layers[0].Count : 0;
        int width = height > 0 ? layers[0][0].Length : 0;
        List<ResolvedTileLayer> resolvedLayers = new(layers.Count);

        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            IReadOnlyList<string> rows = layers[layerIndex];
            ResolvedTile[,] cells = new ResolvedTile[width, height];
            for (int y = 0; y < height; y++)
            {
                string row = rows[y];
                for (int x = 0; x < width; x++)
                {
                    char glyph = row[x];
                    if (glyph == ' ' || !tileset.Tiles.TryGetValue(glyph, out TileGlyph? def))
                    {
                        cells[x, y] = new ResolvedTile(glyph, -1, 0, null, true, false);
                        continue;
                    }

                    (int frame, int bitmask) = ResolveCell(def, rows, width, height, x, y, layerIndex, tileset.FrameCount);
                    cells[x, y] = new ResolvedTile(glyph, frame, bitmask, def.Autotile?.Mode, def.Passable, def.Door);
                }
            }

            resolvedLayers.Add(new ResolvedTileLayer(width, height, rows.ToList(), cells));
        }

        return new ResolvedTilemap(tileset, width, height, zoom, resolvedLayers);
    }

    /// <summary>Computes the frame index and bitmask for a single cell given a 3×3 neighborhood predicate.</summary>
    public static (int Frame, int Bitmask) ResolveNeighborhood(
        TileGlyph def,
        bool n,
        bool ne,
        bool e,
        bool se,
        bool s,
        bool sw,
        bool w,
        bool nw,
        int x,
        int y,
        int layerIndex,
        int frameCount)
    {
        if (def.Autotile is TileAutotileRule rule)
        {
            if (rule.Mode == "4bit")
            {
                int mask = (n ? 1 : 0) | (e ? 2 : 0) | (s ? 4 : 0) | (w ? 8 : 0);
                int frame = rule.Frames.TryGetValue(mask.ToString(System.Globalization.CultureInfo.InvariantCulture), out int explicitFrame)
                    ? explicitFrame
                    : rule.BaseFrame is int baseIdx
                        ? baseIdx + mask
                        : def.Tile ?? 0;
                return (ClampFrame(frame, frameCount), mask);
            }

            if (rule.Mode == "wall_top")
            {
                // Bitmask encodes: bit 0 = N connected, bit 1 = S connected.
                int mask = (n ? 1 : 0) | (s ? 2 : 0);
                int frame;
                if (!s)
                {
                    frame = rule.Frames.TryGetValue("front", out int explicitFront)
                        ? explicitFront
                        : rule.BaseFrame ?? def.Tile ?? 0;
                }
                else if (!n && rule.Frames.TryGetValue("cap", out int explicitCap))
                {
                    frame = explicitCap;
                }
                else
                {
                    frame = rule.Frames.TryGetValue("top", out int explicitTop)
                        ? explicitTop
                        : rule.BaseFrame is int baseIdx
                            ? baseIdx + 1
                            : def.Tile ?? 0;
                }

                return (ClampFrame(frame, frameCount), mask);
            }

            if (rule.Mode == "blob47")
            {
                int mask = (n ? 1 : 0)
                    | (n && e && ne ? 2 : 0)
                    | (e ? 4 : 0)
                    | (s && e && se ? 8 : 0)
                    | (s ? 16 : 0)
                    | (s && w && sw ? 32 : 0)
                    | (w ? 64 : 0)
                    | (n && w && nw ? 128 : 0);
                int blobIdx = Blob47Index.TryGetValue(mask, out int idx) ? idx : 0;
                int frame;
                if (rule.Frames.TryGetValue(mask.ToString(System.Globalization.CultureInfo.InvariantCulture), out int byMask))
                {
                    frame = byMask;
                }
                else if (rule.Frames.TryGetValue($"b{blobIdx}", out int byBlobKey))
                {
                    frame = byBlobKey;
                }
                else if (rule.BaseFrame is int baseIdx)
                {
                    frame = baseIdx + blobIdx;
                }
                else
                {
                    frame = def.Tile ?? 0;
                }

                return (ClampFrame(frame, frameCount), mask);
            }
        }

        if (def.Variants.Count > 0)
        {
            uint hash = HashCell(x, y, layerIndex);
            int picked = def.Variants[(int)(hash % (uint)def.Variants.Count)];
            return (ClampFrame(picked, frameCount), 0);
        }

        return (ClampFrame(def.Tile ?? 0, frameCount), 0);
    }

    private static (int Frame, int Bitmask) ResolveCell(
        TileGlyph def,
        IReadOnlyList<string> rows,
        int width,
        int height,
        int x,
        int y,
        int layerIndex,
        int frameCount)
    {
        if (def.Autotile is not TileAutotileRule rule)
        {
            return ResolveNeighborhood(def, false, false, false, false, false, false, false, false, x, y, layerIndex, frameCount);
        }

        bool Matches(int nx, int ny)
        {
            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
            {
                return rule.OutOfBoundsSame;
            }

            char other = rows[ny][nx];
            return other == def.Glyph || rule.ConnectsTo.Contains(other);
        }

        return ResolveNeighborhood(
            def,
            n: Matches(x, y - 1),
            ne: Matches(x + 1, y - 1),
            e: Matches(x + 1, y),
            se: Matches(x + 1, y + 1),
            s: Matches(x, y + 1),
            sw: Matches(x - 1, y + 1),
            w: Matches(x - 1, y),
            nw: Matches(x - 1, y - 1),
            x,
            y,
            layerIndex,
            frameCount);
    }

    private static uint HashCell(int x, int y, int layer)
    {
        unchecked
        {
            uint h = ((uint)x * 374761393u) ^ ((uint)y * 668265263u) ^ ((uint)(layer + 1) * 1442695041u);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }

    private static int ClampFrame(int frame, int frameCount) =>
        frameCount <= 0 ? 0 : Math.Clamp(frame, 0, frameCount - 1);

    /// <summary>Reads an area's authored view mode (<c>"overhead"</c> or default <c>"first_person"</c>).</summary>
    public static string ViewOf(Definition area) =>
        area.Json.TryGetProperty("view", out JsonElement view) && view.ValueKind == JsonValueKind.String
            ? view.GetString()!
            : "first_person";

    /// <summary>Returns the canonical <see cref="AreaMap"/> for <paramref name="area"/>, parsing <c>map</c> or synthesizing from its tilemap.</summary>
    public static AreaMap MapOf(RuleSet rules, Definition area)
    {
        if (area.Json.TryGetProperty("map", out JsonElement mapEl))
        {
            List<string> rows = mapEl.EnumerateArray().Select(row => row.GetString()!).ToList();
            return AreaMap.Parse(rows, [])!;
        }

        if (rules.AreaTilemaps.TryGetValue(area, out ResolvedTilemap? tilemap))
        {
            return tilemap.SynthesizeAreaMap();
        }

        throw new InvalidOperationException($"Area '{area.QualifiedId}' has neither a map nor a compiled tilemap.");
    }
}
