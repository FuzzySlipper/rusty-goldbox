using System.Net;
using System.Text.Json;
using RustyTales.Cli;
using RustyTales.Core.Authoring;
using RustyTales.Core.Modules;

namespace RustyTales.Tests;

/// <summary>
/// Verifies <see cref="ModuleGraphBuilder"/>, <c>tales module graph</c>, and the
/// local <c>tales workbench</c> HTTP server (SPA, graph API, definitions,
/// assets, and live reload).
/// </summary>
public sealed class WorkbenchGraphTests
{
    [Fact]
    public void FirstPartyCampaignGraphsCaptureAreasTeleportsScenesAndAssets()
    {
        using TempModules scratch = new();

        // 1. sample-crypt
        (int cryptCode, string cryptJson) = CampaignTests.Run(scratch, "module", "graph", CampaignTests.SampleCrypt, "--json");
        Assert.Equal(TalesCli.Ok, cryptCode);
        using JsonDocument cryptDoc = JsonDocument.Parse(cryptJson);
        JsonElement cryptRoot = cryptDoc.RootElement;
        Assert.Equal("sample-crypt", cryptRoot.GetProperty("module").GetProperty("id").GetString());
        Assert.True(cryptRoot.GetProperty("module").GetProperty("isValid").GetBoolean());
        Assert.True(cryptRoot.GetProperty("counts").GetProperty("areas").GetInt32() >= 2);
        Assert.True(cryptRoot.GetProperty("navigationLinks").GetArrayLength() >= 1);
        Assert.Contains(cryptRoot.GetProperty("areas").EnumerateArray(),
            area => area.GetProperty("isStartArea").GetBoolean() && area.GetProperty("reachable").GetBoolean());

        // 2. wizardry-delve (includes lowered `scene` dialogue graphs)
        string wizardry = Path.Combine(Rules.RepositoryRoot, "modules", "wizardry-delve");
        (int wizCode, string wizJson) = CampaignTests.Run(scratch, "module", "graph", wizardry, "--json");
        Assert.Equal(TalesCli.Ok, wizCode);
        using JsonDocument wizDoc = JsonDocument.Parse(wizJson);
        JsonElement wizRoot = wizDoc.RootElement;
        Assert.Equal("wizardry-delve", wizRoot.GetProperty("module").GetProperty("id").GetString());
        Assert.True(wizRoot.GetProperty("counts").GetProperty("scenes").GetInt32() >= 1);
        Assert.Contains(wizRoot.GetProperty("events").EnumerateArray(),
            evt => evt.TryGetProperty("sceneId", out JsonElement sid) && sid.ValueKind == JsonValueKind.String);
    }

    [Fact]
    public void GraphFlagsUnreachableAreasWalledCellsSecretsDeadEndsAndMissingKeys()
    {
        using TempModules modules = new();
        string campaign = modules.Module("diag-camp", "campaign",
            requires: $"{TempModules.Require("classic", "*")}, {TempModules.Require("placeholder-art", "*")}");

        modules.Write("diag-camp/items.json", """
            { "type": "item", "id": "brass_key", "name": "Brass Key", "kind": "gear", "cost": 1, "currency": "classic:gold", "weight": 0 }
            """);

        // Area A: [0,0] start, [1,0] teleport to Area B behind unlocked door,
        //         [2,0] behind secret door (secret_only_event),
        //         [0,1] completely walled off with event (unreachable_cell_event),
        //         [1,1] behind locked door needing brass_key (locked_door_no_key_source).
        modules.Write("diag-camp/areas.json", """
            [
              {
                "type": "area",
                "id": "entry_hall",
                "name": "Entry Hall",
                "map": [
                  "+--+--+--+",
                  "|  D  S  |",
                  "+--+DD+--+",
                  "|  |  |  |",
                  "+--+--+--+"
                ],
                "entries": { "start": { "at": [0, 0], "facing": "east" } },
                "doors": [
                  { "id": "vault_door", "at": [1, 0], "facing": "south", "locked": true, "key": "diag-camp:brass_key" }
                ],
                "cells": [
                  { "at": [1, 0], "event": "go_cellar" },
                  { "at": [2, 0], "event": "secret_note" },
                  { "at": [0, 1], "event": "walled_note" }
                ]
              },
              {
                "type": "area",
                "id": "dead_cellar",
                "name": "Dead Cellar",
                "map": [
                  "+--+",
                  "|  |",
                  "+--+"
                ],
                "entries": { "from_hall": { "at": [0, 0], "facing": "north" } }
              },
              {
                "type": "area",
                "id": "lost_tower",
                "name": "Lost Tower",
                "map": [
                  "+--+",
                  "|  |",
                  "+--+"
                ],
                "entries": { "in": { "at": [0, 0], "facing": "north" } }
              }
            ]
            """);

        modules.Write("diag-camp/events.json", """
            [
              { "type": "event", "id": "go_cellar", "kind": "teleport", "area": "dead_cellar", "entry": "from_hall" },
              { "type": "event", "id": "secret_note", "kind": "text", "text": "You found a hidden alcove." },
              { "type": "event", "id": "walled_note", "kind": "text", "text": "Nobody can reach this." },
              { "type": "event", "id": "orphan_evt", "kind": "text", "text": "Never triggered." }
            ]
            """);

        modules.Write("diag-camp/campaign.json", """
            {
              "type": "campaign",
              "id": "diag",
              "name": "Diagnostic Trial",
              "start": { "area": "entry_hall", "entry": "start" },
              "party": { "min": 1, "max": 4 }
            }
            """);

        ModuleSet set = ModuleLoader.Load(campaign, [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.True(set.IsValid, string.Join("; ", set.Diagnostics.Select(d => d.Message)));

        ModuleGraph graph = ModuleGraphBuilder.Build(set);
        Assert.Contains(graph.Issues, i => i.Code == "unreachable_area" && i.AreaId == "diag-camp:lost_tower");
        Assert.Contains(graph.Issues, i => i.Code == "dead_end_area" && i.AreaId == "diag-camp:dead_cellar");
        Assert.Contains(graph.Issues, i => i.Code == "unreachable_cell_event" && i.AreaId == "diag-camp:entry_hall" && i.EventId == "diag-camp:walled_note");
        Assert.Contains(graph.Issues, i => i.Code == "secret_only_event" && i.AreaId == "diag-camp:entry_hall" && i.EventId == "diag-camp:secret_note");
        Assert.Contains(graph.Issues, i => i.Code == "locked_door_no_key_source" && i.DoorId == "vault_door");
        Assert.Contains(graph.Issues, i => i.Code == "unreachable_event" && i.EventId == "diag-camp:orphan_evt");
    }

    [Fact]
    public async Task WorkbenchServerServesSpaGraphDefinitionsAssetsAndLiveReload()
    {
        using TempModules scratch = new();
        string campDir = Path.Combine(scratch.Root, "live-camp");
        Directory.CreateDirectory(campDir);
        File.WriteAllText(Path.Combine(campDir, "module.json"), """
            {
              "format": 1,
              "id": "live-camp",
              "kind": "campaign",
              "version": "0.1.0",
              "title": "Live Camp v1",
              "requires": [
                { "id": "classic", "version": "^0.1.0" },
                { "id": "placeholder-art", "version": "^0.1.0" }
              ],
              "provenance": "Test fixture."
            }
            """);
        File.WriteAllText(Path.Combine(campDir, "world.json"), """
            [
              {
                "type": "area",
                "id": "hall",
                "name": "Hall",
                "wall_set": "placeholder-art:crypt",
                "map": ["+--+", "|  |", "+--+"],
                "entries": { "in": { "at": [0, 0], "facing": "north" } }
              },
              {
                "type": "campaign",
                "id": "live",
                "name": "Live Camp",
                "start": { "area": "hall", "entry": "in" },
                "party": { "min": 1, "max": 4 }
              }
            ]
            """);

        using WorkbenchServer server = new(
            campDir,
            [Path.Combine(Rules.RepositoryRoot, "modules")],
            [],
            "127.0.0.1",
            port: 0);
        server.Start();

        using HttpClient http = new() { BaseAddress = new Uri(server.Url) };

        // 1. Embedded SPA assets
        string html = await http.GetStringAsync("/");
        Assert.Contains("Rusty Tales Workbench", html, StringComparison.Ordinal);
        string css = await http.GetStringAsync("/workbench.css");
        Assert.Contains(".topbar", css, StringComparison.Ordinal);
        string js = await http.GetStringAsync("/workbench.js");
        Assert.Contains("/api/graph", js, StringComparison.Ordinal);

        // 2. Health & Graph JSON
        using JsonDocument health = JsonDocument.Parse(await http.GetStringAsync("/api/health"));
        Assert.True(health.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("live-camp", health.RootElement.GetProperty("module").GetString());

        using JsonDocument graph = JsonDocument.Parse(await http.GetStringAsync("/api/graph"));
        Assert.Equal("Live Camp v1", graph.RootElement.GetProperty("module").GetProperty("title").GetString());

        // 3. Definition inspection endpoint
        using JsonDocument def = JsonDocument.Parse(await http.GetStringAsync("/api/definition?id=live-camp:hall&type=area"));
        Assert.True(def.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Hall", def.RootElement.GetProperty("name").GetString());

        // 4. Asset serving endpoint returns real PNG bytes
        using HttpResponseMessage pngResp = await http.GetAsync("/api/asset/placeholder-art/crypt");
        Assert.Equal(HttpStatusCode.OK, pngResp.StatusCode);
        Assert.Equal("image/png", pngResp.Content.Headers.ContentType?.MediaType);
        byte[] pngBytes = await pngResp.Content.ReadAsByteArrayAsync();
        Assert.True(pngBytes.Length > 8);
        Assert.Equal(0x89, pngBytes[0]);
        Assert.Equal((byte)'P', pngBytes[1]);

        // 5. Live reload picks up disk edits without restarting the server
        File.WriteAllText(Path.Combine(campDir, "module.json"), """
            {
              "format": 1,
              "id": "live-camp",
              "kind": "campaign",
              "version": "0.1.0",
              "title": "Live Camp v2 Reloaded",
              "requires": [
                { "id": "classic", "version": "^0.1.0" },
                { "id": "placeholder-art", "version": "^0.1.0" }
              ],
              "provenance": "Test fixture."
            }
            """);
        using HttpResponseMessage reloadResp = await http.PostAsync("/api/reload", content: null);
        Assert.Equal(HttpStatusCode.OK, reloadResp.StatusCode);
        using JsonDocument reloaded = JsonDocument.Parse(await reloadResp.Content.ReadAsStringAsync());
        Assert.Equal("Live Camp v2 Reloaded", reloaded.RootElement.GetProperty("module").GetProperty("title").GetString());
    }

    [Fact]
    public void WorkbenchCliOnceModePrintsStartupJsonAndExitsZero()
    {
        using TempModules scratch = new();
        (int code, string output) = CampaignTests.Run(scratch, "workbench", CampaignTests.SampleCrypt, "--port", "0", "--once", "--json");
        Assert.Equal(TalesCli.Ok, code);
        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("sample-crypt", doc.RootElement.GetProperty("module").GetString());
        Assert.StartsWith("http://127.0.0.1:", doc.RootElement.GetProperty("url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GraphAssetAndSkinMetadataCoversWallSetsAnimatedSpritesAudioAndSkins()
    {
        ModuleSet set = ModuleLoader.Load(CampaignTests.SampleCrypt, [Path.Combine(Rules.RepositoryRoot, "modules")]);
        Assert.True(set.IsValid);
        ModuleGraph graph = ModuleGraphBuilder.Build(set);

        // Wall set carries named regions (wall, door, floor, ceiling) and pixel dimensions.
        GraphAsset wallSet = Assert.Single(graph.Assets, a => a.Id == "placeholder-art:stone_wall");
        Assert.Equal("wall_set", wallSet.Category);
        Assert.NotNull(wallSet.Regions);
        Assert.True(wallSet.Regions!.ContainsKey("wall"));
        Assert.True(wallSet.Regions.ContainsKey("door"));
        Assert.True(wallSet.Width > 0 && wallSet.Height > 0);

        // Animated sprite carries frame size, animations, faces, and height.
        GraphAsset skeleton = Assert.Single(graph.Assets, a => a.Id == "placeholder-art:skeleton");
        Assert.Equal("sprite", skeleton.Category);
        Assert.NotNull(skeleton.FrameSize);
        Assert.NotNull(skeleton.Animations);
        Assert.True(skeleton.Animations!.Count > 0);
        Assert.NotNull(skeleton.Faces);

        // Portraits and audio are classified cleanly.
        Assert.Contains(graph.Assets, a => a.Category == "portrait");
        Assert.Contains(graph.Assets, a => a.Category == "audio");

        // Skins include the campaign default skin plus optional skins with colors & 9-slice art.
        Assert.True(graph.Skins.Count >= 2);
        GraphSkin stone = Assert.Single(graph.Skins, s => s.Id == "placeholder-art:stone");
        Assert.True(stone.IsCampaignSkin);
        Assert.True(stone.Colors.ContainsKey("accent"));
        Assert.Equal("placeholder-art:stone_frame", stone.FrameAsset);
        Assert.Equal(8, stone.FrameSlice);

        // Reviewing a skin via ReviewResolver resolves provenance and revision cleanly.
        ReviewResolver.TargetMeta skinMeta = ReviewResolver.DescribeTarget(ReviewTargetKinds.Definition, $"skin:{stone.Id}", set, graph);
        Assert.True(skinMeta.Exists);
        Assert.NotNull(skinMeta.Revision);
        Assert.Contains("stone.json", skinMeta.File, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkbenchLiveStateEndpointStoresAndReturnsLiveGameContext()
    {
        using TempModules scratch = new();
        string reviewsPath = Path.Combine(scratch.Root, "reviews.json");
        using WorkbenchServer server = new(
            CampaignTests.SampleCrypt,
            [Path.Combine(Rules.RepositoryRoot, "modules")],
            [],
            "127.0.0.1",
            port: 0,
            reviewsPath: reviewsPath);
        server.Start();
        using HttpClient http = new() { BaseAddress = new Uri(server.Url) };

        using JsonDocument before = JsonDocument.Parse(await http.GetStringAsync("/api/live-state"));
        Assert.True(before.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(before.RootElement.GetProperty("connected").GetBoolean());

        using StringContent body = new(
            """{"screen":"play","module":"sample-crypt","area":"sample-crypt:entrance","x":3,"y":1,"facing":"east","activeEvent":"sample-crypt:barred_door"}""",
            System.Text.Encoding.UTF8,
            "application/json");
        using HttpResponseMessage post = await http.PostAsync("/api/live-state", body);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        using JsonDocument after = JsonDocument.Parse(await http.GetStringAsync("/api/live-state"));
        JsonElement live = after.RootElement;
        Assert.Equal("sample-crypt:entrance", live.GetProperty("area").GetString());
        Assert.Equal(3, live.GetProperty("x").GetInt32());
        Assert.Equal(1, live.GetProperty("y").GetInt32());
        Assert.Equal("east", live.GetProperty("facing").GetString());
        Assert.Equal("sample-crypt:barred_door", live.GetProperty("activeEvent").GetString());
    }

    [Fact]
    public async Task WorkbenchPlaytestEndpointsExposeStatusSpaTabAndValidateInactiveCommands()
    {
        using TempModules scratch = new();
        string reviewsPath = Path.Combine(scratch.Root, "reviews.json");
        using WorkbenchServer server = new(
            CampaignTests.SampleCrypt,
            [Path.Combine(Rules.RepositoryRoot, "modules")],
            [],
            "127.0.0.1",
            port: 0,
            reviewsPath: reviewsPath);
        server.Start();
        using HttpClient http = new() { BaseAddress = new Uri(server.Url) };

        // 1. Embedded SPA includes the Playtest tab & API wiring
        string html = await http.GetStringAsync("/");
        Assert.Contains("data-tab=\"playtest\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"playtest-iframe\"", html, StringComparison.Ordinal);
        string js = await http.GetStringAsync("/workbench.js");
        Assert.Contains("/api/playtest/status", js, StringComparison.Ordinal);
        Assert.Contains("/api/playtest/start", js, StringComparison.Ordinal);

        // 2. Status reports campaign availability cleanly before launching
        using JsonDocument status = JsonDocument.Parse(await http.GetStringAsync("/api/playtest/status"));
        JsonElement st = status.RootElement;
        Assert.True(st.GetProperty("ok").GetBoolean());
        Assert.True(st.GetProperty("available").GetBoolean());
        Assert.False(st.GetProperty("running").GetBoolean());
        Assert.True(st.GetProperty("isCampaign").GetBoolean());
        Assert.Equal("sample-crypt", st.GetProperty("moduleId").GetString());

        // 3. Running a live-debug command before starting returns 400 with a clear hint
        using StringContent cmdBody = new("""{"command":"tales.state"}""", System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage cmdResp = await http.PostAsync("/api/playtest/command", cmdBody);
        Assert.Equal(HttpStatusCode.BadRequest, cmdResp.StatusCode);
        using JsonDocument cmdJson = JsonDocument.Parse(await cmdResp.Content.ReadAsStringAsync());
        Assert.False(cmdJson.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("No active playtest session", cmdJson.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);

        // 4. Stopping when inactive is idempotent and succeeds
        using HttpResponseMessage stopResp = await http.PostAsync("/api/playtest/stop", content: null);
        Assert.Equal(HttpStatusCode.OK, stopResp.StatusCode);
        using JsonDocument stopJson = JsonDocument.Parse(await stopResp.Content.ReadAsStringAsync());
        Assert.True(stopJson.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(stopJson.RootElement.GetProperty("stopped").GetBoolean());
    }
}
