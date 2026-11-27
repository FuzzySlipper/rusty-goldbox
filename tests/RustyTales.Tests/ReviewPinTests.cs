using System.Net;
using System.Text;
using System.Text.Json;
using RustyTales.Cli;
using RustyTales.Core.Authoring;
using RustyTales.Core.Modules;

namespace RustyTales.Tests;

/// <summary>
/// Verifies Workbench review pins: sidecar persistence outside module content,
/// exact module/file/JSON-path provenance, stale-approval detection on disk
/// edits, <c>tales review</c> CLI commands, and the live Workbench review API.
/// </summary>
public sealed class ReviewPinTests
{
    [Fact]
    public void CliReviewLoopResolvesCellDoorSceneAndAssetProvenance()
    {
        using TempModules scratch = new();
        string reviewFile = Path.Combine(scratch.Root, "reviews.json");
        string wizardry = Path.Combine(Rules.RepositoryRoot, "modules", "wizardry-delve");

        // 1. Flag a cell, a locked door, a lowered scene node event, and approve an asset.
        (int c1, string o1) = CampaignTests.Run(scratch,
            "review", "set", wizardry,
            "--kind", "cell", "--target", "wizardry-delve:catacombs@[3,0]",
            "--status", "needs-fix", "--note", "Boss room needs a warning sign before entry.",
            "--reviews", reviewFile, "--json");
        Assert.True(c1 == TalesCli.Ok, o1);

        (int c2, string o2) = CampaignTests.Run(scratch,
            "review", "set", wizardry,
            "--kind", "door", "--target", "wizardry-delve:catacombs#sanctum_gate",
            "--status", "question", "--note", "Should this also allow lockpicking?",
            "--reviews", reviewFile, "--json");
        Assert.True(c2 == TalesCli.Ok, o2);

        (int c3, string o3) = CampaignTests.Run(scratch,
            "review", "set", wizardry,
            "--kind", "event", "--target", "wizardry-delve:warden_scene__rumor",
            "--status", "needs-fix", "--note", "Tighten dialogue wording.",
            "--reviews", reviewFile, "--json");
        Assert.True(c3 == TalesCli.Ok, o3);

        (int c4, string o4) = CampaignTests.Run(scratch,
            "review", "set", wizardry,
            "--kind", "asset", "--target", "wizardry-art:sanctum",
            "--status", "approved", "--note", "Great atmosphere!",
            "--reviews", reviewFile, "--json");
        Assert.True(c4 == TalesCli.Ok, o4);

        // 2. Agent runs `tales review list --status open --json` and receives exact file + jsonPath for every pin.
        (int listCode, string listJson) = CampaignTests.Run(scratch,
            "review", "list", wizardry, "--status", "open", "--reviews", reviewFile, "--json");
        Assert.Equal(TalesCli.Ok, listCode);
        using JsonDocument listDoc = JsonDocument.Parse(listJson);
        JsonElement counts = listDoc.RootElement.GetProperty("counts");
        Assert.Equal(4, counts.GetProperty("total").GetInt32());
        Assert.Equal(3, counts.GetProperty("open").GetInt32());
        Assert.Equal(2, counts.GetProperty("needsFix").GetInt32());
        Assert.Equal(1, counts.GetProperty("question").GetInt32());
        Assert.Equal(1, counts.GetProperty("approved").GetInt32());

        JsonElement[] openPins = listDoc.RootElement.GetProperty("pins").EnumerateArray().ToArray();
        Assert.Equal(3, openPins.Length);

        JsonElement cellPin = Assert.Single(openPins, p => p.GetProperty("targetKind").GetString() == "cell");
        Assert.Contains(".cells[", cellPin.GetProperty("jsonPath").GetString(), StringComparison.Ordinal);
        Assert.True(cellPin.GetProperty("exists").GetBoolean());

        JsonElement doorPin = Assert.Single(openPins, p => p.GetProperty("targetKind").GetString() == "door");
        Assert.Contains(".doors[", doorPin.GetProperty("jsonPath").GetString(), StringComparison.Ordinal);
        Assert.Equal("sanctum_gate", doorPin.GetProperty("doorId").GetString());

        JsonElement scenePin = Assert.Single(openPins, p => p.GetProperty("targetKind").GetString() == "event");
        Assert.Contains(".nodes.rumor", scenePin.GetProperty("jsonPath").GetString(), StringComparison.Ordinal);
        Assert.Contains("scenes.json", scenePin.GetProperty("file").GetString(), StringComparison.Ordinal);

        // 3. `tales review show` returns the pin and definition JSON.
        string cellPinId = cellPin.GetProperty("id").GetString()!;
        (int showCode, string showJson) = CampaignTests.Run(scratch,
            "review", "show", wizardry, cellPinId, "--reviews", reviewFile, "--json");
        Assert.Equal(TalesCli.Ok, showCode);
        using JsonDocument showDoc = JsonDocument.Parse(showJson);
        Assert.Equal(cellPinId, showDoc.RootElement.GetProperty("pin").GetProperty("id").GetString());
        Assert.Equal("area", showDoc.RootElement.GetProperty("definition").GetProperty("type").GetString());

        // 4. Agent resolves the cell pin and clears resolved pins.
        (int resCode, string resJson) = CampaignTests.Run(scratch,
            "review", "resolve", wizardry, cellPinId, "--note", "Added warning inscription on [2,0].",
            "--reviews", reviewFile, "--json");
        Assert.Equal(TalesCli.Ok, resCode);
        using JsonDocument resDoc = JsonDocument.Parse(resJson);
        Assert.Equal("resolved", resDoc.RootElement.GetProperty("pin").GetProperty("status").GetString());
        Assert.Equal("Added warning inscription on [2,0].", resDoc.RootElement.GetProperty("pin").GetProperty("resolution").GetString());
        Assert.Equal(2, resDoc.RootElement.GetProperty("counts").GetProperty("open").GetInt32());

        (int clrCode, string clrJson) = CampaignTests.Run(scratch,
            "review", "clear", wizardry, "--status", "resolved", "--reviews", reviewFile, "--json");
        Assert.Equal(TalesCli.Ok, clrCode);
        using JsonDocument clrDoc = JsonDocument.Parse(clrJson);
        Assert.Equal(1, clrDoc.RootElement.GetProperty("cleared").GetInt32());
    }

    [Fact]
    public void ApprovedPinsBecomeStaleWhenDefinitionOrAssetBytesChangeWithoutAlteringModuleIdentity()
    {
        using TempModules scratch = new();
        string campDir = Path.Combine(scratch.Root, "stale-camp");
        Directory.CreateDirectory(campDir);
        File.WriteAllText(Path.Combine(campDir, "module.json"), """
            {
              "format": 1,
              "id": "stale-camp",
              "kind": "campaign",
              "version": "0.1.0",
              "title": "Stale Check",
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
                "map": ["+--+", "|  |", "+--+"],
                "entries": { "in": { "at": [0, 0], "facing": "north" } }
              },
              {
                "type": "event",
                "id": "intro",
                "kind": "text",
                "text": "Original intro text."
              },
              {
                "type": "campaign",
                "id": "camp",
                "name": "Stale Check",
                "start": { "area": "hall", "entry": "in" },
                "intro": "intro",
                "party": { "min": 1, "max": 4 }
              }
            ]
            """);

        string identityBefore = new DirectoryModuleSource(campDir).Identity;
        string defaultReviewPath = ReviewStore.DefaultPathFor(campDir, "stale-camp");
        Assert.DoesNotContain(campDir + Path.DirectorySeparatorChar, defaultReviewPath, StringComparison.Ordinal);

        // Approve the intro event using the default sidecar path.
        (int setCode, string setOut) = CampaignTests.Run(scratch,
            "review", "set", campDir,
            "--modules", Path.Combine(Rules.RepositoryRoot, "modules"),
            "--kind", "event", "--target", "stale-camp:intro",
            "--status", "approved", "--note", "Looks clean.", "--json");
        Assert.True(setCode == TalesCli.Ok, setOut);

        // Module identity is 100% unchanged because the sidecar lives outside the module directory.
        Assert.Equal(identityBefore, new DirectoryModuleSource(campDir).Identity);

        // Initially not stale.
        (int beforeCode, string beforeJson) = CampaignTests.Run(scratch,
            "review", "list", campDir,
            "--modules", Path.Combine(Rules.RepositoryRoot, "modules"),
            "--status", "stale", "--json");
        Assert.Equal(TalesCli.Ok, beforeCode);
        using (JsonDocument doc = JsonDocument.Parse(beforeJson))
        {
            Assert.Empty(doc.RootElement.GetProperty("pins").EnumerateArray());
            Assert.Equal(1, doc.RootElement.GetProperty("counts").GetProperty("approved").GetInt32());
        }

        // Modify the event text on disk -> prior approval automatically flips to stale!
        File.WriteAllText(Path.Combine(campDir, "world.json"),
            File.ReadAllText(Path.Combine(campDir, "world.json")).Replace("Original intro text.", "Edited intro text.", StringComparison.Ordinal));

        (int afterCode, string afterJson) = CampaignTests.Run(scratch,
            "review", "list", campDir,
            "--modules", Path.Combine(Rules.RepositoryRoot, "modules"),
            "--status", "stale", "--json");
        Assert.Equal(TalesCli.Ok, afterCode);
        using (JsonDocument doc = JsonDocument.Parse(afterJson))
        {
            JsonElement stalePin = Assert.Single(doc.RootElement.GetProperty("pins").EnumerateArray());
            Assert.True(stalePin.GetProperty("stale").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("counts").GetProperty("stale").GetInt32());
            Assert.Equal(0, doc.RootElement.GetProperty("counts").GetProperty("approved").GetInt32());
        }
    }

    [Fact]
    public async Task WorkbenchReviewApiRoundTripsWithCliSidecar()
    {
        using TempModules scratch = new();
        string reviewFile = Path.Combine(scratch.Root, "wb-reviews.json");
        using WorkbenchServer server = new(
            CampaignTests.SampleCrypt,
            [Path.Combine(Rules.RepositoryRoot, "modules")],
            [],
            "127.0.0.1",
            port: 0,
            reviewsPath: reviewFile);
        server.Start();

        using HttpClient http = new() { BaseAddress = new Uri(server.Url) };

        // 1. Browser POSTs a needs-fix pin on cell [3,1] and an approved pin on area entrance.
        using HttpResponseMessage r1 = await http.PostAsync("/api/reviews", new StringContent(
            """{"targetKind":"cell","target":"sample-crypt:entrance@[3,1]","status":"needs-fix","note":"Add torch hint near barred door"}""",
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        using JsonDocument d1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync());
        string pinId = d1.RootElement.GetProperty("pin").GetProperty("id").GetString()!;
        Assert.Equal("needs-fix", d1.RootElement.GetProperty("pin").GetProperty("status").GetString());
        Assert.Equal(1, d1.RootElement.GetProperty("counts").GetProperty("open").GetInt32());

        using HttpResponseMessage r2 = await http.PostAsync("/api/reviews", new StringContent(
            """{"targetKind":"area","target":"sample-crypt:depths","status":"approved","note":"Layout feels good"}""",
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        // 2. CLI reads the exact same sidecar file.
        (int cliCode, string cliJson) = CampaignTests.Run(scratch,
            "review", "list", CampaignTests.SampleCrypt, "--status", "needs-fix", "--reviews", reviewFile, "--json");
        Assert.Equal(TalesCli.Ok, cliCode);
        using JsonDocument cliDoc = JsonDocument.Parse(cliJson);
        JsonElement cliPin = Assert.Single(cliDoc.RootElement.GetProperty("pins").EnumerateArray());
        Assert.Equal(pinId, cliPin.GetProperty("id").GetString());
        Assert.Equal("Add torch hint near barred door", cliPin.GetProperty("note").GetString());

        // 3. Resolve via HTTP API, verify counts, then delete via HTTP API.
        using HttpResponseMessage r3 = await http.PostAsync($"/api/reviews/{pinId}/resolve", new StringContent(
            """{"resolution":"Added torch hint in barred_door text"}""",
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        using JsonDocument d3 = JsonDocument.Parse(await r3.Content.ReadAsStringAsync());
        Assert.Equal("resolved", d3.RootElement.GetProperty("pin").GetProperty("status").GetString());
        Assert.Equal(0, d3.RootElement.GetProperty("counts").GetProperty("open").GetInt32());

        using HttpResponseMessage r4 = await http.DeleteAsync($"/api/reviews/{pinId}");
        Assert.Equal(HttpStatusCode.OK, r4.StatusCode);
        using JsonDocument d4 = JsonDocument.Parse(await r4.Content.ReadAsStringAsync());
        Assert.Equal(1, d4.RootElement.GetProperty("counts").GetProperty("total").GetInt32());
    }
}
