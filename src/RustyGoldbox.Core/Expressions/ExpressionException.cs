namespace RustyGoldbox.Core.Expressions;

/// <summary>An expression that can't be parsed, checked or evaluated, with the 1-based column it concerns.</summary>
public sealed class ExpressionException(string message, int column) : Exception(message)
{
    public int Column { get; } = column;
}
