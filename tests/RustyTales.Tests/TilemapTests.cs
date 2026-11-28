using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RustyTales.Core.Authoring;
using RustyTales.Core.Campaigns;
using RustyTales.Core.Characters;
using RustyTales.Core.Definitions;
using RustyTales.Core.Modules;
using RustyTales.Core.Rules;

namespace RustyTales.Tests;

public sealed class TilemapTests
{
    private static byte[] SolidPng(int width, int height, byte r, byte g, byte b)
    {
        using MemoryStream raw = new();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < width; x++)
            {
                raw.Write([r, g, b, 255]);
            }
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        using MemoryStream png = new();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteChunk(png, "IHDR", [
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
            8, 6, 0, 0, 0,
        ]);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream dst, string type, byte[] data)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        dst.Write([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length]);
        dst.Write(typeBytes);
        dst.Write(data);
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in typeBytes.Concat(data))
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        crc ^= 0xFFFFFFFFu;
        dst.Write([(byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc]);
    }

    [Fact]
    public void AutoTilerResolvesFourBitWallTopBlob47AndDeterministicVariants()
    {
        // Build a synthetic 64-frame tileset definition in memory via a temporary module.
        using TempModules scratch = new();
        string modDir = Path.Combine(scratch.Root, "auto-test");
        Directory.CreateDirectory(Path.Combine(modDir, "tiles"));
        File.WriteAllBytes(Path.Combine(modDir, "tiles", "atlas.png"), SolidPng(128, 128, 0x33, 0x66, 0x33));
        File.WriteAllText(Path.Combine(modDir, "module.json"), """
            {
              "format": 1,
              "id": "auto-test",
              "kind": "assets",
              "version": "0.1.0",
              "title": "AutoTiler Test Assets",
              "requires": [],
              "provenance": "Unit test fixture."
            }
            """);
        File.WriteAllText(Path.Combine(modDir, "tiles.json"), """
            [
              {
                "type": "asset",
                "id": "atlas",
                "media": "sheet",
                "file": "tiles/atlas.png",
                "frame_size": [16, 16]
              },
              {
                "type": "tileset",
                "id": "test_ts",
                "name": "Test Tileset",
                "sheet": "atlas",
                "tiles": {
                  "g": { "name": "Grass", "variants": [0, 1, 2, 3], "color": "#447733" },
                  "p": { "name": "Path", "autotile": { "mode": "4bit", "base": 16, "out_of_bounds": "empty", "connects_to": ["d"] } },
                  "d": { "name": "Door", "tile": 4, "door": true },
                  "w": { "name": "Wall", "passable": false, "autotile": { "mode": "wall_top", "frames": { "front": 5, "top": 6, "cap": 7 }, "out_of_bounds": "empty" } },
                  "~": { "name": "Water", "passable": false, "autotile": { "mode": "blob47", "base": 17, "out_of_bounds": "empty" } }
                }
              }
            ]
            """);

        ModuleSet set = ModuleLoader.Load(modDir, []);
        Assert.True(set.IsValid, string.Join('\n', set.Diagnostics.Select(d => d.Message)));
        TilesetDefinition ts = Assert.Single(set.Rules!.Tilesets.Values);
        Assert.Equal(64, ts.FrameCount);

        // 1. 4-bit cardinal mask (N=1, E=2, S=4, W=8) + connects_to ('d')
        string[] roadGrid =
        [
            "gpg",
            "ppd",
            "gpg",
        ];
        ResolvedTilemap road = AutoTiler.Resolve(ts, [roadGrid], null);
        // Center [1,1] connects N(p), E(d via connects_to), S(p), W(p) => mask 15 => frame 16 + 15 = 31
        Assert.Equal(15, road.Layers[0].At(1, 1).Bitmask);
        Assert.Equal(31, road.Layers[0].At(1, 1).Frame);
        // Top [1,0] connects only S => mask 4 => frame 20
        Assert.Equal(4, road.Layers[0].At(1, 0).Bitmask);
        Assert.Equal(20, road.Layers[0].At(1, 0).Frame);
        // Left [0,1] connects only E => mask 2 => frame 18
        Assert.Equal(2, road.Layers[0].At(0, 1).Bitmask);
        Assert.Equal(18, road.Layers[0].At(0, 1).Frame);

        // 2. wall_top (3/4 depth: front=5 when south open, cap=7 when north open & south wall, top=6 when sandwiched)
        string[] wallGrid =
        [
            "gwg",
            "gwg",
            "gwg",
            "ggg",
        ];
        ResolvedTilemap wall = AutoTiler.Resolve(ts, [wallGrid], null);
        Assert.Equal(7, wall.Layers[0].At(1, 0).Frame); // north open, south wall => cap
        Assert.Equal(6, wall.Layers[0].At(1, 1).Frame); // north wall, south wall => top
        Assert.Equal(5, wall.Layers[0].At(1, 2).Frame); // south open => front

        // 3. blob47: solid 3x3 center has all 8 neighbors (mask 255 => blob index 46 => frame 17+46 = 63)
        // Missing SE diagonal [2,2] drops SE bit (8): mask 247 => blob index 44 => frame 17+44 = 61
        string[] waterSolid =
        [
            "~~~",
            "~~~",
            "~~~",
        ];
        ResolvedTilemap blobSolid = AutoTiler.Resolve(ts, [waterSolid], null);
        Assert.Equal(255, blobSolid.Layers[0].At(1, 1).Bitmask);
        Assert.Equal(17 + 46, blobSolid.Layers[0].At(1, 1).Frame);

        string[] waterNotch =
        [
            "~~~",
            "~~~",
            "~~g",
        ];
        ResolvedTilemap blobNotch = AutoTiler.Resolve(ts, [waterNotch], null);
        Assert.Equal(247, blobNotch.Layers[0].At(1, 1).Bitmask);
        Assert.Equal(17 + AutoTiler.Blob47Index[247], blobNotch.Layers[0].At(1, 1).Frame);

        // 4. Deterministic coordinate variants
        ResolvedTilemap roadAgain = AutoTiler.Resolve(ts, [roadGrid], null);
        Assert.Equal(road.Layers[0].At(0, 0).Frame, roadAgain.Layers[0].At(0, 0).Frame);
        Assert.InRange(road.Layers[0].At(0, 0).Frame, 0, 3);
    }

    [Fact]
    public void TileFirstOverheadAreaSupportsCardinalMovementBlockingSavesAndModuleGraph()
    {
        using TempModules scratch = new();
        string campDir = Path.Combine(scratch.Root, "ultima-camp");
        Directory.CreateDirectory(Path.Combine(campDir, "art"));
        File.WriteAllBytes(Path.Combine(campDir, "art", "tiles.png"), SolidPng(128, 64, 0x38, 0x68, 0x38));
        File.WriteAllText(Path.Combine(campDir, "module.json"), """
            {
              "format": 1,
              "id": "ultima-camp",
              "kind": "campaign",
              "version": "0.1.0",
              "title": "Mixed 2D/3D Test",
              "requires": [
                { "id": "classic", "version": "^0.1.0" },
                { "id": "placeholder-art", "version": "^0.1.0" }
              ],
              "provenance": "Unit test fixture."
            }
            """);
        File.WriteAllText(Path.Combine(campDir, "world.json"), """
            [
              {
                "type": "asset",
                "id": "village_sheet",
                "media": "sheet",
                "file": "art/tiles.png",
                "frame_size": [16, 16]
              },
              {
                "type": "tileset",
                "id": "village_ts",
                "name": "Village",
                "sheet": "village_sheet",
                "tiles": {
                  "g": { "name": "Grass", "variants": [0, 1], "color": "#457a3c" },
                  "p": { "name": "Path", "autotile": { "mode": "4bit", "base": 16, "out_of_bounds": "empty" }, "color": "#b89b6a" },
                  "~": { "name": "Water", "tile": 2, "passable": false, "color": "#285888" },
                  "T": { "name": "Tree", "tile": 3, "passable": false, "color": "#245424" },
                  "d": { "name": "Gate", "tile": 4, "door": true, "color": "#8c6239" }
                }
              },
              {
                "type": "area",
                "id": "village",
                "name": "Oakvale",
                "view": "overhead",
                "tilemap": {
                  "tileset": "village_ts",
                  "zoom": 9,
                  "layers": [
                    [
                      "~~~~",
                      "gppg",
                      "gpdg",
                      "gggg"
                    ],
                    [
                      "    ",
                      "   T",
                      "    ",
                      "    "
                    ]
                  ]
                },
                "cells": [
                  { "at": [2, 2], "event": "enter_crypt" }
                ],
                "entries": {
                  "square": { "at": [1, 1], "facing": "east" }
                }
              },
              {
                "type": "area",
                "id": "crypt",
                "name": "Crypt 3D",
                "wall_set": "placeholder-art:stone_wall",
                "map": [
                  "+--+",
                  "|  |",
                  "+--+"
                ],
                "entries": {
                  "stairs": { "at": [0, 0], "facing": "north" }
                }
              },
              {
                "type": "event",
                "id": "enter_crypt",
                "kind": "teleport",
                "area": "crypt",
                "entry": "stairs"
              },
              {
                "type": "encounter",
                "id": "tiled_ambush",
                "name": "Roadside Ambush",
                "monsters": [{ "monster": "classic:skeleton", "count": "1" }],
                "tilemap": {
                  "tileset": "village_ts",
                  "layers": [
                    [
                      "gggggggggg",
                      "gggggggggg",
                      "pppppppppp",
                      "pppppppppp",
                      "gggggggggg",
                      "gggggggggg"
                    ]
                  ]
                }
              },
              {
                "type": "campaign",
                "id": "mixed",
                "name": "Mixed Adventure",
                "start": { "area": "village", "entry": "square" },
                "party": { "min": 1, "max": 4 }
              }
            ]
            """);

        ModuleSet set = ModuleLoader.Load(campDir, [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.True(set.IsValid, string.Join('\n', set.Diagnostics.Select(d => d.Message)));
        RuleSet rules = set.Rules!;

        // Verify ModuleGraph exposes tileset, overhead area tilemap, and encounter tilemap
        ModuleGraph graph = ModuleGraphBuilder.Build(set);
        Assert.NotNull(graph.Tilesets);
        Assert.Single(graph.Tilesets!);
        GraphArea villageArea = Assert.Single(graph.Areas, a => a.LocalId == "village");
        Assert.Equal("overhead", villageArea.View);
        Assert.NotNull(villageArea.Tilemap);
        Assert.Equal(4, villageArea.Tilemap!.Width);
        Assert.Equal(2, villageArea.Tilemap.Layers.Count);
        Assert.True(Assert.Single(graph.Areas, a => a.LocalId == "crypt").Reachable);
        GraphEncounter enc = Assert.Single(graph.Encounters!, e => e.LocalId == "tiled_ambush");
        Assert.NotNull(enc.Tilemap);

        // Create a character & play with cardinal movement: north into water is blocked,
        // east then east into overlay Tree [3,1] is blocked, south from [2,1] enters Gate [2,2] and teleports into 3D crypt!
        Character? hero = null;
        for (ulong attempt = 1; attempt <= 64 && hero is null; attempt++)
        {
            using ITalesRandomStream stream = DeterministicRandom.Shared.CreateScoped(attempt, "tilemap.char");
            List<ModuleDiagnostic> problems = [];
            hero = CharacterRules.Create(
                rules,
                Character.StampsOf(set),
                new CreationRequest("Ada", "classic:fighter", "classic:human"),
                new DiceRoller(DeterministicRandom.Shared, stream),
                problems);
        }

        Assert.NotNull(hero);
        CampaignState state = CampaignRunner.NewState(rules, rules.OfType(DefinitionTypes.Campaign).Single(), [hero!], 42);
        CampaignRunner runner = new(rules, state);
        runner.Begin(DeterministicRandom.Shared);

        // North into Water [1,0] is blocked
        List<PlayFact> blockedWater = runner.Execute("north", DeterministicRandom.Shared);
        Assert.Contains(blockedWater, f => f is RefusedFact);
        Assert.Equal((1, 1), (state.X, state.Y));
        Assert.Equal(Facing.North, state.Facing);

        // East to [2,1] succeeds
        List<PlayFact> movedEast = runner.Execute("east", DeterministicRandom.Shared);
        Assert.Contains(movedEast, f => f is MovedFact);
        Assert.Equal((2, 1), (state.X, state.Y));

        // East into overlay Tree at [3,1] is blocked!
        List<PlayFact> blockedTree = runner.Execute("east", DeterministicRandom.Shared);
        Assert.Contains(blockedTree, f => f is RefusedFact);
        Assert.Equal((2, 1), (state.X, state.Y));

        // Save & reload works cleanly on a tile-first overhead area
        string saveJson = SaveFile.ToJson(state, set);
        List<ModuleDiagnostic> loadProblems = [];
        CampaignState? loaded = SaveFile.Read(Encoding.UTF8.GetBytes(saveJson), "memory.save.json", set, loadProblems);
        Assert.Empty(loadProblems);
        Assert.NotNull(loaded);
        Assert.Equal("ultima-camp:village", loaded!.Area.QualifiedId);
        Assert.Equal((2, 1), (loaded.X, loaded.Y));

        // South into Gate [2,2] triggers teleport into the 3D crypt!
        List<PlayFact> steppedGate = runner.Execute("south", DeterministicRandom.Shared);
        Assert.Contains(steppedGate, f => f is ArrivedFact arrived && arrived.Area == "Crypt 3D");
        Assert.Equal("ultima-camp:crypt", state.Area.QualifiedId);
    }

    [Fact]
    public void ValidationRejectsEntryOnImpassableTileAndUnknownGlyph()
    {
        using TempModules scratch = new();
        string campDir = Path.Combine(scratch.Root, "bad-tilemap");
        Directory.CreateDirectory(Path.Combine(campDir, "art"));
        File.WriteAllBytes(Path.Combine(campDir, "art", "tiles.png"), SolidPng(32, 32, 0x33, 0x33, 0x33));
        File.WriteAllText(Path.Combine(campDir, "module.json"), """
            {
              "format": 1,
              "id": "bad-tilemap",
              "kind": "campaign",
              "version": "0.1.0",
              "title": "Invalid Tilemap",
              "requires": [
                { "id": "classic", "version": "^0.1.0" },
                { "id": "placeholder-art", "version": "^0.1.0" }
              ],
              "provenance": "Unit test fixture."
            }
            """);
        File.WriteAllText(Path.Combine(campDir, "world.json"), """
            [
              { "type": "asset", "id": "ts_img", "media": "sheet", "file": "art/tiles.png", "frame_size": [16, 16] },
              {
                "type": "tileset",
                "id": "ts",
                "name": "TS",
                "sheet": "ts_img",
                "tiles": {
                  "g": { "name": "Grass", "tile": 0 },
                  "~": { "name": "Water", "tile": 1, "passable": false }
                }
              },
              {
                "type": "area",
                "id": "wet",
                "name": "Wet",
                "view": "overhead",
                "tilemap": { "tileset": "ts", "layers": [["~g", "gg"]] },
                "entries": { "start": { "at": [0, 0], "facing": "north" } }
              },
              {
                "type": "campaign",
                "id": "bad",
                "name": "Bad",
                "start": { "area": "wet", "entry": "start" },
                "party": { "min": 1, "max": 4 }
              }
            ]
            """);

        ModuleSet set = ModuleLoader.Load(campDir, [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.False(set.IsValid);
        Assert.Contains(set.Diagnostics, d => d.Rule == "area.entry" && d.Message.Contains("impassable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FirstPartyUltimaValeShowcaseValidatesAndTransitionsBetween2DVillageAnd3DBarrow()
    {
        string modulePath = Path.Combine(Rules.RepositoryRoot, "modules", "ultima-vale");
        ModuleSet set = ModuleLoader.Load(modulePath, [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.True(set.IsValid, string.Join('\n', set.Diagnostics.Select(d => $"{d.Rule} {d.JsonPath}: {d.Message}")));

        RuleSet rules = set.Rules!;
        ModuleGraph graph = ModuleGraphBuilder.Build(set);
        Assert.DoesNotContain(graph.Issues, i => i.Severity == "error");
        Assert.Contains(graph.Tilesets!, t => t.LocalId == "oakvale_tiles");
        GraphArea village = Assert.Single(graph.Areas, a => a.LocalId == "oakvale");
        Assert.Equal("overhead", village.View);
        Assert.NotNull(village.Tilemap);
        Assert.Equal(2, village.Tilemap!.Layers.Count);
        GraphArea barrow = Assert.Single(graph.Areas, a => a.LocalId == "barrow");
        Assert.Equal("first_person", barrow.View);
        Assert.True(barrow.Reachable);
        GraphEncounter ambush = Assert.Single(graph.Encounters!, e => e.LocalId == "meadow_ambush");
        Assert.NotNull(ambush.Tilemap);
    }
}
