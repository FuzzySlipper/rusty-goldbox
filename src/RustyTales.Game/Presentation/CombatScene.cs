using System.Numerics;
using Rusty.Engine;
using RustyTales.Core.Campaigns;
using RustyTales.Core.Combat;
using RustyTales.Core.Definitions;

namespace RustyTales.Game.Presentation;

/// <summary>
/// The combat screen's 3D scene supporting three authored presentation modes
/// over the same Core <see cref="CombatObservation"/>:
/// <list type="bullet">
/// <item><description><c>tactical</c>: Gold Box-style angled 3D floor grid with terrain and cell positions.</description></item>
/// <item><description><c>first_person</c>: Wizardry / Dragon Quest-style eye-level enemy lineup over a stage backdrop (party shown in UI cards).</description></item>
/// <item><description><c>side_view</c>: JRPG-style fixed formation stage with enemies on the left and party on the right over a backdrop.</description></item>
/// </list>
/// </summary>
internal sealed class CombatScene : IDisposable
{
    /// <summary>The field of a tactical fight without positions.</summary>
    public const int Width = 6;
    public const int Depth = 5;

    private const int FirstPersonWidth = 6;
    private const int FirstPersonDepth = 4;
    private const int SideViewWidth = 7;
    private const int SideViewDepth = 4;

    private const ulong FloorObject = 100;
    private const ulong BackdropObject = 101;
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
    private readonly Dictionary<string, Vector3> _stageSpots = [];
    private readonly List<Figure> _terrain = [];
    private string? _fightKey;
    private string _presentation = "tactical";
    private MeshResource? _floorMesh;
    private Appearance? _floor;
    private Transform _floorPlacement = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
    private MeshResource? _backdropMesh;
    private Appearance? _backdrop;
    private Transform _backdropPlacement = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
    private int _acted;
    private int _width = Width;
    private int _depth = Depth;

    public CombatScene(IGraphicsService graphics, Material plain)
    {
        _graphics = graphics;
        _plain = plain;
    }

    /// <summary>The tactical combat camera's vertical field of view: narrower than the corridor's, framing the field.</summary>
    public const double FieldOfView = 46;

    /// <summary>The active camera vertical field of view for the current combat presentation.</summary>
    public double ActiveFieldOfView => _presentation switch
    {
        "first_person" => 44,
        "side_view" => 42,
        _ => FieldOfView,
    };

    /// <summary>How far the tactical camera looks down, in degrees.</summary>
    private const double Pitch = 30;

    /// <summary>How tall a figure stands, in cells, so the far row's heads stay in the frame.</summary>
    private const double FigureHeight = 1.3;

    /// <summary>How much of the frame the field may fill, leaving a margin on every side.</summary>
    private const double Fill = 0.9;

    /// <summary>Where the camera stands for the current fight in a view of the given width-to-height <paramref name="aspect"/>.</summary>
    public CameraPose PoseIn(double aspect) => _presentation switch
    {
        "first_person" => PoseForFirstPerson(_width, _depth, aspect),
        "side_view" => PoseForSideView(_width, _depth, aspect),
        _ => PoseFor(_width, _depth, aspect),
    };

    /// <summary>
    /// Straight on, centred and 30 degrees down, as close as it can stand
    /// with the whole field and the figures on it inside the frame: a wide
    /// view brings it closer, a narrow or deep one sends it back.
    /// </summary>
    public static CameraPose PoseFor(int width, int depth, double aspect) => PoseForAngle(width, depth, aspect, Pitch, FieldOfView, FigureHeight, Fill);

    /// <summary>Eye-level Wizardry / Dragon Quest camera framing the enemy ranks and stage backdrop.</summary>
    public static CameraPose PoseForFirstPerson(int width, int depth, double aspect)
    {
        double pitch = 5;
        double fov = 44;
        double tanY = Math.Tan(fov * Math.PI / 360);
        double tanX = tanY * Math.Max(0.4, aspect);
        // Keep a ~4.4-cell horizontal enemy lineup and ~1.6-cell figure height framed comfortably.
        double halfWidth = 2.3;
        double distForWidth = halfWidth / (0.86 * tanX);
        double distForHeight = 0.9 / (0.84 * tanY);
        double distance = Math.Max(2.3, Math.Max(distForWidth, distForHeight));
        float targetZ = depth * 0.45f;
        return new CameraPose(new Vector3(width / 2f, 0.78f + (float)(distance * Math.Sin(pitch * Math.PI / 180)), targetZ + (float)distance), -pitch, 0);
    }

    /// <summary>Lower theatrical JRPG side-view camera framing party on the right and enemies on the left.</summary>
    public static CameraPose PoseForSideView(int width, int depth, double aspect) => PoseForAngle(width, depth, aspect, 12, 40, 1.35, 0.96);

    private static CameraPose PoseForAngle(int width, int depth, double aspect, double pitchDegrees, double fovDegrees, double figureHeight, double fill)
    {
        double pitch = pitchDegrees * Math.PI / 180;
        Vector3 forward = new(0, (float)-Math.Sin(pitch), (float)-Math.Cos(pitch));
        Vector3 up = new(0, (float)Math.Cos(pitch), (float)-Math.Sin(pitch));
        Vector3 centre = new(width / 2f, 0, depth / 2f);
        double tanY = Math.Tan(fovDegrees * Math.PI / 360);
        double tanX = tanY * Math.Max(0.1, aspect);
        Vector3[] corners =
        [
            new(0, 0, 0), new(width, 0, 0), new(0, 0, depth), new(width, 0, depth),
            new(0, (float)figureHeight, 0), new(width, (float)figureHeight, 0), new(0, (float)figureHeight, depth), new(width, (float)figureHeight, depth),
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
            return corners.All(corner => Seen(eye, corner) is var (x, y) && Math.Abs(x) <= fill && Math.Abs(y) <= fill);
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

        return new CameraPose(Closest(target), -pitchDegrees, 0);
    }

    /// <summary>
    /// Chooses a readable presentation floor for a fight whose ruleset does not
    /// supply a field. The floor grows with the larger side so a campaign-sized
    /// party gets distinct ranks instead of being placed on the six-by-five
    /// default floor. This is presentation geometry only; Core positions are
    /// used unchanged whenever a combat field supplies them.
    /// </summary>
    internal static (int Width, int Depth) FallbackSize(IReadOnlyList<int> sides)
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

    /// <summary>
    /// Shows a Core combat observation, live or finished. The scene is built
    /// once per fight and presentation (<paramref name="fightKey"/> + <paramref name="presentation"/>),
    /// keyed by each combatant's stable ID, and reads positions, defeated state and active state directly
    /// from the observation; it never infers an actor from its display name.
    /// </summary>
    /// <param name="presentation">Combat framing mode: <c>tactical</c>, <c>first_person</c> or <c>side_view</c>.</param>
    /// <param name="spriteFor">The sprite a monster or class is drawn with, or null.</param>
    /// <param name="terrainFor">The sprite a terrain key of the fight's field is drawn with, or null.</param>
    /// <param name="floor">The floor material and frame (the area's wall set floor), or null for plain.</param>
    /// <param name="backdrop">Optional stage backdrop material, UV frame and tiling flag for first-person and side-view modes.</param>
    public void Show(
        string fightKey,
        string presentation,
        CombatObservation observation,
        Func<string, Definition?> kindFor,
        Func<Definition, SpriteArt?> spriteFor,
        Func<char, SpriteArt?> terrainFor,
        CombatField? field,
        (Material Material, UvRect Frame)? floor,
        (Material Material, UvRect Frame, bool Tile)? backdrop,
        List<AppearanceFact> facts)
    {
        string sceneKey = $"{fightKey}@{presentation}";
        if (sceneKey != _fightKey)
        {
            Build(sceneKey, presentation, observation, kindFor, spriteFor, terrainFor, field, floor, backdrop);
        }

        facts.Add(new AppearanceFact(FloorObject, false, 0, _floorPlacement, _floor!, true, RenderLayer.Scene));
        if (_backdrop is not null)
        {
            facts.Add(new AppearanceFact(BackdropObject, false, 0, _backdropPlacement, _backdrop, true, RenderLayer.Scene));
        }

        ulong ground = TerrainObjects;
        foreach (Figure piece in _terrain)
        {
            facts.Add(new AppearanceFact(ground++, false, 0, piece.Placement, piece.Appearance, true, RenderLayer.Scene));
        }

        HashSet<string> actingSubjects = [];
        CombatFact? latestAction = observation.Facts
            .LastOrDefault(fact => fact.Kind is "action" or "portion" or "reaction" or "move");
        if (latestAction is not null)
        {
            actingSubjects = latestAction.SubjectIds.ToHashSet(StringComparer.Ordinal);
        }

        if (observation.Facts.Count != _acted)
        {
            _acted = observation.Facts.Count;
            foreach ((string key, Figure figure) in _figures)
            {
                figure.Play(actingSubjects.Contains(key) ? "attack" : "idle");
            }
        }

        Dictionary<string, CombatantObservation> current = observation.Combatants.ToDictionary(member => member.Id, StringComparer.Ordinal);
        ulong id = FigureObjects;
        foreach ((string key, Figure figure) in _figures)
        {
            if (current.TryGetValue(key, out CombatantObservation? member))
            {
                if (_presentation == "tactical")
                {
                    if (member.Position is Cell cell)
                    {
                        figure.StandOn(new Vector3(cell.X + 0.5f, 0, cell.Y + 0.5f));
                    }
                }
                else if (_stageSpots.TryGetValue(key, out Vector3 spot))
                {
                    // Step slightly forward during an attack so the acting combatant pops visually.
                    Vector3 lunge = actingSubjects.Contains(key)
                        ? _presentation == "first_person"
                            ? new Vector3(0, 0, 0.32f)
                            : new Vector3(member.Side == 0 ? -0.45f : 0.45f, 0, 0)
                        : Vector3.Zero;
                    figure.StandOn(spot + lunge);
                }

                facts.Add(new AppearanceFact(id++, false, 0, figure.Placement, figure.Appearance, !member.Defeated && !member.Escaped, RenderLayer.Scene));
            }
        }
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

    private void Build(
        string sceneKey,
        string presentation,
        CombatObservation observation,
        Func<string, Definition?> kindFor,
        Func<Definition, SpriteArt?> spriteFor,
        Func<char, SpriteArt?> terrainFor,
        CombatField? field,
        (Material Material, UvRect Frame)? floor,
        (Material Material, UvRect Frame, bool Tile)? backdrop)
    {
        Retire();
        _fightKey = sceneKey;
        _presentation = presentation;
        _acted = 0;

        (int width, int depth) = presentation switch
        {
            "first_person" => (FirstPersonWidth, FirstPersonDepth),
            "side_view" => (SideViewWidth, SideViewDepth),
            _ => field is not null
                ? (field.Width, field.Height)
                : FallbackSize(observation.Combatants.Select(member => member.Side).ToList()),
        };
        _width = width;
        _depth = depth;

        bool stageMode = presentation is "first_person" or "side_view";
        int floorMargin = stageMode ? 4 : 0;
        int floorDepthExtra = stageMode ? 4 : 0;
        _floorPlacement = new Transform(new Vector3(-floorMargin, 0, 0), Quaternion.Identity, Vector3.One);
        AreaGeometry geometry = AreaMesh.Floor(_width + (floorMargin * 2), _depth + floorDepthExtra, floor?.Frame);
        _floorMesh = _graphics.CreateMeshResource(new MeshResourceCreateRequest(
            geometry.Positions,
            geometry.Normals,
            geometry.Uvs,
            geometry.Indices,
            geometry.Groups.Select(group => new MeshGroup(group.Slot, group.Start, group.Count)).ToArray(),
            geometry.Groups.Select(group => new MeshMaterialBinding(group.Slot, group.Slot == AreaMesh.TexturedSlot ? floor!.Value.Material : _plain)).ToArray()));
        _floor = _graphics.CreateMeshAppearance(_floorMesh);

        if (stageMode)
        {
            // Fill the viewport edge-to-edge behind the fighters without dark side bars on widescreen layouts.
            int wallMargin = presentation == "first_person" ? 2 : 3;
            float wallHeight = presentation == "first_person" ? 4.2f : 4.8f;
            _backdropPlacement = new Transform(new Vector3(-wallMargin, 0, 0), Quaternion.Identity, Vector3.One);
            AreaGeometry wallGeo = AreaMesh.BackdropWall(_width + (wallMargin * 2), wallHeight, 0f, backdrop?.Frame, backdrop?.Tile ?? true);
            _backdropMesh = _graphics.CreateMeshResource(new MeshResourceCreateRequest(
                wallGeo.Positions,
                wallGeo.Normals,
                wallGeo.Uvs,
                wallGeo.Indices,
                wallGeo.Groups.Select(group => new MeshGroup(group.Slot, group.Start, group.Count)).ToArray(),
                wallGeo.Groups.Select(group => new MeshMaterialBinding(group.Slot, group.Slot == AreaMesh.TexturedSlot ? backdrop!.Value.Material : _plain)).ToArray()));
            _backdrop = _graphics.CreateMeshAppearance(_backdropMesh);
        }

        if (presentation == "first_person")
        {
            BuildFirstPersonFigures(observation, kindFor, spriteFor);
        }
        else if (presentation == "side_view")
        {
            BuildSideViewFigures(observation, kindFor, spriteFor);
        }
        else
        {
            BuildTacticalFigures(observation, kindFor, spriteFor, terrainFor, field);
        }

        foreach (Figure figure in _figures.Values)
        {
            figure.Play("idle");
        }
    }

    private void BuildFirstPersonFigures(CombatObservation observation, Func<string, Definition?> kindFor, Func<Definition, SpriteArt?> spriteFor)
    {
        // Only foes stand in front of the camera; the party is represented by the UI roster cards.
        List<CombatantObservation> foes = observation.Combatants.Where(member => member.Side != 0).ToList();
        int frontCount = Math.Min(foes.Count, 4);
        int backCount = Math.Max(0, foes.Count - frontCount);

        for (int index = 0; index < foes.Count; index++)
        {
            CombatantObservation member = foes[index];
            bool back = index >= frontCount;
            int rowIndex = back ? index - frontCount : index;
            int rowSize = back ? backCount : frontCount;
            float spacing = rowSize <= 2 ? 1.35f : 1.1f;
            float startX = (_width / 2f) - ((rowSize - 1) * spacing / 2f);
            float x = startX + (rowIndex * spacing) + (back ? 0.25f : 0f);
            float z = back ? 1.1f : 2.0f;
            Vector3 spot = new(x, 0, z);
            _stageSpots[member.Id] = spot;
            AddFigure(member, spot, faceRight: false, kindFor, spriteFor);
        }
    }

    private void BuildSideViewFigures(CombatObservation observation, Func<string, Definition?> kindFor, Func<Definition, SpriteArt?> spriteFor)
    {
        // Classic JRPG staging: foes on the left facing right, party in a staggered diagonal column on the right facing left.
        List<CombatantObservation> party = observation.Combatants.Where(member => member.Side == 0).ToList();
        List<CombatantObservation> foes = observation.Combatants.Where(member => member.Side != 0).ToList();

        PlaceSideColumn(party, leftSide: false, baseX: 5.3f, rankStep: 0.7f, staggerX: 0.18f, kindFor, spriteFor);
        PlaceSideColumn(foes, leftSide: true, baseX: 1.75f, rankStep: -0.75f, staggerX: -0.15f, kindFor, spriteFor);
    }

    private void PlaceSideColumn(
        List<CombatantObservation> members,
        bool leftSide,
        float baseX,
        float rankStep,
        float staggerX,
        Func<string, Definition?> kindFor,
        Func<Definition, SpriteArt?> spriteFor)
    {
        int perRank = Math.Max(1, Math.Min(4, members.Count));
        for (int index = 0; index < members.Count; index++)
        {
            CombatantObservation member = members[index];
            int rank = index / perRank;
            int slot = index % perRank;
            int countInRank = Math.Min(perRank, members.Count - (rank * perRank));
            float zSpan = countInRank <= 1 ? 0f : Math.Min(2.3f, (countInRank - 1) * 0.85f);
            float z = (_depth / 2f) - (zSpan / 2f) + (countInRank <= 1 ? 0f : slot * (zSpan / (countInRank - 1)));
            float x = baseX + (rank * rankStep) + (slot * staggerX);
            Vector3 spot = new(x, 0, z);
            _stageSpots[member.Id] = spot;
            AddFigure(member, spot, faceRight: leftSide, kindFor, spriteFor);
        }
    }

    private void BuildTacticalFigures(
        CombatObservation observation,
        Func<string, Definition?> kindFor,
        Func<Definition, SpriteArt?> spriteFor,
        Func<char, SpriteArt?> terrainFor,
        CombatField? field)
    {
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

                AddFigure(member, new Vector3(x, 0, z), faceRight: left, kindFor, spriteFor);
            }
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

    private void AddFigure(CombatantObservation member, Vector3 spot, bool faceRight, Func<string, Definition?> kindFor, Func<Definition, SpriteArt?> spriteFor)
    {
        Definition? kind = kindFor(member.Id);
        SpriteArt? art = kind is null ? null : spriteFor(kind);
        _figures[member.Id] = art is null
            ? Figure.Block(_graphics, spot, SideColours[Math.Min(member.Side, 1)])
            : new Figure(art, art.CreateFigure(BillboardMode.Spherical), new Transform(spot, Quaternion.Identity, art.Scale(faceRight)));
    }

    /// <summary>Takes the current fight's scene out of use; it is released after the next publish.</summary>
    private void Retire()
    {
        foreach (Figure figure in _figures.Values)
        {
            _retired.AddRange(figure.Owned());
        }

        _figures.Clear();
        _stageSpots.Clear();
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

        if (_backdrop is not null)
        {
            _retired.Add(_backdrop);
            _retired.Add(_backdropMesh!);
        }

        _floor = null;
        _floorMesh = null;
        _backdrop = null;
        _backdropMesh = null;
        _fightKey = null;
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
