using System.Globalization;
using Rusty.Engine.Persistence;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Cli;

/// <summary>
/// <c>goldbox play</c>: plays a campaign from a command script (or stdin)
/// and prints the transcript. The runner scopes each command's dice, so a
/// save resumed later rolls exactly as an unbroken run.
/// </summary>
internal static class PlayCommand
{
    private const string Usage =
        "Usage: goldbox play --campaign <path> --party <file>,... [--seed <n>] [--script <file>] [--save <save>] [--store <dir>] [--modules <dir>]...\n"
        + "       goldbox play --campaign <path> --load <save> [--script <file>] [--save <save>] [--store <dir>]\n"
        + "A save is a file, or with --store a save slot in that Engine persistence root (the Game's is .runtime/persistence under rusty dev).";

    public static int Run(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--campaign", "--modules", "--party", "--seed", "--script", "--save", "--load", "--store"], []);
        bool loading = parsed.Single("--load") is not null;
        if (error is null && (parsed.Positionals.Count != 0 || parsed.Single("--campaign") is null || loading == (parsed.Single("--party") is not null)))
        {
            error = Usage;
        }

        if (error is null && loading && parsed.Single("--seed") is not null)
        {
            error = "--seed can't be used with --load: a save continues with its own seed.";
        }

        ulong seed = 1;
        if (error is null && parsed.Single("--seed") is string seedText && !ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
        {
            error = $"--seed must be a whole number from 0 to {ulong.MaxValue}, but was '{seedText}'.";
        }

        string? store = parsed.Single("--store") is string storeText ? Path.GetFullPath(storeText, workingDirectory) : null;
        if (error is null && store is not null)
        {
            error = SlotError("--load", parsed.Single("--load")) ?? SlotError("--save", parsed.Single("--save"));
            if (error is null && loading && !Directory.Exists(store))
            {
                error = $"--store: there is no persistence root at {store}.";
            }
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        ModuleSet set = ModuleSets.Load(
            Path.GetFullPath(parsed.Single("--campaign")!, workingDirectory),
            parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList());
        if (set.Rules is null || !set.IsValid)
        {
            return output.ModuleErrors(set);
        }

        RuleSet rules = set.Rules;
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = store });
        List<ModuleDiagnostic> problems = [];
        CampaignState? state = loading
            ? Load(host, store, parsed.Single("--load")!, set, workingDirectory, problems)
            : NewGame(rules, set, parsed.Single("--party")!, seed, workingDirectory, problems);
        if (state is null)
        {
            return output.Problems(problems);
        }

        // Commands are read once the game is ready, so a save or party that can't load never waits on standard input.
        List<string> commands;
        try
        {
            commands = ReadScript(parsed.Single("--script"), workingDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return output.UsageError($"--script: {exception.Message}");
        }

        List<(string? Command, List<PlayFact> Facts)> transcript = [];
        CampaignRunner runner = new(rules, state);
        try
        {
            host.Call(engine =>
            {
                if (!loading)
                {
                    transcript.Add((null, runner.Begin(engine.Random)));
                }

                foreach (string command in commands)
                {
                    transcript.Add((command, runner.Execute(command, engine.Random)));
                }
            });
        }
        catch (RuleFailure failure)
        {
            output.PlayTranscript(state, transcript);
            return output.Problems([failure.Diagnostic]);
        }

        if (parsed.Single("--save") is string save && Save(host, store, save, SaveFile.ToJson(state, set), workingDirectory) is ModuleDiagnostic problem)
        {
            return output.Problems([problem]);
        }

        output.PlayTranscript(state, transcript);
        return GoldboxCli.Ok;
    }

    private static string? SlotError(string option, string? slot)
    {
        return slot is null || SaveSlots.IsValidName(slot)
            ? null
            : $"{option} names a save slot when --store is given; '{slot}' isn't one. Use {SaveSlots.NameDescription}.";
    }

    private static CampaignState? Load(EngineTestHost host, string? store, string save, ModuleSet set, string workingDirectory, List<ModuleDiagnostic> problems)
    {
        if (store is null)
        {
            return SaveFile.Read(Path.GetFullPath(save, workingDirectory), set, problems);
        }

        string location = $"{SaveSlots.Location(save)} in {store}";
        byte[]? json;
        try
        {
            json = host.Call(engine =>
            {
                using SaveSlots slots = new(engine);
                return slots.Read(save);
            });
        }
        catch (PersistenceStorageException exception)
        {
            problems.Add(new ModuleDiagnostic("save.store", $"Can't read the save: {exception.Message}", File: location));
            return null;
        }

        if (json is null)
        {
            problems.Add(new ModuleDiagnostic("save.store", $"The slot is empty. Save to it with --store {store} --save {save}.", File: location));
            return null;
        }

        return SaveFile.Read(json, location, set, problems);
    }

    private static ModuleDiagnostic? Save(EngineTestHost host, string? store, string save, string json, string workingDirectory)
    {
        if (store is null)
        {
            string full = Path.GetFullPath(save, workingDirectory);
            try
            {
                File.WriteAllText(full, json);
                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new ModuleDiagnostic("save.write", $"Can't write the save: {exception.Message}", File: full);
            }
        }

        try
        {
            host.Call(engine =>
            {
                using SaveSlots slots = new(engine);
                slots.Write(save, json);
            });
            return null;
        }
        catch (PersistenceStorageException exception)
        {
            return new ModuleDiagnostic("save.store", $"Can't write the save: {exception.Message}", File: $"{SaveSlots.Location(save)} in {store}");
        }
    }

    private static CampaignState? NewGame(RuleSet rules, ModuleSet set, string partyFiles, ulong seed, string workingDirectory, List<ModuleDiagnostic> problems)
    {
        List<Definition> campaigns = rules.OfType(DefinitionTypes.Campaign).Where(campaign => campaign.Module == set.Root!.Id).ToList();
        if (campaigns.Count != 1)
        {
            problems.Add(new ModuleDiagnostic("play.campaign", $"--campaign must be a campaign module; {set.Root!.Id} has no campaign definition."));
            return null;
        }

        List<Character> party = [];
        foreach (string file in partyFiles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (CharacterFile.Read(Path.GetFullPath(file, workingDirectory), set, problems) is Character character)
            {
                party.Add(character);
            }
        }

        System.Text.Json.JsonElement size = campaigns[0].Json.GetProperty("party");
        int min = size.GetProperty("min").GetInt32();
        int max = size.GetProperty("max").GetInt32();
        if (problems.Count == 0 && (party.Count < min || party.Count > max))
        {
            problems.Add(new ModuleDiagnostic("play.party", $"{campaigns[0].Name} takes a party of {min} to {max}; this one has {party.Count}.", campaigns[0].Module, campaigns[0].File, "$.party"));
        }

        if (problems.Count > 0)
        {
            return null;
        }

        try
        {
            return CampaignRunner.NewState(rules, campaigns[0], party, seed);
        }
        catch (RuleFailure failure)
        {
            problems.Add(failure.Diagnostic);
            return null;
        }
    }

    private static List<string> ReadScript(string? script, string workingDirectory)
    {
        IEnumerable<string> lines = script is null
            ? ReadAll(Console.In)
            : File.ReadAllLines(Path.GetFullPath(script, workingDirectory));
        return lines
            .Select(line => (line.Contains('#', StringComparison.Ordinal) ? line[..line.IndexOf('#', StringComparison.Ordinal)] : line).Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static IEnumerable<string> ReadAll(TextReader reader)
    {
        while (reader.ReadLine() is string line)
        {
            yield return line;
        }
    }
}
