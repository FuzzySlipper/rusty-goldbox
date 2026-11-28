using System.Numerics;
using RustyTales.Core.Campaigns;
using RustyTales.Core.Definitions;

namespace RustyTales.Game.Presentation;

/// <summary>A wall set's frames as texture coordinates: left, top, right, bottom (V runs down the image).</summary>
internal readonly record struct UvRect(float U0, float V0, float U1, float V1);

/// <summary>Generated geometry for one area: one mesh group per material slot.</summary>
internal sealed record AreaGeometry(Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, uint[] Indices, IReadOnlyList<(uint Slot, uint Start, uint Count)> Groups);

/// <summary>
/// Turns an area's edge-wall map into first-person geometry. Cell (x, y) is
/// the unit square from (x, 0, y) to (x + 1, 1, y + 1): X runs east, Z south
/// and Y up, so north is -Z as the Engine camera's zero yaw. Every wall, door
/// or secret door on a cell edge is extruded to thickness <c>2 * WallHalfThickness</c>
/// (inset <see cref="WallHalfThickness"/> into each adjacent cell), and grid
/// vertices touching any edge get matching square corner posts so outer corners,
/// doorway jambs and standalone edges form watertight solids from every angle.
/// Each cell has a floor and a ceiling.
/// </summary>
internal static class AreaMesh
{
    /// <summary>Half the thickness of a wall slab, in cells (full thickness is 1/8 of a cell).</summary>
    public const float WallHalfThickness = 0.0625f;

    /// <summary>Faces drawn with the wall set's texture.</summary>
    public const uint TexturedSlot = 0;

    /// <summary>Floors and ceilings a wall set has no frame for.</summary>
    public const uint PlainSlot = 1;

    /// <param name="frames">The wall set's frames by name; wall and door are always there.</param>
    public static AreaGeometry Build(AreaMap map, IReadOnlyDictionary<string, UvRect> frames)
    {
        Builder textured = new();
        Builder plain = new();
        UvRect wallFrame = frames["wall"];
        UvRect doorFrame = frames["door"];
        float h = WallHalfThickness;

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                // PlayerMap keeps undiscovered secret edges as Secret (wall); found ones become Door.
                Edge north = map.EdgeOf(x, y, Facing.North);
                Edge east = map.EdgeOf(x, y, Facing.East);
                Edge south = map.EdgeOf(x, y, Facing.South);
                Edge west = map.EdgeOf(x, y, Facing.West);
                bool n = north != Edge.Open;
                bool e = east != Edge.Open;
                bool s = south != Edge.Open;
                bool w = west != Edge.Open;

                if (n)
                {
                    textured.Wall(x, y, Facing.North, w ? h : 0, e ? 1 - h : 1, h, north == Edge.Door ? doorFrame : wallFrame);
                }

                if (e)
                {
                    textured.Wall(x, y, Facing.East, n ? h : 0, s ? 1 - h : 1, h, east == Edge.Door ? doorFrame : wallFrame);
                }

                if (s)
                {
                    textured.Wall(x, y, Facing.South, e ? h : 0, w ? 1 - h : 1, h, south == Edge.Door ? doorFrame : wallFrame);
                }

                if (w)
                {
                    textured.Wall(x, y, Facing.West, s ? h : 0, n ? 1 - h : 1, h, west == Edge.Door ? doorFrame : wallFrame);
                }

                // Protruding quarter-posts at open cell corners whose grid vertex touches a wall or door in a neighbour.
                if (!n && !w && HasEdgeAtCorner(map, x, y))
                {
                    textured.Wall(x, y, Facing.North, 0, h, h, wallFrame);
                    textured.Wall(x, y, Facing.West, 1 - h, 1, h, wallFrame);
                }

                if (!n && !e && HasEdgeAtCorner(map, x + 1, y))
                {
                    textured.Wall(x, y, Facing.North, 1 - h, 1, h, wallFrame);
                    textured.Wall(x, y, Facing.East, 0, h, h, wallFrame);
                }

                if (!s && !e && HasEdgeAtCorner(map, x + 1, y + 1))
                {
                    textured.Wall(x, y, Facing.South, 0, h, h, wallFrame);
                    textured.Wall(x, y, Facing.East, 1 - h, 1, h, wallFrame);
                }

                if (!s && !w && HasEdgeAtCorner(map, x, y + 1))
                {
                    textured.Wall(x, y, Facing.South, 1 - h, 1, h, wallFrame);
                    textured.Wall(x, y, Facing.West, 0, h, h, wallFrame);
                }

                (frames.TryGetValue("floor", out UvRect floor) ? textured : plain).Flat(x, y, 0, Vector3.UnitY, floor);
                (frames.TryGetValue("ceiling", out UvRect ceiling) ? textured : plain).Flat(x, y, 1, -Vector3.UnitY, ceiling);
            }
        }

        return Builder.Join(textured, plain);
    }

    /// <summary>Whether any edge meeting grid vertex (<paramref name="vx"/>, <paramref name="vy"/>) is not open.</summary>
    private static bool HasEdgeAtCorner(AreaMap map, int vx, int vy)
    {
        if (vx > 0)
        {
            Edge westEdge = vy == map.Height ? map.EdgeOf(vx - 1, map.Height - 1, Facing.South) : map.EdgeOf(vx - 1, vy, Facing.North);
            if (westEdge != Edge.Open)
            {
                return true;
            }
        }

        if (vx < map.Width)
        {
            Edge eastEdge = vy == map.Height ? map.EdgeOf(vx, map.Height - 1, Facing.South) : map.EdgeOf(vx, vy, Facing.North);
            if (eastEdge != Edge.Open)
            {
                return true;
            }
        }

        if (vy > 0)
        {
            Edge northEdge = vx == map.Width ? map.EdgeOf(map.Width - 1, vy - 1, Facing.East) : map.EdgeOf(vx, vy - 1, Facing.West);
            if (northEdge != Edge.Open)
            {
                return true;
            }
        }

        if (vy < map.Height)
        {
            Edge southEdge = vx == map.Width ? map.EdgeOf(map.Width - 1, vy, Facing.East) : map.EdgeOf(vx, vy, Facing.West);
            if (southEdge != Edge.Open)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A flat field of <paramref name="width"/> by <paramref name="depth"/>
    /// cells from the origin, each textured with <paramref name="floor"/> or
    /// plain without one: the floor combat stands on.
    /// </summary>
    public static AreaGeometry Floor(int width, int depth, UvRect? floor)
    {
        Builder textured = new();
        Builder plain = new();
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                (floor is null ? plain : textured).Flat(x, z, 0, Vector3.UnitY, floor ?? default);
            }
        }

        return Builder.Join(textured, plain);
    }

    /// <summary>
    /// Builds a single batched multi-layer 2D tilemap mesh from a compiled
    /// <see cref="ResolvedTilemap"/>: ground layer 0 sits at y = 0 and each
    /// transparent overlay layer sits slightly above it (y = layer * 0.01f).
    /// </summary>
    public static AreaGeometry Tilemap(ResolvedTilemap tilemap, Definition sheetAsset, (int Width, int Height) sheetSize)
    {
        Builder textured = new();
        Builder plain = new();
        TilesetDefinition ts = tilemap.Tileset;
        for (int layerIndex = 0; layerIndex < tilemap.Layers.Count; layerIndex++)
        {
            ResolvedTileLayer layer = tilemap.Layers[layerIndex];
            float y = layerIndex * 0.01f;
            for (int z = 0; z < tilemap.Height; z++)
            {
                for (int x = 0; x < tilemap.Width; x++)
                {
                    int frame = layer.At(x, z).Frame;
                    if (frame < 0)
                    {
                        continue;
                    }

                    int col = frame % ts.Columns;
                    int row = frame / ts.Columns;
                    UvRect uv = TextureSampling.Region(
                        sheetAsset,
                        col * ts.FrameWidth,
                        row * ts.FrameHeight,
                        ts.FrameWidth,
                        ts.FrameHeight,
                        sheetSize);
                    textured.Flat(x, z, y, Vector3.UnitY, uv);
                }
            }
        }

        return Builder.Join(textured, plain);
    }

    /// <summary>
    /// Low perimeter walls framing a top-down orthographic tactical chamber:
    /// a full-height north wall (showing the dungeon wall stonework) plus low
    /// west and east rim walls with dark stone top caps so the room reads like
    /// a Gold Box / SKALD chamber without hiding front-row figures.
    /// </summary>
    public static AreaGeometry TacticalRoomWalls(int width, int depth, UvRect? wall, UvRect? cap)
    {
        Builder textured = new();
        Builder plain = new();
        Builder wallTarget = wall is null ? plain : textured;
        Builder capTarget = cap is null ? plain : textured;
        UvRect wallUv = wall ?? default;
        UvRect capUv = cap ?? default;
        const float northHeight = 1.0f;
        const float sideHeight = 0.45f;
        const float rim = 0.35f;

        // North wall face (+Z) and top cap (+Y)
        for (int x = 0; x < width; x++)
        {
            wallTarget.VerticalQuad(x, x + 1, 0, northHeight, 0f, Vector3.UnitZ, wallUv.U0, wallUv.V0, wallUv.U1, wallUv.V1);
            capTarget.FlatRect(x, x + 1, -rim, 0f, northHeight, Vector3.UnitY, capUv);
        }

        // Corner caps at NW and NE, plus vertical stonework risers down to the side walls so no corner notch shows.
        capTarget.FlatRect(-rim, 0f, -rim, 0f, northHeight, Vector3.UnitY, capUv);
        capTarget.FlatRect(width, width + rim, -rim, 0f, northHeight, Vector3.UnitY, capUv);
        float uRim = wallUv.U0 + ((wallUv.U1 - wallUv.U0) * rim);
        float vRiserBottom = wallUv.V0 + ((wallUv.V1 - wallUv.V0) * (1f - sideHeight));
        wallTarget.VerticalQuad(-rim, 0f, sideHeight, northHeight, 0f, Vector3.UnitZ, wallUv.U0, wallUv.V0, uRim, vRiserBottom);
        wallTarget.VerticalQuad(width, width + rim, sideHeight, northHeight, 0f, Vector3.UnitZ, wallUv.U0, wallUv.V0, uRim, vRiserBottom);

        // West and east low side walls (inward face + top cap)
        float vSideTop = wallUv.V0 + ((wallUv.V1 - wallUv.V0) * (1f - sideHeight));
        for (int z = 0; z < depth; z++)
        {
            wallTarget.SideQuad(0f, z, z + 1, 0f, sideHeight, Vector3.UnitX, wallUv.U0, vSideTop, wallUv.U1, wallUv.V1);
            wallTarget.SideQuad(width, z, z + 1, 0f, sideHeight, -Vector3.UnitX, wallUv.U0, vSideTop, wallUv.U1, wallUv.V1);
            capTarget.FlatRect(-rim, 0f, z, z + 1, sideHeight, Vector3.UnitY, capUv);
            capTarget.FlatRect(width, width + rim, z, z + 1, sideHeight, Vector3.UnitY, capUv);
        }

        return Builder.Join(textured, plain);
    }

    /// <summary>
    /// A vertical stage backdrop quad at <paramref name="z"/> facing +Z, either
    /// tiled cell-by-cell (for dungeon wall sets) or stretched across one full
    /// scene picture for first-person and side-view combat modes.
    /// </summary>
    public static AreaGeometry BackdropWall(int width, float height, float z, UvRect? frame, bool tileHorizontally)
    {
        Builder textured = new();
        Builder plain = new();
        Builder target = frame is null ? plain : textured;
        UvRect uv = frame ?? default;
        if (tileHorizontally)
        {
            int rows = Math.Max(1, (int)MathF.Ceiling(height));
            for (int row = 0; row < rows; row++)
            {
                float y0 = row;
                float y1 = MathF.Min(height, row + 1f);
                float vTop = uv.V0 + ((uv.V1 - uv.V0) * (1f - (y1 - y0)));
                for (int x = 0; x < width; x++)
                {
                    target.VerticalQuad(x, x + 1, y0, y1, z, Vector3.UnitZ, uv.U0, vTop, uv.U1, uv.V1);
                }
            }
        }
        else
        {
            target.VerticalQuad(0, width, 0, height, z, Vector3.UnitZ, uv.U0, uv.V0, uv.U1, uv.V1);
        }

        return Builder.Join(textured, plain);
    }

    private sealed class Builder
    {
        public List<Vector3> Positions { get; } = [];

        public List<Vector3> Normals { get; } = [];

        public List<Vector2> Uvs { get; } = [];

        public List<uint> Indices { get; } = [];

        /// <summary>
        /// A wall segment on one side of a cell, inset <paramref name="inset"/> into the cell and spanning
        /// <paramref name="startT"/>..<paramref name="endT"/> from left to right as seen facing that side.
        /// </summary>
        public void Wall(int x, int y, Facing side, float startT, float endT, float inset, UvRect frame)
        {
            (Vector2 left, Vector2 right, Vector3 normal) = side switch
            {
                Facing.North => (new Vector2(x + startT, y + inset), new Vector2(x + endT, y + inset), Vector3.UnitZ),
                Facing.East => (new Vector2(x + 1 - inset, y + startT), new Vector2(x + 1 - inset, y + endT), -Vector3.UnitX),
                Facing.South => (new Vector2(x + 1 - startT, y + 1 - inset), new Vector2(x + 1 - endT, y + 1 - inset), -Vector3.UnitZ),
                _ => (new Vector2(x + inset, y + 1 - startT), new Vector2(x + inset, y + 1 - endT), Vector3.UnitX),
            };
            float u0 = frame.U0 + ((frame.U1 - frame.U0) * startT);
            float u1 = frame.U0 + ((frame.U1 - frame.U0) * endT);
            Quad(
                [new(left.X, 1, left.Y), new(right.X, 1, right.Y), new(right.X, 0, right.Y), new(left.X, 0, left.Y)],
                [new(u0, frame.V0), new(u1, frame.V0), new(u1, frame.V1), new(u0, frame.V1)],
                normal);
        }

        /// <summary>A floor (facing up) or ceiling (facing down) over the whole cell.</summary>
        public void Flat(int x, int y, float height, Vector3 normal, UvRect frame) => FlatRect(x, x + 1, y, y + 1, height, normal, frame);

        /// <summary>A horizontal rectangle spanning x0..x1 and z0..z1 at <paramref name="height"/>.</summary>
        public void FlatRect(float x0, float x1, float z0, float z1, float height, Vector3 normal, UvRect frame)
        {
            Quad(
                [new(x0, height, z0), new(x1, height, z0), new(x1, height, z1), new(x0, height, z1)],
                [new(frame.U0, frame.V0), new(frame.U1, frame.V0), new(frame.U1, frame.V1), new(frame.U0, frame.V1)],
                normal);
        }

        /// <summary>A vertical quad at fixed <paramref name="z"/> spanning x0..x1 and y0..y1 facing <paramref name="normal"/>.</summary>
        public void VerticalQuad(float x0, float x1, float y0, float y1, float z, Vector3 normal, float u0, float v0, float u1, float v1)
        {
            Quad(
                [new(x0, y1, z), new(x1, y1, z), new(x1, y0, z), new(x0, y0, z)],
                [new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1)],
                normal);
        }

        /// <summary>A vertical quad at fixed <paramref name="x"/> spanning z0..z1 and y0..y1 facing <paramref name="normal"/>.</summary>
        public void SideQuad(float x, float z0, float z1, float y0, float y1, Vector3 normal, float u0, float v0, float u1, float v1)
        {
            Quad(
                [new(x, y1, z0), new(x, y1, z1), new(x, y0, z1), new(x, y0, z0)],
                [new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1)],
                normal);
        }

        /// <summary>Two triangles over four corners in order, wound so the front faces <paramref name="normal"/>.</summary>
        private void Quad(Vector3[] corners, Vector2[] uvs, Vector3 normal)
        {
            uint start = (uint)Positions.Count;
            Positions.AddRange(corners);
            Uvs.AddRange(uvs);
            Normals.AddRange(Enumerable.Repeat(normal, 4));

            // The Engine draws a triangle (a, b, c) from the side cross(b - a, c - a) points to.
            bool forward = Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]), normal) > 0;
            Indices.AddRange(forward
                ? [start, start + 1, start + 2, start, start + 2, start + 3]
                : [start, start + 2, start + 1, start, start + 3, start + 2]);
        }

        public static AreaGeometry Join(Builder textured, Builder plain)
        {
            List<(uint, uint, uint)> groups = [];
            if (textured.Indices.Count > 0)
            {
                groups.Add((TexturedSlot, 0, (uint)textured.Indices.Count));
            }

            uint offset = (uint)textured.Positions.Count;
            if (plain.Indices.Count > 0)
            {
                groups.Add((PlainSlot, (uint)textured.Indices.Count, (uint)plain.Indices.Count));
            }

            return new AreaGeometry(
                [.. textured.Positions, .. plain.Positions],
                [.. textured.Normals, .. plain.Normals],
                [.. textured.Uvs, .. plain.Uvs],
                [.. textured.Indices, .. plain.Indices.Select(index => index + offset)],
                groups);
        }
    }
}
