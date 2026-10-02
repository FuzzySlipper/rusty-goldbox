using System.Globalization;

namespace RustyGoldbox.Core.Expressions;

internal enum TokenKind
{
    Number,
    Dice,
    Name,
    Text,
    Operator,
    End,
}

internal readonly record struct Token(TokenKind Kind, string Text, int Column);

/// <summary>Splits expression text into tokens.</summary>
internal static class Lexer
{
    private static readonly string[] Operators = ["==", "!=", "<=", ">=", "<", ">", "+", "-", "*", "/", "(", ")", ",", "."];

    public static List<Token> Tokenize(string text)
    {
        List<Token> tokens = [];
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (char.IsAsciiDigit(c))
            {
                tokens.Add(ReadNumberOrDice(text, ref i));
            }
            else if (char.IsAsciiLetterLower(c))
            {
                bool afterDot = tokens.Count > 0 && tokens[^1] is { Kind: TokenKind.Operator, Text: "." };
                tokens.Add(ReadName(text, ref i, afterDot));
            }
            else if (c is '"' or '\'')
            {
                tokens.Add(ReadText(text, ref i));
            }
            else
            {
                tokens.Add(ReadOperator(text, ref i));
            }
        }

        tokens.Add(new Token(TokenKind.End, "", text.Length + 1));
        return tokens;
    }

    private static Token ReadNumberOrDice(string text, ref int i)
    {
        int start = i;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
        }

        if (i < text.Length && text[i] == 'd' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
            }

            return new Token(TokenKind.Dice, text[start..i], start + 1);
        }

        if (i + 1 < text.Length && text[i] == '.' && char.IsAsciiDigit(text[i + 1]))
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
            }
        }

        if (i < text.Length && (char.IsAsciiLetter(text[i]) || text[i] == '_'))
        {
            throw new ExpressionException($"'{text[start..(i + 1)]}' is not a number. Numbers are like 3 or 3.5; dice are like 2d6.", start + 1);
        }

        if (!decimal.TryParse(text[start..i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
        {
            throw new ExpressionException($"{text[start..i]} is too large to be a number.", start + 1);
        }

        return new Token(TokenKind.Number, text[start..i], start + 1);
    }

    private static Token ReadName(string text, ref int i, bool afterDot)
    {
        int start = i;
        while (i < text.Length && (char.IsAsciiLetterLower(text[i]) || char.IsAsciiDigit(text[i]) || text[i] == '_'))
        {
            i++;
        }

        string name = text[start..i];
        if (name.Length > 1 && name[0] == 'd' && name[1..].All(char.IsAsciiDigit))
        {
            return new Token(TokenKind.Dice, name, start + 1);
        }

        // A module-qualified definition name: module IDs may contain hyphens,
        // so "stone-crypt:wall" is one token. Never after a dot (self.x).
        if (!afterDot && TryReadQualified(text, start, out int end))
        {
            i = end;
            return new Token(TokenKind.Name, text[start..end], start + 1);
        }

        return new Token(TokenKind.Name, name, start + 1);
    }

    private static bool TryReadQualified(string text, int start, out int end)
    {
        end = start;
        int i = start;
        while (i < text.Length && (char.IsAsciiLetterLower(text[i]) || char.IsAsciiDigit(text[i]) || text[i] == '-'))
        {
            i++;
        }

        if (i >= text.Length || text[i] != ':' || i + 1 >= text.Length || !char.IsAsciiLetterLower(text[i + 1]))
        {
            return false;
        }

        i++;
        while (i < text.Length && (char.IsAsciiLetterLower(text[i]) || char.IsAsciiDigit(text[i]) || text[i] == '_'))
        {
            i++;
        }

        end = i;
        return true;
    }

    private static Token ReadText(string text, ref int i)
    {
        char quote = text[i];
        int start = i;
        int close = text.IndexOf(quote, i + 1);
        if (close < 0)
        {
            throw new ExpressionException($"Text starting with {quote} is never closed. Close it with a matching {quote}.", start + 1);
        }

        i = close + 1;
        return new Token(TokenKind.Text, text[(start + 1)..close], start + 1);
    }

    private static Token ReadOperator(string text, ref int i)
    {
        foreach (string op in Operators)
        {
            if (string.CompareOrdinal(text, i, op, 0, op.Length) == 0)
            {
                int start = i;
                i += op.Length;
                return new Token(TokenKind.Operator, op, start + 1);
            }
        }

        string shown = text[i] == '=' ? "'=' (use '==' to compare)" : $"'{text[i]}'";
        throw new ExpressionException($"Unexpected character {shown}.", i + 1);
    }

    public static decimal ParseNumber(Token token)
    {
        return decimal.Parse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }
}
