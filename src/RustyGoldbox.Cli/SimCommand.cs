using System.Globalization;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Cli;

/// <summary><c>goldbox sim combat</c>: headless fights between a party and an encounter.</summary>
internal static class SimCommand
{
    private const string Usage =
        "Usage: goldbox sim combat --module <path> --party <file>[,<file>...] --encounter <id> [--combat <id>] [--seed <n>] [--runs <k>] [--max-rounds <n>] [--modules <dir>]... [--extension <id>]...";

    public static int Run(IReadOnlyList<string> args, Output output, string workingDirectory)
    {
        if (args.Count == 0 || args[0] != "combat")
        {
            return output.UsageError(Usage);
        }

        (Arguments parsed, string? error) = Arguments.Parse(args.Skip(1), ["--module", "--modules", "--extension", "--party", "--encounter", "--combat", "--seed", "--runs", "--max-rounds"], []);
        if (error is null && (parsed.Positionals.Count != 0 || parsed.Single("--module") is null || parsed.Single("--party") is null || parsed.Single("--encounter") is null))
        {
            error = Usage;
        }

        ulong seed = 1;
        int runs = 1;
        int? maxRounds = null;
        error ??= Whole(parsed.Single("--seed"), "--seed", value => seed = value, 0)
            ?? Whole(parsed.Single("--runs"), "--runs", value => runs = (int)value, 1)
            ?? Whole(parsed.Single("--max-rounds"), "--max-rounds", value => maxRounds = (int)value, 1);
        // Without --max-rounds, the combat definition's round_limit (or the default) applies; set below.
        if (error is not null)
        {
            return output.UsageError(error);
        }

        ModuleSet set = ModuleSets.Load(
            Path.GetFullPath(parsed.Single("--module")!, workingDirectory),
            parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList(),
            ModuleSets.Extensions(parsed));
        if (set.Rules is null || !set.IsValid)
        {
            return output.ModuleErrors(set);
        }

        RuleSet rules = set.Rules;
        List<ModuleDiagnostic> problems = [];
        List<Character> party = [];
        foreach (string file in parsed.Single("--party")!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (CharacterFile.Read(Path.GetFullPath(file, workingDirectory), set, problems) is Character character)
            {
                party.Add(character);
            }
        }

        if (party.Count == 0 && problems.Count == 0)
        {
            return output.UsageError("--party needs at least one character file.");
        }

        Definition? encounter = Find(rules, DefinitionTypes.Encounter, parsed.Single("--encounter")!, "--encounter", problems);
        Definition? combat = parsed.Single("--combat") is string combatId
            ? Find(rules, DefinitionTypes.Combat, combatId, "--combat", problems)
            : OnlyCombat(rules, problems);
        if (problems.Count > 0 || encounter is null || combat is null)
        {
            return output.Problems(problems);
        }

        int roundLimit = maxRounds ?? CombatRunner.RoundLimit(combat);

        try
        {
            using EngineTestHost host = EngineTestHost.Create();
            List<CombatResult> results = host.Call(engine =>
            {
                List<CombatResult> all = [];
                for (int run = 1; run <= runs; run++)
                {
                    using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(seed, $"goldbox.sim.{run}"));
                    DiceRoller dice = new(engine.Random, stream);
                    List<CombatSide> sides = Encounters.Distinct(
                    [
                        new("Party", party.Select(character => Combatant.FromCharacter(rules, character)).ToList()),
                        new(encounter.Name, Encounters.Spawn(rules, encounter, dice)),
                    ]);
                    all.Add(CombatRunner.Run(rules, combat, sides, dice, roundLimit, encounter));
                }

                return all;
            });

            if (runs == 1)
            {
                output.CombatTranscript(rules, results[0], seed);
            }
            else
            {
                output.CombatSummary(results, seed);
            }

            return GoldboxCli.Ok;
        }
        catch (RuleFailure failure)
        {
            return output.Problems([failure.Diagnostic]);
        }
    }

    private static Definition? OnlyCombat(RuleSet rules, List<ModuleDiagnostic> problems)
    {
        List<Definition> all = rules.OfType(DefinitionTypes.Combat).ToList();
        if (all.Count == 1)
        {
            return all[0];
        }

        problems.Add(new ModuleDiagnostic("combat.definition", all.Count == 0
            ? "The module set has no combat definition (see `goldbox schema combat`)."
            : $"The module set has more than one combat definition ({string.Join(", ", all.Select(definition => definition.QualifiedId))}); choose one with --combat."));
        return null;
    }

    private static Definition? Find(RuleSet rules, DefinitionType type, string reference, string option, List<ModuleDiagnostic> problems)
    {
        Definition? found = rules.Find(type, reference, out string? problem);
        if (found is null)
        {
            problems.Add(new ModuleDiagnostic("combat.reference", $"{option}: {problem}"));
        }

        return found;
    }

    private static string? Whole(string? text, string option, Action<ulong> set, ulong minimum)
    {
        if (text is null)
        {
            return null;
        }

        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) || value < minimum || (option != "--seed" && value > int.MaxValue))
        {
            return $"{option} must be a whole number of at least {minimum}, but was '{text}'.";
        }

        set(value);
        return null;
    }
}
