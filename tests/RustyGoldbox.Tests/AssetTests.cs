using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Definitions;
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
    public void AnImagesRegionsLieInsideIt()
    {
        using TempModules modules = new();
        string art = modules.Module("art", "assets");
        File.Copy(Image("rgba-32x96.png"), Path.Combine(art, "strip.png"));

        // A vertical strip of non-square regions: the format fixes no layout or size.
        modules.Write("art/strip.json", """{ "type": "asset", "id": "strip", "media": "image", "file": "strip.png", "regions": { "wall": [0, 0, 32, 48], "door": [0, 48, 32, 48] } }""");
        ModuleSet set = ModuleLoader.Load(art, []);
        Assert.Empty(set.Diagnostics);
        Assert.Equal((32, 96), set.Rules!.ImageSizes.Single().Value);

        // Any names: what a use needs is the slot's business.
        modules.Write("art/strip.json", """{ "type": "asset", "id": "strip", "media": "image", "file": "strip.png", "regions": { "wall": [0, 60, 32, 48], "window": [0, 0, 1, 1] } }""");
        modules.Write("art/frames.json", """{ "type": "asset", "id": "frames", "media": "sheet", "file": "strip.png", "frame_size": [32, 48], "regions": { "wall": [0, 0, 1, 1] } }""");
        Assert.Equal(
            [("asset.regions", "$.regions"), ("asset.regions", "$.regions.wall")],
            ModuleLoader.Load(art, []).Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)).Order());
    }

    [Fact]
    public void AudioFitsSoundAndMusicSlotsOnly()
    {
        using TempModules modules = new();
        modules.Module("rules", "ruleset");
        string art = modules.Module("art", "assets");
        File.Copy(Image("rgba-32x96.png"), Path.Combine(art, "a.png"));
        File.Copy(Path.Combine(Rules.RepositoryRoot, "modules", "placeholder-art", "sounds", "bones.wav"), Path.Combine(art, "crack.wav"));
        modules.Write("art/picture.json", """{ "type": "asset", "id": "picture", "media": "image", "file": "a.png" }""");
        modules.Write("art/crack.json", """{ "type": "asset", "id": "crack", "media": "audio", "file": "crack.wav" }""");
        Assert.Empty(ModuleLoader.Load(art, []).Diagnostics);

        // A PNG isn't audio, and audio has no picture fields.
        modules.Write("art/fake.json", """{ "type": "asset", "id": "fake", "media": "audio", "file": "a.png", "frame_size": [1, 1] }""");
        Assert.Equal([("asset.audio", "$.file"), ("asset.audio", "$.frame_size")],
            ModuleLoader.Load(art, []).Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)).Order());
        File.Delete(Path.Combine(art, "fake.json"));

        string tale = modules.Module("tale", "campaign", requires: $"{TempModules.Require("rules", "*")}, {TempModules.Require("art", "*")}");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 1 } }""");
        modules.Write("tale/hall.json", """{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+", "|  |", "+--+"], "entries": { "in": { "at": [0, 0], "facing": "north" } } }""");
        modules.Write("tale/swapped.json", """{ "type": "event", "id": "swapped", "kind": "text", "text": "!", "picture": "art:crack", "sound": "art:picture", "music": "art:crack" }""");

        Assert.Equal(
            [("$.picture", "art:crack is audio, but a picture is something seen."), ("$.sound", "art:picture is a picture (image), but a sound is audio.")],
            ModuleLoader.Load(tale, []).Diagnostics.Select(diagnostic => (diagnostic.JsonPath!, diagnostic.Message)).Order());
    }

    [Fact]
    public void ImagesMustBeRgbaPngs()
    {
        using TempModules modules = new();
        string art = modules.Module("art", "assets");
        File.Copy(Image("rgb.png"), Path.Combine(art, "rgb.png"));
        modules.Write("art/notes.svg", "<svg/>");
        modules.Write("art/rgb.json", """{ "type": "asset", "id": "rgb", "media": "image", "file": "rgb.png" }""");
        modules.Write("art/svg.json", """{ "type": "asset", "id": "svg", "media": "image", "file": "notes.svg" }""");

        List<ModuleDiagnostic> diagnostics = ModuleLoader.Load(art, []).Diagnostics.OrderBy(diagnostic => diagnostic.File, StringComparer.Ordinal).ToList();

        Assert.Equal(["asset.image", "asset.image"], diagnostics.Select(diagnostic => diagnostic.Rule));
        Assert.Contains("8-bit RGB PNG", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("is not a PNG file", diagnostics[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SlotsDecideWhichMediaFit()
    {
        using TempModules modules = new();
        modules.Module("rules", "ruleset");
        string art = modules.Module("art", "assets");
        File.Copy(Image("rgba-32x96.png"), Path.Combine(art, "a.png"));
        modules.Write("art/picture.json", """{ "type": "asset", "id": "picture", "media": "image", "file": "a.png" }""");
        modules.Write("art/flicker.json", """{ "type": "asset", "id": "flicker", "media": "sheet", "file": "a.png", "frame_size": [32, 48], "animations": { "flicker": { "frames": [0, 1], "fps": 4 } } }""");
        string tale = modules.Module("tale", "campaign", requires: $"{TempModules.Require("rules", "*")}, {TempModules.Require("art", "*")}");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 1 } }""");
        void Hall(string wallSet) => modules.Write("tale/hall.json", $$"""{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|     |", "+--+--+"], "wall_set": "{{wallSet}}", "cells": [ { "at": [0, 0], "backdrop": "art:picture" }, { "at": [1, 0], "backdrop": "art:flicker" } ], "entries": { "in": { "at": [0, 0], "facing": "north" } } }""");

        // A picture slot takes an image or a sheet; a wall set needs an image with wall and door regions.
        Hall("art:picture");
        ModuleDiagnostic regions = Assert.Single(ModuleLoader.Load(tale, []).Diagnostics);
        Hall("art:flicker");
        ModuleDiagnostic sheet = Assert.Single(ModuleLoader.Load(tale, []).Diagnostics);

        Assert.Equal(("reference.media", "$.wall_set"), (regions.Rule, regions.JsonPath));
        Assert.Equal("art:picture is used as a wall set, so it needs \"wall\" and \"door\" in its \"regions\" (pixel rectangles in the image).", regions.Message);
        Assert.Equal("art:flicker is a sheet, but a wall set is an image with wall and door regions.", sheet.Message);
    }

    [Fact]
    public void ASheetIsAWholeGridOfFramesWithAnimationsThatPlayFramesItHas()
    {
        using TempModules modules = new();
        string art = modules.Module("art", "assets");
        File.Copy(Image("rgba-32x96.png"), Path.Combine(art, "sheet.png"));

        // Two 32 x 48 frames down a strip, any size: the format fixes no layout.
        modules.Write("art/walker.json", """{ "type": "asset", "id": "walker", "media": "sheet", "file": "sheet.png", "frame_size": [32, 48], "faces": "left", "height": 1.5, "anchor": [16, 47], "animations": { "walk": { "frames": [0, 1], "fps": 6 } } }""");
        Assert.Empty(ModuleLoader.Load(art, []).Diagnostics);

        modules.Write("art/walker.json", """{ "type": "asset", "id": "walker", "media": "sheet", "file": "sheet.png", "frame_size": [32, 40], "faces": "left", "height": 1 }""");
        modules.Write("art/runner.json", """{ "type": "asset", "id": "runner", "media": "sheet", "file": "sheet.png", "frame_size": [16, 48], "frame_count": 5, "height": 0 }""");
        modules.Write("art/jumper.json", """{ "type": "asset", "id": "jumper", "media": "sheet", "file": "sheet.png", "frame_size": [32, 48], "frame_count": 2, "faces": "right", "height": 1, "anchor": [32, 0], "animations": { "jump": { "frames": [0, 2], "fps": 0 } } }""");
        modules.Write("art/still.json", """{ "type": "asset", "id": "still", "media": "image", "file": "sheet.png", "faces": "left" }""");
        Assert.Equal(
            [
                ("jumper.json", "$.anchor"),
                ("jumper.json", "$.animations.jump.fps"),
                ("jumper.json", "$.animations.jump.frames[1]"),
                ("runner.json", "$.frame_count"),
                ("runner.json", "$.height"),
                ("still.json", "$.faces"),
                ("walker.json", "$.frame_size"),
            ],
            ModuleLoader.Load(art, []).Diagnostics.Select(diagnostic => (Path.GetFileName(diagnostic.File!), diagnostic.JsonPath!)).Order());
    }

    [Fact]
    public void TheSampleSpriteBecomesAnEngineAtlasWithAnimations()
    {
        ModuleSet set = ModuleLoader.Load(Path.Combine(Rules.RepositoryRoot, "modules", "placeholder-art"), []);
        Definition skeleton = set.Rules!.Find(Core.Definitions.DefinitionTypes.Asset, "skeleton", out _)!;
        byte[] png = File.ReadAllBytes(Path.Combine(Rules.RepositoryRoot, "modules", "placeholder-art", "sprites", "skeleton.png"));

        using EngineTestHost host = EngineTestHost.Create();
        host.Call(engine =>
        {
            using ContentReference reference = engine.Content.AdmitReference(new ContentAdmissionRequest("skeleton.png", png, Array.Empty<ContentSourceFile>()));
            using RenderResource texture = engine.Graphics.OpenResourceFromContent(new RenderResourceContentRequest(reference, TextureFilter.Nearest, TextureWrap.Clamp)).Handle;
            using Game.Presentation.SpriteArt art = Game.Presentation.SpriteArt.Admit(engine.Graphics, texture, skeleton, set.Rules.ImageSizes[skeleton]);
            using Appearance figure = art.CreateFigure();

            Assert.Equal(["idle", "attack"], art.Animations);
            using SpritePlayback idle = art.Play(figure, "idle")!;
            Assert.Null(art.Play(figure, "dance"));
            Assert.Equal(new System.Numerics.Vector3(-1, 1, 1), art.Scale(faceRight: false));

            // A flipped figure publishes like any other.
            engine.Graphics.PublishSnapshot(new[]
            {
                new AppearanceFact(1, false, 0, new Transform(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, art.Scale(faceRight: false)), figure, true, RenderLayer.Scene),
            });

            // Unpublish before the figure, atlas and texture are released.
            engine.Graphics.PublishSnapshot(Array.Empty<AppearanceFact>());
        });
    }

    [Fact]
    public void AFigureDrawsOneMonsterOrClassWithASheet()
    {
        using TempModules modules = new();
        string art = modules.Module("art", "assets");
        File.Copy(Image("rgba-32x96.png"), Path.Combine(art, "a.png"));
        modules.Write("art/rat.json", """{ "type": "asset", "id": "rat", "media": "sheet", "file": "a.png", "frame_size": [32, 48], "faces": "left", "height": 0.4 }""");
        modules.Write("art/picture.json", """{ "type": "asset", "id": "picture", "media": "image", "file": "a.png" }""");
        string house = modules.Module("house", "extension", requires: $"{TempModules.Require("classic", "*")}, {TempModules.Require("art", "*")}");
        modules.Write("house/rat.json", """{ "type": "figure", "id": "rat", "monster": "classic:giant_rat", "sprite": "art:rat" }""");
        string[] search = [modules.Root, Path.Combine(Rules.RepositoryRoot, "modules")];

        ModuleSet set = ModuleLoader.Load(house, search);
        Assert.Empty(set.Diagnostics);
        Assert.Equal("art:rat", set.Rules!.Figures.Single().Value.QualifiedId);
        Assert.Empty(set.Rules.Icons);

        File.Copy(Image("rgba-32x96.png"), Path.Combine(art, "i.png"));
        modules.Write("art/badge.json", """{ "type": "asset", "id": "badge", "media": "image", "file": "i.png" }""");
        modules.Write("house/rat.json", """{ "type": "figure", "id": "rat", "monster": "classic:giant_rat", "sprite": "art:rat", "icon": "art:badge" }""");
        Assert.Equal("art:badge", ModuleLoader.Load(house, search).Rules!.Icons.Single().Value.QualifiedId);

        modules.Write("house/again.json", """{ "type": "figure", "id": "again", "monster": "classic:giant_rat", "sprite": "art:rat" }""");
        modules.Write("house/nothing.json", """{ "type": "figure", "id": "nothing", "sprite": "art:rat" }""");
        modules.Write("house/portrait.json", """{ "type": "figure", "id": "portrait", "class": "classic:thief", "sprite": "art:picture" }""");
        Assert.Equal(
            [("nothing.json", "figure.subject"), ("portrait.json", "reference.media"), ("rat.json", "figure.duplicate")],
            ModuleLoader.Load(house, search).Diagnostics.Select(diagnostic => (Path.GetFileName(diagnostic.File!), diagnostic.Rule)).Order());
    }

    [Fact]
    public void AFigureCanDrawACombatFieldsTerrain()
    {
        // The sample crypt draws classic's pillars with the placeholder pillar.
        ModuleSet crypt = ModuleLoader.Load(Path.Combine(Rules.RepositoryRoot, "modules", "sample-crypt"), []);
        Assert.Empty(crypt.Diagnostics);
        ((Definition combat, char key), Definition sprite) = crypt.Rules!.TerrainFigures.Single();
        Assert.Equal(("classic:standard", '#', "placeholder-art:pillar"), (combat.QualifiedId, key, sprite.QualifiedId));

        using TempModules modules = new();
        string house = modules.Module("house", "extension", requires: $"{TempModules.Require("classic", "*")}, {TempModules.Require("placeholder-art", "*")}");
        modules.Write("house/mud.json", """{ "type": "figure", "id": "mud", "combat": "classic:standard", "terrain": "~", "sprite": "placeholder-art:pillar" }""");
        modules.Write("house/loose.json", """{ "type": "figure", "id": "loose", "terrain": "#", "sprite": "placeholder-art:pillar" }""");
        Assert.Equal(
            [("loose.json", "$.combat"), ("mud.json", "$.terrain")],
            ModuleLoader.Load(house, [Path.Combine(Rules.RepositoryRoot, "modules")]).Diagnostics
                .Select(diagnostic => (Path.GetFileName(diagnostic.File!), diagnostic.JsonPath!)).Order());
    }

    [Fact]
    public void APropIsASheetWithABooleanCondition()
    {
        using TempModules modules = new();
        modules.Module("rules", "ruleset");
        string art = modules.Module("art", "assets");
        File.Copy(Image("rgba-32x96.png"), Path.Combine(art, "a.png"));
        modules.Write("art/chest.json", """{ "type": "asset", "id": "chest", "media": "sheet", "file": "a.png", "frame_size": [32, 48], "faces": "right", "height": 0.5 }""");
        modules.Write("art/picture.json", """{ "type": "asset", "id": "picture", "media": "image", "file": "a.png" }""");
        string tale = modules.Module("tale", "campaign", requires: $"{TempModules.Require("rules", "*")}, {TempModules.Require("art", "*")}");
        modules.Write("tale/opened.json", """{ "type": "variable", "id": "opened", "value_type": "boolean", "initial": "false" }""");
        modules.Write("tale/campaign.json", """{ "type": "campaign", "id": "tale", "name": "Tale", "start": { "area": "hall", "entry": "in" }, "party": { "min": 1, "max": 1 } }""");
        void Hall(string cells) => modules.Write("tale/hall.json", $$"""{ "type": "area", "id": "hall", "name": "Hall", "map": ["+--+--+", "|     |", "+--+--+"], "cells": [{{cells}}], "entries": { "in": { "at": [0, 0], "facing": "east" } } }""");

        Hall("""{ "at": [1, 0], "prop": { "sprite": "art:chest", "hidden": "campaign.var.opened" } }""");
        Assert.Empty(ModuleLoader.Load(tale, []).Diagnostics);

        Hall("""{ "at": [1, 0], "prop": { "sprite": "art:picture", "hidden": "1 + 1" } }""");
        Assert.Equal(
            [("expression.type", "$.cells[0].prop.hidden"), ("reference.media", "$.cells[0].prop.sprite")],
            ModuleLoader.Load(tale, []).Diagnostics.Select(diagnostic => (diagnostic.Rule, diagnostic.JsonPath!)).Order());
    }
}
