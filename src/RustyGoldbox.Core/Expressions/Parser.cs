using System.Globalization;

namespace RustyGoldbox.Core.Expressions;

/// <summary>
/// Parses expression text. Precedence, lowest first: <c>if/then/else</c>,
/// <c>or</c>, <c>and</c>, <c>not</c>, comparisons, <c>+ -</c>, <c>* /</c>,
/// unary minus, then literals, dice, <c>root.name</c> reads, calls and parentheses.
/// </summary>
public static class Parser
{
    private static readonly HashSet<string> Keywords = ["if", "then", "else", "and", "or", "not", "true", "false"];
    private static readonly HashSet<string> Comparisons = ["==", "!=", "<", "<=", ">", ">="];

    /// <exception cref="ExpressionException">The text is not a valid expression.</exception>
    public static Expr Parse(string text)
    {
        State state = new(Lexer.Tokenize(text));
        Expr expr = state.ParseExpression();
        Token next = state.Peek();
        if (next.Kind != TokenKind.End)
        {
            throw new ExpressionException($"Unexpected '{next.Text}' after a complete expression.", next.Column);
        }

        return expr;
    }

    private sealed class State(List<Token> tokens)
    {
        private int _position;

        public Token Peek() => tokens[_position];

        public Expr ParseExpression()
        {
            Token token = Peek();
            if (IsKeyword(token, "if"))
            {
                _position++;
                Expr condition = ParseExpression();
                ExpectKeyword("then");
                Expr then = ParseExpression();
                ExpectKeyword("else");
                Expr otherwise = ParseExpression();
                return new ConditionalExpr(token.Column, condition, then, otherwise);
            }

            return ParseOr();
        }

        private Expr ParseOr()
        {
            Expr left = ParseAnd();
            while (IsKeyword(Peek(), "or"))
            {
                Token op = Next();
                left = new BinaryExpr(op.Column, "or", left, ParseAnd());
            }

            return left;
        }

        private Expr ParseAnd()
        {
            Expr left = ParseNot();
            while (IsKeyword(Peek(), "and"))
            {
                Token op = Next();
                left = new BinaryExpr(op.Column, "and", left, ParseNot());
            }

            return left;
        }

        private Expr ParseNot()
        {
            if (IsKeyword(Peek(), "not"))
            {
                Token op = Next();
                return new UnaryExpr(op.Column, "not", ParseNot());
            }

            return ParseComparison();
        }

        private Expr ParseComparison()
        {
            Expr left = ParseAdditive();
            Token token = Peek();
            if (token.Kind == TokenKind.Operator && Comparisons.Contains(token.Text))
            {
                _position++;
                Expr right = ParseAdditive();
                Token after = Peek();
                if (after.Kind == TokenKind.Operator && Comparisons.Contains(after.Text))
                {
                    throw new ExpressionException("Comparisons can't be chained. Combine them with 'and', like 'a < b and b < c'.", after.Column);
                }

                return new BinaryExpr(token.Column, token.Text, left, right);
            }

            return left;
        }

        private Expr ParseAdditive()
        {
            Expr left = ParseMultiplicative();
            while (Peek() is { Kind: TokenKind.Operator, Text: "+" or "-" })
            {
                Token op = Next();
                left = new BinaryExpr(op.Column, op.Text, left, ParseMultiplicative());
            }

            return left;
        }

        private Expr ParseMultiplicative()
        {
            Expr left = ParseUnary();
            while (Peek() is { Kind: TokenKind.Operator, Text: "*" or "/" or "%" })
            {
                Token op = Next();
                left = new BinaryExpr(op.Column, op.Text, left, ParseUnary());
            }

            return left;
        }

        private Expr ParseUnary()
        {
            if (Peek() is { Kind: TokenKind.Operator, Text: "-" })
            {
                Token op = Next();
                return new UnaryExpr(op.Column, "-", ParseUnary());
            }

            return ParsePrimary();
        }

        private Expr ParsePrimary()
        {
            Token token = Next();
            switch (token.Kind)
            {
                case TokenKind.Number:
                    return new NumberLiteral(token.Column, Lexer.ParseNumber(token));
                case TokenKind.Dice:
                    return ParseDice(token);
                case TokenKind.Text:
                    return new TextLiteral(token.Column, token.Text);
                case TokenKind.Name:
                    return ParseName(token);
                case TokenKind.Operator when token.Text == "(":
                    Expr inner = ParseExpression();
                    Expect(")");
                    return inner;
                case TokenKind.End:
                    throw new ExpressionException("The expression ends too early; a value is missing.", token.Column);
                default:
                    throw new ExpressionException($"Expected a value but found '{token.Text}'.", token.Column);
            }
        }

        private Expr ParseName(Token token)
        {
            switch (token.Text)
            {
                case "true":
                    return new BooleanLiteral(token.Column, true);
                case "false":
                    return new BooleanLiteral(token.Column, false);
            }

            if (Keywords.Contains(token.Text))
            {
                throw new ExpressionException($"Expected a value but found the keyword '{token.Text}'.", token.Column);
            }

            if (Peek() is { Kind: TokenKind.Operator, Text: "." })
            {
                _position++;
                Token name = Next();
                if (name.Kind != TokenKind.Name || Keywords.Contains(name.Text))
                {
                    throw new ExpressionException($"Expected a name after '{token.Text}.', like '{token.Text}.level'.", name.Column);
                }

                if (token.Text == "campaign")
                {
                    // Campaign variables: campaign.var.<name>, read as PathExpr("campaign", name).
                    if (name.Text != "var" || Peek() is not { Kind: TokenKind.Operator, Text: "." })
                    {
                        throw new ExpressionException("Read campaign variables as campaign.var.<name>.", token.Column);
                    }

                    _position++;
                    Token variable = Next();
                    if (variable.Kind != TokenKind.Name || Keywords.Contains(variable.Text))
                    {
                        throw new ExpressionException("Expected a variable name after 'campaign.var.'.", variable.Column);
                    }

                    name = variable;
                }

                // A creature's conditions: self.condition.<id>, read as PathExpr(root, "condition", id).
                if (token.Text is "self" or "target" && name.Text == "condition" && Peek() is { Kind: TokenKind.Operator, Text: "." })
                {
                    _position++;
                    Token condition = Next();
                    if (condition.Kind != TokenKind.Name || Keywords.Contains(condition.Text))
                    {
                        throw new ExpressionException($"Expected a condition ID after '{token.Text}.condition.'.", condition.Column);
                    }

                    return new PathExpr(token.Column, token.Text, name.Text, condition.Text);
                }

                if (Peek() is { Kind: TokenKind.Operator, Text: "." } after)
                {
                    throw new ExpressionException($"Reads have one level, like {token.Text}.{name.Text}; there is nothing after it to read.", after.Column);
                }

                return new PathExpr(token.Column, token.Text, name.Text);
            }

            if (Peek() is { Kind: TokenKind.Operator, Text: "(" })
            {
                _position++;
                List<Expr> arguments = [];
                if (Peek() is not { Kind: TokenKind.Operator, Text: ")" })
                {
                    arguments.Add(ParseExpression());
                    while (Peek() is { Kind: TokenKind.Operator, Text: "," })
                    {
                        _position++;
                        arguments.Add(ParseExpression());
                    }
                }

                Expect(")");
                return new CallExpr(token.Column, token.Text, arguments);
            }

            return new NameExpr(token.Column, token.Text);
        }

        private static DiceLiteral ParseDice(Token token)
        {
            int d = token.Text.IndexOf('d', StringComparison.Ordinal);
            string countText = token.Text[..d];
            int count = countText.Length == 0 ? 1 : ParseCount(countText, token);
            int sides = ParseCount(token.Text[(d + 1)..], token);
            if (count < 1 || sides < 1)
            {
                throw new ExpressionException($"'{token.Text}' needs at least one die with at least one side.", token.Column);
            }

            return new DiceLiteral(token.Column, count, sides);
        }

        private static int ParseCount(string text, Token token)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                throw new ExpressionException($"'{token.Text}' has a die count or size that is too large.", token.Column);
            }

            return value;
        }

        private Token Next() => tokens[_position++];

        private void Expect(string op)
        {
            Token token = Next();
            if (token.Kind != TokenKind.Operator || token.Text != op)
            {
                string found = token.Kind == TokenKind.End ? "the end" : $"'{token.Text}'";
                throw new ExpressionException($"Expected '{op}' but found {found}.", token.Column);
            }
        }

        private void ExpectKeyword(string keyword)
        {
            Token token = Next();
            if (!IsKeyword(token, keyword))
            {
                string found = token.Kind == TokenKind.End ? "the end" : $"'{token.Text}'";
                throw new ExpressionException($"Expected '{keyword}' but found {found}. Write conditionals as 'if <condition> then <value> else <value>'.", token.Column);
            }
        }

        private static bool IsKeyword(Token token, string keyword) => token.Kind == TokenKind.Name && token.Text == keyword;
    }
}
