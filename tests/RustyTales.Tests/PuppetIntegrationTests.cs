using System.Text.Json;
using RustyTales.Cli;
using RustyTales.Core.Modules;
using Xunit;

namespace RustyTales.Tests;

public sealed class PuppetIntegrationTests
{
    [Fact]
    public void PngRgbaRoundTripsAndPassesCorePngImageValidation()
    {
        PngRgba img = new(16, 12);
        // Paint an opaque red square inside a transparent frame.
        for (int y = 2; y < 10; y++)
        {
            for (int x = 4; x < 12; x++)
            {
                int idx = ((y * img.Width) + x) * 4;
                img.Pixels[idx] = 220;
                img.Pixels[idx + 1] = 40;
                img.Pixels[idx + 2] = 60;
                img.Pixels[idx + 3] = 255;
            }
        }

        byte[] encoded = img.Encode();
        Assert.Null(PngImage.Read(encoded, out int w, out int h));
        Assert.Equal(16, w);
        Assert.Equal(12, h);

        PngRgba decoded = PngRgba.Decode(encoded);
        Assert.Equal(img.Pixels, decoded.Pixels);

        PngRgba resized = decoded.Resize(8, 6);
        Assert.Equal(8, resized.Width);
        Assert.Equal(6, resized.Height);
        // Center pixel stays opaque red, corner stays transparent.
        int corner = 0;
        Assert.Equal(0, resized.Pixels[corner + 3]);
        int center = ((3 * 8) + 4) * 4;
        Assert.Equal(255, resized.Pixels[center + 3]);
        Assert.True(resized.Pixels[center] > 180);
    }

    [Fact]
    public async Task PuppetExportSheetPacksTightUnionGridWithProjectedAnchorAndValidatesAsModule()
    {
        using TempModules temp = new();
        string charDir = Path.Combine(temp.Root, "puppets", "hero_test");
        string idleDir = Path.Combine(charDir, "baked", "idle");
        string attackDir = Path.Combine(charDir, "baked", "attack");
        string moduleDir = Path.Combine(temp.Root, "hero-art");
        Directory.CreateDirectory(idleDir);
        Directory.CreateDirectory(attackDir);
        Directory.CreateDirectory(moduleDir);

        File.WriteAllText(Path.Combine(charDir, "character.json"), """
        {
          "spec": "puppet-character/1",
          "name": "Hero Test",
          "archetype": "melee",
          "fps": 12
        }
        """);

        File.WriteAllText(Path.Combine(charDir, "review-flags.json"), """
        {
          "spec": "puppet-review-flags/1",
          "flags": [
            {
              "id": "flag_001",
              "animation": "attack",
              "frame": 1,
              "tag": "grip-offset",
              "part": "weapon",
              "bbox_px": [10, 12, 30, 40],
              "crop": "flags/flag_001.png",
              "note": "Shift sword hilt 4px right.",
              "resolved": false
            }
          ]
        }
        """);

        // Write two small 8-bit RGBA sprite pages simulating multi-clip TexturePacker output.
        PngRgba idlePage = new(40, 40);
        FillRect(idlePage, 2, 2, 16, 24, 60, 180, 90, 255);
        FillRect(idlePage, 20, 2, 16, 24, 70, 200, 100, 255);
        idlePage.Save(Path.Combine(idleDir, "sheet.png"));
        File.WriteAllText(Path.Combine(idleDir, "sheet-manifest.json"), """
        {
          "spec": "puppet-sheet/1",
          "animation": "idle",
          "fps": 10,
          "frameCount": 2,
          "frames": [
            {
              "index": 0, "image": "sheet.png", "empty": false,
              "frame": { "x": 2, "y": 2, "w": 16, "h": 24 },
              "spriteSourceSize": { "x": 40, "y": 30, "w": 16, "h": 24 },
              "sourceSize": { "w": 100, "h": 100 },
              "anchor": { "x": 0.48, "y": 0.54 }
            },
            {
              "index": 1, "image": "sheet.png", "empty": false,
              "frame": { "x": 20, "y": 2, "w": 16, "h": 24 },
              "spriteSourceSize": { "x": 40, "y": 28, "w": 16, "h": 24 },
              "sourceSize": { "w": 100, "h": 100 },
              "anchor": { "x": 0.48, "y": 0.54 }
            }
          ]
        }
        """);

        PngRgba attackPage = new(40, 40);
        FillRect(attackPage, 2, 2, 24, 24, 220, 90, 60, 255);
        attackPage.Save(Path.Combine(attackDir, "sheet.png"));
        File.WriteAllText(Path.Combine(attackDir, "sheet-manifest.json"), """
        {
          "spec": "puppet-sheet/1",
          "animation": "attack",
          "fps": 14,
          "frameCount": 1,
          "frames": [
            {
              "index": 0, "image": "sheet.png", "empty": false,
              "frame": { "x": 2, "y": 2, "w": 24, "h": 24 },
              "spriteSourceSize": { "x": 40, "y": 30, "w": 24, "h": 24 },
              "sourceSize": { "w": 100, "h": 100 },
              "anchor": { "x": 0.48, "y": 0.54 }
            }
          ]
        }
        """);

        File.WriteAllText(Path.Combine(moduleDir, "module.json"), """
        {
          "format": 1,
          "id": "hero-art",
          "kind": "assets",
          "version": "0.1.0",
          "title": "Hero Puppet Art",
          "requires": [],
          "provenance": "Unit test."
        }
        """);

        // Run CLI export-sheet (tight union is x=40..64 (w=24), y=28..54 (h=26), +2px pad => 28x30 native).
        using StringWriter sw = new();
        int exit = TalesCli.Run(
            ["puppet", "export-sheet", charDir, "--module", moduleDir, "--clips", "idle,attack", "--height", "0", "--world-height", "1.05", "--json"],
            sw,
            temp.Root);
        Assert.Equal(TalesCli.Ok, exit);

        using JsonDocument doc = JsonDocument.Parse(sw.ToString());
        JsonElement exported = doc.RootElement.GetProperty("exported");
        Assert.Equal("hero_test", exported.GetProperty("assetId").GetString());
        Assert.Equal(28, exported.GetProperty("frameWidth").GetInt32());
        Assert.Equal(30, exported.GetProperty("frameHeight").GetInt32());
        Assert.Equal(3, exported.GetProperty("frameCount").GetInt32());
        // Anchor: orig=(48, 54), crop=(38, 26) -> projected=(10, 28)
        Assert.Equal(10, exported.GetProperty("anchorX").GetInt32());
        Assert.Equal(28, exported.GetProperty("anchorY").GetInt32());

        // The exported module must validate cleanly in Core!
        ModuleSet loaded = ModuleLoader.Load(moduleDir, [temp.Root]);
        Assert.True(loaded.IsValid, string.Join("\n", loaded.Diagnostics.Select(d => d.Message)));

        // Flags CLI and schema topic must surface structured JSON.
        using StringWriter flagsSw = new();
        int flagsExit = TalesCli.Run(["puppet", "flags", charDir, "--open-only", "--json"], flagsSw, temp.Root);
        Assert.Equal(TalesCli.Ok, flagsExit);
        using JsonDocument flagsDoc = JsonDocument.Parse(flagsSw.ToString());
        Assert.Equal(1, flagsDoc.RootElement.GetProperty("openCount").GetInt32());
        Assert.Equal("grip-offset", flagsDoc.RootElement.GetProperty("flags")[0].GetProperty("tag").GetString());

        using StringWriter schemaSw = new();
        Assert.Equal(TalesCli.Ok, TalesCli.Run(["schema", "puppets", "--json"], schemaSw, temp.Root));
        using JsonDocument schemaDoc = JsonDocument.Parse(schemaSw.ToString());
        Assert.Equal("puppets", schemaDoc.RootElement.GetProperty("topic").GetString());

        // WorkbenchServer exposes /api/puppets/status cleanly with discovered characters.
        using WorkbenchServer server = new(moduleDir, [temp.Root], [], "127.0.0.1", 0, puppetsDir: Path.Combine(temp.Root, "puppets"));
        server.Start();
        using HttpClient http = new();
        string statusJson = await http.GetStringAsync($"{server.Url}api/puppets/status");
        using JsonDocument statusDoc = JsonDocument.Parse(statusJson);
        Assert.True(statusDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, statusDoc.RootElement.GetProperty("characters").GetArrayLength());
        Assert.Equal("hero_test", statusDoc.RootElement.GetProperty("characters")[0].GetProperty("id").GetString());
        Assert.Equal(1, statusDoc.RootElement.GetProperty("openFlagCount").GetInt32());
    }

    private static void FillRect(PngRgba img, int x0, int y0, int w, int h, byte r, byte g, byte b, byte a)
    {
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                int idx = ((y * img.Width) + x) * 4;
                img.Pixels[idx] = r;
                img.Pixels[idx + 1] = g;
                img.Pixels[idx + 2] = b;
                img.Pixels[idx + 3] = a;
            }
        }
    }
}
