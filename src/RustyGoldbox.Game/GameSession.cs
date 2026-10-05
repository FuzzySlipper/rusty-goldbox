using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Game;

internal enum Screen
{
    /// <summary>Pick a campaign or load a save.</summary>
    Title,

    /// <summary>Roll and equip a party for the opened campaign.</summary>
    Party,

    /// <summary>Play: the same text commands as <c>goldbox play</c> scripts.</summary>
    Play,

    /// <summary>A fight the last command started, played back; continue returns to play.</summary>
    Combat,
}

/// <summary>
/// What the player is doing: the screen, the loaded module set, the party
/// being made and the running campaign. The product feeds it commands from
/// Engine input and publishes it as a projection; every rule decision is
/// Core's.
/// </summary>
internal sealed class GameSession(ModuleLibrary library)
{
    /// <summary>How many log lines the readout keeps; older lines scroll away.</summary>
    public const int LogLines = 60;

    private const string CharacterScope = "goldbox.character";

    private int _rolls;
    private int _shownCombatFacts;
    // Presentation cursor identity: a chained live fight starts a fresh fact
    // list even though the campaign command returns both fights together.
    private string? _shownCombatKey;
    private CombatObservation? _completedCombat;
    private PendingCombatState? _completedCombatMetadata;

    public Screen Screen { get; private set; } = Screen.Title;

    public List<CampaignChoice> Campaigns { get; private set; } = [];

    /// <summary>Fetching, updating and removing published modules in the module library.</summary>
    public ModuleInstaller Installer { get; } = new(library);

    /// <summary>The module set of the open campaign.</summary>
    public ModuleSet? Set { get; private set; }

    public Definition? Campaign { get; private set; }

    /// <summary>
    /// The seed of the open campaign: character rolls use it with a scope per
    /// roll, and the game starts from it, so the session replays from it.
    /// </summary>
    public ulong Seed { get; private set; }

    /// <summary>The party being made, before play starts.</summary>
    public List<Character> Party { get; } = [];

    private readonly List<Definition> _sounds = [];

    public CampaignRunner? Runner { get; private set; }

    /// <summary>The combat on the combat screen: the live fight, or the one that just finished.</summary>
    // Finishing a fight clears Core's pending state. Keep the finished fight's
    // last observation on screen, with its stable IDs and resources, until
    // Continue is pressed.
    public CombatObservation? Combat => _completedCombat ?? Runner?.Combat;

    /// <summary>The metadata captured at the live combat boundary, including after a terminal command.</summary>
    public PendingCombatState? CombatMetadata => Runner?.State.PendingCombat ?? _completedCombatMetadata;

    /// <summary>A fight that has just finished and is still on the combat screen, until Continue.</summary>
    public PendingCombatState? FinishedFight => _completedCombat is null ? null : _completedCombatMetadata;

    /// <summary>
    /// True for a multiplayer guest: its screen comes from the host's views
    /// (<see cref="ShowView"/>) and its commands go to the host instead of
    /// running here.
    /// </summary>
    public bool Guest { get; private set; }

    /// <summary>
    /// Who plays what when this game is hosted, or null for single-player.
    /// A guest keeps the host's copy, from its views, only to show it.
    /// </summary>
    public PartyTable? Table { get; set; }

    /// <summary>This player's Engine session member (the host is 1).</summary>
    public uint LocalMember { get; set; } = PartyTable.HostMember;

    /// <summary>
    /// A player left the hosted table: their seat waits for a rejoin, the
    /// lead returns to the host if they held it, and their characters fight
    /// under Core's automatic control meanwhile, mid-fight included.
    /// </summary>
    public void SeatLeft(IEngineContext engine, uint member)
    {
        if (Table is not PartyTable table || table.SeatOf(member) is not Seat seat)
        {
            return;
        }

        table.Leave(member);
        foreach (int index in seat.Characters)
        {
            if (Control(engine, index, CombatControlMode.Automatic))
            {
                table.Covered.Add(index);
            }
        }
    }

    /// <summary>A player joined, or rejoined with the same key and gets back their seat and the characters covered for them.</summary>
    public Seat? SeatJoined(IEngineContext engine, uint member, string key, string name)
    {
        if (Table is not PartyTable table)
        {
            return null;
        }

        Seat seat = table.Join(member, key, name);
        foreach (int index in seat.Characters.Where(table.Covered.Remove).ToList())
        {
            Control(engine, index, CombatControlMode.Manual);
        }

        return seat;
    }

    /// <summary>Sets a party member's controller: in a live fight through Core, and as their preference for fights to come.</summary>
    private bool Control(IEngineContext engine, int index, CombatControlMode mode)
    {
        if (Runner?.State.Party is not List<Character> party || index < 0 || index >= party.Count)
        {
            return false;
        }

        party[index].CombatControlPreference = mode;
        if (Runner.State.PendingCombat?.Participants.FirstOrDefault(participant => participant.PartyIndex == index) is PendingCombatantSource fighter)
        {
            SetCombatController(engine, fighter.Id, mode);
        }

        return true;
    }

    /// <summary>A guest's commands waiting to be sent to the host, oldest first.</summary>
    public List<JsonElement> Outbox { get; } = [];

    /// <summary>Hosting or joining a game over the Engine session service.</summary>
    public HostedGame Hosting => _hosting ??= new HostedGame(this);

    private HostedGame? _hosting;

    /// <summary>The campaign module a host's view needs that this guest lacks, or null.</summary>
    public (string Id, string Version)? Missing { get; private set; }

    /// <summary>A guest's game ended or was left: back to the title screen, playing alone again.</summary>
    public void LeaveAsGuest()
    {
        Guest = false;
        Missing = null;
        Outbox.Clear();
        Quit();
    }

    /// <summary>The play transcript's latest lines, oldest first.</summary>
    public List<string> Log { get; } = [];

    /// <summary>The player's interface scale, which the panels give the Engine's UI scale; kept with the settings.</summary>
    public double UiScale { get; private set; } = 1;

    /// <summary>The interface scales a player may pick: the Engine allows 0.25 to 4; under half or over two and a half is no use to the panels.</summary>
    public const double UiScaleMinimum = 0.5;

    public const double UiScaleMaximum = 2.5;

    /// <summary>Sets the player's interface scale.</summary>
    public void SetUiScale(double scale)
    {
        Notes.Clear();
        if (double.IsNaN(scale) || scale < UiScaleMinimum || scale > UiScaleMaximum)
        {
            Notes.Add($"An interface scale is from {UiScaleMinimum} to {UiScaleMaximum}, not {scale}.");
            return;
        }

        UiScale = scale;
    }

    /// <summary>The Music bus volume, from 0 (silent) to 1.</summary>
    public float MusicVolume { get; private set; } = 1;

    /// <summary>The Sfx bus volume (event sounds), from 0 (silent) to 1.</summary>
    public float SoundVolume { get; private set; } = 1;

    /// <summary>Sets a bus volume: "music" or "sound", from 0 to 1.</summary>
    public void SetVolume(string bus, float volume)
    {
        Notes.Clear();
        if (volume is < 0 or > 1 || float.IsNaN(volume))
        {
            Notes.Add($"A volume is from 0 to 1, not {volume}.");
            return;
        }

        switch (bus)
        {
            case "music":
                MusicVolume = volume;
                break;
            case "sound":
                SoundVolume = volume;
                break;
            default:
                Notes.Add($"'{bus}' is not a volume; volumes are music and sound.");
                break;
        }
    }

    /// <summary>The sounds play brought since the last call, in order, to be played once each.</summary>
    public List<Definition> TakeSounds()
    {
        List<Definition> sounds = [.. _sounds];
        _sounds.Clear();
        return sounds;
    }

    /// <summary>What the last command produced that isn't play: problems and confirmations.</summary>
    public List<string> Notes { get; } = [];

    /// <summary>The skins the player may pick from.</summary>
    public List<SkinChoice> Skins { get; private set; } = [];

    /// <summary>The skin the player picked, with the module set it was loaded from; null for the campaign's own (or none).</summary>
    public (SkinChoice Choice, ModuleSet Set, Definition Skin)? PickedSkin { get; private set; }

    /// <summary>The skin the panels wear: the player's pick, else the open campaign's, else none.</summary>
    public (ModuleSet Set, Definition Skin)? ActiveSkin =>
        PickedSkin is var (_, set, skin) ? (set, skin)
        : Set?.Rules is RuleSet rules && Campaign is Definition campaign && campaign.Json.TryGetProperty("skin", out _) ? (Set, rules.Reference(campaign, "$.skin"))
        : null;

    /// <summary>The player's own layout proportions, by part name; null for the skin's (or the Game's) own.</summary>
    public Dictionary<string, double>? LayoutPicked { get; private set; }

    /// <summary>The proportions the panels use: the Game's defaults, then the active skin's, then the player's.</summary>
    public Dictionary<string, double> Layout
    {
        get
        {
            Dictionary<string, double> layout = SkinLayout.Over(SkinLayoutValues());
            foreach ((string name, double value) in LayoutPicked ?? [])
            {
                layout[name] = value;
            }

            return layout;
        }
    }

    /// <summary>Sets the player's layout (checked against the parts and their ranges, over the active skin's), or with null goes back to the skin's.</summary>
    public void SetLayout(Dictionary<string, double>? values)
    {
        Notes.Clear();
        if (values is null)
        {
            LayoutPicked = null;
            return;
        }

        Dictionary<string, double> combined = SkinLayoutValues();
        foreach ((string name, double value) in values)
        {
            combined[name] = value;
        }

        List<(string Part, string Problem)> problems = SkinLayout.Problems(combined);
        if (problems.Count > 0)
        {
            Notes.AddRange(problems.Select(problem => problem.Problem));
            return;
        }

        LayoutPicked = values;
    }

    private Dictionary<string, double> SkinLayoutValues()
    {
        return ActiveSkin is var (_, skin) && skin.Json.TryGetProperty("layout", out JsonElement layout)
            ? layout.EnumerateObject().ToDictionary(part => part.Name, part => part.Value.GetDouble())
            : [];
    }

    /// <summary>Picks an installed skin by ID, or with null goes back to the campaign's own.</summary>
    public void PickSkin(string? id)
    {
        Notes.Clear();
        if (id is null)
        {
            PickedSkin = null;
            return;
        }

        if (Skins.FirstOrDefault(skin => skin.Id == id) is not SkinChoice choice)
        {
            Notes.Add($"'{id}' is not an installed skin; skins are {(Skins.Count == 0 ? "none" : string.Join(", ", Skins.Select(skin => skin.Id)))}.");
            return;
        }

        ModuleSet set = library.Load(choice.Bundle, []);
        if (set.Rules?.Find(DefinitionTypes.Skin, choice.Id, out _) is not Definition skin)
        {
            Notes.AddRange(set.Diagnostics.Select(Describe));
            return;
        }

        PickedSkin = (choice, set, skin);
    }

    public void Refresh()
    {
        Notes.Clear();
        Skins = library.Skins();
        Campaigns = library.Campaigns(Notes);
        if (Campaigns.Count == 0)
        {
            Notes.Add($"No campaign modules are in the product's content bundles or its module library ({InstalledModules.DefaultDirectory()}).");
        }
    }

    /// <param name="extensions">Extension IDs to add to the campaign's module set, from those it offers.</param>
    public void Open(string bundle, ulong seed, IReadOnlyList<string>? extensions = null)
    {
        Notes.Clear();
        if (Campaigns.FirstOrDefault(campaign => campaign.Bundle == bundle) is not CampaignChoice choice)
        {
            Notes.Add($"'{bundle}' is not one of the campaigns offered.");
            return;
        }

        extensions ??= [];
        if (extensions.FirstOrDefault(id => choice.Extensions.All(offered => offered.Id != id)) is string unknown)
        {
            string offered = choice.Extensions.Count == 0 ? "none" : string.Join(", ", choice.Extensions.Select(offered => offered.Id));
            Notes.Add($"'{unknown}' is not an extension {choice.Title} offers (it offers {offered}).");
            return;
        }

        if (LoadSet(bundle, extensions) is not ModuleSet set)
        {
            return;
        }

        Set = set;
        Campaign = set.Rules!.OfType(DefinitionTypes.Campaign).Single(definition => definition.Module == set.Root!.Id);
        Seed = seed;
        _rolls = 0;
        Party.Clear();
        Runner = null;
        _completedCombat = null;
        _completedCombatMetadata = null;
        _shownCombatFacts = 0;
        _shownCombatKey = null;
        Screen = Screen.Party;
    }

    /// <summary>Rolls a character with the ruleset's character creation.</summary>
    /// <param name="portrait">A portrait asset for the character, or null for none.</param>
    /// <param name="race">The race, or null in a ruleset without races.</param>
    /// <param name="characterClass">The class, or null in a ruleset without classes.</param>
    /// <param name="lifepath">The optional term-by-term career procedure.</param>
    /// <param name="careers">Career IDs, one per term, or one repeated with <paramref name="terms"/>.</param>
    /// <param name="skillTables">Skill-table IDs consumed by the actual term rolls.</param>
    /// <param name="benefits">Cash or material choices consumed by mustering-out rolls.</param>
    /// <param name="terms">Number of repeated terms; zero means one.</param>
    /// <param name="attributes">Scores supplied by an authored point-buy method, when selected.</param>
    /// <param name="priority">Attribute priority supplied by an authored array or arranged-roll method, when selected.</param>
    public void Roll(IEngineContext engine, string name, string? race, string? characterClass, string? portrait = null, IReadOnlyList<string>? features = null, IReadOnlyList<string>? boosts = null, IReadOnlyList<SkillAllocation>? skillPoints = null, string? creation = null, string? lifepath = null, IReadOnlyList<string>? careers = null, IReadOnlyList<string>? skillTables = null, IReadOnlyList<string>? benefits = null, int terms = 0, IReadOnlyDictionary<string, decimal>? attributes = null, IReadOnlyList<string>? priority = null)
    {
        Notes.Clear();
        if (Screen != Screen.Party)
        {
            Notes.Add("Open a campaign before making characters.");
            return;
        }

        RuleSet rules = Set!.Rules!;
        List<ModuleDiagnostic> problems = [];
        Definition? chosen = null;
        if (portrait is not null && (chosen = CharacterRules.FindPortrait(rules, portrait, problems)) is null)
        {
            Notes.AddRange(problems.Select(problem => problem.Message));
            return;
        }

        int roll = ++_rolls;
        using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(Seed, $"{CharacterScope}.{roll}"));
        DiceRoller dice = new(engine.Random, stream);
        Character? character = CharacterRules.Create(rules, Character.StampsOf(Set), new CreationRequest(name, characterClass, race, Creation: creation, Attributes: attributes, Priority: priority, SkillPoints: skillPoints, Features: features, Boosts: boosts, Lifepath: lifepath, Careers: careers, SkillTables: skillTables, Benefits: benefits, Terms: terms), dice, problems);
        if (character is null)
        {
            Notes.AddRange(problems.Select(problem => problem.Message));
            Notes.Add($"Roll again for new scores (seed {Seed}, roll {roll}).");
            return;
        }

        character.Portrait = chosen;
        Party.Add(character);
        string kind = string.Join(" ", new[] { character.Race?.Name, character.Class?.Name }.Where(part => part is not null));
        Notes.Add($"Rolled {character.Name}{(kind.Length > 0 ? $", a {kind}" : "")} (seed {Seed}, roll {roll}).");
    }

    public void Drop(int member)
    {
        Notes.Clear();
        if (Screen == Screen.Party && member >= 0 && member < Party.Count)
        {
            Party.RemoveAt(member);
        }
    }

    /// <summary>Commits one party member's staged profession and personal skill choices.</summary>
    public void SpendSkillPoints(IEngineContext engine, int member, IReadOnlyList<SkillAllocation> allocations)
    {
        Notes.Clear();
        if (Screen != Screen.Party || member < 0 || member >= Party.Count)
        {
            Notes.Add("Choose a party member after opening a campaign before spending skill points.");
            return;
        }

        Character character = Party[member];
        List<ModuleDiagnostic> problems = [];
        using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(Seed, $"{CharacterScope}.skills.{member}"));
        DiceRoller dice = new(engine.Random, stream);
        if (!CharacterRules.ApplySkillPoints(Set!.Rules!, character, allocations, dice, problems))
        {
            Notes.AddRange(problems.Select(problem => problem.Message));
            return;
        }

        Notes.Add($"Committed staged skill points for {character.Name}.");
    }

    /// <summary>Gives a party member an item, or takes it back when they have it.</summary>
    public void Equip(int member, string item)
    {
        Notes.Clear();
        if (Screen != Screen.Party || member < 0 || member >= Party.Count)
        {
            return;
        }

        if (Set!.Rules!.Find(DefinitionTypes.Item, item, out string? problem) is not Definition found)
        {
            Notes.Add(problem!);
            return;
        }

        List<Definition> equipment = Party[member].Equipment;
        if (equipment.Remove(found))
        {
            return;
        }

        if (CharacterRules.EquipmentProblem(Set.Rules, Party[member], found) is string refused)
        {
            Notes.Add(refused);
            return;
        }

        equipment.Add(found);
    }

    /// <summary>
    /// Sets the spells a party member knows, while making the party or between
    /// fights in play; a list Core refuses leaves them as they were.
    /// </summary>
    public void SetSpells(int member, IReadOnlyList<string> spells)
    {
        Notes.Clear();
        List<Character>? party = Screen switch
        {
            Screen.Party => Party,
            Screen.Play => Runner!.State.Party,
            _ => null,
        };
        if (party is null || member < 0 || member >= party.Count)
        {
            return;
        }

        List<ModuleDiagnostic> problems = [];
        if (!CharacterRules.SetSpells(Set!.Rules!, party[member], spells, problems))
        {
            Notes.AddRange(problems.Select(problem => problem.Message));
        }
    }

    /// <summary>
    /// Sets the copies a party member memorises each day (an empty list: its
    /// known spells in order). Making the party, they are prepared at once; in
    /// play, at the next rest that prepares spells.
    /// </summary>
    public void SetMemorised(int member, IReadOnlyList<string> spells)
    {
        Notes.Clear();
        List<Character>? party = Screen switch
        {
            Screen.Party => Party,
            Screen.Play => Runner!.State.Party,
            _ => null,
        };
        if (party is null || member < 0 || member >= party.Count)
        {
            return;
        }

        List<ModuleDiagnostic> problems = [];
        if (!CharacterRules.SetMemorised(Set!.Rules!, party[member], spells, problems, prepareNow: Screen == Screen.Party))
        {
            Notes.AddRange(problems.Select(problem => problem.Message));
        }
    }

    public void Begin(IEngineContext engine)
    {
        Notes.Clear();
        if (Screen != Screen.Party)
        {
            return;
        }

        JsonElement size = Campaign!.Json.GetProperty("party");
        int min = size.GetProperty("min").GetInt32();
        int max = size.GetProperty("max").GetInt32();
        if (Party.Count < min || Party.Count > max)
        {
            Notes.Add($"{Campaign.Name} takes a party of {min} to {max}; this one has {Party.Count}.");
            return;
        }

        RuleSet rules = Set!.Rules!;
        try
        {
            Runner = new CampaignRunner(rules, CampaignRunner.NewState(rules, Campaign, Party, Seed));
            // The Game is the ordinary player-facing host, so party actors
            // wait for a visible choice. Core still owns every rule and
            // automatically advances eligible non-party actors.
            Runner.DefaultCombatControl = CombatControlMode.Manual;
            _completedCombat = null;
            _completedCombatMetadata = null;
            _shownCombatFacts = 0;
            _shownCombatKey = null;
            Log.Clear();
            Screen = Screen.Play;
            Record(null, Runner.Begin(engine.Random));
            SyncCombat(engine);
        }
        catch (RuleFailure failure)
        {
            Notes.Add(Describe(failure.Diagnostic));
        }
    }

    /// <summary>Runs one play command, including movement, search, door opening, event choices and status.</summary>
    public void Execute(IEngineContext engine, string command)
    {
        Notes.Clear();
        if (Screen == Screen.Combat)
        {
            Notes.Add("Continue past the fight first.");
            return;
        }

        if (Screen != Screen.Play)
        {
            return;
        }

        try
        {
            Record(command, Runner!.Execute(command, engine.Random));
            SyncCombat(engine);
        }
        catch (RuleFailure failure)
        {
            Notes.Add(Describe(failure.Diagnostic));
        }
    }

    /// <summary>Changes the Core controller for one live combatant.</summary>
    public void SetCombatController(IEngineContext engine, string actorId, CombatControlMode mode)
    {
        Notes.Clear();
        if (Screen != Screen.Combat || Runner?.State.PendingCombat is null)
        {
            Notes.Add("No live combat is waiting for a controller choice.");
            return;
        }

        try
        {
            PendingCombatState? before = Runner.State.PendingCombat;
            ApplyCombat(Runner.SetCombatController(actorId, mode, engine.Random), before);
        }
        catch (RuleFailure failure)
        {
            Notes.Add(Describe(failure.Diagnostic));
        }
    }

    /// <summary>Submits an ordinary action through the Core live combat owner.</summary>
    public void CombatAction(IEngineContext engine, string actorId, string actionId, IReadOnlyList<string> targetIds, IReadOnlyList<Cell>? path)
    {
        SubmitCombat(engine, new CombatCommand.UseAction(actorId, actionId, targetIds, path));
    }

    /// <summary>Ends the active actor's turn without spending a future action.</summary>
    public void CombatEndTurn(IEngineContext engine, string actorId)
    {
        SubmitCombat(engine, new CombatCommand.EndTurn(actorId));
    }

    /// <summary>Accepts or declines the current optional live decision.</summary>
    public void CombatDecide(IEngineContext engine, string decisionId, string? optionId)
    {
        SubmitCombat(engine, new CombatCommand.Decide(decisionId, optionId));
    }

    private void SubmitCombat(IEngineContext engine, CombatCommand command)
    {
        Notes.Clear();
        if (Screen != Screen.Combat || Runner?.State.PendingCombat is null)
        {
            Notes.Add("No live combat is waiting for a command.");
            return;
        }

        try
        {
            PendingCombatState? before = Runner.State.PendingCombat;
            ApplyCombat(Runner.SubmitCombat(command, engine.Random), before);
        }
        catch (RuleFailure failure)
        {
            Notes.Add(Describe(failure.Diagnostic));
        }
    }

    private void ApplyCombat(CampaignCombatCommandResult result, PendingCombatState? before)
    {
        AppendCombatFacts(result.Observation, Runner?.State.PendingCombat ?? before);
        Record(null, result.Facts);
        if (!result.Accepted && result.Reason is string reason)
        {
            Notes.Add(reason);
        }

        // A finished fight stays on the combat view until the player continues.
        Screen = Runner?.State.PendingCombat is not null || _completedCombat is not null ? Screen.Combat : Screen.Play;
    }

    private void SyncCombat(IEngineContext engine)
    {
        if (Runner?.State.PendingCombat is not null)
        {
            CombatObservation? observation = Runner.ObserveCombat(engine.Random);
            if (observation is not null)
            {
                AppendCombatFacts(observation, Runner.State.PendingCombat);
                Screen = Screen.Combat;
            }

            return;
        }

        if (_completedCombat is null && Screen == Screen.Combat)
        {
            Screen = Screen.Play;
        }
    }

    private void AppendCombatFacts(CombatObservation observation, PendingCombatState? pending)
    {
        string? key = pending?.Continuation.RandomScope;
        if (key is not null && key != _shownCombatKey)
        {
            _shownCombatFacts = 0;
            _shownCombatKey = key;
        }

        IReadOnlyList<CombatFact> facts = observation.Facts;
        foreach (CombatFact fact in facts.Skip(_shownCombatFacts))
        {
            Log.Add(fact.Describe());
        }

        _shownCombatFacts = facts.Count;
        if (Log.Count > LogLines)
        {
            Log.RemoveRange(0, Log.Count - LogLines);
        }
    }

    public void Save(IEngineContext engine, string slot)
    {
        Notes.Clear();
        if (Screen is not (Screen.Play or Screen.Combat) || Runner is null)
        {
            return;
        }

        try
        {
            // Observation reconstructs a saved live owner without advancing
            // it, ensuring the continuation written here is the current one.
            if (Screen == Screen.Combat && Runner.State.PendingCombat is not null)
            {
                Runner.ObserveCombat(engine.Random);
            }

            using SaveSlots slots = new(engine);
            slots.Write(slot, SaveFile.ToJson(Runner.State, Set!));
            Notes.Add($"Saved to {SaveSlots.Location(slot)}.");
        }
        catch (Exception exception) when (exception is PersistenceStorageException or EngineCallException)
        {
            // EngineCallException: the host was started without a persistence root.
            Notes.Add($"Can't save: {exception.Message}");
        }
    }

    public void Load(IEngineContext engine, string slot)
    {
        Notes.Clear();
        byte[]? json;
        try
        {
            using SaveSlots slots = new(engine);
            json = slots.Read(slot);
        }
        catch (Exception exception) when (exception is PersistenceStorageException or EngineCallException)
        {
            Notes.Add($"Can't read {SaveSlots.Location(slot)}: {exception.Message}");
            return;
        }

        if (json is null)
        {
            Notes.Add($"{SaveSlots.Location(slot)} is empty.");
            return;
        }

        if (CampaignModule(json) is not (string module, string version, string identity, List<string> extensions))
        {
            Notes.Add($"{SaveSlots.Location(slot)} isn't a campaign save.");
            return;
        }

        if (library.BundleOf(module, version, identity) is not string bundle)
        {
            Notes.Add($"{SaveSlots.Location(slot)} was made with {module} {version} as it was then; the product has no module with that content.");
            return;
        }

        if (LoadSet(bundle, extensions, SaveFile.Modules(json)) is not ModuleSet set)
        {
            return;
        }

        List<ModuleDiagnostic> problems = [];
        if (SaveFile.Read(json, SaveSlots.Location(slot), set, problems) is not CampaignState state)
        {
            Notes.AddRange(problems.Select(Describe));
            return;
        }

        Set = set;
        Campaign = state.Campaign;
        Seed = state.Seed;
        Runner = new CampaignRunner(set.Rules!, state);
        Runner.DefaultCombatControl = CombatControlMode.Manual;
        Log.Clear();
        Log.Add($"Loaded {SaveSlots.Location(slot)}.");
        Screen = state.PendingCombat is null ? Screen.Play : Screen.Combat;
        _completedCombat = null;
        _completedCombatMetadata = null;
        _shownCombatFacts = 0;
        _shownCombatKey = null;
        if (Screen == Screen.Combat)
        {
            try
            {
                SyncCombat(engine);
            }
            catch (RuleFailure failure)
            {
                Notes.Add(Describe(failure.Diagnostic));
            }
        }
    }

    /// <summary>
    /// Shows a host's view (<see cref="GameView"/>) as this guest's screen:
    /// the same module set, loaded from this machine's bundles and library at
    /// exactly the versions the host plays, and a campaign that is never given
    /// a command. Problems become notes; the previous screen stays.
    /// </summary>
    public void ShowView(IEngineContext engine, JsonElement view)
    {
        Guest = true;
        Notes.Clear();
        List<ModuleDiagnostic> problems = [];
        try
        {
            string screen = view.GetProperty("screen").GetString() ?? "";
            if (view.TryGetProperty("save", out JsonElement save) && save.ValueKind == JsonValueKind.Object)
            {
                byte[] json = System.Text.Encoding.UTF8.GetBytes(save.GetRawText());
                if (ViewSet(CampaignModule(json), SaveFile.Modules(json)) is not ModuleSet set
                    || SaveFile.Read(json, "the host's view", set, problems) is not CampaignState state)
                {
                    Notes.AddRange(problems.Select(Describe));
                    return;
                }

                Set = set;
                Campaign = state.Campaign;
                Seed = state.Seed;
                Runner = new CampaignRunner(set.Rules!, state);
                if (state.PendingCombat is not null)
                {
                    Runner.ObserveCombat(engine.Random);
                }

                _completedCombat = null;
                _completedCombatMetadata = null;
                if (view.TryGetProperty("finished", out JsonElement finished) && finished.ValueKind == JsonValueKind.Object
                    && SaveFile.Read(System.Text.Encoding.UTF8.GetBytes(finished.GetRawText()), "the host's finished fight", set, problems) is CampaignState ended
                    && new CampaignRunner(set.Rules!, ended).ObserveCombat(engine.Random) is CombatObservation last)
                {
                    _completedCombat = last;
                    _completedCombatMetadata = ended.PendingCombat;
                }
            }
            else if (view.TryGetProperty("campaign", out JsonElement campaign) && campaign.ValueKind == JsonValueKind.Object)
            {
                (string, string, string, List<string>) module = (
                    campaign.GetProperty("id").GetString()!,
                    campaign.GetProperty("version").GetString()!,
                    campaign.GetProperty("identity").GetString()!,
                    campaign.GetProperty("extensions").EnumerateArray().Select(entry => entry.GetString()!).ToList());
                if (ViewSet(module, null) is not ModuleSet set)
                {
                    return;
                }

                Set = set;
                Campaign = set.Rules!.OfType(DefinitionTypes.Campaign).Single(definition => definition.Module == set.Root!.Id);
                Seed = ulong.Parse(campaign.GetProperty("seed").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                Runner = null;
                Party.Clear();
                int index = 0;
                foreach (JsonElement character in view.GetProperty("party").EnumerateArray())
                {
                    if (CharacterFile.Read(character, "the host's party", $"$.party[{index++}]", set, problems) is Character member)
                    {
                        Party.Add(member);
                    }
                }

                Notes.AddRange(problems.Select(Describe));
            }

            Screen = screen switch
            {
                "party" => Screen.Party,
                "play" => Screen.Play,
                "combat" => Screen.Combat,
                _ => Screen,
            };
            Table = view.TryGetProperty("table", out JsonElement table) && table.ValueKind == JsonValueKind.Object ? PartyTable.FromJson(table) : null;
            Log.Clear();
            Log.AddRange(view.GetProperty("log").EnumerateArray().Select(line => line.GetString() ?? ""));
            Notes.AddRange(view.GetProperty("notes").EnumerateArray().Select(line => line.GetString() ?? ""));
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or RuleFailure)
        {
            Notes.Add($"The host's view couldn't be shown: {exception.Message}");
        }
    }

    /// <summary>The module set a view is played with, reusing the loaded one when it is the same campaign at the same content.</summary>
    private ModuleSet? ViewSet((string Id, string Version, string Identity, List<string> Extensions)? campaign, IReadOnlyList<SavedModule>? saved)
    {
        if (campaign is not (string id, string version, string identity, List<string> extensions))
        {
            Notes.Add("The host's view doesn't name its campaign.");
            return null;
        }

        if (Set?.Root is ModuleManifest root && root.Id == id && root.Version.ToString() == version && root.Source.Identity == identity
            && Set.Extensions.Order().SequenceEqual(extensions.Order()))
        {
            Missing = null;
            return Set;
        }

        if (library.BundleOf(id, version, identity) is not string bundle)
        {
            Missing = (id, version);
            Notes.Add($"The host plays {id} {version}, which isn't installed here with the same content.");
            return null;
        }

        Missing = null;

        ModuleSet set = library.Load(bundle, extensions, saved);
        if (set.Rules is null || !set.IsValid)
        {
            Notes.AddRange(set.Diagnostics.Select(Describe));
            return null;
        }

        return set;
    }

    /// <summary>On the combat screen after a fight has finished: returns to play.</summary>
    public void Continue()
    {
        Notes.Clear();
        if (Screen != Screen.Combat)
        {
            return;
        }

        // Live combat is advanced only by a typed Core command. This button
        // acknowledges already committed facts and never makes a choice for
        // the player or consumes a future roll.
        if (Runner?.State.PendingCombat is not null)
        {
            Notes.Add("Choose a combat action first; committed facts are already shown.");
            return;
        }

        _completedCombat = null;
        _completedCombatMetadata = null;
        Screen = Screen.Play;
    }

    public void Quit()
    {
        Notes.Clear();
        _completedCombat = null;
        _completedCombatMetadata = null;
        _shownCombatFacts = 0;
        _shownCombatKey = null;
        Screen = Screen.Title;
        Runner = null;
        Set = null;
        Campaign = null;
        Party.Clear();
        Log.Clear();
        _sounds.Clear();
    }

    private ModuleSet? LoadSet(string bundle, IReadOnlyList<string> extensions, IReadOnlyList<SavedModule>? saved = null)
    {
        ModuleSet set = library.Load(bundle, extensions, saved);
        if (set.Rules is null || !set.IsValid)
        {
            Notes.AddRange(set.Diagnostics.Select(Describe));
            return null;
        }

        return set;
    }

    /// <summary>The campaign module (ID, version and identity) a save names and the extensions it added, without trusting the rest of it yet.</summary>
    private static (string Id, string Version, string Identity, List<string> Extensions)? CampaignModule(byte[] json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("campaign", out JsonElement campaign)
                || campaign.ValueKind != JsonValueKind.String
                || campaign.GetString()!.Split(':') is not [string module, _]
                || !root.TryGetProperty("modules", out JsonElement modules)
                || modules.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement entry in modules.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String && id.GetString() == module
                    && entry.TryGetProperty("version", out JsonElement version) && version.ValueKind == JsonValueKind.String
                    && entry.TryGetProperty("identity", out JsonElement identity) && identity.ValueKind == JsonValueKind.String)
                {
                    List<string> extensions = root.TryGetProperty("extensions", out JsonElement added) && added.ValueKind == JsonValueKind.Array
                        ? added.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.String).Select(entry => entry.GetString()!).ToList()
                        : [];
                    return (module, version.GetString()!, identity.GetString()!, extensions);
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Record(string? command, IReadOnlyList<PlayFact> facts)
    {
        if (command is not null)
        {
            Log.Add($"> {command}");
        }

        foreach (PlayFact fact in facts)
        {
            if (fact is MediaFact { Sound: Definition sound })
            {
                _sounds.Add(sound);
            }

            // A finished fight shows on the combat screen, unless the next
            // fight it led to is already waiting there.
            if (fact is FightFact fight && Runner?.State.PendingCombat is null)
            {
                _completedCombat = fight.Ending;
                _completedCombatMetadata = fight.Combat;
                AppendCombatFacts(fight.Ending, fight.Combat);
                Screen = Screen.Combat;
            }

            Log.Add(fact.Describe());
        }

        if (Log.Count > LogLines)
        {
            Log.RemoveRange(0, Log.Count - LogLines);
        }
    }

    private static string Describe(ModuleDiagnostic diagnostic)
    {
        string where = string.Join(" ", new[] { diagnostic.File, diagnostic.JsonPath }.Where(part => part is not null));
        return where.Length == 0 ? $"[{diagnostic.Rule}] {diagnostic.Message}" : $"[{diagnostic.Rule}] {where}: {diagnostic.Message}";
    }
}
