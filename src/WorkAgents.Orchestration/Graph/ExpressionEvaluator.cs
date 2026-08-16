using System.Globalization;
using System.Text;

namespace WorkAgents.Orchestration.Graph;

/// <summary>Safe evaluator for the small comparison/boolean expression language in graph.yaml.</summary>
public sealed class ExpressionEvaluator
{
    public bool EvaluateBoolean(string expression, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(values);
        var parser = new Parser(Tokenize(expression), values);
        var value = parser.ParseExpression();
        parser.ExpectEnd();
        return ToBoolean(value);
    }

    public object? Evaluate(string expression, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(values);
        var parser = new Parser(Tokenize(expression), values);
        var value = parser.ParseExpression();
        parser.ExpectEnd();
        return value;
    }

    private static bool ToBoolean(object? value)
        => value switch
        {
            bool boolean => boolean,
            null => false,
            double number => Math.Abs(number) > double.Epsilon,
            int integer => integer != 0,
            string text => !string.IsNullOrEmpty(text),
            _ => true,
        };

    private static IReadOnlyList<Token> Tokenize(string expression)
    {
        var tokens = new List<Token>();
        for (var index = 0; index < expression.Length;)
        {
            if (char.IsWhiteSpace(expression[index]))
            {
                index++;
                continue;
            }
            if (expression[index] == '$' && index + 1 < expression.Length && expression[index + 1] == '{')
            {
                var end = expression.IndexOf('}', index + 2);
                if (end < 0)
                {
                    throw new FormatException("Unclosed graph reference.");
                }
                tokens.Add(new Token(TokenKind.Value, expression[(index + 2)..end], true));
                index = end + 1;
                continue;
            }
            if (expression[index] is '\'' or '"')
            {
                var quote = expression[index++];
                var start = index;
                var builder = new StringBuilder();
                while (index < expression.Length && expression[index] != quote)
                {
                    if (expression[index] == '\\' && index + 1 < expression.Length)
                    {
                        index++;
                    }
                    builder.Append(expression[index++]);
                }
                if (index >= expression.Length)
                {
                    throw new FormatException("Unclosed string literal.");
                }
                index++;
                tokens.Add(new Token(TokenKind.Value, builder.ToString(), false));
                continue;
            }
            if (char.IsDigit(expression[index]) || (expression[index] == '-' && index + 1 < expression.Length && char.IsDigit(expression[index + 1])))
            {
                var start = index++;
                while (index < expression.Length && (char.IsDigit(expression[index]) || expression[index] == '.'))
                {
                    index++;
                }
                tokens.Add(new Token(TokenKind.Value, expression[start..index], false));
                continue;
            }
            if (char.IsLetter(expression[index]) || expression[index] == '_')
            {
                var start = index++;
                while (index < expression.Length && (char.IsLetterOrDigit(expression[index]) || expression[index] is '_' or '.' or '[' or ']'))
                {
                    index++;
                }
                var value = expression[start..index];
                tokens.Add(value is "true" or "false"
                    ? new Token(TokenKind.Value, value, false)
                    : new Token(TokenKind.Value, value, true));
                continue;
            }
            var two = index + 1 < expression.Length ? expression.Substring(index, 2) : string.Empty;
            if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||")
            {
                tokens.Add(new Token(TokenKind.Operator, two, false));
                index += 2;
                continue;
            }
            if (expression[index] is '<' or '>' or '!')
            {
                tokens.Add(new Token(TokenKind.Operator, expression[index].ToString(), false));
                index++;
                continue;
            }
            if (expression[index] is '(' or ')')
            {
                tokens.Add(new Token(expression[index] == '(' ? TokenKind.LeftParen : TokenKind.RightParen, expression[index].ToString(), false));
                index++;
                continue;
            }
            throw new FormatException($"Unsupported expression character '{expression[index]}'.");
        }
        return tokens;
    }

    private enum TokenKind
    {
        Value,
        Operator,
        LeftParen,
        RightParen,
    }

    private sealed record Token(TokenKind Kind, string Text, bool Reference);

    private sealed class Parser
    {
        private readonly IReadOnlyList<Token> _tokens;
        private readonly IReadOnlyDictionary<string, object?> _values;
        private int _position;

        public Parser(IReadOnlyList<Token> tokens, IReadOnlyDictionary<string, object?> values)
        {
            _tokens = tokens;
            _values = values;
        }

        public object? ParseExpression() => ParseOr();

        public void ExpectEnd()
        {
            if (_position != _tokens.Count)
            {
                throw new FormatException($"Unexpected token '{_tokens[_position].Text}'.");
            }
        }

        private object? ParseOr()
        {
            var value = ParseAnd();
            while (Match("||"))
            {
                value = ToBoolean(value) || ToBoolean(ParseAnd());
            }
            return value;
        }

        private object? ParseAnd()
        {
            var value = ParseUnary();
            while (Match("&&"))
            {
                value = ToBoolean(value) && ToBoolean(ParseUnary());
            }
            return value;
        }

        private object? ParseUnary()
        {
            if (Match("!"))
            {
                return !ToBoolean(ParseUnary());
            }
            return ParseComparison();
        }

        private object? ParseComparison()
        {
            var left = ParsePrimary();
            if (_position >= _tokens.Count || _tokens[_position].Kind != TokenKind.Operator || _tokens[_position].Text is "&&" or "||" or "!")
            {
                return left;
            }
            var op = _tokens[_position++].Text;
            var right = ParsePrimary();
            return Compare(left, right, op);
        }

        private object? ParsePrimary()
        {
            if (_position >= _tokens.Count)
            {
                throw new FormatException("Expression ended before a value.");
            }
            var token = _tokens[_position++];
            if (token.Kind == TokenKind.LeftParen)
            {
                var value = ParseExpression();
                if (_position >= _tokens.Count || _tokens[_position].Kind != TokenKind.RightParen)
                {
                    throw new FormatException("Missing closing parenthesis.");
                }
                _position++;
                return value;
            }
            if (token.Kind != TokenKind.Value)
            {
                throw new FormatException($"Expected a value, got '{token.Text}'.");
            }
            if (token.Reference)
            {
                return Resolve(token.Text);
            }
            if (bool.TryParse(token.Text, out var boolean))
            {
                return boolean;
            }
            if (double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }
            return token.Text;
        }

        private object? Resolve(string path)
        {
            if (_values.TryGetValue(path, out var direct))
            {
                return direct;
            }
            var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            object? current = _values;
            foreach (var part in parts)
            {
                if (current is IReadOnlyDictionary<string, object?> readOnly && readOnly.TryGetValue(part, out var readOnlyValue))
                {
                    current = readOnlyValue;
                }
                else if (current is IDictionary<string, object?> dictionary && dictionary.TryGetValue(part, out var value))
                {
                    current = value;
                }
                else
                {
                    throw new KeyNotFoundException($"Unknown graph expression reference '{path}'.");
                }
            }
            return current;
        }

        private bool Match(string text)
        {
            if (_position < _tokens.Count && _tokens[_position].Kind == TokenKind.Operator && _tokens[_position].Text == text)
            {
                _position++;
                return true;
            }
            return false;
        }

        private static object Compare(object? left, object? right, string op)
        {
            if (op is "==" or "!=")
            {
                var equal = Numeric(left, out var leftNumber) && Numeric(right, out var rightNumber)
                    ? leftNumber == rightNumber
                    : string.Equals(left?.ToString(), right?.ToString(), StringComparison.Ordinal);
                return op == "==" ? equal : !equal;
            }
            if (!Numeric(left, out var leftValue) || !Numeric(right, out var rightValue))
            {
                throw new FormatException($"Operator '{op}' requires numeric operands.");
            }
            return op switch
            {
                "<" => leftValue < rightValue,
                "<=" => leftValue <= rightValue,
                ">" => leftValue > rightValue,
                ">=" => leftValue >= rightValue,
                _ => throw new FormatException($"Unsupported comparison operator '{op}'."),
            };
        }

        private static bool Numeric(object? value, out double number)
            => double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
}
