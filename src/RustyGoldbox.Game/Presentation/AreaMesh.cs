using System.Numerics;
using RustyGoldbox.Core.Campaigns;

namespace RustyGoldbox.Game.Presentation;

/// <summary>A wall set's frames as texture coordinates: left, top, right, bottom (V runs down the image).</summary>
internal readonly record struct UvRect(float U0, float V0, float U1, float V1);

/// <summary>Generated geometry for one area: one mesh group per material slot.</summary>
internal sealed record AreaGeometry(Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, uint[] Indices, IReadOnlyList<(uint Slot, uint Start, uint Count)> Groups);

/// <summary>
/// Turns an area's edge-wall map into first-person geometry. Cell (x, y) is
/// the unit square from (x, 0, y) to (x + 1, 1, y + 1): X runs east, Z south
/// and Y up, so north is -Z as the Engine camera's zero yaw. Every wall, door
/// or secret door on a cell edge is a quad facing into that cell, so an edge
/// between two cells has one face for each and back faces are never drawn.
/// Each cell has a floor and a ceiling.
/// </summary>
internal static class AreaMesh
{
    /// <summary>Faces drawn with the wall set's texture.</summary>
    public const uint TexturedSlot = 0;

    /// <summary>Floors and ceilings a wall set has no frame for.</summary>
    public const uint PlainSlot = 1;

    /// <param name="frames">The wall set's frames by name; wall and door are always there.</param>
    public static AreaGeometry Build(AreaMap map, IReadOnlyDictionary<string, UvRect> frames)
    {
        Builder textured = new();
        Builder plain = new();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                foreach (Facing side in Enum.GetValues<Facing>())
                {
                    // A secret door looks like wall to the party.
                    Edge edge = map.EdgeOf(x, y, side);
                    if (edge != Edge.Open)
                    {
                        textured.Wall(x, y, side, frames[edge == Edge.Door ? "door" : "wall"]);
                    }
                }

                (frames.TryGetValue("floor", out UvRect floor) ? textured : plain).Flat(x, y, 0, Vector3.UnitY, floor);
                (frames.TryGetValue("ceiling", out UvRect ceiling) ? textured : plain).Flat(x, y, 1, -Vector3.UnitY, ceiling);
            }
        }

        return Builder.Join(textured, plain);
    }

    private sealed class Builder
    {
        public List<Vector3> Positions { get; } = [];

        public List<Vector3> Normals { get; } = [];

        public List<Vector2> Uvs { get; } = [];

        public List<uint> Indices { get; } = [];

        /// <summary>A wall on one side of a cell, seen from inside the cell with the frame upright.</summary>
        public void Wall(int x, int y, Facing side, UvRect frame)
        {
            // The wall's left and right ends as the party sees them facing that side.
            (Vector2 left, Vector2 right, Vector3 normal) = side switch
            {
                Facing.North => (new Vector2(x, y), new Vector2(x + 1, y), Vector3.UnitZ),
                Facing.East => (new Vector2(x + 1, y), new Vector2(x + 1, y + 1), -Vector3.UnitX),
                Facing.South => (new Vector2(x + 1, y + 1), new Vector2(x, y + 1), -Vector3.UnitZ),
                _ => (new Vector2(x, y + 1), new Vector2(x, y), Vector3.UnitX),
            };
            Quad(
                [new(left.X, 1, left.Y), new(right.X, 1, right.Y), new(right.X, 0, right.Y), new(left.X, 0, left.Y)],
                [new(frame.U0, frame.V0), new(frame.U1, frame.V0), new(frame.U1, frame.V1), new(frame.U0, frame.V1)],
                normal);
        }

        /// <summary>A floor (facing up) or ceiling (facing down) over the whole cell.</summary>
        public void Flat(int x, int y, float height, Vector3 normal, UvRect frame)
        {
            Quad(
                [new(x, height, y), new(x + 1, height, y), new(x + 1, height, y + 1), new(x, height, y + 1)],
                [new(frame.U0, frame.V0), new(frame.U1, frame.V0), new(frame.U1, frame.V1), new(frame.U0, frame.V1)],
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
