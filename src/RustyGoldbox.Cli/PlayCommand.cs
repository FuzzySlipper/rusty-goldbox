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
        "Usage: goldbox play --campaign <path> --party <file>,... [--seed <n>] [--script <file>] [--save <save>] [--combat-control auto|manual] [--trace] [--fail-on-refusal] [--store <dir>] [--modules <dir>]... [--extension <id>]...\n"
        + "       goldbox play --campaign <path> --load <save> [--script <file>] [--save <save>] [--trace] [--fail-on-refusal] [--store <dir>] [--modules <dir>]... [--extension <id>]...\n"
        + "A save is a file, or with --store a save slot in that Engine persistence root (the Game's is .runtime/persistence under rusty dev).";

    private sealed record ScriptLine(string Text, int Number);

    public static int Run(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--campaign", "--modules", "--extension", "--party", "--seed", "--script", "--save", "--load", "--store", "--combat-control"], ["--trace", "--fail-on-refusal"]);
        bool loading = parsed.Single("--load") is not null;
        if (error is null && (parsed.Positionals.Count != 0 || parsed.Single("--campaign") is null || loading == (parsed.Single("--party") is not null)))
        {
            error = Usage;
        }

        if (error is null && loading && parsed.Single("--seed") is not null)
        {
            error = "--seed can't be used with --load: a save continues with its own seed.";
        }

        if (error is null && parsed.Has("--fail-on-refusal") && parsed.Single("--script") is null)
        {
            error = "--fail-on-refusal requires --script; it cannot be used with interactive input.";
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

        CombatControlMode control = CombatControlMode.Automatic;
        if (parsed.Single("--combat-control") is string controlText && !TryControl(controlText, out control))
        {
            return output.UsageError($"--combat-control must be auto or manual, but was '{controlText}'.");
        }

        ModuleSet set = ModuleSets.Load(
            Path.GetFullPath(parsed.Single("--campaign")!, workingDirectory),
            parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList(),
            ModuleSets.Extensions(parsed));
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
        List<ScriptLine> commands;
        string? scriptPath = parsed.Single("--script") is string script
            ? Path.GetFullPath(script, workingDirectory)
            : null;
        try
        {
            commands = ReadScript(scriptPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return output.UsageError($"--script: {exception.Message}");
        }

        List<PlayStep> transcript = [];
        List<ModuleDiagnostic> refusals = [];
        CampaignRunner runner = new(rules, state);
        runner.DefaultCombatControl = control;
        runner.CollectCombatBehaviorTraces = parsed.Has("--trace");
        bool invalidScript = false;
        try
        {
            host.Call(engine =>
            {
                if (!loading)
                {
                    transcript.Add(new PlayStep(null, runner.Begin(engine.Random)));
                }

                foreach (ScriptLine scriptLine in commands)
                {
                    string command = scriptLine.Text;
                    if (!CombatScript.IsCombatCommand(command))
                    {
                        List<PlayFact> facts = runner.Execute(command, engine.Random);
                        transcript.Add(new PlayStep(command, facts));
                        if (parsed.Has("--fail-on-refusal"))
                        {
                            AddRefusals(refusals, facts, scriptLine, scriptPath!, state, set.Root!.Id);
                        }

                        continue;
                    }

                    (CombatScriptCommand? combatCommand, string? scriptError) = CombatScript.Parse(command);
                    if (scriptError is not null)
                    {
                        invalidScript = true;
                        transcript.Add(new PlayStep(command, [], Error: scriptError));
                        continue;
                    }

                    CampaignCombatCommandResult result = ExecuteCombat(runner, combatCommand!, engine.Random);
                    bool trace = parsed.Has("--trace");
                    transcript.Add(new PlayStep(
                        command,
                        result.Facts,
                        result,
                        Trace: trace,
                        BehaviorTrace: trace ? result.Observation.BehaviorTrace : null));
                    if (parsed.Has("--fail-on-refusal") && !result.Accepted)
                    {
                        AddRefusal(
                            refusals,
                            result.Reason ?? "the combat command was refused.",
                            scriptLine,
                            scriptPath!,
                            state,
                            set.Root!.Id,
                            "Use `combat inspect` to see the active actor, legal actions, target IDs, and paths, then retry with a valid combat choice.");
                    }
                }
            });
        }
        catch (RuleFailure failure)
        {
            if (parsed.Has("--fail-on-refusal") && refusals.Count > 0)
            {
                refusals.Add(failure.Diagnostic);
                return output.PlayFailure(state, transcript, refusals);
            }

            output.PlayTranscript(state, transcript);
            return output.Problems([failure.Diagnostic]);
        }

        if (parsed.Single("--save") is string save && Save(host, store, save, SaveFile.ToJson(state, set), workingDirectory) is ModuleDiagnostic problem)
        {
            return output.Problems([problem]);
        }

        if (refusals.Count > 0)
        {
            return output.PlayFailure(state, transcript, refusals);
        }

        output.PlayTranscript(state, transcript);
        return invalidScript ? GoldboxCli.Invalid : GoldboxCli.Ok;
    }

    private static CampaignCombatCommandResult ExecuteCombat(CampaignRunner runner, CombatScriptCommand command, Rusty.Engine.IRandomService random)
    {
        if (command is CombatInspectCommand)
        {
            CombatObservation? observation = runner.ObserveCombat(random);
            return observation is null
                ? new CampaignCombatCommandResult(false, "No combat is waiting for a command.", CombatScript.EmptyObservation(), [])
                : new CampaignCombatCommandResult(true, null, observation, []);
        }

        if (command is CombatAutoStepCommand)
        {
            CombatObservation? observation = runner.ObserveCombat(random);
            if (observation is null)
            {
                return new CampaignCombatCommandResult(false, "No combat is waiting for a command.", CombatScript.EmptyObservation(), []);
            }

            if (observation.ActiveActorId is not string activeActor)
            {
                return new CampaignCombatCommandResult(false, "Combat is advancing automatically; there is no active actor to step.", observation, []);
            }

            CombatantObservation? activeCombatant = observation.Combatants.FirstOrDefault(combatant => combatant.Id == activeActor);
            if (activeCombatant?.Controller != CombatControlMode.Manual)
            {
                return new CampaignCombatCommandResult(false, $"Active actor '{activeActor}' is already automatic; use combat control {activeActor} manual before auto-step, or keep the persistent automatic controller.", observation, []);
            }

            return runner.StepCombatAutomatically(activeActor, random);
        }

        return command switch
        {
            CombatControlCommand control => runner.SetCombatController(control.ActorId, control.Mode, random),
            CombatUseActionCommand action => runner.SubmitCombat(new CombatCommand.UseAction(action.ActorId, action.ActionId, action.TargetIds, action.Path), random),
            CombatMoveCommand move => runner.SubmitCombat(new CombatCommand.UseAction(move.ActorId, move.ActionId, [move.TargetId], move.Path), random),
            CombatEndTurnCommand endTurn => runner.SubmitCombat(new CombatCommand.EndTurn(endTurn.ActorId), random),
            CombatDecisionCommand decision => runner.SubmitCombat(new CombatCommand.Decide(decision.DecisionId, decision.OptionId), random),
            _ => new CampaignCombatCommandResult(false, "The combat script command is not supported.", CombatScript.EmptyObservation(), []),
        };
    }

    private static bool TryControl(string text, out CombatControlMode mode)
    {
        mode = text.ToLowerInvariant() switch
        {
            "auto" or "automatic" => CombatControlMode.Automatic,
            "manual" => CombatControlMode.Manual,
            _ => (CombatControlMode)(-1),
        };
        return mode is CombatControlMode.Automatic or CombatControlMode.Manual;
    }

    private static void AddRefusals(
        List<ModuleDiagnostic> diagnostics,
        IReadOnlyList<PlayFact> facts,
        ScriptLine scriptLine,
        string scriptPath,
        CampaignState state,
        string campaignModule)
    {
        foreach (RefusedFact refused in facts.OfType<RefusedFact>())
        {
            AddRefusal(
                diagnostics,
                refused.Reason,
                scriptLine,
                scriptPath,
                state,
                campaignModule,
                $"Use status or look in the script and `goldbox map render {state.Area.QualifiedId} --module <campaign path>` to inspect the current state and map before retrying.");
        }
    }

    private static void AddRefusal(
        List<ModuleDiagnostic> diagnostics,
        string reason,
        ScriptLine scriptLine,
        string scriptPath,
        CampaignState state,
        string campaignModule,
        string guidance)
    {
        diagnostics.Add(new ModuleDiagnostic(
            "play.refusal",
            $"Script line {scriptLine.Number} command '{scriptLine.Text}' was refused: {reason} The party is in {state.Area.QualifiedId} at [{state.X}, {state.Y}], facing {Facings.Name(state.Facing)}. {guidance}",
            campaignModule,
            scriptPath,
            $"line {scriptLine.Number}"));
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

    private static List<ScriptLine> ReadScript(string? scriptPath)
    {
        IEnumerable<string> lines = scriptPath is null
            ? ReadAll(Console.In)
            : File.ReadAllLines(scriptPath);
        return lines
            .Select((line, index) => new ScriptLine(
                (line.Contains('#', StringComparison.Ordinal) ? line[..line.IndexOf('#', StringComparison.Ordinal)] : line).Trim(),
                index + 1))
            .Where(line => line.Text.Length > 0)
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
