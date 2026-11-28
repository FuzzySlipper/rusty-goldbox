using System.Text.Json;
using RustyTales.Core.Campaigns;
using RustyTales.Core.Combat;
using RustyTales.Core.Definitions;
using RustyTales.Core.Rules;

namespace RustyTales.Core.Grids;

/// <summary>Where in a campaign area a contextual combat fight broke out.</summary>
public readonly record struct CombatAreaOrigin(string AreaId, int X, int Y, Facing Facing);

/// <summary>Result of projecting a campaign area neighborhood onto a tactical combat field.</summary>
public sealed record ProjectedBattlefield(
    IReadOnlyDictionary<Cell, Terrain> Terrain,
    ResolvedTilemap? Tilemap);

/// <summary>
/// Ultima IV-style contextual battlefield projector: translates the local neighborhood
/// of any campaign area (either a 3D edge-wall <see cref="AreaMap"/> or a 2D overhead
/// <see cref="ResolvedTilemap"/>) onto a tactical <see cref="CombatField"/> while preserving
/// corridors, rooms, doorway chokepoints, and overhead terrain features.
/// </summary>
public static class AreaCombatProjector
{
    public const string OpenLayout = "open";
    public const string FromAreaLayout = "from_area";

    /// <summary>
    /// Resolves whether a fight should project its battlefield layout from the surrounding
    /// campaign area. Precedence: <c>encounter.layout</c> -> <c>campaign.combat_layout</c> -> <c>combat.layout</c> -> <c>"open"</c>.
    /// </summary>
    public static bool UsesAreaLayout(Definition? campaign, Definition combat, Definition encounter)
    {
        if (TryString(encounter.Json, "layout") is string encounterLayout)
        {
            return encounterLayout == FromAreaLayout;
        }

        // An encounter that explicitly authors its own terrain or tilemap keeps it unless it opts into from_area.
        if (encounter.Json.TryGetProperty("tilemap", out _) || encounter.Json.TryGetProperty("terrain", out _))
        {
            return false;
        }

        if (campaign is not null && TryString(campaign.Json, "combat_layout") is string campaignLayout)
        {
            return campaignLayout == FromAreaLayout;
        }

        return TryString(combat.Json, "layout") == FromAreaLayout;
    }

    /// <summary>
    /// Projects the local neighborhood of <paramref name="area"/> around (<paramref name="originX"/>, <paramref name="originY"/>)
    /// facing <paramref name="facing"/> onto a <paramref name="width"/> by <paramref name="height"/> combat field.
    /// </summary>
    public static ProjectedBattlefield Project(
        RuleSet rules,
        Definition area,
        AreaMap structuralMap,
        int originX,
        int originY,
        Facing facing,
        int width,
        int height)
    {
        if (AutoTiler.ViewOf(area) == "overhead" && rules.AreaTilemaps.TryGetValue(area, out ResolvedTilemap? areaTilemap))
        {
            return ProjectTilemapSlice(areaTilemap, originX, originY, width, height);
        }

        return ProjectDungeonArena(structuralMap, originX, originY, facing, width, height);
    }

    public static ProjectedBattlefield ProjectTilemapSlice(
        ResolvedTilemap source,
        int originX,
        int originY,
        int width,
        int height)
    {
        int startX = source.Width >= width
            ? Math.Clamp(originX - (width / 2), 0, source.Width - width)
            : originX - (width / 2);
        int startY = source.Height >= height
            ? Math.Clamp(originY - (height / 2), 0, source.Height - height)
            : originY - (height / 2);

        List<ResolvedTileLayer> layers = new(source.Layers.Count);
        Dictionary<Cell, Terrain> outOfBoundsTerrain = [];
        Terrain borderWall = new('#', "Area Border", Passable: false, Cost: 1, BlocksSight: true, FromTilemap: false);

        for (int layerIndex = 0; layerIndex < source.Layers.Count; layerIndex++)
        {
            ResolvedTileLayer srcLayer = source.Layers[layerIndex];
            ResolvedTile[,] cells = new ResolvedTile[width, height];
            List<string> rawRows = new(height);

            for (int y = 0; y < height; y++)
            {
                char[] rowChars = new char[width];
                int sy = startY + y;
                for (int x = 0; x < width; x++)
                {
                    int sx = startX + x;
                    if (sx >= 0 && sy >= 0 && sx < source.Width && sy < source.Height)
                    {
                        ResolvedTile tile = srcLayer.At(sx, sy);
                        cells[x, y] = tile;
                        rowChars[x] = tile.Glyph;
                    }
                    else
                    {
                        cells[x, y] = new ResolvedTile(' ', -1, 0, null, Passable: false, Door: false, MoveCost: 1, BlocksSight: true);
                        rowChars[x] = ' ';
                        if (layerIndex == 0)
                        {
                            outOfBoundsTerrain[new Cell(x, y)] = borderWall;
                        }
                    }
                }

                rawRows.Add(new string(rowChars));
            }

            layers.Add(new ResolvedTileLayer(width, height, rawRows, cells));
        }

        ResolvedTilemap sliced = new(source.Tileset, width, height, source.Zoom, layers);
        return new ProjectedBattlefield(outOfBoundsTerrain, sliced);
    }

    public static ProjectedBattlefield ProjectDungeonArena(
        AreaMap map,
        int originX,
        int originY,
        Facing facing,
        int width,
        int height)
    {
        Dictionary<Cell, Terrain> terrain = [];
        if (width < 5 || height < 4 || !map.Contains(originX, originY))
        {
            return new ProjectedBattlefield(terrain, Tilemap: null);
        }

        Terrain wall = new('#', "Dungeon Wall", Passable: false, Cost: 1, BlocksSight: true, FromTilemap: false);
        GridCell origin = new(originX, originY);
        (GridCell cellA, GridCell? cellB, Facing axisFacing) = SelectPair(map, origin, facing);

        Facing leftDir = Facings.Turn(axisFacing, -1);
        Facing rightDir = Facings.Turn(axisFacing, 1);
        int midX = width / 2;

        if (cellB is GridCell nextCell)
        {
            StampHorizontalWall(terrain, wall, map.EdgeOf(cellA.X, cellA.Y, leftDir), xStart: 0, xEndExclusive: midX, y: 0);
            StampHorizontalWall(terrain, wall, map.EdgeOf(cellA.X, cellA.Y, rightDir), xStart: 0, xEndExclusive: midX, y: height - 1);

            StampHorizontalWall(terrain, wall, map.EdgeOf(nextCell.X, nextCell.Y, leftDir), xStart: midX + 1, xEndExclusive: width, y: 0);
            StampHorizontalWall(terrain, wall, map.EdgeOf(nextCell.X, nextCell.Y, rightDir), xStart: midX + 1, xEndExclusive: width, y: height - 1);

            Edge shared = map.EdgeOf(cellA.X, cellA.Y, axisFacing);
            if (shared == Edge.Door)
            {
                int doorY = (height - 1) / 2;
                for (int y = 0; y < height; y++)
                {
                    if (y != doorY)
                    {
                        terrain[new Cell(midX, y)] = wall;
                    }
                }
            }
            else
            {
                if (map.EdgeOf(cellA.X, cellA.Y, leftDir) != Edge.Open || map.EdgeOf(nextCell.X, nextCell.Y, leftDir) != Edge.Open)
                {
                    terrain[new Cell(midX, 0)] = wall;
                }

                if (map.EdgeOf(cellA.X, cellA.Y, rightDir) != Edge.Open || map.EdgeOf(nextCell.X, nextCell.Y, rightDir) != Edge.Open)
                {
                    terrain[new Cell(midX, height - 1)] = wall;
                }
            }
        }
        else
        {
            // Single enclosed cell: project its side walls along top and bottom while keeping the center open for combat.
            StampHorizontalWall(terrain, wall, map.EdgeOf(cellA.X, cellA.Y, leftDir), xStart: 0, xEndExclusive: width, y: 0);
            StampHorizontalWall(terrain, wall, map.EdgeOf(cellA.X, cellA.Y, rightDir), xStart: 0, xEndExclusive: width, y: height - 1);
        }

        return new ProjectedBattlefield(terrain, Tilemap: null);
    }

    private static (GridCell A, GridCell? B, Facing Axis) SelectPair(AreaMap map, GridCell origin, Facing facing)
    {
        Facing back = Facings.Turn(facing, 2);
        Facing left = Facings.Turn(facing, -1);
        Facing right = Facings.Turn(facing, 1);

        GridCell? StepIfEdge(Facing dir, Edge wanted)
        {
            if (map.EdgeOf(origin.X, origin.Y, dir) != wanted)
            {
                return null;
            }

            (int dx, int dy) = Facings.Step(dir);
            GridCell next = new(origin.X + dx, origin.Y + dy);
            return map.Contains(next) ? next : null;
        }

        // Prioritize doorways directly ahead or just crossed behind the party so doorway chokepoints always shine.
        if (StepIfEdge(facing, Edge.Door) is GridCell aheadDoor)
        {
            return (origin, aheadDoor, facing);
        }

        if (StepIfEdge(back, Edge.Door) is GridCell behindDoor)
        {
            return (behindDoor, origin, facing);
        }

        if (StepIfEdge(facing, Edge.Open) is GridCell aheadOpen)
        {
            return (origin, aheadOpen, facing);
        }

        if (StepIfEdge(back, Edge.Open) is GridCell behindOpen)
        {
            return (behindOpen, origin, facing);
        }

        foreach (Facing side in new[] { left, right })
        {
            if (StepIfEdge(side, Edge.Door) is GridCell sideDoor)
            {
                return (origin, sideDoor, side);
            }
        }

        foreach (Facing side in new[] { left, right })
        {
            if (StepIfEdge(side, Edge.Open) is GridCell sideOpen)
            {
                return (origin, sideOpen, side);
            }
        }

        return (origin, null, facing);
    }

    private static void StampHorizontalWall(
        Dictionary<Cell, Terrain> terrain,
        Terrain wall,
        Edge edge,
        int xStart,
        int xEndExclusive,
        int y)
    {
        if (edge == Edge.Open)
        {
            return;
        }

        int doorX = edge == Edge.Door ? xStart + ((xEndExclusive - xStart) / 2) : -1;
        for (int x = xStart; x < xEndExclusive; x++)
        {
            if (x != doorX)
            {
                terrain[new Cell(x, y)] = wall;
            }
        }
    }

    private static string? TryString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
