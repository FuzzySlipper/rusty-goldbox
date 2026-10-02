namespace RustyGoldbox.Cli;

/// <summary>
/// A command's arguments: positionals, value options (<c>--dir x</c>, which
/// may repeat) and flags (<c>--json</c>).
/// </summary>
internal sealed class Arguments
{
    private readonly Dictionary<string, List<string>> _values = [];
    private readonly HashSet<string> _flags = [];

    private Arguments()
    {
    }

    public List<string> Positionals { get; } = [];

    public bool Json => _flags.Contains("--json");

    /// <summary>Parses <paramref name="args"/>; returns an error message for anything not declared.</summary>
    public static (Arguments Arguments, string? Error) Parse(
        IEnumerable<string> args,
        IReadOnlyCollection<string> valueOptions,
        IReadOnlyCollection<string> flags)
    {
        Arguments parsed = new();
        using IEnumerator<string> enumerator = args.GetEnumerator();
        while (enumerator.MoveNext())
        {
            string arg = enumerator.Current;
            if (arg == "--json" || flags.Contains(arg))
            {
                parsed._flags.Add(arg);
            }
            else if (valueOptions.Contains(arg))
            {
                if (!enumerator.MoveNext())
                {
                    return (parsed, $"{arg} needs a value.");
                }

                if (!parsed._values.TryGetValue(arg, out List<string>? values))
                {
                    values = [];
                    parsed._values[arg] = values;
                }

                values.Add(enumerator.Current);
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                string known = string.Join(", ", valueOptions.Concat(flags).Append("--json").Distinct());
                return (parsed, $"Unknown option {arg}. Options for this command: {known}.");
            }
            else
            {
                parsed.Positionals.Add(arg);
            }
        }

        return (parsed, null);
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public IReadOnlyList<string> All(string option)
    {
        return _values.TryGetValue(option, out List<string>? values) ? values : [];
    }

    public string? Single(string option)
    {
        IReadOnlyList<string> values = All(option);
        return values.Count == 0 ? null : values[^1];
    }
}
