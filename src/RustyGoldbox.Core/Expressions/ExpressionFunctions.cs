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
        new("roll_keep", "roll_keep(count, sides, keep)", "Rolls count dice and adds the highest keep of them: roll_keep(4, 6, 3) is 4d6 dropping the lowest.", 3, 3),
        new("roll_count", "roll_count(count, sides, at_least)", "Rolls count dice and counts the faces of at_least or more, for dice pools: roll_count(5, 10, 8).", 3, 3),
        new("roll_explode", "roll_explode(count, sides, again)", "Rolls count dice and adds every face, where each face of again or more rolls another die too: roll_explode(1, 6, 6) is an open-ended d6. again must be 2 or more.", 3, 3),
        new("roll_fudge", "roll_fudge(count)", "Rolls count Fudge dice, each -1, 0 or +1, and adds them: roll_fudge(4) is 4dF, -4 to +4.", 1, 1),
        new("roll_pool", "roll_pool(count, sides, at_least, again, cancel)", "A dice pool: counts the faces of at_least or more, less the faces of cancel or lower, where each face of again or more rolls another die. Use again above sides for no rerolls and cancel 0 for none: roll_pool(5, 10, 8, 10, 0) rerolls tens; roll_pool(5, 10, 6, 11, 1) has ones cancel successes, so it can go below 0.", 5, 5),
        new("class_min", "class_min(x)", "The smallest of x worked out once for each class self has, reading that class as class.id and class.level: class_min(table(thac0, class.id, class.level)) is the best attack table of a multi-classed character.", 1, 1),
        new("class_max", "class_max(x)", "The largest of x worked out once for each class self has (class.id, class.level).", 1, 1),
        new("class_sum", "class_sum(x)", "The total of x worked out once for each class self has (class.id, class.level).", 1, 1),
        new("equipped", "equipped(x)", "How many of the items self has equipped x holds for, reading each as item.id, item.kind, item.weight and item.cost: equipped(item.kind == 'shield') > 0 needs a shield, equipped(item.kind == 'armour' and item.id != 'leather_armour') == 0 allows only leather armour.", 1, 1),
        new("spell_slots", "spell_slots(level)", "Spells per day of a spell level that self's classes give at their levels (their spell_slots), added over its classes: the maximum for a track of spell slots.", 1, 1),
        new("table", "table(id, key, ...)", "Looks up a table definition by its keys; gives the table's value type.", 2, int.MaxValue),
    ];

    public static ExpressionFunction? Find(string name) => All.FirstOrDefault(function => function.Name == name);
}
