using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Stanza.Gui.Helpers;

/// <summary>
/// Evaluates inline math expressions enclosed in $(...) within chat messages.
/// Escaping with \$ prevents expression evaluation and renders as literal $.
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>
    /// Evaluates all unescaped $(...) expressions in the input text.
    /// If an expression is malformed or invalid (e.g. division by zero), it is left untouched.
    /// Escaped \$ sequences are unescaped to $.
    /// </summary>
    public static (string EvaluatedText, bool WasEvaluated) Evaluate(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return (input ?? string.Empty, false);
        }

        var sb = new StringBuilder(input.Length);
        int i = 0;
        bool anyEvaluated = false;

        while (i < input.Length)
        {
            // Handle backslash escapes
            if (input[i] == '\\')
            {
                if (i + 1 < input.Length && input[i + 1] == '$')
                {
                    // "\$" escapes "$" -> produce literal "$" and do not evaluate
                    sb.Append('$');
                    i += 2;
                    continue;
                }
                else if (i + 1 < input.Length && input[i + 1] == '\\')
                {
                    // "\\$" -> escaped backslash (literal "\") and normal "$" follows
                    if (i + 2 < input.Length && input[i + 2] == '$')
                    {
                        sb.Append('\\');
                        i += 2; // Position at the '$'
                        continue;
                    }
                }

                sb.Append(input[i]);
                i++;
                continue;
            }

            // Check for start of expression "$("
            if (input[i] == '$' && i + 1 < input.Length && input[i + 1] == '(')
            {
                int startParen = i + 1;
                int closeParen = FindMatchingCloseParen(input, startParen);
                if (closeParen > startParen)
                {
                    string inner = input.Substring(startParen + 1, closeParen - startParen - 1);
                    if (TryEvaluateExpression(inner, out double val))
                    {
                        sb.Append(FormatNumber(val));
                        i = closeParen + 1;
                        anyEvaluated = true;
                        continue;
                    }
                }
            }

            sb.Append(input[i]);
            i++;
        }

        return (sb.ToString(), anyEvaluated);
    }

    /// <summary>
    /// Convenience helper returning the evaluated text directly.
    /// </summary>
    public static string EvaluateText(string? input) => Evaluate(input).EvaluatedText;

    /// <summary>
    /// Checks if the input text contains unescaped expressions that produce a different result.
    /// </summary>
    public static bool TryEvaluatePreview(string? input, out string previewText)
    {
        previewText = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || !input.Contains("$("))
        {
            return false;
        }

        var (evaluated, wasEvaluated) = Evaluate(input);
        if (wasEvaluated && !string.Equals(evaluated, input, StringComparison.Ordinal))
        {
            previewText = evaluated;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses and evaluates an expression string (e.g. "1 + 3 * 4" -> 14).
    /// Returns true if successfully evaluated without NaN or Infinity.
    /// </summary>
    public static bool TryEvaluateExpression(string expression, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        if (!Tokenize(expression, out var tokens))
        {
            return false;
        }

        if (tokens.Count == 0)
        {
            return false;
        }

        int index = 0;
        if (!ParseExpr(tokens, ref index, out result))
        {
            return false;
        }

        if (index != tokens.Count || double.IsNaN(result) || double.IsInfinity(result))
        {
            return false;
        }

        return true;
    }

    private static int FindMatchingCloseParen(string text, int openParenIndex)
    {
        int depth = 0;
        for (int i = openParenIndex; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private static string FormatNumber(double val)
    {
        if (double.IsNegative(val) && val == 0.0)
        {
            val = 0.0;
        }

        if (val >= long.MinValue && val <= long.MaxValue && val == Math.Truncate(val))
        {
            return ((long)val).ToString(CultureInfo.InvariantCulture);
        }

        return val.ToString("G14", CultureInfo.InvariantCulture);
    }

    #region Parser & Lexer

    private enum TokenType
    {
        Number,
        Identifier,
        Plus,
        Minus,
        Star,
        Slash,
        Percent,
        Caret,
        OpenParen,
        CloseParen,
        Comma
    }

    private readonly record struct Token(TokenType Type, double NumberValue = 0, string? TextValue = null);

    private static bool Tokenize(string expr, out List<Token> tokens)
    {
        tokens = [];
        int i = 0;

        while (i < expr.Length)
        {
            char c = expr[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < expr.Length && char.IsDigit(expr[i + 1])))
            {
                int start = i;
                while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.' || expr[i] == 'e' || expr[i] == 'E' ||
                       ((expr[i] == '+' || expr[i] == '-') && i > start && (expr[i - 1] == 'e' || expr[i - 1] == 'E'))))
                {
                    i++;
                }

                var numStr = expr[start..i];
                if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double numVal))
                {
                    return false;
                }
                tokens.Add(new Token(TokenType.Number, NumberValue: numVal));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_'))
                {
                    i++;
                }
                tokens.Add(new Token(TokenType.Identifier, TextValue: expr[start..i]));
                continue;
            }

            switch (c)
            {
                case '+':
                    tokens.Add(new Token(TokenType.Plus));
                    i++;
                    break;
                case '-':
                    tokens.Add(new Token(TokenType.Minus));
                    i++;
                    break;
                case '*':
                    if (i + 1 < expr.Length && expr[i + 1] == '*')
                    {
                        tokens.Add(new Token(TokenType.Caret)); // "**" exponentiation
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Star));
                        i++;
                    }
                    break;
                case '/':
                    tokens.Add(new Token(TokenType.Slash));
                    i++;
                    break;
                case '%':
                    tokens.Add(new Token(TokenType.Percent));
                    i++;
                    break;
                case '^':
                    tokens.Add(new Token(TokenType.Caret));
                    i++;
                    break;
                case '(':
                    tokens.Add(new Token(TokenType.OpenParen));
                    i++;
                    break;
                case ')':
                    tokens.Add(new Token(TokenType.CloseParen));
                    i++;
                    break;
                case ',':
                    tokens.Add(new Token(TokenType.Comma));
                    i++;
                    break;
                default:
                    return false; // Unknown character
            }
        }

        return true;
    }

    // Expr ::= Term (('+' | '-') Term)*
    private static bool ParseExpr(IReadOnlyList<Token> tokens, ref int index, out double result)
    {
        if (!ParseTerm(tokens, ref index, out result))
        {
            return false;
        }

        while (index < tokens.Count && (tokens[index].Type == TokenType.Plus || tokens[index].Type == TokenType.Minus))
        {
            var op = tokens[index++].Type;
            if (!ParseTerm(tokens, ref index, out double right))
            {
                return false;
            }

            result = op == TokenType.Plus ? result + right : result - right;
            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                return false;
            }
        }

        return true;
    }

    // Term ::= Power (('*' | '/' | '%') Power)*
    private static bool ParseTerm(IReadOnlyList<Token> tokens, ref int index, out double result)
    {
        if (!ParsePower(tokens, ref index, out result))
        {
            return false;
        }

        while (index < tokens.Count && (tokens[index].Type == TokenType.Star || tokens[index].Type == TokenType.Slash || tokens[index].Type == TokenType.Percent))
        {
            var op = tokens[index++].Type;
            if (!ParsePower(tokens, ref index, out double right))
            {
                return false;
            }

            if (op == TokenType.Star)
            {
                result *= right;
            }
            else if (op == TokenType.Slash)
            {
                if (right == 0.0) return false;
                result /= right;
            }
            else if (op == TokenType.Percent)
            {
                if (right == 0.0) return false;
                result %= right;
            }

            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                return false;
            }
        }

        return true;
    }

    // Power ::= Unary (('^' | '**') Power)?  (Right-associative)
    private static bool ParsePower(IReadOnlyList<Token> tokens, ref int index, out double result)
    {
        if (!ParseUnary(tokens, ref index, out result))
        {
            return false;
        }

        if (index < tokens.Count && tokens[index].Type == TokenType.Caret)
        {
            index++;
            if (!ParsePower(tokens, ref index, out double exponent))
            {
                return false;
            }
            result = Math.Pow(result, exponent);
            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                return false;
            }
        }

        return true;
    }

    // Unary ::= ('+' | '-') Unary | Primary
    private static bool ParseUnary(IReadOnlyList<Token> tokens, ref int index, out double result)
    {
        result = 0;
        if (index >= tokens.Count)
        {
            return false;
        }

        if (tokens[index].Type == TokenType.Plus)
        {
            index++;
            return ParseUnary(tokens, ref index, out result);
        }

        if (tokens[index].Type == TokenType.Minus)
        {
            index++;
            if (!ParseUnary(tokens, ref index, out double val))
            {
                return false;
            }
            result = -val;
            return true;
        }

        return ParsePrimary(tokens, ref index, out result);
    }

    // Primary ::= Number | Identifier ('(' Args? ')')? | '(' Expr ')'
    private static bool ParsePrimary(IReadOnlyList<Token> tokens, ref int index, out double result)
    {
        result = 0;
        if (index >= tokens.Count)
        {
            return false;
        }

        var token = tokens[index];

        if (token.Type == TokenType.Number)
        {
            result = token.NumberValue;
            index++;
            return true;
        }

        if (token.Type == TokenType.OpenParen)
        {
            index++;
            if (!ParseExpr(tokens, ref index, out result))
            {
                return false;
            }
            if (index >= tokens.Count || tokens[index].Type != TokenType.CloseParen)
            {
                return false;
            }
            index++; // Consume ')'
            return true;
        }

        if (token.Type == TokenType.Identifier)
        {
            var name = token.TextValue ?? string.Empty;
            index++;

            // Check if function call
            if (index < tokens.Count && tokens[index].Type == TokenType.OpenParen)
            {
                index++; // Consume '('
                var args = new List<double>();
                if (index < tokens.Count && tokens[index].Type != TokenType.CloseParen)
                {
                    while (true)
                    {
                        if (!ParseExpr(tokens, ref index, out double arg))
                        {
                            return false;
                        }
                        args.Add(arg);

                        if (index < tokens.Count && tokens[index].Type == TokenType.Comma)
                        {
                            index++;
                            continue;
                        }
                        break;
                    }
                }

                if (index >= tokens.Count || tokens[index].Type != TokenType.CloseParen)
                {
                    return false;
                }
                index++; // Consume ')'

                return EvaluateFunction(name, args, out result);
            }

            // Constant evaluation
            return EvaluateConstant(name, out result);
        }

        return false;
    }

    private static bool EvaluateConstant(string name, out double val)
    {
        if (string.Equals(name, "pi", StringComparison.OrdinalIgnoreCase))
        {
            val = Math.PI;
            return true;
        }

        if (string.Equals(name, "e", StringComparison.OrdinalIgnoreCase))
        {
            val = Math.E;
            return true;
        }

        val = 0;
        return false;
    }

    private static bool EvaluateFunction(string name, IReadOnlyList<double> args, out double result)
    {
        result = 0;
        var lower = name.ToLowerInvariant();

        switch (lower)
        {
            case "sqrt":
                if (args.Count != 1 || args[0] < 0) return false;
                result = Math.Sqrt(args[0]);
                return true;

            case "abs":
                if (args.Count != 1) return false;
                result = Math.Abs(args[0]);
                return true;

            case "round":
                if (args.Count == 1)
                {
                    result = Math.Round(args[0]);
                    return true;
                }
                if (args.Count == 2 && args[1] >= 0 && args[1] <= 15)
                {
                    result = Math.Round(args[0], (int)args[1]);
                    return true;
                }
                return false;

            case "floor":
                if (args.Count != 1) return false;
                result = Math.Floor(args[0]);
                return true;

            case "ceil":
            case "ceiling":
                if (args.Count != 1) return false;
                result = Math.Ceiling(args[0]);
                return true;

            case "min":
                if (args.Count != 2) return false;
                result = Math.Min(args[0], args[1]);
                return true;

            case "max":
                if (args.Count != 2) return false;
                result = Math.Max(args[0], args[1]);
                return true;

            case "pow":
                if (args.Count != 2) return false;
                result = Math.Pow(args[0], args[1]);
                return true;

            case "sin":
                if (args.Count != 1) return false;
                result = Math.Sin(args[0]);
                return true;

            case "cos":
                if (args.Count != 1) return false;
                result = Math.Cos(args[0]);
                return true;

            case "tan":
                if (args.Count != 1) return false;
                result = Math.Tan(args[0]);
                return true;

            case "log":
            case "ln":
                if (args.Count != 1 || args[0] <= 0) return false;
                result = Math.Log(args[0]);
                return true;

            case "log10":
                if (args.Count != 1 || args[0] <= 0) return false;
                result = Math.Log10(args[0]);
                return true;

            case "exp":
                if (args.Count != 1) return false;
                result = Math.Exp(args[0]);
                return true;

            default:
                return false;
        }
    }

    #endregion
}
