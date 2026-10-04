using System.Numerics;
using Rusty.Engine;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;

namespace RustyGoldbox.Game.Presentation;

/// <summary>
/// The combat screen's scene: a floor field, the party on the left facing
/// right and its foes on the right facing left, each a side-view sprite
/// billboarded fully to a camera looking down at 30 degrees. It follows a
/// <see cref="FightReplay"/>: the acting figure plays its attack, the others
/// idle, the defeated leave the field, and in a fight on a combat field each
/// figure stands on its cell and moves as the fight's moves show. Figures
/// without a figure definition draw as plain blocks.
/// </summary>
internal sealed class CombatScene : IDisposable
{
    /// <summary>The field of a fight without positions.</summary>
    public const int Width = 6;
    public const int Depth = 5;

    private const ulong FloorObject = 100;
    private const ulong FigureObjects = 1000;
    private const ulong TerrainObjects = 100_000;

    // Terrain without a figure: impassable ground as a grey block, rough ground as a low brown slab.
    private static readonly Color BlockingColour = new(0.42f, 0.42f, 0.45f, 1);
    private static readonly Color RoughColour = new(0.45f, 0.33f, 0.2f, 1);

    private static readonly Color[] SideColours = [new(0.3f, 0.45f, 0.8f, 1), new(0.75f, 0.3f, 0.25f, 1)];

    private readonly IGraphicsService _graphics;
    private readonly Material _plain;
    private readonly List<IDisposable> _retired = [];
    private readonly Dictionary<string, Figure> _figures = [];
    private readonly List<Figure> _terrain = [];
    private FightReplay? _fight;
    private CombatObservation? _live;
    private MeshResource? _floorMesh;
    private Appearance? _floor;
    private int _acted;
    private int _width = Width;
    private int _depth = Depth;

    public CombatScene(IGraphicsService graphics, Material plain)
    {
        _graphics = graphics;
        _plain = plain;
    }

    /// <summary>The combat camera's vertical field of view: narrower than the corridor's, framing the field.</summary>
    public const double FieldOfView = 46;

    /// <summary>How far the camera looks down, in degrees.</summary>
    private const double Pitch = 30;

    /// <summary>How tall a figure stands, in cells, so the far row's heads stay in the frame.</summary>
    private const double FigureHeight = 1.3;

    /// <summary>How much of the frame the field may fill, leaving a margin on every side.</summary>
    private const double Fill = 0.9;

    /// <summary>Where the camera stands for the current field in a view of the given width-to-height <paramref name="aspect"/>.</summary>
    public CameraPose PoseIn(double aspect) => PoseFor(_width, _depth, aspect);

    /// <summary>
    /// Straight on, centred and 30 degrees down, as close as it can stand
    /// with the whole field and the figures on it inside the frame: a wide
    /// view brings it closer, a narrow or deep one sends it back.
    /// </summary>
    public static CameraPose PoseFor(int width, int depth, double aspect)
    {
        double pitch = Pitch * Math.PI / 180;
        Vector3 forward = new(0, (float)-Math.Sin(pitch), (float)-Math.Cos(pitch));
        Vector3 up = new(0, (float)Math.Cos(pitch), (float)-Math.Sin(pitch));
        Vector3 centre = new(width / 2f, 0, depth / 2f);
        double tanY = Math.Tan(FieldOfView * Math.PI / 360);
        double tanX = tanY * Math.Max(0.1, aspect);
        Vector3[] corners =
        [
            new(0, 0, 0), new(width, 0, 0), new(0, 0, depth), new(width, 0, depth),
            new(0, (float)FigureHeight, 0), new(width, (float)FigureHeight, 0), new(0, (float)FigureHeight, depth), new(width, (float)FigureHeight, depth),
        ];

        // Where each corner shows in the frame from an eye: -1 to 1 across and up, or null behind the camera.
        (double X, double Y)? Seen(Vector3 eye, Vector3 corner)
        {
            Vector3 seen = corner - eye;
            double ahead = Vector3.Dot(seen, forward);
            return ahead <= 0.1 ? null : (seen.X / (ahead * tanX), Vector3.Dot(seen, up) / (ahead * tanY));
        }

        bool Fits(Vector3 eye)
        {
            return corners.All(corner => Seen(eye, corner) is var (x, y) && Math.Abs(x) <= Fill && Math.Abs(y) <= Fill);
        }

        // The field shrinks in the frame as the camera backs away, so the closest distance that fits is a bisection.
        Vector3 Closest(Vector3 target)
        {
            double near = 0.5;
            double far = 4 * (width + depth + 4);
            for (int step = 0; step < 40; step++)
            {
                double middle = (near + far) / 2;
                if (Fits(target - (forward * (float)middle)))
                {
                    far = middle;
                }
                else
                {
                    near = middle;
                }
            }

            return target - (forward * (float)far);
        }

        // Looking down, the field's near edge sits low and the far edge high but not as far; aim
        // a little off the centre so the field sits in the middle of the frame, then close in again.
        Vector3 target = centre;
        for (int pass = 0; pass < 3; pass++)
        {
            Vector3 eye = Closest(target);
            List<double> heights = corners.Select(corner => Seen(eye, corner)!.Value.Y).ToList();
            double middle = (heights.Min() + heights.Max()) / 2;
            target += up * (float)(middle * Vector3.Distance(eye, target) * tanY);
        }

        return new CameraPose(Closest(target), -Pitch, 0);
    }

    /// <summary>
    /// Chooses a readable presentation floor for a fight whose ruleset does not
    /// supply a field. The floor grows with the larger side so a campaign-sized
    /// party gets distinct ranks instead of being placed on the six-by-five
    /// default floor. This is presentation geometry only; Core positions are
    /// used unchanged whenever a combat field supplies them.
    /// </summary>
    internal static (int Width, int Depth) FallbackSize(IReadOnlyList<FightMember> members)
    {
        return FallbackSize(members.Select(member => member.Side).ToList());
    }

    internal static (int Width, int Depth) FallbackSize(IReadOnlyList<CombatantObservation> members)
    {
        return FallbackSize(members.Select(member => member.Side).ToList());
    }

    private static (int Width, int Depth) FallbackSize(IReadOnlyList<int> sides)
    {
        int largestSide = sides
            .GroupBy(side => side)
            .Select(group => group.Count())
            .DefaultIfEmpty(1)
            .Max();
        int slots = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(largestSide)));
        int ranks = (largestSide + slots - 1) / slots;
        return (Math.Max(Width, (ranks * 2) + 4), Math.Max(Depth, slots + 2));
    }

    /// <summary>Adds the scene for <paramref name="fight"/>, building it when the fight is new.</summary>
    /// <param name="spriteFor">The sprite a monster or class is drawn with, or null.</param>
    /// <param name="terrainFor">The sprite a terrain key of the fight's field is drawn with, or null.</param>
    /// <param name="floor">The floor material and frame (the area's wall set floor), or null for plain.</param>
    public void Show(FightReplay fight, Func<Definition, SpriteArt?> spriteFor, Func<char, SpriteArt?> terrainFor, (Material Material, UvRect Frame)? floor, List<AppearanceFact> facts)
    {
        if (fight != _fight)
        {
            Build(fight, spriteFor, terrainFor, floor);
        }

        facts.Add(new AppearanceFact(FloorObject, false, 0, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), _floor!, true, RenderLayer.Scene));
        ulong ground = TerrainObjects;
        foreach (Figure piece in _terrain)
        {
            facts.Add(new AppearanceFact(ground++, false, 0, piece.Placement, piece.Appearance, true, RenderLayer.Scene));
        }

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
            if (fight.Positions.TryGetValue(name, out Cell cell))
            {
                figure.StandOn(new Vector3(cell.X + 0.5f, 0, cell.Y + 0.5f));
            }

            facts.Add(new AppearanceFact(id++, false, 0, figure.Placement, figure.Appearance, !fight.Defeated.Contains(name), RenderLayer.Scene));
        }
    }

    /// <summary>
    /// Shows a live Core observation. The scene is keyed by the combatant's
    /// stable ID and reads positions, defeated state and active state directly
    /// from the observation; it never infers an actor from its display name.
    /// </summary>
    public void Show(CombatObservation observation, Func<string, Definition?> kindFor, Func<Definition, SpriteArt?> spriteFor, Func<char, SpriteArt?> terrainFor, CombatField? field, (Material Material, UvRect Frame)? floor, List<AppearanceFact> facts)
    {
        bool sameMembers = _live is not null
            && _fight is null
            && _figures.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(observation.Combatants.Select(member => member.Id));
        if (!sameMembers)
        {
            BuildLive(observation, kindFor, spriteFor, terrainFor, field, floor);
        }

        facts.Add(new AppearanceFact(FloorObject, false, 0, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), _floor!, true, RenderLayer.Scene));
        ulong ground = TerrainObjects;
        foreach (Figure piece in _terrain)
        {
            facts.Add(new AppearanceFact(ground++, false, 0, piece.Placement, piece.Appearance, true, RenderLayer.Scene));
        }

        if (observation.Facts.Count != _acted)
        {
            _acted = observation.Facts.Count;
            CombatFact? action = observation.Facts
                .LastOrDefault(fact => fact.Kind is "action" or "portion" or "reaction" or "move");
            HashSet<string> subjects = action?.SubjectIds.ToHashSet(StringComparer.Ordinal) ?? [];
            foreach ((string key, Figure figure) in _figures)
            {
                figure.Play(subjects.Contains(key) ? "attack" : "idle");
            }
        }

        Dictionary<string, CombatantObservation> current = observation.Combatants.ToDictionary(member => member.Id, StringComparer.Ordinal);
        ulong id = FigureObjects;
        foreach ((string key, Figure figure) in _figures)
        {
            if (current.TryGetValue(key, out CombatantObservation? member))
            {
                if (member.Position is Cell cell)
                {
                    figure.StandOn(new Vector3(cell.X + 0.5f, 0, cell.Y + 0.5f));
                }

                facts.Add(new AppearanceFact(id++, false, 0, figure.Placement, figure.Appearance, !member.Defeated && !member.Escaped, RenderLayer.Scene));
            }
        }

        _live = observation;
    }

    /// <summary>Advances the figures' animations; call in every update while the scene shows. True when a figure's frame changed.</summary>
    public bool Tick(PlaybackFrames frames)
    {
        bool changed = false;
        foreach (Figure figure in _figures.Values)
        {
            if (figure.Playback is SpritePlayback playback)
            {
                changed |= frames.Advance(playback);
            }
        }

        return changed;
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

    private void Build(FightReplay fight, Func<Definition, SpriteArt?> spriteFor, Func<char, SpriteArt?> terrainFor, (Material Material, UvRect Frame)? floor)
    {
        Retire();
        _fight = fight;
        _acted = 0;
        (int width, int depth) = fight.Fight.Field is CombatField field
            ? (field.Width, field.Height)
            : FallbackSize(fight.Fight.Members);
        _width = width;
        _depth = depth;
        AreaGeometry geometry = AreaMesh.Floor(_width, _depth, floor?.Frame);
        _floorMesh = _graphics.CreateMeshResource(new MeshResourceCreateRequest(
            geometry.Positions,
            geometry.Normals,
            geometry.Uvs,
            geometry.Indices,
            geometry.Groups.Select(group => new MeshGroup(group.Slot, group.Start, group.Count)).ToArray(),
            geometry.Groups.Select(group => new MeshMaterialBinding(group.Slot, group.Slot == AreaMesh.TexturedSlot ? floor!.Value.Material : _plain)).ToArray()));
        _floor = _graphics.CreateMeshAppearance(_floorMesh);

        foreach (IGrouping<int, (FightMember Member, int Index)> side in fight.Fight.Members
            .Select((member, index) => (Member: member, Index: index))
            .GroupBy(entry => entry.Member.Side))
        {
            List<(FightMember Member, int Index)> members = side.ToList();
            int slots = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(members.Count)));
            int rows = Math.Max(1, _depth - 2);
            slots = Math.Min(slots, rows);
            int rowOffset = Math.Max(0, (rows - slots) / 2);
            for (int i = 0; i < members.Count; i++)
            {
                // Two opposing rank columns per side. A missing field gets
                // enough columns/depth from FallbackSize for every member;
                // authored cells still take precedence below.
                bool left = side.Key == 0;
                int rank = i / slots;
                int row = i % slots;
                float x = left ? 1f + rank : _width - 1f - rank;
                float z = 1f + rowOffset + row;
                if (members[i].Member.Position is Cell cell)
                {
                    x = cell.X + 0.5f;
                    z = cell.Y + 0.5f;
                }

                Definition? kind = members[i].Member.Monster ?? members[i].Member.Class;
                SpriteArt? art = kind is null ? null : spriteFor(kind);
                string key = fight.MemberKeys[members[i].Index];
                _figures[key] = art is null
                    ? Figure.Block(_graphics, new Vector3(x, 0, z), SideColours[Math.Min(side.Key, 1)])
                    : new Figure(art, art.CreateFigure(BillboardMode.Spherical), new Transform(new Vector3(x, 0, z), Quaternion.Identity, art.Scale(faceRight: left)));
            }
        }

        foreach (Figure figure in _figures.Values)
        {
            figure.Play("idle");
        }

        // Each terrain cell: its figure's sprite standing there, or a plain block or slab.
        foreach ((Cell cell, Terrain terrain) in fight.Fight.Field?.Terrain ?? new Dictionary<Cell, Terrain>())
        {
            Vector3 at = new(cell.X + 0.5f, 0, cell.Y + 0.5f);
            SpriteArt? art = terrainFor(terrain.Key);
            _terrain.Add(art is not null
                ? new Figure(art, art.CreateFigure(BillboardMode.Spherical), new Transform(at, Quaternion.Identity, art.Scale(faceRight: true)))
                : terrain.Passable
                    ? Figure.Box(_graphics, at, RoughColour, new Vector3(0.95f, 0.06f, 0.95f))
                    : Figure.Box(_graphics, at, BlockingColour, new Vector3(0.9f, 1f, 0.9f)));
        }
    }

    private void BuildLive(CombatObservation observation, Func<string, Definition?> kindFor, Func<Definition, SpriteArt?> spriteFor, Func<char, SpriteArt?> terrainFor, CombatField? field, (Material Material, UvRect Frame)? floor)
    {
        Retire();
        _live = observation;
        _acted = 0;
        (int width, int depth) = field is not null
            ? (field.Width, field.Height)
            : FallbackSize(observation.Combatants);
        _width = width;
        _depth = depth;
        AreaGeometry geometry = AreaMesh.Floor(_width, _depth, floor?.Frame);
        _floorMesh = _graphics.CreateMeshResource(new MeshResourceCreateRequest(
            geometry.Positions,
            geometry.Normals,
            geometry.Uvs,
            geometry.Indices,
            geometry.Groups.Select(group => new MeshGroup(group.Slot, group.Start, group.Count)).ToArray(),
            geometry.Groups.Select(group => new MeshMaterialBinding(group.Slot, group.Slot == AreaMesh.TexturedSlot ? floor!.Value.Material : _plain)).ToArray()));
        _floor = _graphics.CreateMeshAppearance(_floorMesh);

        foreach (IGrouping<int, CombatantObservation> side in observation.Combatants.GroupBy(member => member.Side))
        {
            List<CombatantObservation> members = side.ToList();
            int slots = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(members.Count)));
            int rows = Math.Max(1, _depth - 2);
            slots = Math.Min(slots, rows);
            int rowOffset = Math.Max(0, (rows - slots) / 2);
            for (int index = 0; index < members.Count; index++)
            {
                CombatantObservation member = members[index];
                bool left = side.Key == 0;
                int rank = index / slots;
                int row = index % slots;
                float x = left ? 1f + rank : _width - 1f - rank;
                float z = 1f + rowOffset + row;
                if (member.Position is Cell cell)
                {
                    x = cell.X + 0.5f;
                    z = cell.Y + 0.5f;
                }

                Definition? kind = kindFor(member.Id);
                SpriteArt? art = kind is null ? null : spriteFor(kind);
                _figures[member.Id] = art is null
                    ? Figure.Block(_graphics, new Vector3(x, 0, z), SideColours[Math.Min(side.Key, 1)])
                    : new Figure(art, art.CreateFigure(BillboardMode.Spherical), new Transform(new Vector3(x, 0, z), Quaternion.Identity, art.Scale(faceRight: left)));
            }
        }

        foreach (Figure figure in _figures.Values)
        {
            figure.Play("idle");
        }

        foreach ((Cell cell, Terrain terrain) in field?.Terrain ?? new Dictionary<Cell, Terrain>())
        {
            Vector3 at = new(cell.X + 0.5f, 0, cell.Y + 0.5f);
            SpriteArt? art = terrainFor(terrain.Key);
            _terrain.Add(art is not null
                ? new Figure(art, art.CreateFigure(BillboardMode.Spherical), new Transform(at, Quaternion.Identity, art.Scale(faceRight: true)))
                : terrain.Passable
                    ? Figure.Box(_graphics, at, RoughColour, new Vector3(0.95f, 0.06f, 0.95f))
                    : Figure.Box(_graphics, at, BlockingColour, new Vector3(0.9f, 1f, 0.9f)));
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
        foreach (Figure piece in _terrain)
        {
            _retired.AddRange(piece.Owned());
        }

        _terrain.Clear();
        if (_floor is not null)
        {
            _retired.Add(_floor);
            _retired.Add(_floorMesh!);
        }

        _floor = null;
        _floorMesh = null;
        _fight = null;
        _live = null;
    }

    /// <summary>One combatant on the field and its current animation.</summary>
    private sealed class Figure(SpriteArt? art, Appearance appearance, Transform placement)
    {
        // A block stands on its centre, raised to half its height; a sprite on its anchor.
        private readonly float _lift = placement.Translation.Y;

        public Appearance Appearance { get; } = appearance;

        public Transform Placement { get; private set; } = placement;

        /// <summary>Moves the figure to stand at <paramref name="ground"/>.</summary>
        public void StandOn(Vector3 ground)
        {
            Placement = Placement with { Translation = ground + new Vector3(0, _lift, 0) };
        }

        public SpritePlayback? Playback { get; private set; }

        public static Figure Block(IGraphicsService graphics, Vector3 at, Color colour) => Box(graphics, at, colour, new Vector3(0.35f, 0.7f, 0.35f));

        /// <summary>A plain cube of <paramref name="size"/> standing on <paramref name="at"/>.</summary>
        public static Figure Box(IGraphicsService graphics, Vector3 at, Color colour, Vector3 size)
        {
            Appearance block = graphics.CreatePrimitive(new PrimitiveAppearanceRequest(PrimitiveGeometry.Cube, false, colour));
            return new Figure(null, block, new Transform(at + new Vector3(0, size.Y / 2, 0), Quaternion.Identity, size));
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
