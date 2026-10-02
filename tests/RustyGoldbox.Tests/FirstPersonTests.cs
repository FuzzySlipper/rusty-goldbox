using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Game;
using RustyGoldbox.Game.Presentation;

namespace RustyGoldbox.Tests;

/// <summary>The first-person view's geometry and its Engine calls.</summary>
public sealed class FirstPersonTests
{
    private static readonly Dictionary<string, UvRect> Frames = new()
    {
        ["wall"] = new UvRect(0, 0, 0.25f, 1),
        ["door"] = new UvRect(0.25f, 0, 0.5f, 1),
    };

    [Fact]
    public void EveryWallFacesIntoItsCellAndOpenEdgesHaveNone()
    {
        // Two cells side by side with a door between them; the outside is wall.
        AreaMap map = AreaMap.Parse(["+--+--+", "|  D  |", "+--+--+"], [])!;

        AreaGeometry geometry = AreaMesh.Build(map, Frames);

        // Per cell: three walls and a door face (8 wall quads), plus floor and ceiling.
        List<(Vector3 Normal, Vector3 Centre, float U)> walls = Quads(geometry).Where(quad => quad.Normal.Y == 0).ToList();
        Assert.Equal(8, walls.Count);
        foreach ((Vector3 normal, Vector3 centre, _) in walls)
        {
            // Stepping along the normal from the face lands inside the map, in the cell it belongs to.
            Vector3 inside = centre + (normal * 0.25f);
            Assert.InRange(inside.X, 0, 2);
            Assert.InRange(inside.Z, 0, 1);
        }

        Assert.Equal(2, walls.Count(quad => quad.U >= 0.25f));
        Assert.Equal([(AreaMesh.TexturedSlot, 0u, 48u), (AreaMesh.PlainSlot, 48u, 24u)], geometry.Groups);
    }

    [Fact]
    public void TrianglesAreWoundTowardTheirNormals()
    {
        AreaMap map = AreaMap.Parse(["+--+", "|  |", "+--+"], [])!;
        AreaGeometry geometry = AreaMesh.Build(map, new Dictionary<string, UvRect>(Frames) { ["floor"] = new(0.5f, 0, 0.75f, 1), ["ceiling"] = new(0.75f, 0, 1, 1) });

        Assert.Equal(AreaMesh.TexturedSlot, Assert.Single(geometry.Groups).Slot);
        for (int i = 0; i < geometry.Indices.Length; i += 3)
        {
            Vector3 a = geometry.Positions[geometry.Indices[i]];
            Vector3 b = geometry.Positions[geometry.Indices[i + 1]];
            Vector3 c = geometry.Positions[geometry.Indices[i + 2]];
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), geometry.Normals[geometry.Indices[i]]) > 0, $"triangle {i / 3}");
        }
    }

    [Fact]
    public void TheViewBuildsThroughTheEngine()
    {
        using TempModules scratch = new();
        List<string> containers = new[] { "classic", "placeholder-art", "sample-crypt" }
            .Select(id => EngineContentTests.Pack(Path.Combine(Rules.RepositoryRoot, "modules", id), scratch))
            .ToList();
        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            ModuleLibrary library = new(_ => containers.Select(path => ProductContentBundle.OpenContainer(engine.Content, path)).ToList());
            GameSession session = new(library);
            session.Refresh();
            GameCommands.Run(session, engine, JsonDocument.Parse(JsonSerializer.Serialize(new { action = "open", campaign = session.Campaigns[0].Bundle, seed = "3" })).RootElement);
            for (int attempt = 0; attempt < 50 && session.Party.Count == 0; attempt++)
            {
                session.Roll(engine, "Ada", "classic:human", "classic:fighter");
            }

            session.Begin(engine);
            using SceneView view = new(engine, library);

            // The start cell, then the cell with a backdrop, then the second area.
            view.Show(session);
            session.Execute(engine, "forward");
            session.Execute(engine, "forward");
            view.Show(session);
            session.Quit();
            view.Show(session);
        });
    }

    /// <summary>Each quad's normal, centre and smallest U.</summary>
    private static IEnumerable<(Vector3 Normal, Vector3 Centre, float U)> Quads(AreaGeometry geometry)
    {
        for (int start = 0; start < geometry.Positions.Length; start += 4)
        {
            Vector3 centre = (geometry.Positions[start] + geometry.Positions[start + 1] + geometry.Positions[start + 2] + geometry.Positions[start + 3]) / 4;
            yield return (geometry.Normals[start], centre, geometry.Uvs.Skip(start).Take(4).Min(uv => uv.X));
        }
    }
}
