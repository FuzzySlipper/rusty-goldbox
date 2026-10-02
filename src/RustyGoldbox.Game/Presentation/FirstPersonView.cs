using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Game.Presentation;

/// <summary>
/// The first-person view: the area as one generated mesh textured from its
/// wall set, a camera at the party's cell facing its way, and the cell's
/// backdrop over the view when it has one. The view sits in a Gold Box-style
/// window at the top left; the DOM panels go around it. The mesh is rebuilt
/// only when the area changes; each publish sends the whole small snapshot.
/// </summary>
internal sealed class FirstPersonView : IDisposable
{
    /// <summary>The view window, as fractions of the screen from its top left.</summary>
    public static readonly (float X, float Y, float Width, float Height) Window = (0.01f, 0.02f, 0.47f, 0.62f);

    private static readonly Transform Placed = new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    private const ulong AreaObject = 1;
    private const ulong BackdropObject = 2;
    private const double EyeHeight = 0.5;
    private const double FieldOfView = 70;

    private readonly IEngineContext _engine;
    private readonly ModuleLibrary _library;
    private readonly Camera _camera;
    private readonly Material _plain;
    private readonly Dictionary<string, Art> _art = [];
    private string? _areaKey;
    private MeshResource? _mesh;
    private Appearance? _area;

    public FirstPersonView(IEngineContext engine, ModuleLibrary library)
    {
        _engine = engine;
        _library = library;
        _camera = engine.CameraView.CreateCamera(Camera(Vector3.Zero, 0));
        engine.CameraView.SetActiveCamera(_camera);
        engine.CameraView.SetBackgroundColor(new SetBackgroundColorRequest(new Color(0.03f, 0.03f, 0.05f, 1)));
        _plain = engine.Graphics.CreateMaterial(new MaterialRequest(new Color(0.25f, 0.24f, 0.22f, 1), default, 0.95f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false));
    }

    /// <summary>Shows the session: the area around the party while playing, nothing otherwise.</summary>
    public void Show(GameSession session)
    {
        List<AppearanceFact> facts = [];
        if (session.Screen == Screen.Play && session.Runner is CampaignRunner runner && session.Set?.Rules is RuleSet rules)
        {
            CampaignState state = runner.State;
            ShowArea(rules, session.Set, state.Area, facts);
            Vector3 eye = new(state.X + 0.5f, (float)EyeHeight, state.Y + 0.5f);
            _engine.CameraView.UpdateCamera(new CameraUpdateRequest(_camera, Camera(eye, (int)state.Facing * 90)));
            if (Backdrop(rules, state) is Definition backdrop && ArtFor(rules, session.Set, backdrop) is { Sprite: Appearance sprite })
            {
                facts.Add(new AppearanceFact(BackdropObject, false, 0, Placed, sprite, true, RenderLayer.Ui));
            }
        }

        _engine.Graphics.PublishSnapshot(facts.ToArray());
    }

    public void Dispose()
    {
        _area?.Dispose();
        _mesh?.Dispose();
        foreach (Art art in _art.Values)
        {
            art.Sprite?.Dispose();
            art.Material.Dispose();
            art.Texture.Dispose();
        }

        _plain.Dispose();
        _camera.Dispose();
    }

    private void ShowArea(RuleSet rules, ModuleSet set, Definition area, List<AppearanceFact> facts)
    {
        Definition? wallSet = area.Json.TryGetProperty("wall_set", out _) ? rules.Reference(area, "$.wall_set") : null;
        string key = $"{area.QualifiedId}@{set.LoadOrder.First(loaded => loaded.Manifest.Id == area.Module).Manifest.Source.Identity}";
        if (key != _areaKey)
        {
            _area?.Dispose();
            _mesh?.Dispose();
            _area = null;
            _mesh = null;
            _areaKey = key;
            Build(rules, set, area, wallSet);
        }

        if (_area is not null)
        {
            facts.Add(new AppearanceFact(AreaObject, false, 0, Placed, _area, true, RenderLayer.Scene));
        }
    }

    private void Build(RuleSet rules, ModuleSet set, Definition area, Definition? wallSet)
    {
        AreaMap map = AreaMap.Parse(area.Json.GetProperty("map").EnumerateArray().Select(row => row.GetString()!).ToList(), [])!;
        Art? art = wallSet is null ? null : ArtFor(rules, set, wallSet);
        Dictionary<string, UvRect> frames = art is null ? PlainFrames() : Frames(wallSet!, rules.ImageSizes[wallSet!]);
        AreaGeometry geometry = AreaMesh.Build(map, frames);
        Material textured = art?.Material ?? _plain;
        _mesh = _engine.Graphics.CreateMeshResource(new MeshResourceCreateRequest(
            geometry.Positions,
            geometry.Normals,
            geometry.Uvs,
            geometry.Indices,
            geometry.Groups.Select(group => new MeshGroup(group.Slot, group.Start, group.Count)).ToArray(),
            // The Engine wants a binding for each slot the groups use, and no others.
            geometry.Groups.Select(group => new MeshMaterialBinding(group.Slot, group.Slot == AreaMesh.TexturedSlot ? textured : _plain)).ToArray()));
        _area = _engine.Graphics.CreateMeshAppearance(_mesh);
    }

    /// <summary>Without a wall set, walls and doors draw in the plain material.</summary>
    private static Dictionary<string, UvRect> PlainFrames()
    {
        UvRect whole = new(0, 0, 1, 1);
        return new Dictionary<string, UvRect> { ["wall"] = whole, ["door"] = whole };
    }

    private static Dictionary<string, UvRect> Frames(Definition wallSet, (int Width, int Height) size)
    {
        Dictionary<string, UvRect> frames = [];
        foreach (JsonProperty frame in wallSet.Json.GetProperty("frames").EnumerateObject())
        {
            int[] rect = frame.Value.EnumerateArray().Select(value => value.GetInt32()).ToArray();
            frames[frame.Name] = new UvRect(
                (float)rect[0] / size.Width,
                (float)rect[1] / size.Height,
                (float)(rect[0] + rect[2]) / size.Width,
                (float)(rect[1] + rect[3]) / size.Height);
        }

        return frames;
    }

    private static Definition? Backdrop(RuleSet rules, CampaignState state)
    {
        if (!state.Area.Json.TryGetProperty("cells", out JsonElement cells))
        {
            return null;
        }

        int index = 0;
        foreach (JsonElement cell in cells.EnumerateArray())
        {
            JsonElement at = cell.GetProperty("at");
            if (at[0].GetInt32() == state.X && at[1].GetInt32() == state.Y && cell.TryGetProperty("backdrop", out _))
            {
                return rules.Reference(state.Area, $"$.cells[{index}].backdrop");
            }

            index++;
        }

        return null;
    }

    /// <summary>
    /// The asset's texture (and, for a wall set, its material; for a backdrop,
    /// its sprite in the view window), admitted once per asset content.
    /// </summary>
    private Art? ArtFor(RuleSet rules, ModuleSet set, Definition asset)
    {
        ModuleSource source = set.LoadOrder.First(loaded => loaded.Manifest.Id == asset.Module).Manifest.Source;
        string key = $"{asset.QualifiedId}@{source.Identity}";
        if (_art.TryGetValue(key, out Art? cached))
        {
            return cached;
        }

        string file = asset.Json.GetProperty("file").GetString()!;
        RenderResource? texture = _library.ReadModule(source.Location, bundle =>
        {
            using ContentReference reference = bundle.OpenReference(file);
            return _engine.Graphics.OpenResourceFromContent(new RenderResourceContentRequest(reference, TextureFilter.Nearest, TextureWrap.Clamp)).Handle;
        });
        if (texture is null)
        {
            return null;
        }

        Material material = _engine.Graphics.CreateMaterial(new MaterialRequest(new Color(1, 1, 1, 1), texture, 0.95f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false));
        Appearance? sprite = null;
        if (asset.Json.GetProperty("kind").GetString() == "backdrop")
        {
            (int width, int height) = rules.ImageSizes[asset];
            sprite = _engine.Graphics.CreateSprite(new SpriteAppearanceRequest(
                texture, Vector2.Zero, Vector2.One, new Vector2(0.5f, 0.5f), new Vector2(width, height),
                BillboardMode.None, SpriteSizeMode.Pixel, 100, SpriteDepthPolicy.DepthTestOff, new Color(1, 1, 1, 1)));

            // Sprite placement is a rectangle within the camera's viewport: this fills the view window.
            _engine.Graphics.SetSpriteViewport(new SpriteViewportUpdateRequest(
                sprite, true, Vector2.Zero, Vector2.One, new Vector2(0.5f, 0.5f), SpriteViewportFit.Contain));
        }

        Art art = new(texture, material, sprite);
        _art[key] = art;
        return art;
    }

    private static CameraDescriptor Camera(Vector3 eye, double yaw)
    {
        return new CameraDescriptor(
            new CameraPose(eye, 0, yaw),
            CameraBasisMode.Derived,
            default,
            new CameraProjection(CameraProjectionKind.Perspective, FieldOfView, 0, 0.05, 64),
            // Camera viewports, like sprite placement, measure from the screen's lower left.
            new CameraViewport(Window.X, 1 - Window.Y - Window.Height, Window.Width, Window.Height));
    }

    private sealed record Art(RenderResource Texture, Material Material, Appearance? Sprite);
}
