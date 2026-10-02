namespace RustyGoldbox.Core.Expressions;

/// <summary>The type of an expression's value.</summary>
public enum ExprType
{
    Number,
    Boolean,
    Text,
}

public static class ExprTypes
{
    public static string Name(ExprType type)
    {
        return type switch
        {
            ExprType.Number => "number",
            ExprType.Boolean => "boolean",
            ExprType.Text => "text",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    public static bool TryParse(string text, out ExprType type)
    {
        switch (text)
        {
            case "number":
                type = ExprType.Number;
                return true;
            case "boolean":
                type = ExprType.Boolean;
                return true;
            case "text":
                type = ExprType.Text;
                return true;
            default:
                type = default;
                return false;
        }
    }
}
