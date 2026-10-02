using RustyGoldbox.Core.Modules;

namespace RustyGoldbox.Cli;

/// <summary>Parses <c>goldbox</c> arguments, calls Core and prints the result.</summary>
internal static class GoldboxCli
{
    public const int Ok = 0;
    public const int Invalid = 1;
    public const int Usage = 2;

    private const string Help = """
        goldbox: author and check Rusty Goldbox modules.

        Usage:
          goldbox module new <kind> <id> [--dir <parent>] [--title <title>]
                                         [--provenance <text>] [--require <id>@<range>]...
              Creates <parent>/<id>/module.json. <parent> defaults to the first
              "modules" directory of the nearest goldbox.json, else ".".
          goldbox module validate <path> [--modules <dir>]...
          goldbox module deps <path> [--modules <dir>]...

        Every command accepts --json for structured output.

        Kinds: ruleset, extension, assets, campaign.
        Ranges: 1.2.3, ^1.2.0, ~1.2.0, ">=1.0.0 <2.0.0", *.
        Required modules are found in --modules directories, the "modules" list
        of the nearest goldbox.json, or (with neither) the module's siblings.

        Exit codes: 0 ok, 1 the module has errors, 2 bad arguments.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output, string workingDirectory)
    {
        Output printer = new(output, workingDirectory, args.Contains("--json"));
        if (args.Count == 0 || args[0] is "help" or "--help" or "-h")
        {
            output.Write(Help);
            return args.Count == 0 ? Usage : Ok;
        }

        if (args[0] != "module" || args.Count < 2)
        {
            return printer.UsageError($"Unknown command '{string.Join(' ', args.Take(2))}'. Run `goldbox --help` for the commands.");
        }

        IEnumerable<string> rest = args.Skip(2);
        return args[1] switch
        {
            "new" => ModuleNew(rest, printer, workingDirectory),
            "validate" => ModuleValidate(rest, printer, workingDirectory),
            "deps" => ModuleDeps(rest, printer, workingDirectory),
            "--help" or "-h" => Print(output, Help),
            _ => printer.UsageError($"Unknown command 'module {args[1]}'. Module commands are new, validate and deps."),
        };
    }

    private static int ModuleNew(IEnumerable<string> args, Output printer, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--dir", "--title", "--provenance", "--require"], []);
        if (error is not null)
        {
            return printer.UsageError(error);
        }

        if (parsed.Positionals.Count != 2)
        {
            return printer.UsageError("Usage: goldbox module new <kind> <id> [--dir <parent>] [--title <title>] [--provenance <text>] [--require <id>@<range>]...");
        }

        string kindText = parsed.Positionals[0];
        string id = parsed.Positionals[1];
        if (!ModuleKinds.TryParse(kindText, out ModuleKind kind))
        {
            return printer.UsageError($"'{kindText}' is not a module kind. Use one of {string.Join(", ", ModuleKinds.Names)}.");
        }

        if (!ModuleIds.IsValid(id))
        {
            return printer.UsageError($"'{id}' is not a valid module ID. Use {ModuleIds.FormatDescription}.");
        }

        List<(string Id, VersionRange Range)> requires = [];
        foreach (string require in parsed.All("--require"))
        {
            int at = require.IndexOf('@', StringComparison.Ordinal);
            string requiredId = at < 0 ? require : require[..at];
            string rangeText = at < 0 ? "" : require[(at + 1)..];
            if (!ModuleIds.IsValid(requiredId) || !VersionRange.TryParse(rangeText, out VersionRange? range))
            {
                return printer.UsageError($"--require '{require}' must be <id>@<range>, for example osric@^0.1.0. Ranges: {VersionRange.FormatDescription}.");
            }

            requires.Add((requiredId, range!));
        }

        List<ModuleDiagnostic> diagnostics = [];
        string? dir = parsed.Single("--dir");
        string parent = dir is null
            ? ModuleSearchPaths.DefaultNewModuleParent(workingDirectory, diagnostics)
            : Path.GetFullPath(dir, workingDirectory);
        if (diagnostics.Count > 0)
        {
            return printer.Created(null, id, kind, diagnostics);
        }

        string title = parsed.Single("--title") ?? id;
        string provenance = parsed.Single("--provenance") ?? "Original content.";
        if (title.Trim().Length == 0 || provenance.Trim().Length == 0)
        {
            return printer.UsageError("--title and --provenance must not be empty.");
        }

        string? directory = ModuleScaffold.Create(parent, kind, id, title, provenance, requires, diagnostics);
        return printer.Created(directory, id, kind, diagnostics);
    }

    private static int ModuleValidate(IEnumerable<string> args, Output printer, string workingDirectory)
    {
        ModuleSet? set = LoadModule(args, "validate", printer, workingDirectory, out int exitCode);
        return set is null ? exitCode : printer.Validated(set);
    }

    private static int ModuleDeps(IEnumerable<string> args, Output printer, string workingDirectory)
    {
        ModuleSet? set = LoadModule(args, "deps", printer, workingDirectory, out int exitCode);
        return set is null ? exitCode : printer.Dependencies(set);
    }

    private static ModuleSet? LoadModule(IEnumerable<string> args, string command, Output printer, string workingDirectory, out int exitCode)
    {
        exitCode = Ok;
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--modules"], []);
        if (error is null && parsed.Positionals.Count != 1)
        {
            error = $"Usage: goldbox module {command} <path> [--modules <dir>]...";
        }

        if (error is not null)
        {
            exitCode = printer.UsageError(error);
            return null;
        }

        string path = Path.GetFullPath(parsed.Positionals[0], workingDirectory);
        List<string> searchDirectories = parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList();
        return ModuleLoader.Load(path, searchDirectories);
    }

    private static int Print(TextWriter output, string text)
    {
        output.Write(text);
        return Ok;
    }
}
