using System.Text.Json;
using RustyTales.Core.Campaigns;
using RustyTales.Core.Combat;
using RustyTales.Core.Definitions;
using RustyTales.Core.Grids;
using RustyTales.Core.Modules;
using RustyTales.Core.Rules;
using Xunit;

namespace RustyTales.Tests;

public sealed class GridTopologyTests
{
    [Fact]
    public void AreaMapImplementsCanonicalGridTopologyWithEdgeSteppingAndLineOfSight()
    {
        // 3x2 dungeon with a dividing wall between x=0 and x=1 except a doorway at y=1,
        // and an open chamber across x=1..2.
        List<(int, int, string)> problems = [];
        AreaMap? map = AreaMap.Parse(
        [
            "+--+--+--+",
            "|  |     |",
            "+  +  +  +",
            "|  D     |",
            "+--+--+--+",
        ],
        problems);
        Assert.NotNull(map);
        Assert.Empty(problems);

        IGridTopology grid = map!;
        Assert.Equal(3, grid.Width);
        Assert.Equal(2, grid.Height);
        Assert.False(grid.AllowsDiagonal);

        // Edge wall blocks step and sight between (0,0) and (1,0).
        Assert.False(grid.CanStep(new GridCell(0, 0), new GridCell(1, 0)));
        Assert.True(grid.BlocksSightBetween(new GridCell(0, 0), new GridCell(1, 0)));
        Assert.False(GridSearch.CanSee(grid, new GridCell(0, 0), new GridCell(1, 0)));
        Assert.False(GridSearch.CanSee(grid, new GridCell(0, 0), new GridCell(2, 0)));

        // Doorway at (0,1)->(1,1) permits stepping, blocks sight while closed, and opens sight once opened.
        Assert.True(grid.CanStep(new GridCell(0, 1), new GridCell(1, 1)));
        Assert.True(grid.BlocksSightBetween(new GridCell(0, 1), new GridCell(1, 1)));
        Assert.False(GridSearch.CanSee(grid, new GridCell(0, 1), new GridCell(2, 1)));

        AreaMap opened = map.WithEdges((_, _, _, edge) => edge == Edge.Door ? Edge.Open : edge);
        Assert.True(GridSearch.CanSee(opened, new GridCell(0, 1), new GridCell(2, 1)));

        // Flood fill & shortest path navigate south then through the doorway to reach (2,0).
        HashSet<GridCell> reachable = GridSearch.FloodFill(grid, new GridCell(0, 0));
        Assert.Equal(6, reachable.Count);

        Dictionary<GridCell, (int Cost, IReadOnlyList<GridCell> Path)> paths = GridSearch.ReachableWithin(grid, new GridCell(0, 0), budget: 10);
        Assert.Equal(4, paths[new GridCell(2, 0)].Cost);
        Assert.Equal(new GridCell(0, 1), paths[new GridCell(2, 0)].Path[0]);
        Assert.Equal(new GridCell(1, 1), paths[new GridCell(2, 0)].Path[1]);
    }

    [Fact]
    public void ResolvedTilemapImplementsCanonicalGridTopologyWithMultiLayerTraits()
    {
        Definition dummySheet = new(DefinitionTypes.Asset, "sheet", "test", "test.json", JsonDocument.Parse("""{"type":"asset","id":"sheet","media":"sheet","file":"t.png","frame_size":[16,16]}""").RootElement.Clone(), [], [], "$");
        Definition dummyTileset = new(DefinitionTypes.Tileset, "ts", "test", "test.json", JsonDocument.Parse("""{"type":"tileset","id":"ts","name":"Test","sheet":"sheet","tiles":{}}""").RootElement.Clone(), [], [], "$");
        Dictionary<char, TileGlyph> tiles = new()
        {
            ['g'] = new TileGlyph('g', "Grass", 0, [], null, Passable: true, Door: false, Color: null, MoveCost: 1, BlocksSight: false),
            ['m'] = new TileGlyph('m', "Mud", 1, [], null, Passable: true, Door: false, Color: null, MoveCost: 2, BlocksSight: false),
            ['~'] = new TileGlyph('~', "River", 2, [], null, Passable: false, Door: false, Color: null, MoveCost: 1, BlocksSight: false),
            ['T'] = new TileGlyph('T', "Pine Tree", 3, [], null, Passable: false, Door: false, Color: null, MoveCost: 1, BlocksSight: true),
        };
        TilesetDefinition tileset = new(dummyTileset, dummySheet, 64, 16, 16, 16, 4, 1, 4, tiles);

        ResolvedTilemap tm = AutoTiler.Resolve(
            tileset,
            [
                ["gg~gg", "gm~mg", "ggggg"],
                ["  T  ", "     ", "     "],
            ],
            zoom: null);

        IGridTopology grid = tm;
        // Overlay pine at (2,0) over river makes it impassable AND sight-blocking.
        CellTraits pineOverRiver = grid.TraitsAt(new GridCell(2, 0));
        Assert.False(pineOverRiver.Passable);
        Assert.True(pineOverRiver.BlocksSight);
        Assert.Equal("Pine Tree", pineOverRiver.Label);

        // River alone at (2,1) blocks movement but NOT sight!
        CellTraits river = grid.TraitsAt(new GridCell(2, 1));
        Assert.False(river.Passable);
        Assert.False(river.BlocksSight);
        Assert.Equal("River", river.Label);

        Assert.True(GridSearch.CanSee(grid, new GridCell(0, 1), new GridCell(4, 1)));
        Assert.False(GridSearch.CanSee(grid, new GridCell(0, 0), new GridCell(4, 0)));

        // Mud costs 2 to enter, so pathfinding prefers open grass around the river.
        Assert.Equal(2, grid.TraitsAt(new GridCell(1, 1)).MoveCost);
        Dictionary<GridCell, (int Cost, IReadOnlyList<GridCell> Path)> paths = GridSearch.ReachableWithin(grid, new GridCell(0, 0), budget: 10);
        Assert.False(paths.ContainsKey(new GridCell(2, 1)));
        Assert.True(paths.ContainsKey(new GridCell(4, 0)));
    }

    [Fact]
    public void EncounterTilemapPopulatesCombatFieldObstaclesMovementCostAndLineOfSight()
    {
        Definition combat = new(
            DefinitionTypes.Combat,
            "standard",
            "test",
            "combat.json",
            JsonDocument.Parse("""
            {
              "type": "combat",
              "id": "standard",
              "name": "Standard",
              "field": {
                "width": 6,
                "height": 4,
                "metric": "manhattan",
                "terrain": {
                  "#": { "name": "Boulder", "passable": false, "blocks_sight": true }
                }
              }
            }
            """).RootElement.Clone(),
            [],
            [],
            "$");

        Definition encounter = new(
            DefinitionTypes.Encounter,
            "bridge",
            "test",
            "enc.json",
            JsonDocument.Parse("""
            {
              "type": "encounter",
              "id": "bridge",
              "name": "Bridge",
              "monsters": [],
              "terrain": [
                "......",
                ".#....",
                "......",
                "......"
              ]
            }
            """).RootElement.Clone(),
            [],
            [],
            "$");

        Definition dummySheet = new(DefinitionTypes.Asset, "sheet", "test", "test.json", JsonDocument.Parse("""{"type":"asset","id":"sheet","media":"sheet","file":"t.png","frame_size":[16,16]}""").RootElement.Clone(), [], [], "$");
        Definition dummyTileset = new(DefinitionTypes.Tileset, "ts", "test", "test.json", JsonDocument.Parse("""{"type":"tileset","id":"ts","name":"Test","sheet":"sheet","tiles":{}}""").RootElement.Clone(), [], [], "$");
        Dictionary<char, TileGlyph> tiles = new()
        {
            ['g'] = new TileGlyph('g', "Grass", 0, [], null, Passable: true, Door: false, Color: null, MoveCost: 1, BlocksSight: false),
            ['m'] = new TileGlyph('m', "Mud", 1, [], null, Passable: true, Door: false, Color: null, MoveCost: 2, BlocksSight: false),
            ['B'] = new TileGlyph('B', "Bridge", 2, [], null, Passable: true, Door: false, Color: null, MoveCost: 1, BlocksSight: false),
            ['~'] = new TileGlyph('~', "River", 3, [], null, Passable: false, Door: false, Color: null, MoveCost: 1, BlocksSight: false),
            ['T'] = new TileGlyph('T', "Pine Tree", 4, [], null, Passable: false, Door: false, Color: null, MoveCost: 1, BlocksSight: true),
        };
        TilesetDefinition tileset = new(dummyTileset, dummySheet, 80, 16, 16, 16, 5, 1, 5, tiles);

        ResolvedTilemap tilemap = AutoTiler.Resolve(
            tileset,
            [
                ["gg~~gg", "gg~~gg", "gmBBgg", "gg~~gg"],
                ["T    T", "      ", "      ", "      "],
            ],
            zoom: null);

        CombatField? field = CombatField.Of(combat, encounter, tilemap);
        Assert.NotNull(field);

        // River blocks movement, NOT sight, and is marked FromTilemap so CombatScene won't draw placeholder cubes.
        Assert.False(field!.Passable(new Cell(2, 0)));
        Assert.True(field.CanSee(new Cell(1, 0), new Cell(4, 0)));
        Assert.True(field.Terrain[new Cell(2, 0)].FromTilemap);
        Assert.Equal("River", field.Terrain[new Cell(2, 0)].Name);

        // Pine overlay at (0,0) blocks movement AND sight.
        Assert.False(field.Passable(new Cell(0, 0)));
        Assert.True(field.Terrain[new Cell(0, 0)].BlocksSight);

        // Mud costs 2 to enter, bridge is passable (cost 1).
        Assert.True(field.Passable(new Cell(1, 2)));
        Assert.Equal(2, field.Cost(new Cell(1, 2)));
        Assert.True(field.Passable(new Cell(2, 2)));
        Assert.Equal(1, field.Cost(new Cell(2, 2)));

        // Explicit encounter.terrain '#' at (1,1) layers on top with FromTilemap == false.
        Assert.False(field.Passable(new Cell(1, 1)));
        Assert.False(field.Terrain[new Cell(1, 1)].FromTilemap);
        Assert.Equal("Boulder", field.Terrain[new Cell(1, 1)].Name);

        // Pathfinding from west bank (0,1) to east bank (4,2) must funnel across the bridge at y=2!
        Dictionary<GridCell, (int Cost, IReadOnlyList<GridCell> Path)> reachable = GridSearch.ReachableWithin(field, new GridCell(0, 1), budget: 10);
        Assert.True(reachable.ContainsKey(new GridCell(4, 2)));
        Assert.Contains(new GridCell(2, 2), reachable[new GridCell(4, 2)].Path);
        Assert.Contains(new GridCell(3, 2), reachable[new GridCell(4, 2)].Path);
        Assert.False(reachable.ContainsKey(new GridCell(2, 1)));
    }
}
