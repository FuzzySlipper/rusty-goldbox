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
          goldbox module inspect <path> [<type> | <id> | <module>:<id>] [--trace] [--modules <dir>]...
              Lists the resolved definitions and stats, or shows the selected ones. With --trace,
              combat-behavior selections include authored guards, scores, plan steps and source paths.
          goldbox module pack <module-dir> [--output <file>.rpak | --install] [--modules <dir>]...
              Validates the module, then packs it into an Engine content container
              (<id>-<version>.rpak here, or in the Game's module library with --install:
              $GOLDBOX_MODULE_LIBRARY, else $XDG_DATA_HOME/rusty-goldbox/modules,
              else ~/.local/share/rusty-goldbox/modules).
          goldbox schema [<type> | workspace | module | expressions | operations | events | media | live-combat]
              The format reference: definition types with fields and examples.
          goldbox workspace new <dir>
              Creates goldbox.json plus editable canon/art/prompts/scripts and generated staging/export directories.
          goldbox workspace inspect [<path>]
              Finds the nearest goldbox.json and lists dependency search paths, authored modules, editable roots and outputs.
          goldbox workspace build [<path>]
              Validates each authored runtime module and prepares a clean staging tree; canon, prompts and source art stay outside it.
          goldbox workspace export [<path>]
              Builds the workspace, then independently packs each staged module into the configured exports directory.
          goldbox authoring list [--json]
          goldbox authoring show <resource> [--json]
          goldbox authoring copy <resource> --out <dir> [--overwrite] [--json]
          goldbox authoring copy --all --out <dir> [--overwrite] [--json]
              Discovers or copies the embedded revision-1.0 campaign-authoring kit;
              copied files include the kit index, brief, canon, chapter, encounter,
              art, individual/batch image judges, handoff, workflow, revision, and
              the complete Lantern worked example. Use `goldbox schema live-combat
              --json` for the suspended combat command and observation schema;
              live-combat is a schema topic, not an authoring resource.
          goldbox eval <expression> --module <path> [--context <json> | @<file>] [--seed <n>]
          goldbox eval --check <check-id> --module <path> --context <json> [--seed <n>]
              Evaluates against the module set. Context: {"self": creature, "target": creature};
              a creature is {"monster": id} or {"class": id, "race": id, "level": n, "<stat>": n,
              "conditions": [ids], "equipment": [ids]}, or "@<character file>" for a saved character,
              for example {"self": "@brom.json", "target": {"monster": "skeleton"}}. Dice use Engine
              Random; the seed defaults to 1.

          goldbox character new --module <path> [--class <id>] [--race <id>] [--name <name>]
                [--attributes <id>=<n>,...] [--priority <id>,...] [--creation <id>] [--feature <id>,...]
                [--boosts <id>,...] [--spells <id>,...] [--equipment <id>,...] [--portrait <asset>] [--lifepath <id>]
                [--career <id>,...] [--terms <n>] [--skill-table <id>,...] [--benefit cash|material,...]
                [--seed <n>] [--out <file>]
              --class and --race are needed where the ruleset has classes and races.
              Makes attributes by the creation's method (roll, array, point buy or boosts), applies the race, checks requirements, rolls the
              first level's gain for the level track (usually hit points), starts every
              track, and rolls any declared starting balances. --priority arranges rolls where the ruleset allows.
              --attributes skips the ruleset's roll entirely (for given or point-bought scores);
              scores must be within each attribute's range. --portrait gives the character a
              portrait asset from the module set. --equipment gives the character the listed
              item IDs after creation, checking each against the class and race equipment rules;
              repeat the option or comma-separate IDs. --feature fills the choices creation and the
              first level grant (a background, a feat), matched to them by kind in order.
              Where creation makes scores by boosts, --boosts names the attribute for each
              boost that offers a choice: race, creation features, class, then creation.
              Where it uses point buy, --attributes are the bought scores.
              Where experience is split between classes, --class fighter,thief starts with
              both, as the race's multiclasses allow.
              With --lifepath, --career chooses one career per term (or repeat one with --terms);
              --skill-table supplies the visible table choice for each actual skill roll (one
              choice repeats when the policy asks for more), and --benefit chooses cash or
              material for each mustering-out roll (one choice repeats too). The saved character
              records every term roll, choice and result in career_terms.
          goldbox character skills <file> --module <path> --skill <id>=profession:<n>+personal:<n>,... [--seed <n>]
              Commits a staged character's skill points, profession and personal per skill,
              and saves the character. It also takes the lifepath options above.
          goldbox character level <file> --module <path> --xp <n> [--trained] [--class <id>] [--feature <id>,...] [--boosts <id>,...] [--seed <n>]
              Adds experience, gains every level reached and saves the file. Where the
              advancement counts experience by character level, levels go to --class (a
              new class must accept the character), else to the class of the latest level.
              Where experience is split, a new --class is a class change (dual-classing).
              --feature fills the choices the new levels grant, --boosts the boosts they grant.
          goldbox character milestone <file> --module <path> [--raise <id>,...] [--swap <from=to>,...] [--feature <id>,...]
              Applies a ruleset milestone's skill and feature choices and saves the character.
          goldbox character mark <file> --module <path> --skill <id>
              Records a successful use for a ruleset's improvement advancement.
          goldbox character improve <file> --module <path> [--seed <n>]
              Rolls the improvement check for every marked skill and saves the character.
          goldbox character spells <file> --module <path> [--set <id>,...] [--memorise <id>,...]
              --set sets the spells the character knows: each on one of its classes' lists and
              payable from its tracks. Combat casts them first while it can pay. For classes
              that prepare spells, --memorise lists the copies it readies each day (repeat an ID
              for two), all payable together; without it, its known spells in order fill its slots.
          goldbox character former <file> --module <path> on|off
              A dual-classed character whose old class waits for the new one to pass it calls
              on the old class's abilities anyway (on), or stops (off). While it does, the old
              class works in fights, and in play it earns no experience for the rest of the
              adventure; stopping doesn't give that back.
          goldbox character npc <file> --module <path> --id <id> --out <npc.json>
              Writes an NPC definition with the character as its fixed data, for a
              campaign's join and dismiss events.
          goldbox character show <file> --module <path>
              Prints the derived sheet.

          goldbox sim combat --module <path> --party <file>,... --encounter <id> [--seed <n>]
                [--runs <k>] [--max-rounds <n>] [--combat <id>] [--trace]   (rounds default to the combat's round_limit)
              Fights the party against the encounter with the ruleset's combat loop. One run
              prints the transcript; more print outcomes and distributions. --trace adds a
              side-effect-free resolved-action projection. Run k uses the random scope goldbox.sim.<k>,
              so each run repeats for a seed.

          goldbox map render <area> --module <path> [--player]
              Draws an area: edge walls and doors, entries, event triggers (--player hides secret doors).
          goldbox play --campaign <path> --party <file>,... [--seed <n>] [--script <file>] [--save <file>] [--combat-control auto|manual] [--trace] [--fail-on-refusal]
          goldbox play --campaign <path> --load <save> [--script <file>] [--save <file>] [--trace] [--fail-on-refusal] [--modules <dir>]... [--extension <id>]...
              Plays a campaign from a command script (or stdin), one command per line; # starts
              a comment. Commands: forward, back, left, right, around, search [direction], open [direction], pick [direction], force [direction], choose <n>, look, view <member>, status.
              Commands include equip <member> <item-id>, unequip <member> <item-id>, and use <member> <item-id>
              (one-based party members; item IDs can be local or module:id). Gear changes transfer one existing carried
              copy; use consumes one existing carried item with a declared use on the selected member. These commands are
              refused during live combat or while a pending interaction owns the command.
              Add --combat-control manual to suspend the party at a fight. Combat commands include
              combat inspect, combat control <actor-id> auto|manual, combat action <actor-id> <action-id>
              [target-id...] [--target <id>]... [--targets <id>,...] [--path <x,y;x,y>] (quote IDs containing spaces), combat move, combat end-turn, combat decide and
              combat auto-step (one automatic turn, then manual control); `goldbox schema live-combat`
              shows the complete grammar and JSON shape. Use combat control ... auto for a persistent takeover.
              --save writes the state at the end, including a pending fight; --load continues a save exactly.
              --fail-on-refusal turns scripted command refusals into diagnostics and exit code 1.

        Every command accepts --json for structured output.

        Kinds: ruleset, extension, assets, campaign.
        Ranges: 1.2.3, ^1.2.0, ~1.2.0, ">=1.0.0 <2.0.0", *.
        Required modules are found in --modules directories, the "modules" list
        of the nearest goldbox.json, or (with neither) the module's siblings: module
        directories, and installed .rpak containers directly in those directories.
        A <path> may be an installed .rpak as well as a module directory.
        Commands that load a module set (module validate, deps and inspect, eval,
        character, sim and play) take --extension <id>,...: extension modules to add
        to the set though nothing in it requires them, such as your own class book
        for a ruleset, found where required modules are. Character files and saves
        record them, and refuse a set without them.

        Exit codes: 0 ok, 1 the module has errors or evaluation failed, 2 bad arguments.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output, string workingDirectory)
    {
        Output printer = new(output, workingDirectory, args.Contains("--json"));
        if (args.Count == 0 || args[0] is "help" or "--help" or "-h")
        {
            output.Write(Help);
            return args.Count == 0 ? Usage : Ok;
        }

        switch (args[0])
        {
            case "schema":
                return SchemaCommand.Run(args.Skip(1), printer);
            case "workspace":
                return WorkspaceCommand.Run(args.Skip(1), printer, workingDirectory);
            case "authoring":
                return AuthoringCommand.Run(args.Skip(1), printer, workingDirectory);
            case "eval":
                return EvalCommand.Run(args.Skip(1), printer, workingDirectory);
            case "character":
                return CharacterCommand.Run(args.Skip(1).ToList(), printer, workingDirectory);
            case "sim":
                return SimCommand.Run(args.Skip(1).ToList(), printer, workingDirectory);
            case "map":
                return MapCommand.Run(args.Skip(1).ToList(), printer, workingDirectory);
            case "play":
                return PlayCommand.Run(args.Skip(1), printer, workingDirectory);
            case "module" when args.Count >= 2:
                break;
            default:
                string message = $"Unknown command '{string.Join(' ', args.Take(2))}'. Run `goldbox --help` for the commands.";
                if (args.Count >= 2 && args[0] == "author" && args[1] == "build")
                {
                    message += " To build an authored workspace, use `goldbox workspace build <path>`.";
                }

                return printer.UsageError(message);
        }

        IEnumerable<string> rest = args.Skip(2);
        return args[1] switch
        {
            "new" => ModuleNew(rest, printer, workingDirectory),
            "validate" => ModuleValidate(rest, printer, workingDirectory),
            "deps" => ModuleDeps(rest, printer, workingDirectory),
            "inspect" => InspectCommand.Run(rest, printer, workingDirectory),
            "pack" => PackCommand.Run(rest, printer, workingDirectory),
            "--help" or "-h" => Print(output, Help),
            _ => printer.UsageError($"Unknown command 'module {args[1]}'. Module commands are new, validate, deps, inspect and pack."),
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
                return printer.UsageError($"--require '{require}' must be <id>@<range>, for example classic@^0.1.0. Ranges: {VersionRange.FormatDescription}.");
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
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--modules", "--extension"], []);
        if (error is null && parsed.Positionals.Count != 1)
        {
            error = $"Usage: goldbox module {command} <path> [--modules <dir>]... [--extension <id>]...";
        }

        if (error is not null)
        {
            exitCode = printer.UsageError(error);
            return null;
        }

        string path = Path.GetFullPath(parsed.Positionals[0], workingDirectory);
        List<string> searchDirectories = parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList();
        return ModuleSets.Load(path, searchDirectories, ModuleSets.Extensions(parsed));
    }

    private static int Print(TextWriter output, string text)
    {
        output.Write(text);
        return Ok;
    }
}
