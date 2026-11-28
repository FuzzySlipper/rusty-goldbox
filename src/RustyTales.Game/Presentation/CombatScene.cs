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
    private const int SideViewWidth = 5;
    private const int SideViewDepth = 3;

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

    /// <summary>Whether the active combat presentation uses an orthographic top-down camera.</summary>
    public bool IsOrthographic => _presentation == "tactical";

    /// <summary>The tactical combat camera's downward pitch, in degrees (Gold Box / SKALD 3/4 top-down).</summary>
    public const double TacticalPitch = 60;

    /// <summary>The active camera vertical field of view for perspective combat presentations.</summary>
    public double ActiveFieldOfView => _presentation switch
    {
        "first_person" => 36,
        "side_view" => 34,
        _ => 36,
    };

    /// <summary>How much of the viewport the tactical orthographic room fills, leaving a slim margin on every side.</summary>
    public const double Fill = 0.94;

    /// <summary>Board dimensions used for <paramref name="presentation"/>.</summary>
    public static (int Width, int Depth) FieldSizeFor(string presentation, CombatObservation observation, CombatField? field) => presentation switch
    {
        "first_person" => (FirstPersonWidth, FirstPersonDepth),
        "side_view" => (SideViewWidth, SideViewDepth),
        _ => field is not null
            ? (field.Width, field.Height)
            : FallbackSize(observation.Combatants.Select(member => member.Side).ToList()),
    };

    /// <summary>
    /// Computes the exact 3D ground position of every combatant in the given
    /// <paramref name="presentation"/> so the DOM overlay can project hitboxes
    /// and floating combat text pixel-aligned with the 3D figures.
    /// </summary>
    public static Dictionary<string, Vector3> WorldSpotsFor(string presentation, CombatObservation observation, CombatField? field)
    {
        (int width, int depth) = FieldSizeFor(presentation, observation, field);
        Dictionary<string, Vector3> spots = new(StringComparer.Ordinal);
        if (presentation == "first_person")
        {
            List<CombatantObservation> foes = observation.Combatants.Where(member => member.Side != 0).ToList();
            int frontCount = Math.Min(foes.Count, 4);
            int backCount = Math.Max(0, foes.Count - frontCount);
            for (int index = 0; index < foes.Count; index++)
            {
                CombatantObservation member = foes[index];
                bool back = index >= frontCount;
                int rowIndex = back ? index - frontCount : index;
                int rowSize = back ? backCount : frontCount;
                float spacing = rowSize <= 2 ? 1.05f : 0.88f;
                float startX = (width / 2f) - ((rowSize - 1) * spacing / 2f);
                float x = startX + (rowIndex * spacing) + (back ? 0.2f : 0f);
                float z = back ? 1.95f : 2.65f;
                spots[member.Id] = new Vector3(x, 0, z);
            }

            return spots;
        }

        if (presentation == "side_view")
        {
            static void PlaceColumn(List<CombatantObservation> members, int d, float baseX, float rankStep, float staggerX, Dictionary<string, Vector3> output)
            {
                int perRank = Math.Max(1, Math.Min(4, members.Count));
                for (int index = 0; index < members.Count; index++)
                {
                    CombatantObservation member = members[index];
                    int rank = index / perRank;
                    int slot = index % perRank;
                    int countInRank = Math.Min(perRank, members.Count - (rank * perRank));
                    float zSpan = countInRank <= 1 ? 0f : Math.Min(1.22f, (countInRank - 1) * 0.42f);
                    float z = (d * 0.55f) - (zSpan / 2f) + (countInRank <= 1 ? 0f : slot * (zSpan / (countInRank - 1)));
                    float x = baseX + (rank * rankStep) + (slot * staggerX);
                    output[member.Id] = new Vector3(x, 0, z);
                }
            }

            PlaceColumn(observation.Combatants.Where(member => member.Side == 0).ToList(), depth, baseX: 3.12f, rankStep: 0.56f, staggerX: 0.24f, spots);
            PlaceColumn(observation.Combatants.Where(member => member.Side != 0).ToList(), depth, baseX: 1.72f, rankStep: -0.56f, staggerX: -0.22f, spots);
            return spots;
        }

        foreach (IGrouping<int, CombatantObservation> side in observation.Combatants.GroupBy(member => member.Side))
        {
            List<CombatantObservation> members = side.ToList();
            int slots = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(members.Count)));
            int rows = Math.Max(1, depth - 2);
            slots = Math.Min(slots, rows);
            int rowOffset = Math.Max(0, (rows - slots) / 2);
            for (int index = 0; index < members.Count; index++)
            {
                CombatantObservation member = members[index];
                bool left = side.Key == 0;
                int rank = index / slots;
                int row = index % slots;
                float x = left ? 1f + rank : width - 1f - rank;
                float z = 1f + rowOffset + row;
                if (member.Position is Cell cell)
                {
                    x = cell.X + 0.5f;
                    z = cell.Y + 0.5f;
                }

                spots[member.Id] = new Vector3(x, 0, z);
            }
        }

        return spots;
    }

    /// <summary>Where the camera stands for the current fight in a view of the given width-to-height <paramref name="aspect"/>.</summary>
    public CameraPose PoseIn(double aspect) => _presentation switch
    {
        "first_person" => PoseForFirstPerson(_width, _depth, aspect),
        "side_view" => PoseForSideView(_width, _depth, aspect),
        _ => PoseFor(_width, _depth, aspect),
    };

    /// <summary>Orthographic vertical world size framing the current tactical room cleanly in <paramref name="aspect"/>.</summary>
    public double OrthographicHeightIn(double aspect) => OrthographicHeightFor(_width, _depth, aspect);

    /// <summary>
    /// Gold Box / SKALD-style orthographic top-down camera (60 degrees down):
    /// every tile across the room keeps uniform screen proportions with no
    /// perspective trapezoid distortion, and upright figures read identically
    /// from the back row to the front row.
    /// </summary>
    public static CameraPose PoseFor(int width, int depth, double aspect)
    {
        double pitch = TacticalPitch * Math.PI / 180;
        Vector3 forward = new(0, (float)-Math.Sin(pitch), (float)-Math.Cos(pitch));
        Vector3 up = new(0, (float)Math.Cos(pitch), (float)-Math.Sin(pitch));

        // Center vertically between the near floor edge (z = depth, y = 0) and top of the north wall (z = -0.35, y = 1.0).
        double topY = (1.0 * Math.Cos(pitch)) + (0.35 * Math.Sin(pitch));
        double botY = -depth * Math.Sin(pitch);
        double midY = (topY + botY) / 2.0;
        Vector3 centre = new(width / 2f, 0, 0);
        // Shift from (width/2, 0, 0) along `up` by midY so the projected vertical bounds sit centered.
        Vector3 target = centre + (up * (float)midY);
        const float distance = 18f;
        return new CameraPose(target - (forward * distance), -TacticalPitch, 0);
    }

    /// <summary>Orthographic vertical height that fits a <paramref name="width"/> by <paramref name="depth"/> tactical room (with perimeter walls) into <paramref name="aspect"/>.</summary>
    public static double OrthographicHeightFor(int width, int depth, double aspect)
    {
        double pitch = TacticalPitch * Math.PI / 180;
        double spanX = width + 0.7;
        double spanY = (depth * Math.Sin(pitch)) + (1.0 * Math.Cos(pitch)) + (0.35 * Math.Sin(pitch));
        double safeAspect = Math.Max(0.25, aspect);
        return Math.Max(spanY / Fill, (spanX / safeAspect) / Fill);
    }

    /// <summary>Eye-level Wizardry / Dragon Quest camera framing the enemy ranks heroically against the stage backdrop.</summary>
    public static CameraPose PoseForFirstPerson(int width, int depth, double aspect)
    {
        double pitch = 4;
        double fov = 36;
        double tanY = Math.Tan(fov * Math.PI / 360);
        double tanX = tanY * Math.Max(0.4, aspect);
        // Frame a ~3.0-cell enemy lineup closely so sprites fill ~55-65% of the viewport height.
        double halfWidth = 1.5;
        double distForWidth = halfWidth / (0.88 * tanX);
        double distForHeight = 0.54 / (0.86 * tanY);
        double distance = Math.Max(1.65, Math.Max(distForWidth, distForHeight));
        float targetZ = depth * 0.66f;
        return new CameraPose(new Vector3(width / 2f, 0.52f + (float)(distance * Math.Sin(pitch * Math.PI / 180)), targetZ + (float)distance), -pitch, 0);
    }

    /// <summary>Close theatrical JRPG side-view camera framing party on the right and enemies on the left.</summary>
    public static CameraPose PoseForSideView(int width, int depth, double aspect)
    {
        double pitch = 12;
        double fov = 34;
        double tanY = Math.Tan(fov * Math.PI / 360);
        double tanX = tanY * Math.Max(0.4, aspect);
        // Frame both opposing columns tightly so heroes and monsters read large and clear.
        double halfWidth = Math.Max(1.85, width * 0.36);
        double distForWidth = halfWidth / (0.90 * tanX);
        double distForHeight = 0.72 / (0.88 * tanY);
        double distance = Math.Max(2.2, Math.Max(distForWidth, distForHeight));
        float targetZ = depth * 0.68f;
        float cameraZ = Math.Max(depth + 0.6f, targetZ + (float)distance);
        float dz = cameraZ - (depth * 0.5f);
        return new CameraPose(new Vector3(width / 2f, 0.42f + (float)(dz * Math.Sin(pitch * Math.PI / 180)), cameraZ), -pitch, 0);
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
    /// <param name="roomWalls">Optional wall set material and wall/cap frames for framing the tactical room perimeter.</param>
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
        (Material Material, UvRect Wall, UvRect? Cap)? roomWalls,
        List<AppearanceFact> facts,
        (ResolvedTilemap Tilemap, Material Material, Definition Sheet, (int Width, int Height) Size)? tiledFloor = null)
    {
        string sceneKey = $"{fightKey}@{presentation}";
        if (sceneKey != _fightKey)
        {
            Build(sceneKey, presentation, observation, kindFor, spriteFor, terrainFor, field, floor, backdrop, roomWalls, tiledFloor);
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
        (Material Material, UvRect Frame, bool Tile)? backdrop,
        (Material Material, UvRect Wall, UvRect? Cap)? roomWalls,
        (ResolvedTilemap Tilemap, Material Material, Definition Sheet, (int Width, int Height) Size)? tiledFloor = null)
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
        bool useTiledFloor = !stageMode && tiledFloor is not null;
        int floorMargin = stageMode ? 4 : 0;
        int floorDepthExtra = stageMode ? 4 : 0;
        _floorPlacement = new Transform(new Vector3(-floorMargin, 0, 0), Quaternion.Identity, Vector3.One);
        AreaGeometry geometry = useTiledFloor
            ? AreaMesh.Tilemap(tiledFloor!.Value.Tilemap, tiledFloor.Value.Sheet, tiledFloor.Value.Size)
            : AreaMesh.Floor(_width + (floorMargin * 2), _depth + floorDepthExtra, floor?.Frame);
        Material floorMat = useTiledFloor
            ? tiledFloor!.Value.Material
            : floor is not null ? floor.Value.Material : _plain;
        _floorMesh = _graphics.CreateMeshResource(new MeshResourceCreateRequest(
            geometry.Positions,
            geometry.Normals,
            geometry.Uvs,
            geometry.Indices,
            geometry.Groups.Select(group => new MeshGroup(group.Slot, group.Start, group.Count)).ToArray(),
            geometry.Groups.Select(group => new MeshMaterialBinding(group.Slot, group.Slot == AreaMesh.TexturedSlot ? floorMat : _plain)).ToArray()));
        _floor = _graphics.CreateMeshAppearance(_floorMesh);

        HashSet<Cell> wallMeshCells = [];
        if (stageMode)
        {
            // Fill the viewport edge-to-edge behind the fighters without dark side bars on widescreen layouts.
            int wallMargin = 1;
            float wallHeight = presentation == "first_person" ? 2.6f : 2.9f;
            float wallZ = presentation == "first_person" ? 0.6f : 0.0f;
            _backdropPlacement = new Transform(new Vector3(-wallMargin, 0, wallZ), Quaternion.Identity, Vector3.One);
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
        else if (!useTiledFloor && roomWalls is not null)
        {
            // Frame the orthographic tactical battlefield with perimeter dungeon stone walls plus any
            // interior stone wall blocks (such as Ultima IV projected dungeon corridors/doorways).
            List<Cell> interiorWalls = [];
            if (field is not null)
            {
                foreach ((Cell cell, Terrain terrain) in field.Terrain)
                {
                    if (!terrain.Passable && terrain.BlocksSight && !terrain.FromTilemap && terrainFor(terrain.Key) is null)
                    {
                        interiorWalls.Add(cell);
                        wallMeshCells.Add(cell);
                    }
                }
            }

            _backdropPlacement = new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One);
            AreaGeometry roomGeo = AreaMesh.TacticalRoomWalls(_width, _depth, roomWalls.Value.Wall, roomWalls.Value.Cap ?? roomWalls.Value.Wall, interiorWalls);
            _backdropMesh = _graphics.CreateMeshResource(new MeshResourceCreateRequest(
                roomGeo.Positions,
                roomGeo.Normals,
                roomGeo.Uvs,
                roomGeo.Indices,
                roomGeo.Groups.Select(group => new MeshGroup(group.Slot, group.Start, group.Count)).ToArray(),
                roomGeo.Groups.Select(group => new MeshMaterialBinding(group.Slot, group.Slot == AreaMesh.TexturedSlot ? roomWalls.Value.Material : _plain)).ToArray()));
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
            BuildTacticalFigures(observation, kindFor, spriteFor, terrainFor, field, wallMeshCells);
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
            float spacing = rowSize <= 2 ? 1.05f : 0.88f;
            float startX = (_width / 2f) - ((rowSize - 1) * spacing / 2f);
            float x = startX + (rowIndex * spacing) + (back ? 0.2f : 0f);
            float z = back ? 1.95f : 2.65f;
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

        PlaceSideColumn(party, leftSide: false, baseX: 3.12f, rankStep: 0.56f, staggerX: 0.24f, kindFor, spriteFor);
        PlaceSideColumn(foes, leftSide: true, baseX: 1.72f, rankStep: -0.56f, staggerX: -0.22f, kindFor, spriteFor);
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
            float zSpan = countInRank <= 1 ? 0f : Math.Min(1.22f, (countInRank - 1) * 0.42f);
            float z = (_depth * 0.55f) - (zSpan / 2f) + (countInRank <= 1 ? 0f : slot * (zSpan / (countInRank - 1)));
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
        CombatField? field,
        HashSet<Cell> wallMeshCells)
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
            if (terrain.FromTilemap || wallMeshCells.Contains(cell))
            {
                continue;
            }

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
