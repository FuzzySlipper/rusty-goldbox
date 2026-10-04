using System.Globalization;
using RustyGoldbox.Core.Campaigns;
using RustyGoldbox.Core.Combat;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Cli;

/// <summary>One campaign transcript step, optionally carrying a live combat result.</summary>
internal sealed record PlayStep(
    string? Command,
    IReadOnlyList<PlayFact> Facts,
    CampaignCombatCommandResult? Combat = null,
    string? Error = null,
    bool Trace = false,
    CombatBehaviorTrace? BehaviorTrace = null);

/// <summary>
/// Parses the small, data-only command language used by <c>goldbox play</c>
/// for a suspended combat. The Core command types remain the only source of
/// combat rules; this parser only turns IDs and authored paths into those types.
/// </summary>
internal static class CombatScript
{
    public const string Usage = "combat inspect | combat control <actor-id> auto|manual | combat action <actor-id> <action-id> [target-id...] [--target <id>]... [--targets <id>,...] [--path <x,y;x,y>] | combat move <actor-id> <action-id> <target-id> <x,y;x,y> | combat end-turn <actor-id> | combat decide <decision-id> [option-id] | combat auto-step";

    public static bool IsCombatCommand(string text) => text.Equals("combat", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("combat ", StringComparison.OrdinalIgnoreCase);

    public static (CombatScriptCommand? Command, string? Error) Parse(string text)
    {
        (List<string>? tokenList, string? tokenError) = Tokens(text);
        if (tokenError is not null)
        {
            return (null, tokenError);
        }

        string[] words = tokenList!.ToArray();
        if (words.Length == 0 || !string.Equals(words[0], "combat", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        if (words.Length < 2)
        {
            return (null, Usage);
        }

        return words[1].ToLowerInvariant() switch
        {
            "inspect" => Exact(words, 2, new CombatInspectCommand()),
            "auto-step" => Exact(words, 2, new CombatAutoStepCommand()),
            "control" => ParseControl(words),
            "action" => ParseAction(words),
            "move" => ParseMove(words),
            "end-turn" => words.Length == 3
                ? (new CombatEndTurnCommand(words[2]), null)
                : (null, Usage),
            "decide" => ParseDecision(words),
            _ => (null, $"Unknown combat script command '{words[1]}'. Use: {Usage}"),
        };
    }

    public static CombatObservation EmptyObservation() => new(
        CombatPhase.NotStarted,
        0,
        null,
        null,
        null,
        null,
        [],
        []);

    private static (CombatScriptCommand? Command, string? Error) Exact(string[] words, int count, CombatScriptCommand command)
    {
        return words.Length == count ? (command, null) : (null, Usage);
    }

    private static (CombatScriptCommand? Command, string? Error) ParseControl(string[] words)
    {
        if (words.Length != 4)
        {
            return (null, Usage);
        }

        if (!TryMode(words[3], out CombatControlMode mode))
        {
            return (null, $"combat control mode must be auto or manual, but was '{words[3]}'.");
        }

        return (new CombatControlCommand(words[2], mode), null);
    }

    private static (CombatScriptCommand? Command, string? Error) ParseAction(string[] words)
    {
        if (words.Length < 4)
        {
            return (null, Usage);
        }

        List<string> targets = [];
        IReadOnlyList<Cell>? path = null;
        for (int index = 4; index < words.Length; index++)
        {
            string word = words[index];
            if (word.Equals("--target", StringComparison.Ordinal))
            {
                if (++index >= words.Length || words[index].Length == 0)
                {
                    return (null, "combat action --target needs an actor ID.");
                }

                targets.Add(words[index]);
            }
            else if (word.Equals("--targets", StringComparison.Ordinal))
            {
                if (++index >= words.Length)
                {
                    return (null, "combat action --targets needs a comma-separated ID list.");
                }

                targets.AddRange(words[index].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                if (targets.Count == 0)
                {
                    return (null, "combat action --targets needs at least one actor ID.");
                }
            }
            else if (word.Equals("--path", StringComparison.Ordinal))
            {
                if (++index >= words.Length)
                {
                    return (null, "combat action --path must be x,y;x,y.");
                }

                if (!TryPath(words[index], out path, out string? pathError))
                {
                    return (null, pathError ?? "combat action --path must be x,y;x,y.");
                }
            }
            else if (word.StartsWith("--", StringComparison.Ordinal))
            {
                return (null, $"Unknown combat action option '{word}'. Use: {Usage}");
            }
            else
            {
                targets.Add(word);
            }
        }

        return (new CombatUseActionCommand(words[2], words[3], targets, path), null);
    }

    private static (CombatScriptCommand? Command, string? Error) ParseMove(string[] words)
    {
        if (words.Length < 6)
        {
            return (null, Usage);
        }

        string pathText = words[5];
        if (pathText.Equals("--path", StringComparison.Ordinal))
        {
            if (words.Length != 7)
            {
                return (null, "combat move --path needs x,y;x,y.");
            }

            pathText = words[6];
        }
        else if (words.Length != 6)
        {
            return (null, Usage);
        }

        if (!TryPath(pathText, out IReadOnlyList<Cell>? path, out string? pathError))
        {
            return (null, pathError ?? "combat move path must be x,y;x,y.");
        }

        return (new CombatMoveCommand(words[2], words[3], words[4], path!), null);
    }

    private static (CombatScriptCommand? Command, string? Error) ParseDecision(string[] words)
    {
        if (words.Length is < 3 or > 4)
        {
            return (null, Usage);
        }

        string? option = words.Length == 4 && !words[3].Equals("none", StringComparison.OrdinalIgnoreCase)
            && !words[3].Equals("decline", StringComparison.OrdinalIgnoreCase)
            ? words[3]
            : null;
        return (new CombatDecisionCommand(words[2], option), null);
    }

    private static bool TryMode(string value, out CombatControlMode mode)
    {
        mode = value.ToLowerInvariant() switch
        {
            "auto" or "automatic" => CombatControlMode.Automatic,
            "manual" => CombatControlMode.Manual,
            _ => (CombatControlMode)(-1),
        };
        return mode is CombatControlMode.Automatic or CombatControlMode.Manual;
    }

    private static (List<string>? Tokens, string? Error) Tokens(string text)
    {
        List<string> words = [];
        System.Text.StringBuilder word = new();
        char quote = '\0';
        bool escaped = false;
        foreach (char character in text)
        {
            if (escaped)
            {
                word.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\' && quote != '\'')
            {
                escaped = true;
                continue;
            }

            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    word.Append(character);
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }
            }
            else
            {
                word.Append(character);
            }
        }

        if (escaped)
        {
            word.Append('\\');
        }

        if (quote != '\0')
        {
            return (null, "combat script has an unterminated quote.");
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return (words, null);
    }

    private static bool TryPath(string text, out IReadOnlyList<Cell>? path, out string? error)
    {
        List<Cell> cells = [];
        foreach (string point in text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] coordinates = point.Split(',', StringSplitOptions.TrimEntries);
            if (coordinates.Length != 2
                || !int.TryParse(coordinates[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(coordinates[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                path = null;
                error = $"combat path point '{point}' must be x,y with whole-number coordinates.";
                return false;
            }

            cells.Add(new Cell(x, y));
        }

        if (cells.Count == 0)
        {
            path = null;
            error = "combat path needs at least one x,y point.";
            return false;
        }

        path = cells;
        error = null;
        return true;
    }
}

internal abstract record CombatScriptCommand;
internal sealed record CombatInspectCommand : CombatScriptCommand;
internal sealed record CombatAutoStepCommand : CombatScriptCommand;
internal sealed record CombatControlCommand(string ActorId, CombatControlMode Mode) : CombatScriptCommand;
internal sealed record CombatUseActionCommand(string ActorId, string ActionId, IReadOnlyList<string> TargetIds, IReadOnlyList<Cell>? Path) : CombatScriptCommand;
internal sealed record CombatMoveCommand(string ActorId, string ActionId, string TargetId, IReadOnlyList<Cell> Path) : CombatScriptCommand;
internal sealed record CombatEndTurnCommand(string ActorId) : CombatScriptCommand;
internal sealed record CombatDecisionCommand(string DecisionId, string? OptionId) : CombatScriptCommand;
