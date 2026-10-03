using System.Globalization;
using RustyGoldbox.Core.Characters;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Cli;

/// <summary><c>goldbox character new | level | show</c>.</summary>
internal static class CharacterCommand
{
    private const string RandomScope = "goldbox.character";

    private const string Usage =
        "Usage: goldbox character new --module <path> --class <id> --race <id> [--name <name>] [--attributes <id>=<n>,...] [--priority <id>,...] [--creation <id>] [--feature <id>,...] [--boosts <id>,...] [--spells <id>,...] [--portrait <asset>] [--seed <n>] [--out <file>]\n"
        + "       goldbox character level <file> --module <path> --xp <n> [--class <id>] [--feature <id>,...] [--boosts <id>,...] [--seed <n>]\n"
        + "       goldbox character spells <file> --module <path> [--set <id>,...] [--memorise <id>,...]\n"
        + "       goldbox character show <file> --module <path>";

    public static int Run(IReadOnlyList<string> args, Output output, string workingDirectory)
    {
        if (args.Count == 0)
        {
            return output.UsageError(Usage);
        }

        return args[0] switch
        {
            "new" => New(args.Skip(1), output, workingDirectory),
            "level" => Level(args.Skip(1), output, workingDirectory),
            "show" => Show(args.Skip(1), output, workingDirectory),
            "spells" => Spells(args.Skip(1), output, workingDirectory),
            _ => output.UsageError($"Unknown command 'character {args[0]}'. Character commands are new, level, spells and show.\n{Usage}"),
        };
    }

    private static int New(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--module", "--modules", "--class", "--race", "--name", "--attributes", "--priority", "--creation", "--feature", "--boosts", "--spells", "--portrait", "--seed", "--out"], []);
        if (error is null && (parsed.Positionals.Count != 0 || parsed.Single("--module") is null || parsed.Single("--class") is null || parsed.Single("--race") is null))
        {
            error = Usage;
        }

        ulong seed = 1;
        Dictionary<string, decimal>? attributes = null;
        error ??= ParseSeed(parsed, ref seed) ?? ParseAttributes(parsed.Single("--attributes"), out attributes);
        if (error is not null)
        {
            return output.UsageError(error);
        }

        ModuleSet set = Load(parsed, workingDirectory);
        if (set.Rules is null || !set.IsValid)
        {
            return output.ModuleErrors(set);
        }

        IReadOnlyList<string>? priority = parsed.Single("--priority")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string[] classes = parsed.Single("--class")!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        CreationRequest request = new(
            parsed.Single("--name") ?? "Unnamed",
            classes.FirstOrDefault() ?? "",
            parsed.Single("--race")!,
            attributes,
            priority,
            parsed.Single("--creation"),
            Features(parsed),
            parsed.Single("--boosts")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            classes.Skip(1).ToList());
        List<ModuleDiagnostic> problems = [];
        (Character? character, IReadOnlyList<DiceRoll> rolls) = EngineDice.Run(seed, RandomScope, dice =>
            CharacterRules.Create(set.Rules, Character.StampsOf(set), request, dice, problems));
        if (character is null
            || (parsed.Single("--portrait") is string portrait && !CharacterRules.SetPortrait(set.Rules, character, portrait, problems))
            || (parsed.Single("--spells") is string spells && !CharacterRules.SetSpells(set.Rules, character, List(spells), problems)))
        {
            return output.Problems(problems);
        }

        string? outPath = parsed.Single("--out") is string target ? Path.GetFullPath(target, workingDirectory) : null;
        if (outPath is not null && Save(outPath, character, output) is int failed)
        {
            return failed;
        }

        output.CharacterSheet(set.Rules, character, outPath, seed, rolls, []);
        return GoldboxCli.Ok;
    }

    private static int Level(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--module", "--modules", "--xp", "--class", "--feature", "--boosts", "--seed"], []);
        if (error is null && (parsed.Positionals.Count != 1 || parsed.Single("--module") is null || parsed.Single("--xp") is null))
        {
            error = Usage;
        }

        ulong seed = 1;
        decimal experience = 0;
        error ??= ParseSeed(parsed, ref seed);
        if (error is null && (!decimal.TryParse(parsed.Single("--xp"), NumberStyles.None, CultureInfo.InvariantCulture, out experience)))
        {
            error = $"--xp must be a whole number of experience points 0 or more, but was '{parsed.Single("--xp")}'.";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        (ModuleSet set, Character? character, string path, int? failure) = LoadCharacter(parsed, output, workingDirectory);
        if (failure is int code)
        {
            return code;
        }

        List<ModuleDiagnostic> problems = [];
        (List<LevelGain>? gains, IReadOnlyList<DiceRoll> rolls) = EngineDice.Run(seed, RandomScope, dice =>
            CharacterRules.AddExperience(set.Rules!, character!, experience, dice, problems, parsed.Single("--class"), Features(parsed), parsed.Single("--boosts")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        if (gains is null)
        {
            // Problems from ruleset expressions name their definition; the rest are about this character.
            return output.Problems(problems.Select(problem => problem.File is null ? problem with { File = path } : problem).ToList());
        }

        if (Save(path, character!, output) is int failed)
        {
            return failed;
        }

        output.CharacterSheet(set.Rules!, character!, path, seed, rolls, gains);
        return GoldboxCli.Ok;
    }

    /// <summary>--feature ids, comma-separated, in the order the grants take them.</summary>
    private static IReadOnlyList<string> Features(Arguments parsed)
    {
        return parsed.Single("--feature")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
    }

    private static string[] List(string text) => text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary><c>character spells</c>: sets the spells a character knows, checked against its classes and tracks.</summary>
    private static int Spells(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--module", "--modules", "--set", "--memorise"], []);
        if (error is null && (parsed.Positionals.Count != 1 || parsed.Single("--module") is null || (parsed.Single("--set") is null && parsed.Single("--memorise") is null)))
        {
            error = Usage;
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        (ModuleSet set, Character? character, string path, int? failure) = LoadCharacter(parsed, output, workingDirectory);
        if (failure is int code)
        {
            return code;
        }

        List<ModuleDiagnostic> problems = [];
        if ((parsed.Single("--set") is string known && !CharacterRules.SetSpells(set.Rules!, character!, List(known), problems))
            || (parsed.Single("--memorise") is string memorised && !CharacterRules.SetMemorised(set.Rules!, character!, List(memorised), problems)))
        {
            return output.Problems(problems);
        }

        if (Save(path, character!, output) is int failed)
        {
            return failed;
        }

        output.CharacterSheet(set.Rules!, character!, path, null, [], []);
        return GoldboxCli.Ok;
    }

    private static int Show(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--module", "--modules"], []);
        if (error is null && (parsed.Positionals.Count != 1 || parsed.Single("--module") is null))
        {
            error = Usage;
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        (ModuleSet set, Character? character, string path, int? failure) = LoadCharacter(parsed, output, workingDirectory);
        if (failure is int code)
        {
            return code;
        }

        output.CharacterSheet(set.Rules!, character!, null, null, [], []);
        return GoldboxCli.Ok;
    }

    private static (ModuleSet Set, Character? Character, string Path, int? Failure) LoadCharacter(Arguments parsed, Output output, string workingDirectory)
    {
        string path = Path.GetFullPath(parsed.Positionals[0], workingDirectory);
        ModuleSet set = Load(parsed, workingDirectory);
        if (set.Rules is null || !set.IsValid)
        {
            return (set, null, path, output.ModuleErrors(set));
        }

        List<ModuleDiagnostic> problems = [];
        Character? character = CharacterFile.Read(path, set, problems);
        return character is null ? (set, null, path, output.Problems(problems)) : (set, character, path, null);
    }

    private static ModuleSet Load(Arguments parsed, string workingDirectory)
    {
        return ModuleSets.Load(
            Path.GetFullPath(parsed.Single("--module")!, workingDirectory),
            parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList());
    }

    private static int? Save(string path, Character character, Output output)
    {
        try
        {
            File.WriteAllText(path, CharacterFile.ToJson(character));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return output.Problems([new ModuleDiagnostic("character.write", $"Can't write the character: {exception.Message}", File: path)]);
        }
    }

    private static string? ParseSeed(Arguments parsed, ref ulong seed)
    {
        if (parsed.Single("--seed") is string text && !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
        {
            return $"--seed must be a whole number from 0 to {ulong.MaxValue}, but was '{text}'.";
        }

        return null;
    }

    private static string? ParseAttributes(string? text, out Dictionary<string, decimal>? attributes)
    {
        attributes = null;
        if (text is null)
        {
            return null;
        }

        attributes = [];
        foreach (string pair in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !decimal.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal score))
            {
                return $"--attributes must be <id>=<number> pairs separated by commas, like str=16,dex=12; '{pair}' isn't.";
            }

            if (!attributes.TryAdd(parts[0], score))
            {
                return $"--attributes gives {parts[0]} more than once.";
            }
        }

        return null;
    }
}
