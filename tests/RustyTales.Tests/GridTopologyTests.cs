using System.Text.Json;
using RustyTales.Core.Campaigns;
using RustyTales.Core.Characters;
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

    [Fact]
    public void AreaCombatProjectorTranslatesDungeonDoorwayIntoCentralChokepoint()
    {
        // A 3x1 stone dungeon corridor with a doorway between (0,0) and (1,0) and an open hall to (2,0).
        List<(int, int, string)> problems = [];
        AreaMap? map = AreaMap.Parse(
        [
            "+--+--+--+",
            "|  D     |",
            "+--+--+--+",
        ],
        problems);
        Assert.NotNull(map);

        Definition dummyArea = new(DefinitionTypes.Area, "hall", "test", "world.json", JsonDocument.Parse("""{"type":"area","id":"hall","name":"Hall","map":[],"entries":{"start":{"at":[0,0],"facing":"east"}}}""").RootElement.Clone(), [], [], "$");
        Definition combat = new(
            DefinitionTypes.Combat,
            "standard",
            "test",
            "combat.json",
            JsonDocument.Parse("""{"type":"combat","id":"standard","name":"Standard","field":{"width":10,"height":6,"metric":"chebyshev"}}""").RootElement.Clone(),
            [],
            [],
            "$");

        ProjectedBattlefield projected = AreaCombatProjector.ProjectDungeonArena(map!, originX: 0, originY: 0, Facing.East, width: 10, height: 6);
        Assert.Null(projected.Tilemap);

        CombatField? field = CombatField.Of(combat, encounter: null, tilemap: null, baseTerrain: projected.Terrain);
        Assert.NotNull(field);

        // Top and bottom corridor walls along y=0 and y=5.
        Assert.False(field!.Passable(new Cell(1, 0)));
        Assert.False(field.Passable(new Cell(7, 5)));

        // Central doorway wall along x=5 with a single-tile chokepoint at (5,2)!
        Assert.False(field.Passable(new Cell(5, 0)));
        Assert.False(field.Passable(new Cell(5, 1)));
        Assert.True(field.Passable(new Cell(5, 2)));
        Assert.False(field.Passable(new Cell(5, 3)));
        Assert.False(field.Passable(new Cell(5, 4)));
        Assert.False(field.Passable(new Cell(5, 5)));

        // Every path from left chamber (2,3) to right chamber (7,3) MUST funnel through (5,2)!
        Dictionary<GridCell, (int Cost, IReadOnlyList<GridCell> Path)> paths = GridSearch.ReachableWithin(field, new GridCell(2, 3), budget: 12);
        Assert.True(paths.ContainsKey(new GridCell(7, 3)));
        Assert.Contains(new GridCell(5, 2), paths[new GridCell(7, 3)].Path);

        // Standing directly in the doorway blocks line of sight through the flanking wall cells.
        Assert.False(field.CanSee(new Cell(2, 1), new Cell(7, 1)));
        Assert.True(field.CanSee(new Cell(2, 2), new Cell(7, 2)));
    }

    [Fact]
    public void AreaCombatProjectorSlicesOverhead2DTilemapIntoBothVisualAndTacticalGrid()
    {
        Definition dummySheet = new(DefinitionTypes.Asset, "sheet", "test", "test.json", JsonDocument.Parse("""{"type":"asset","id":"sheet","media":"sheet","file":"t.png","frame_size":[16,16]}""").RootElement.Clone(), [], [], "$");
        Definition dummyTileset = new(DefinitionTypes.Tileset, "ts", "test", "test.json", JsonDocument.Parse("""{"type":"tileset","id":"ts","name":"Test","sheet":"sheet","tiles":{}}""").RootElement.Clone(), [], [], "$");
        Dictionary<char, TileGlyph> tiles = new()
        {
            ['g'] = new TileGlyph('g', "Grass", 0, [], null, Passable: true, Door: false, Color: null, MoveCost: 1, BlocksSight: false),
            ['~'] = new TileGlyph('~', "River", 1, [], null, Passable: false, Door: false, Color: null, MoveCost: 1, BlocksSight: false),
            ['B'] = new TileGlyph('B', "Bridge", 2, [], null, Passable: true, Door: false, Color: null, MoveCost: 1, BlocksSight: false),
        };
        TilesetDefinition tileset = new(dummyTileset, dummySheet, 48, 16, 16, 16, 3, 1, 3, tiles);

        ResolvedTilemap town = AutoTiler.Resolve(
            tileset,
            [
                [
                    "gggg~ggggg",
                    "gggg~ggggg",
                    "gggg~ggggg",
                    "ggggBggggg",
                    "gggg~ggggg",
                    "gggg~ggggg",
                    "gggg~ggggg",
                    "gggg~ggggg",
                ],
            ],
            zoom: null);

        ProjectedBattlefield projected = AreaCombatProjector.ProjectTilemapSlice(town, originX: 4, originY: 3, width: 6, height: 4);
        Assert.NotNull(projected.Tilemap);
        Assert.Equal(6, projected.Tilemap!.Width);
        Assert.Equal(4, projected.Tilemap.Height);

        // Bridge cell at town (4,3) lands inside the 6x4 slice (startX=1, startY=1 -> local (3,2)) and stays passable while river stays blocked.
        Assert.True(projected.Tilemap.IsPassable(3, 2));
        Assert.False(projected.Tilemap.IsPassable(3, 1));
    }

    [Fact]
    public void UltimaValeBridgeAmbushAndBarrowDoorwayProjectionWorkEndToEndAndAcrossSaves()
    {
        string modulesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "modules"));
        string valeDir = Path.Combine(modulesRoot, "ultima-vale");
        ModuleSet set = ModuleLoader.Load(valeDir, [modulesRoot]);
        Assert.True(set.IsValid, string.Join("\n", set.Diagnostics.Select(d => d.Message)));
        RuleSet rules = set.Rules!;

        Character MakeHero(string name)
        {
            List<ModuleDiagnostic> creationProblems = [];
            Character? created = CharacterRules.Create(
                rules,
                Character.StampsOf(set),
                new CreationRequest(name, "classic:fighter", "classic:human"),
                new DiceRoller(DeterministicRandom.Shared, DeterministicRandom.Shared.CreateScoped(11, "test.hero")),
                creationProblems);
            Assert.NotNull(created);
            return created!;
        }

        Definition campaign = rules.OfType(DefinitionTypes.Campaign).Single();

        // 1. Trigger bridge_ambush in Oakvale Village (auto-tiled encounter tilemap).
        CampaignState state1 = CampaignRunner.NewState(rules, campaign, [MakeHero("Aldric"), MakeHero("Bram"), MakeHero("Cora"), MakeHero("Dain")], seed: 1);
        CampaignRunner runner = new(rules, state1) { DefaultCombatControl = CombatControlMode.Manual };

        Definition bridgeEvent = rules.Find(DefinitionTypes.Event, "ultima-vale:bridge_ambush", out _)!;
        runner.RunEvent(bridgeEvent, DeterministicRandom.Shared);
        CombatObservation? bridgeObs = runner.ObserveCombat(DeterministicRandom.Shared);
        Assert.NotNull(bridgeObs);
        Assert.Equal(CombatPhase.AwaitingAction, bridgeObs!.Phase);
        Assert.NotNull(bridgeObs.PendingDecision);

        // Move choices must NEVER include impassable Brook Water (~ at x=4..5, y=0..1 & y=4..5) or Pine Trees!
        CombatActionChoice moveAction = bridgeObs.PendingDecision!.Actions.First(a => a.IsMoveAction);
        Assert.NotNull(moveAction.FreeMoves);
        Assert.NotEmpty(moveAction.FreeMoves!);
        Assert.DoesNotContain(moveAction.FreeMoves!, mv => mv.Destination is { X: 4 or 5, Y: 0 or 1 or 4 or 5 });
        Assert.DoesNotContain(moveAction.FreeMoves!, mv => mv.Destination is { X: 0, Y: 0 or 5 });

        // 2. Now start a fresh run, descend into the 3D Barrow, and step south through the doorway into [1, 1] -> barrow_ambush!
        CampaignState state2 = CampaignRunner.NewState(rules, campaign, [MakeHero("Aldric"), MakeHero("Bram"), MakeHero("Cora"), MakeHero("Dain")], seed: 1);
        CampaignRunner barrowRunner = new(rules, state2) { DefaultCombatControl = CombatControlMode.Manual };

        Definition enterBarrow = rules.Find(DefinitionTypes.Event, "ultima-vale:enter_barrow", out _)!;
        barrowRunner.RunEvent(enterBarrow, DeterministicRandom.Shared);
        Assert.Equal("ultima-vale:barrow", barrowRunner.State.Area.QualifiedId);
        Assert.Equal((1, 0), (barrowRunner.State.X, barrowRunner.State.Y));
        Assert.Equal(Facing.South, barrowRunner.State.Facing);

        barrowRunner.Execute("forward", DeterministicRandom.Shared);
        Assert.Equal((1, 1), (barrowRunner.State.X, barrowRunner.State.Y));
        Assert.NotNull(barrowRunner.State.PendingCombat);
        Assert.NotNull(barrowRunner.State.PendingCombat!.AreaOrigin);
        Assert.Equal("ultima-vale:barrow", barrowRunner.State.PendingCombat.AreaOrigin!.Value.AreaId);

        ProjectedBattlefield? projected = barrowRunner.ProjectedBattlefieldFor(barrowRunner.State.PendingCombat);
        Assert.NotNull(projected);
        Assert.True(projected!.Terrain.ContainsKey(new Cell(5, 1)));
        Assert.False(projected.Terrain.ContainsKey(new Cell(5, 2))); // 1-tile doorway chokepoint!
        Assert.True(projected.Terrain.ContainsKey(new Cell(5, 3)));

        // Save mid-combat and reload: AreaOrigin and projected doorway chokepoint must survive bit-for-bit!
        string saveJson = SaveFile.ToJson(barrowRunner.State, set);
        List<ModuleDiagnostic> loadProblems = [];
        CampaignState? loaded = SaveFile.Read(System.Text.Encoding.UTF8.GetBytes(saveJson), "memory.save.json", set, loadProblems);
        Assert.NotNull(loaded);
        Assert.Empty(loadProblems);
        Assert.Equal(barrowRunner.State.PendingCombat.AreaOrigin, loaded!.PendingCombat?.AreaOrigin);

        CampaignRunner resumed = new(rules, loaded) { DefaultCombatControl = CombatControlMode.Manual };
        ProjectedBattlefield? resumedProjected = resumed.ProjectedBattlefieldFor(loaded.PendingCombat!);
        Assert.NotNull(resumedProjected);
        Assert.Equal(projected.Terrain.Count, resumedProjected!.Terrain.Count);

        CombatObservation? barrowObs = resumed.ObserveCombat(DeterministicRandom.Shared);
        Assert.NotNull(barrowObs);
        Assert.Equal(CombatPhase.AwaitingAction, barrowObs!.Phase);
        CombatActionChoice barrowMove = barrowObs.PendingDecision!.Actions.First(a => a.IsMoveAction);
        Assert.NotNull(barrowMove.FreeMoves);
        // Wall cells at x=5 (y != 2) must not be reachable destinations!
        Assert.DoesNotContain(barrowMove.FreeMoves!, mv => mv.Destination is { X: 5, Y: 0 or 1 or 3 or 4 or 5 });
    }
}
