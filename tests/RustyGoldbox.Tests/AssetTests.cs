using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Tests;

/// <summary>Presentation assets: RGBA PNG images, wall-set frames and kind-checked references.</summary>
public sealed class AssetTests
{
    private static string Image(string name) => Path.Combine(Rules.RepositoryRoot, "tests", "RustyGoldbox.Tests", "Fixtures", "images", name);

    [Fact]
    public void TheImageCheckAgreesWithTheEngineRenderer()
    {
        using EngineTestHost host = EngineTestHost.Create();
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(Image("rgb.png"))!, "*.png"))
        {
            byte[] bytes = File.ReadAllBytes(file);
            bool admitted = host.Call(engine =>
            {
                using ContentReference reference = engine.Content.AdmitReference(new ContentAdmissionRequest(Path.GetFileName(file), bytes, Array.Empty<ContentSourceFile>()));
                try
                {
                    using RenderResource texture = engine.Graphics.OpenResourceFromContent(new RenderResourceContentRequest(reference, TextureFilter.Nearest, TextureWrap.Clamp)).Handle;
                    return true;
                }
                catch (EngineCallException)
                {
                    return false;
                }
            });

            Assert.True(admitted == (PngImage.Read(bytes, out _, out _) is null), $"{Path.GetFileName(file)}: Engine admitted {admitted}");
        }
    }

    [Fact]
    public void AWallSetIsAnyImageWithItsFramesInside()
    {
        using TempModules modules = new();
        string art = modules.Module("art", "assets");
        File.Copy(Image("rgba-32x96.png"), Path.Combine(art, "strip.png"));

        // A vertical strip of non-square frames: the format fixes no layout or size.
        modules.Write("art/strip.json", """{ "type": "asset", "id": "strip", "kind": "wall_set", "file": "strip.png", "frames": { "wall": [0, 0, 32, 48], "door": [0, 48, 32, 48] } }""");
        ModuleSet set = ModuleLoader.Load(art, []);
        Assert.Empty(set.Diagnostics);
        Assert.Equal((32, 96), set.Rules!.ImageSizes.Single().Value);

        modules.Write("art/strip.json", """{ "type": "asset", "id": "strip", "kind": "wall_set", "file": "strip.png", "frames": { "wall": [0, 60, 32, 48], "window": [0, 0, 1, 1] } }""");
        modules.Write("art/still.json", """{ "type": "asset", "id": "still", "kind": "backdrop", "file": "strip.png", "frames": { "wall": [0, 0, 1, 1] } }""");
        Assert.Equal(
            [("asset.frames", "$.frames"), ("asset.frames", "$.frames"), ("asset.frames", "$.frames.wall"), ("asset.frames", "$.frames.window")],
            ModuleLoader.Load(art, []).Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)).Order());
    }

    [Fact]
    public void ImagesMustBeRgbaPngs()
    {
        using TempModules modules = new();
        string art = modules.Module("art", "assets");
        File.Copy(Image("rgb.png"), Path.Combine(art, "rgb.png"));
        modules.Write("art/notes.svg", "<svg/>");
        modules.Write("art/rgb.json", """{ "type": "asset", "id": "rgb", "kind": "portrait", "file": "rgb.png" }""");
        modules.Write("art/svg.json", """{ "type": "asset", "id": "svg", "kind": "icon", "file": "notes.svg" }""");

        List<ModuleDiagnostic> diagnostics = ModuleLoader.Load(art, []).Diagnostics.OrderBy(diagnostic => diagnostic.File, StringComparer.Ordinal).ToList();

        Assert.Equal(["asset.image", "asset.image"], diagnostics.Select(diagnostic => diagnostic.Rule));
        Assert.Contains("8-bit RGB PNG", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("is not a PNG file", diagnostics[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AreasNeedAssetsOfTheRightKind()
    {
        using TempModules modules = new();
        modules.Module("rules", "ruleset");
        string art = modules.Module("art", "assets");
        File.Copy(Image("rgba-32x96.png"), Path.Combine(art, "a.png"));
        modules.Write("art/picture.json", """{ "type": "asset", "id": "picture", "kind": "backdrop", "file": "a.png" }""");
        string tale = modules.Module("tale", "campaign", requires: $"{TempModules.Require("rules", "*")}, {TempModules.Require("art", "*")}");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "wall_set": "art:picture", "cells": [ { "at": [0, 0], "backdrop": "art:picture" } ], "entries": { "in": { "at": [0, 0], "facing": "north" } } }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 1 } }""");

        ModuleDiagnostic diagnostic = Assert.Single(ModuleLoader.Load(tale, []).Diagnostics);

        Assert.Equal(("reference.asset-kind", "$.wall_set"), (diagnostic.Rule, diagnostic.JsonPath));
        Assert.Equal("art:picture is a backdrop asset, but this needs a wall_set.", diagnostic.Message);
    }
}
