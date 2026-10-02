using System.Numerics;
using Rusty.Engine;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Game.Presentation;

/// <summary>
/// The combat screen's scene: a floor field, the party on the left facing
/// right and its foes on the right facing left, each a side-view sprite
/// billboarded fully to a camera looking down at 30 degrees. It follows a
/// <see cref="FightReplay"/>: the acting figure plays its attack, the others
/// idle, and the defeated leave the field. Figures without a figure
/// definition draw as plain blocks.
/// </summary>
internal sealed class CombatScene : IDisposable
{
    public const int Width = 6;
    public const int Depth = 5;

    private const ulong FloorObject = 100;
    private const ulong FigureObjects = 1000;

    private static readonly Color[] SideColours = [new(0.3f, 0.45f, 0.8f, 1), new(0.75f, 0.3f, 0.25f, 1)];

    private readonly IGraphicsService _graphics;
    private readonly Material _plain;
    private readonly List<IDisposable> _retired = [];
    private readonly Dictionary<string, Figure> _figures = [];
    private FightReplay? _fight;
    private MeshResource? _floorMesh;
    private Appearance? _floor;
    private int _acted;

    public CombatScene(IGraphicsService graphics, Material plain)
    {
        _graphics = graphics;
        _plain = plain;
    }

    /// <summary>The combat camera's vertical field of view: narrower than the corridor's, framing the field.</summary>
    public const double FieldOfView = 46;

    /// <summary>Where the camera stands: straight on, centred, 30 degrees down.</summary>
    public static CameraPose Pose { get; } = new(new Vector3(Width / 2f, 3.2f, (Depth / 2f) + 4.6f), -30, 0);

    /// <summary>Adds the scene for <paramref name="fight"/>, building it when the fight is new.</summary>
    /// <param name="spriteFor">The sprite a monster or class is drawn with, or null.</param>
    /// <param name="floor">The floor material and frame (the area's wall set floor), or null for plain.</param>
    public void Show(FightReplay fight, Func<Definition, SpriteArt?> spriteFor, (Material Material, UvRect Frame)? floor, List<AppearanceFact> facts)
    {
        if (fight != _fight)
        {
            Build(fight, spriteFor, floor);
        }

        facts.Add(new AppearanceFact(FloorObject, false, 0, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), _floor!, true, RenderLayer.Scene));
        if (fight.Acting.Count != _acted)
        {
            _acted = fight.Acting.Count;
            foreach ((string name, Figure figure) in _figures)
            {
                figure.Play(name == fight.Acting.Who ? "attack" : "idle");
            }
        }

        ulong id = FigureObjects;
        foreach ((string name, Figure figure) in _figures)
        {
            facts.Add(new AppearanceFact(id++, false, 0, figure.Placement, figure.Appearance, !fight.Defeated.Contains(name), RenderLayer.Scene));
        }
    }

    /// <summary>Advances the figures' animations; call in every update while the scene shows.</summary>
    public void Tick()
    {
        foreach (Figure figure in _figures.Values)
        {
            if (figure.Playback is SpritePlayback playback)
            {
                _graphics.AdvanceSpritePlayback(new SpritePlaybackAdvanceRequest(playback));
            }
        }
    }

    /// <summary>Releases what earlier fights showed, once a publish has stopped showing it.</summary>
    public void ReleaseRetired()
    {
        foreach (IDisposable retired in _retired)
        {
            retired.Dispose();
        }

        _retired.Clear();
    }

    public void Dispose()
    {
        Retire();
        ReleaseRetired();
    }

    private void Build(FightReplay fight, Func<Definition, SpriteArt?> spriteFor, (Material Material, UvRect Frame)? floor)
    {
        Retire();
        _fight = fight;
        _acted = 0;
        AreaGeometry geometry = AreaMesh.Floor(Width, Depth, floor?.Frame);
        _floorMesh = _graphics.CreateMeshResource(new MeshResourceCreateRequest(
            geometry.Positions,
            geometry.Normals,
            geometry.Uvs,
            geometry.Indices,
            geometry.Groups.Select(group => new MeshGroup(group.Slot, group.Start, group.Count)).ToArray(),
            geometry.Groups.Select(group => new MeshMaterialBinding(group.Slot, group.Slot == AreaMesh.TexturedSlot ? floor!.Value.Material : _plain)).ToArray()));
        _floor = _graphics.CreateMeshAppearance(_floorMesh);

        foreach (IGrouping<int, FightMember> side in fight.Fight.Members.GroupBy(member => member.Side))
        {
            List<FightMember> members = side.ToList();
            for (int i = 0; i < members.Count; i++)
            {
                // Two staggered ranks per side, spread across the field's depth.
                bool left = side.Key == 0;
                float x = left ? 1.7f - (i % 2 * 0.4f) : Width - 1.7f + (i % 2 * 0.4f);
                float z = 0.6f + ((Depth - 1.2f) * (i + 0.5f) / members.Count);
                Definition? kind = members[i].Monster ?? members[i].Class;
                SpriteArt? art = kind is null ? null : spriteFor(kind);
                _figures[members[i].Name] = art is null
                    ? Figure.Block(_graphics, new Vector3(x, 0, z), SideColours[Math.Min(side.Key, 1)])
                    : new Figure(art, art.CreateFigure(BillboardMode.Spherical), new Transform(new Vector3(x, 0, z), Quaternion.Identity, art.Scale(faceRight: left)));
            }
        }

        foreach (Figure figure in _figures.Values)
        {
            figure.Play("idle");
        }
    }

    /// <summary>Takes the current fight's scene out of use; it is released after the next publish.</summary>
    private void Retire()
    {
        foreach (Figure figure in _figures.Values)
        {
            _retired.AddRange(figure.Owned());
        }

        _figures.Clear();
        if (_floor is not null)
        {
            _retired.Add(_floor);
            _retired.Add(_floorMesh!);
        }

        _floor = null;
        _floorMesh = null;
        _fight = null;
    }

    /// <summary>One combatant on the field and its current animation.</summary>
    private sealed class Figure(SpriteArt? art, Appearance appearance, Transform placement)
    {
        public Appearance Appearance { get; } = appearance;

        public Transform Placement { get; } = placement;

        public SpritePlayback? Playback { get; private set; }

        public static Figure Block(IGraphicsService graphics, Vector3 at, Color colour)
        {
            Appearance block = graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false, colour));
            return new Figure(null, block, new Transform(at + new Vector3(0, 0.35f, 0), Quaternion.Identity, new Vector3(0.35f, 0.7f, 0.35f)));
        }

        /// <summary>Switches to <paramref name="animation"/> when the sprite has it.</summary>
        public void Play(string animation)
        {
            if (art?.Play(Appearance, animation) is SpritePlayback next)
            {
                Playback?.Dispose();
                Playback = next;
            }
        }

        public IEnumerable<IDisposable> Owned()
        {
            if (Playback is not null)
            {
                yield return Playback;
            }

            yield return Appearance;
        }
    }
}
