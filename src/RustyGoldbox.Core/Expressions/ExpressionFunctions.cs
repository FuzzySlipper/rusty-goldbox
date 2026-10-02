namespace RustyGoldbox.Core.Expressions;

/// <summary>A function expressions can call.</summary>
public sealed record ExpressionFunction(string Name, string Signature, string Description, int MinArguments, int MaxArguments)
{
    public string ArgumentCountText => MinArguments == MaxArguments
        ? $"{MinArguments} argument{(MinArguments == 1 ? "" : "s")}"
        : MaxArguments == int.MaxValue ? $"{MinArguments} or more arguments" : $"{MinArguments} to {MaxArguments} arguments";
}

public static class ExpressionFunctions
{
    public static IReadOnlyList<ExpressionFunction> All { get; } =
    [
        new("min", "min(a, b, ...)", "The smallest of two or more numbers.", 2, int.MaxValue),
        new("max", "max(a, b, ...)", "The largest of two or more numbers.", 2, int.MaxValue),
        new("floor", "floor(x)", "x rounded down to a whole number.", 1, 1),
        new("ceil", "ceil(x)", "x rounded up to a whole number.", 1, 1),
        new("abs", "abs(x)", "x without its sign.", 1, 1),
        new("roll", "roll(count, sides)", "Rolls count dice with the given number of sides and adds them; for counts that aren't fixed, like roll(self.level, 4).", 2, 2),
        new("table", "table(id, key, ...)", "Looks up a table definition by its keys; gives the table's value type.", 2, int.MaxValue),
    ];

    public static ExpressionFunction? Find(string name) => All.FirstOrDefault(function => function.Name == name);
}
