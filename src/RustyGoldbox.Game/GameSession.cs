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

    public CampaignRunner? Runner { get; private set; }

    /// <summary>The play transcript's latest lines, oldest first.</summary>
    public List<string> Log { get; } = [];

    /// <summary>What the last command produced that isn't play: problems and confirmations.</summary>
    public List<string> Notes { get; } = [];

    public void Refresh()
    {
        Notes.Clear();
        Campaigns = library.Campaigns(Notes);
        if (Campaigns.Count == 0)
        {
            Notes.Add($"No campaign modules are in the product's content bundles or its module library ({InstalledModules.DefaultDirectory()}).");
        }
    }

    public void Open(string bundle, ulong seed)
    {
        Notes.Clear();
        if (Campaigns.All(campaign => campaign.Bundle != bundle))
        {
            Notes.Add($"'{bundle}' is not one of the campaigns offered.");
            return;
        }

        if (LoadSet(bundle) is not ModuleSet set)
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
    public void Roll(IEngineContext engine, string name, string race, string characterClass)
    {
        Notes.Clear();
        if (Screen != Screen.Party)
        {
            Notes.Add("Open a campaign before making characters.");
            return;
        }

        RuleSet rules = Set!.Rules!;
        List<ModuleDiagnostic> problems = [];
        int roll = ++_rolls;
        using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(Seed, $"{CharacterScope}.{roll}"));
        DiceRoller dice = new(engine.Random, stream);
        Character? character = CharacterRules.Create(rules, Character.StampsOf(Set), new CreationRequest(name, characterClass, race), dice, problems);
        if (character is null)
        {
            Notes.AddRange(problems.Select(problem => problem.Message));
            Notes.Add($"Roll again for new scores (seed {Seed}, roll {roll}).");
            return;
        }

        Party.Add(character);
        Notes.Add($"Rolled {character.Name}, a {character.Race.Name} {character.Class.Name} (seed {Seed}, roll {roll}).");
    }

    public void Drop(int member)
    {
        Notes.Clear();
        if (Screen == Screen.Party && member >= 0 && member < Party.Count)
        {
            Party.RemoveAt(member);
        }
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
        if (!equipment.Remove(found))
        {
            equipment.Add(found);
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

    /// <summary>Runs one play command: forward, back, left, right, around, choose n, look or status.</summary>
    public void Execute(IEngineContext engine, string command)
    {
        Notes.Clear();
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

        if (CampaignModule(json) is not (string module, string version, string identity))
        {
            Notes.Add($"{SaveSlots.Location(slot)} isn't a campaign save.");
            return;
        }

        if (library.BundleOf(module, version, identity) is not string bundle)
        {
            Notes.Add($"{SaveSlots.Location(slot)} was made with {module} {version} as it was then; the product has no module with that content.");
            return;
        }

        if (LoadSet(bundle) is not ModuleSet set)
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

    public void Quit()
    {
        Notes.Clear();
        Screen = Screen.Title;
        Runner = null;
        Set = null;
        Campaign = null;
        Party.Clear();
        Log.Clear();
    }

    private ModuleSet? LoadSet(string bundle)
    {
        ModuleSet set = library.Load(bundle);
        if (set.Rules is null || !set.IsValid)
        {
            Notes.AddRange(set.Diagnostics.Select(Describe));
            return null;
        }

        return set;
    }

    /// <summary>The campaign module (ID, version and identity) a save names, without trusting the rest of it yet.</summary>
    private static (string Id, string Version, string Identity)? CampaignModule(byte[] json)
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
                    return (module, version.GetString()!, identity.GetString()!);
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
            if (fact is FightFact fight)
            {
                Log.AddRange(fight.Facts.Select(combatFact => $"    {combatFact.Describe()}"));
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
