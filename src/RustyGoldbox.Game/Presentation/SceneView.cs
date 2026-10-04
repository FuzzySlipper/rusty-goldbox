using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Game.Presentation;

/// <summary>
/// The Engine scene in the view window at the top left (the DOM panels go
/// around it). While playing it is the first-person view: the area as one
/// generated mesh textured from its wall set, a camera at the party's cell
/// facing its way, and a picture over the view (the latest event's, else the
/// cell's backdrop) through <see cref="PictureArt"/>, animated if it is; the
/// mesh is rebuilt only when the area changes. On the combat screen it is the
/// <see cref="CombatScene"/>. Each publish sends the whole small snapshot.
/// </summary>
internal sealed class SceneView : IDisposable
{
    private static readonly Transform Placed = new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    private const ulong AreaObject = 1;
    private const ulong BackdropObject = 2;
    private const ulong PropObjects = 10_000;
    private const double EyeHeight = 0.5;
    private const double FieldOfView = 70;

    /// <summary>The UI anchor the camera follows: the DOM's view panel.</summary>
    public const string ViewAnchor = "hero";

    private readonly IEngineContext _engine;
    private readonly ModuleLibrary _library;
    private readonly Camera _camera;
    private readonly Material _plain;
    private readonly Dictionary<string, Art> _art = [];
    private readonly CombatScene _combat;
    private readonly List<IDisposable> _retired = [];
    private readonly List<Prop> _props = [];
    private bool _showingCombat;
    private bool _showingArea;
    private string? _areaKey;
    private MeshResource? _mesh;
    private Appearance? _area;
    private Definition? _shownPicture;
    private SpritePlayback? _picturePlayback;
    private readonly PlaybackFrames _frames;
    private AppearanceFact[] _published = [];

    public SceneView(IEngineContext engine, ModuleLibrary library)
    {
        _engine = engine;
        _library = library;
        _camera = engine.CameraView.CreateCamera(Camera(new CameraPose(Vector3.Zero, 0, 0)));
        engine.CameraView.SetActiveCamera(_camera);
        // The camera draws over the panels' view element wherever the layout puts it; the whole window until a page anchors it.
        engine.CameraView.SetViewportAnchor(new CameraViewportAnchorRequest(_camera, ViewAnchor));
        engine.CameraView.SetBackgroundColor(new SetBackgroundColorRequest(new Color(0.03f, 0.03f, 0.05f, 1)));
        _plain = engine.Graphics.CreateMaterial(new MaterialRequest(new Color(0.25f, 0.24f, 0.22f, 1), default, 0.95f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false));
        _combat = new CombatScene(engine.Graphics, _plain);
        _frames = new PlaybackFrames(engine.Graphics);
    }

    /// <summary>Shows the session: the area around the party while playing, the fight on the combat screen, nothing otherwise.</summary>
    public void Show(GameSession session)
    {
        List<AppearanceFact> facts = [];
        _showingCombat = false;
        _showingArea = false;
        if (session.Runner is CampaignRunner runner && session.Set?.Rules is RuleSet rules)
        {
            CampaignState state = runner.State;
            if (session.Screen == Screen.Combat && session.Fight is FightReplay fight)
            {
                _showingCombat = true;
                _combat.Show(
                    fight,
                    kind => SpriteFor(rules, session.Set, kind),
                    key => fight.Fight.Field is CombatField field && rules.TerrainFigures.TryGetValue((field.Combat, key), out Definition? sprite) ? SpriteArtOf(rules, session.Set, sprite!) : null,
                    Floor(rules, session.Set, state.Area),
                    facts);
                _engine.CameraView.UpdateCamera(new CameraUpdateRequest(_camera, Camera(_combat.Pose, CombatScene.FieldOfView)));
            }
            else if (session.Screen == Screen.Play)
            {
                _showingArea = true;
                ShowArea(rules, session.Set, runner, state.Area, facts);
                ShowProps(runner, state.Area, facts);
                Vector3 eye = new(state.X + 0.5f, (float)EyeHeight, state.Y + 0.5f);
                _engine.CameraView.UpdateCamera(new CameraUpdateRequest(_camera, Camera(new CameraPose(eye, 0, (int)state.Facing * 90))));
                // An event's picture covers the view until the party moves; otherwise the cell's backdrop shows.
                Definition? shown = state.Picture ?? Backdrop(rules, state);
                if (shown is not null && PictureOf(rules, session.Set, shown) is PictureArt picture)
                {
                    if (shown != _shownPicture)
                    {
                        StopPicture();
                        _shownPicture = shown;
                        _picturePlayback = picture.Play();
                    }

                    facts.Add(new AppearanceFact(BackdropObject, false, 0, Placed, picture.Sprite, true, RenderLayer.Ui));
                }
                else
                {
                    StopPicture();
                }
            }
        }

        if (!_showingArea)
        {
            StopPicture();
        }

        _published = facts.ToArray();
        _engine.Graphics.PublishSnapshot(_published);
        _frames.Published();
        _combat.ReleaseRetired();
        ReleaseRetired();
    }

    /// <summary>
    /// Advances animations; call in every update. The renderer shows a
    /// playback's frame as of the latest snapshot, so when any frame changes
    /// the same snapshot is published again.
    /// </summary>
    public void Tick()
    {
        bool changed = false;
        if (_showingCombat)
        {
            changed |= _combat.Tick(_frames);
        }

        if (_showingArea)
        {
            if (_picturePlayback is SpritePlayback picture)
            {
                changed |= _frames.Advance(picture);
            }

            foreach (Prop prop in _props)
            {
                if (prop.Playback is SpritePlayback playback)
                {
                    changed |= _frames.Advance(playback);
                }
            }
        }

        if (changed)
        {
            _engine.Graphics.PublishSnapshot(_published);
        }
    }

    public void Dispose()
    {
        // Nothing may still be published when it is released.
        _engine.Graphics.PublishSnapshot(Array.Empty<AppearanceFact>());
        _combat.Dispose();
        StopPicture();
        RetireArea();
        ReleaseRetired();
        foreach (Art art in _art.Values)
        {
            art.Figures?.Dispose();
            art.Picture?.Dispose();
            art.Material.Dispose();
            art.Texture.Dispose();
        }

        _plain.Dispose();
        _camera.Dispose();
    }

    private void ShowArea(RuleSet rules, ModuleSet set, CampaignRunner runner, Definition area, List<AppearanceFact> facts)
    {
        Definition? wallSet = area.Json.TryGetProperty("wall_set", out _) ? rules.Reference(area, "$.wall_set") : null;
        string key = $"{area.QualifiedId}@{set.LoadOrder.First(loaded => loaded.Manifest.Id == area.Module).Manifest.Source.Identity}@{string.Join(',', runner.State.FoundSecrets.Order(StringComparer.Ordinal))}@{string.Join(',', runner.State.OpenedDoors.Order(StringComparer.Ordinal))}";
        if (key != _areaKey)
        {
            RetireArea();
            _areaKey = key;
            Build(rules, set, runner.PlayerMap(area), area, wallSet);
            BuildProps(rules, set, area);
        }

        if (_area is not null)
        {
            facts.Add(new AppearanceFact(AreaObject, false, 0, Placed, _area, true, RenderLayer.Scene));
        }
    }

    /// <summary>Each cell's prop, standing at the cell's centre as a billboard turned to the camera.</summary>
    private void BuildProps(RuleSet rules, ModuleSet set, Definition area)
    {
        if (!area.Json.TryGetProperty("cells", out JsonElement cells))
        {
            return;
        }

        int index = 0;
        foreach (JsonElement cell in cells.EnumerateArray())
        {
            string path = $"$.cells[{index}]";
            index++;
            if (!cell.TryGetProperty("prop", out JsonElement prop) || SpriteArtOf(rules, set, rules.Reference(area, $"{path}.prop.sprite")) is not SpriteArt art)
            {
                continue;
            }

            JsonElement at = cell.GetProperty("at");
            Appearance figure = art.CreateFigure();
            string? hidden = prop.TryGetProperty("hidden", out _) ? $"{path}.prop.hidden" : null;
            Transform placement = new(new Vector3(at[0].GetInt32() + 0.5f, 0, at[1].GetInt32() + 0.5f), Quaternion.Identity, art.Scale(art.FacesRight));
            _props.Add(new Prop(figure, art.Play(figure, "idle"), placement, hidden));
        }
    }

    private void ShowProps(CampaignRunner runner, Definition area, List<AppearanceFact> facts)
    {
        ulong id = PropObjects;
        foreach (Prop prop in _props)
        {
            bool there = prop.HiddenPath is null || !runner.IsTrue(area, prop.HiddenPath);
            facts.Add(new AppearanceFact(id++, false, 0, prop.Placement, prop.Figure, there, RenderLayer.Scene));
        }
    }

    /// <summary>Takes the current area's mesh and props out of use; they are released after the next publish.</summary>
    private void RetireArea()
    {
        foreach (Prop prop in _props)
        {
            if (prop.Playback is not null)
            {
                _retired.Add(prop.Playback);
            }

            _retired.Add(prop.Figure);
        }

        _props.Clear();
        if (_area is not null)
        {
            _retired.Add(_area);
            _retired.Add(_mesh!);
        }

        _area = null;
        _mesh = null;
    }

    private void ReleaseRetired()
    {
        foreach (IDisposable retired in _retired)
        {
            retired.Dispose();
        }

        _retired.Clear();
    }

    private void Build(RuleSet rules, ModuleSet set, AreaMap map, Definition area, Definition? wallSet)
    {
        Art? art = wallSet is null ? null : ArtFor(set, wallSet);
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
        foreach (JsonProperty frame in wallSet.Json.GetProperty("regions").EnumerateObject())
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

    /// <summary>The asset's texture and material, admitted once per asset content.</summary>
    private Art? ArtFor(ModuleSet set, Definition asset)
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
        Art art = new(texture, material);
        _art[key] = art;
        return art;
    }

    /// <summary>The asset as a picture over the view window, admitted once per asset content.</summary>
    private PictureArt? PictureOf(RuleSet rules, ModuleSet set, Definition asset)
    {
        if (ArtFor(set, asset) is not Art art)
        {
            return null;
        }

        art.Picture ??= PictureArt.Admit(_engine.Graphics, art.Texture, asset, rules.ImageSizes[asset]);
        return art.Picture;
    }

    /// <summary>Ends the shown picture's animation; the next picture shown starts its own.</summary>
    private void StopPicture()
    {
        _picturePlayback?.Dispose();
        _picturePlayback = null;
        _shownPicture = null;
    }

    /// <summary>A camera drawing into the view panel; its vertical field of view holds, so a wider panel sees more to the sides.</summary>
    private CameraDescriptor Camera(CameraPose pose, double fieldOfView = FieldOfView)
    {
        return new CameraDescriptor(
            pose,
            CameraBasisMode.Derived,
            default,
            new CameraProjection(CameraProjectionKind.Perspective, fieldOfView, 0, 0.05, 64),
            // The view anchor places it; this is where it draws when no page has anchored it.
            CameraViewports.Full);
    }

    /// <summary>The sprite a monster or class is drawn with, from the set's figures.</summary>
    private SpriteArt? SpriteFor(RuleSet rules, ModuleSet set, Definition kind)
    {
        return rules.Figures.TryGetValue(kind, out Definition? sprite) ? SpriteArtOf(rules, set, sprite) : null;
    }

    /// <summary>A figure-ready sheet's frames and animations, admitted once per asset content.</summary>
    private SpriteArt? SpriteArtOf(RuleSet rules, ModuleSet set, Definition sprite)
    {
        if (ArtFor(set, sprite) is not Art art)
        {
            return null;
        }

        art.Figures ??= SpriteArt.Admit(_engine.Graphics, art.Texture, sprite, rules.ImageSizes[sprite]);
        return art.Figures;
    }

    /// <summary>The area's wall set floor, which combat in the area stands on.</summary>
    private (Material Material, UvRect Frame)? Floor(RuleSet rules, ModuleSet set, Definition area)
    {
        if (!area.Json.TryGetProperty("wall_set", out _))
        {
            return null;
        }

        Definition wallSet = rules.Reference(area, "$.wall_set");
        Dictionary<string, UvRect> frames = Frames(wallSet, rules.ImageSizes[wallSet]);
        return frames.TryGetValue("floor", out UvRect floor) && ArtFor(set, wallSet) is Art art ? (art.Material, floor) : null;
    }

    /// <summary>A cell's prop: its figure and animation, where it stands, and the condition that hides it.</summary>
    private sealed record Prop(Appearance Figure, SpritePlayback? Playback, Transform Placement, string? HiddenPath);

    /// <summary>An admitted asset: its texture and material, and the picture or figure frames made from it once something shows it so.</summary>
    private sealed class Art(RenderResource texture, Material material)
    {
        public RenderResource Texture { get; } = texture;

        public Material Material { get; } = material;

        public PictureArt? Picture { get; set; }

        public SpriteArt? Figures { get; set; }
    }
}
