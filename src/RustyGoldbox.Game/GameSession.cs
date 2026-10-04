using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
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

    public Screen Screen { get; private set; } = Screen.Title;

    public List<CampaignChoice> Campaigns { get; private set; } = [];

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

    /// <summary>The fight being played back on the combat screen.</summary>
    public FightReplay? Fight { get; private set; }

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
    public void Roll(IEngineContext engine, string name, string? race, string? characterClass, string? portrait = null, IReadOnlyList<string>? features = null, IReadOnlyList<string>? boosts = null, IReadOnlyList<SkillAllocation>? skillPoints = null, string? creation = null, string? lifepath = null, IReadOnlyList<string>? careers = null, IReadOnlyList<string>? skillTables = null, IReadOnlyList<string>? benefits = null, int terms = 0)
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
        Character? character = CharacterRules.Create(rules, Character.StampsOf(Set), new CreationRequest(name, characterClass, race, Creation: creation, SkillPoints: skillPoints, Features: features, Boosts: boosts, Lifepath: lifepath, Careers: careers, SkillTables: skillTables, Benefits: benefits, Terms: terms), dice, problems);
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
            Log.Clear();
            Screen = Screen.Play;
            Record(null, Runner.Begin(engine.Random));
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
        }
        catch (RuleFailure failure)
        {
            Notes.Add(Describe(failure.Diagnostic));
        }
    }

    public void Save(IEngineContext engine, string slot)
    {
        Notes.Clear();
        if (Screen != Screen.Play)
        {
            return;
        }

        try
        {
            using SaveSlots slots = new(engine);
            slots.Write(slot, SaveFile.ToJson(Runner!.State, Set!));
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

        if (LoadSet(bundle, extensions) is not ModuleSet set)
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
        Log.Clear();
        Log.Add($"Loaded {SaveSlots.Location(slot)}.");
        Screen = Screen.Play;
    }

    /// <summary>Lets time pass for the fight playback; returns whether anything new showed.</summary>
    public bool Tick(double seconds)
    {
        return Screen == Screen.Combat && Fight!.Advance(seconds);
    }

    /// <summary>On the combat screen: shows the rest of the fight, or once it has all shown, returns to play.</summary>
    public void Continue()
    {
        Notes.Clear();
        if (Screen != Screen.Combat)
        {
            return;
        }

        if (!Fight!.Done)
        {
            Fight.Finish();
            return;
        }

        Fight = null;
        Screen = Screen.Play;
    }

    public void Quit()
    {
        Notes.Clear();
        Fight = null;
        Screen = Screen.Title;
        Runner = null;
        Set = null;
        Campaign = null;
        Party.Clear();
        Log.Clear();
        _sounds.Clear();
    }

    private ModuleSet? LoadSet(string bundle, IReadOnlyList<string> extensions)
    {
        ModuleSet set = library.Load(bundle, extensions);
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

    private void Record(string? command, List<PlayFact> facts)
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

            // A fight plays back on the combat screen; the log keeps its outcome.
            if (fact is FightFact fight)
            {
                Fight = new FightReplay(fight);
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
