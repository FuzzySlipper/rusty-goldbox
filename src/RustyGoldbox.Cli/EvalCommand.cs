using System.Globalization;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Testing;
using RustyGoldbox.Core.Definitions;
using RustyGoldbox.Core.Expressions;
using RustyGoldbox.Core.Modules;
using RustyGoldbox.Core.Rules;

namespace RustyGoldbox.Cli;

/// <summary>
/// <c>goldbox eval</c>: evaluates an expression or a check against a loaded
/// module set, rolling dice through Engine Random with an explicit seed.
/// </summary>
internal static class EvalCommand
{
    public const string Usage =
        "Usage: goldbox eval <expression> --module <path> [--context <json> | --context @<file>] [--seed <n>] [--modules <dir>]...\n"
        + "       goldbox eval --check <check-id> --module <path> [--context ...] [--seed <n>]";

    private const string RandomScope = "goldbox.eval";

    public static int Run(IEnumerable<string> args, Output output, string workingDirectory)
    {
        (Arguments parsed, string? error) = Arguments.Parse(args, ["--module", "--modules", "--context", "--seed", "--check"], []);
        string? checkId = parsed.Single("--check");
        string? modulePath = parsed.Single("--module");
        int expected = checkId is null ? 1 : 0;
        if (error is null && (modulePath is null || parsed.Positionals.Count != expected))
        {
            error = Usage;
        }

        ulong seed = 1;
        if (error is null && parsed.Single("--seed") is string seedText
            && !ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
        {
            error = $"--seed must be a whole number from 0 to {ulong.MaxValue}, but was '{seedText}'.";
        }

        if (error is not null)
        {
            return output.UsageError(error);
        }

        ModuleSet set = ModuleLoader.Load(
            Path.GetFullPath(modulePath!, workingDirectory),
            parsed.All("--modules").Select(directory => Path.GetFullPath(directory, workingDirectory)).ToList());
        if (set.Rules is null || !set.IsValid)
        {
            return output.ModuleErrors(set);
        }

        RuleSet rules = set.Rules;
        (Creature? self, Creature? target, string? contextError) = ReadContext(parsed.Single("--context"), rules, workingDirectory);
        if (contextError is not null)
        {
            return output.UsageError(contextError);
        }

        if (checkId is not null)
        {
            Definition? check = rules.Find(DefinitionTypes.Check, checkId, out string? problem);
            if (check is null)
            {
                return output.UsageError($"--check: {problem}");
            }

            if (self is null)
            {
                return output.UsageError("A check needs a creature making it: pass --context '{\"self\": { ... }}'.");
            }

            return RunCheck(output, rules, check, self, target, seed);
        }

        string text = parsed.Positionals[0];
        CompiledExpression expression;
        try
        {
            expression = rules.Compile(text, set.Root!.Id, Roots.Self | Roots.Target);
        }
        catch (ExpressionException exception)
        {
            return output.ExpressionError(text, exception);
        }

        return RunExpression(output, rules, expression, self, target, seed);
    }

    private static int RunExpression(Output output, RuleSet rules, CompiledExpression expression, Creature? self, Creature? target, ulong seed)
    {
        (Value? value, IReadOnlyList<DiceRoll> rolls, ExpressionException? failure) = InHost(seed, dice =>
            new Evaluator(rules, dice).Evaluate(expression, self, target));
        if (failure is not null)
        {
            return output.EvaluationError(expression.Text, failure, seed, rolls);
        }

        output.Evaluated(expression.Text, value!.Value, seed, rolls);
        return GoldboxCli.Ok;
    }

    private static int RunCheck(Output output, RuleSet rules, Definition check, Creature self, Creature? target, ulong seed)
    {
        (CheckResult? result, IReadOnlyList<DiceRoll> rolls, ExpressionException? failure) = InHost(seed, dice =>
            new Evaluator(rules, dice).Check(check, self, target));
        if (failure is not null)
        {
            return output.EvaluationError($"check {check.QualifiedId}", failure, seed, rolls);
        }

        output.Checked(check, result!, seed, rolls);
        return GoldboxCli.Ok;
    }

    /// <summary>
    /// Runs <paramref name="evaluate"/> inside one Engine host callback with a
    /// dice roller on a stream seeded from <paramref name="seed"/>.
    /// </summary>
    private static (T? Result, IReadOnlyList<DiceRoll> Rolls, ExpressionException? Failure) InHost<T>(ulong seed, Func<DiceRoller, T> evaluate)
    {
        using EngineTestHost host = EngineTestHost.Create();
        return host.Call(engine =>
        {
            using Rng stream = engine.Random.CreateScoped(new ScopedRngCreateRequest(seed, RandomScope));
            DiceRoller dice = new(engine.Random, stream);
            try
            {
                return ((T?)evaluate(dice), dice.Rolls, (ExpressionException?)null);
            }
            catch (ExpressionException exception)
            {
                return (default(T), dice.Rolls, exception);
            }
        });
    }

    private static (Creature? Self, Creature? Target, string? Error) ReadContext(string? context, RuleSet rules, string workingDirectory)
    {
        if (context is null)
        {
            return (null, null, null);
        }

        string text = context;
        if (context.StartsWith('@'))
        {
            string path = Path.GetFullPath(context[1..], workingDirectory);
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return (null, null, $"--context: can't read {path}: {exception.Message}");
            }
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            return (null, null, $"--context is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null, "--context must be an object like {\"self\": {\"class\": \"fighter\", \"level\": 5}, \"target\": {\"monster\": \"skeleton\"}}.");
            }

            List<string> errors = [];
            Creature? self = null;
            Creature? target = null;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "self":
                        self = Creature.Read(property.Value, "self", rules, errors);
                        break;
                    case "target":
                        target = Creature.Read(property.Value, "target", rules, errors);
                        break;
                    default:
                        errors.Add($"'{property.Name}' is not a context field. Fields: self, target.");
                        break;
                }
            }

            return errors.Count > 0 ? (null, null, "--context: " + string.Join(" ", errors)) : (self, target, null);
        }
    }
}
