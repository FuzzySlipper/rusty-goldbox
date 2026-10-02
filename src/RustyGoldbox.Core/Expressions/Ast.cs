namespace RustyGoldbox.Core.Expressions;

/// <summary>A parsed expression node. <see cref="Column"/> is 1-based within the expression text.</summary>
public abstract record Expr(int Column);

public sealed record NumberLiteral(int Column, decimal Value) : Expr(Column);

public sealed record BooleanLiteral(int Column, bool Value) : Expr(Column);

public sealed record TextLiteral(int Column, string Value) : Expr(Column);

/// <summary>A dice literal such as <c>2d6</c> or <c>d20</c>.</summary>
public sealed record DiceLiteral(int Column, int Count, int Sides) : Expr(Column);

/// <summary>A read of the evaluation context: <c>self.str</c>, <c>target.ac</c>.</summary>
public sealed record PathExpr(int Column, string Root, string Name) : Expr(Column);

/// <summary>A bare or module-qualified name; only valid where a definition is expected, such as a table.</summary>
public sealed record NameExpr(int Column, string Name) : Expr(Column);

public sealed record UnaryExpr(int Column, string Operator, Expr Operand) : Expr(Column);

public sealed record BinaryExpr(int Column, string Operator, Expr Left, Expr Right) : Expr(Column);

public sealed record ConditionalExpr(int Column, Expr Condition, Expr Then, Expr Else) : Expr(Column);

public sealed record CallExpr(int Column, string Function, IReadOnlyList<Expr> Arguments) : Expr(Column);
