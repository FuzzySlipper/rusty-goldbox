using System.Globalization;

namespace RustyGoldbox.Core.Expressions;

/// <summary>A number, boolean or text value produced by an expression.</summary>
public readonly record struct Value(ExprType Type, decimal Number, bool Boolean, string Text)
{
    public static Value Of(decimal number) => new(ExprType.Number, number, false, "");

    public static Value Of(bool boolean) => new(ExprType.Boolean, 0, boolean, "");

    public static Value Of(string text) => new(ExprType.Text, 0, false, text);

    public override string ToString()
    {
        return Type switch
        {
            ExprType.Number => Number.ToString("0.############", CultureInfo.InvariantCulture),
            ExprType.Boolean => Boolean ? "true" : "false",
            _ => Text,
        };
    }
}
