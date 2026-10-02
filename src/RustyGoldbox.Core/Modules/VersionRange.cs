namespace RustyGoldbox.Core.Modules;

/// <summary>
/// A set of acceptable versions: <c>*</c>, an exact version, <c>^1.2.0</c>,
/// <c>~1.2.0</c>, or comparators (<c>&gt;=1.0.0 &lt;2.0.0</c>) that must all hold.
/// </summary>
public sealed class VersionRange
{
    public const string FormatDescription =
        "\"1.2.3\" (exact), \"^1.2.0\" (same major; same minor below 1.0.0), \"~1.2.0\" (same minor), "
        + "comparators such as \">=1.0.0 <2.0.0\", or \"*\" (any)";

    private readonly List<Comparator> _comparators;

    private VersionRange(string text, List<Comparator> comparators)
    {
        Text = text;
        _comparators = comparators;
    }

    public string Text { get; }

    public static bool TryParse(string text, out VersionRange? range)
    {
        range = null;
        string trimmed = text.Trim();
        if (trimmed == "*")
        {
            range = new VersionRange(trimmed, []);
            return true;
        }

        string[] tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        List<Comparator> comparators = [];
        foreach (string token in tokens)
        {
            if (!TryAddToken(token, comparators))
            {
                return false;
            }
        }

        range = new VersionRange(trimmed, comparators);
        return true;
    }

    public bool Contains(ModuleVersion version)
    {
        foreach (Comparator comparator in _comparators)
        {
            if (!comparator.Matches(version))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => Text;

    private static bool TryAddToken(string token, List<Comparator> comparators)
    {
        (string op, string remainder) = SplitOperator(token);
        if (!ModuleVersion.TryParse(remainder, out ModuleVersion version))
        {
            return false;
        }

        switch (op)
        {
            case "^":
                comparators.Add(new Comparator(">=", version));
                comparators.Add(new Comparator("<", CaretUpperBound(version)));
                return true;
            case "~":
                comparators.Add(new Comparator(">=", version));
                comparators.Add(new Comparator("<", new ModuleVersion(version.Major, version.Minor + 1, 0)));
                return true;
            case "":
            case "=":
                comparators.Add(new Comparator("=", version));
                return true;
            default:
                comparators.Add(new Comparator(op, version));
                return true;
        }
    }

    private static (string Op, string Remainder) SplitOperator(string token)
    {
        foreach (string op in new[] { ">=", "<=", ">", "<", "=", "^", "~" })
        {
            if (token.StartsWith(op, StringComparison.Ordinal))
            {
                return (op, token[op.Length..]);
            }
        }

        return ("", token);
    }

    private static ModuleVersion CaretUpperBound(ModuleVersion version)
    {
        if (version.Major > 0)
        {
            return new ModuleVersion(version.Major + 1, 0, 0);
        }

        if (version.Minor > 0)
        {
            return new ModuleVersion(0, version.Minor + 1, 0);
        }

        return new ModuleVersion(0, 0, version.Patch + 1);
    }

    private readonly record struct Comparator(string Op, ModuleVersion Version)
    {
        public bool Matches(ModuleVersion candidate)
        {
            return Op switch
            {
                "=" => candidate == Version,
                ">=" => candidate >= Version,
                "<=" => candidate <= Version,
                ">" => candidate > Version,
                "<" => candidate < Version,
                _ => throw new InvalidOperationException($"Unknown comparator '{Op}'."),
            };
        }
    }
}
