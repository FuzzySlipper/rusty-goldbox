namespace RustyGoldbox.Core.Expressions;

/// <summary>The expression language reference that <c>goldbox schema expressions</c> prints.</summary>
public static class ExpressionReference
{
    public static IReadOnlyList<(string Topic, string Text)> Topics { get; } =
    [
        ("values", "Numbers (3, 3.5), text in single or double quotes ('fighter'), true and false. Every expression has one type: number, boolean or text, checked when the module loads."),
        ("dice", "2d6, d20, 3d6 + 2. Each evaluation rolls again through Engine Random, so a seed reproduces the rolls. For a count that isn't fixed, use roll(count, sides)."),
        ("reads", "self.<stat> and target.<stat> read a creature: an attribute, a derived value, or the built-ins level (number), class (text, the class ID) and race (text, the race ID). Each field says which of self and target it may read."),
        ("arithmetic", "+ - * / on numbers, and unary minus. Division is exact; use floor() or ceil() to round."),
        ("comparison", "< <= > >= on numbers; == and != on two values of the same type. Comparisons can't be chained."),
        ("logic", "and, or, not on booleans."),
        ("conditional", "if <boolean> then <value> else <value>; both branches have the same type."),
        ("references", "Table IDs in table() are the module's own (str_to_hit) or a required module's (classic:str_to_hit)."),
        ("not allowed", "No assignment, loops, user-defined functions or side effects. State changes come from operations, not expressions."),
    ];

    public const string Example = "if self.class == 'fighter' then table(con_hp_fighter, self.con) else table(con_hp, self.con)";
}
