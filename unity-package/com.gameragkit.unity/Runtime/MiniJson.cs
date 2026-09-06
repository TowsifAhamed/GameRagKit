using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameRagKit.Unity
{
    /// <summary>
    /// Minimal dependency-free JSON parser used only for the parts of GameRagKit's
    /// responses that Unity's built-in JsonUtility cannot handle: objects with dynamic
    /// keys (action args). Everything else in this package uses JsonUtility, which is
    /// faster and requires no extra code. Not a general-purpose JSON library -- supports
    /// exactly the JSON grammar (objects, arrays, strings, numbers, booleans, null) and
    /// nothing engine- or GameRagKit-specific beyond that.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            var index = 0;
            var value = ParseValue(json, ref index);
            SkipWhitespace(json, ref index);
            return value;
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length)
            {
                throw new MiniJsonException("Unexpected end of JSON input.");
            }

            switch (json[index])
            {
                case '{':
                    return ParseObject(json, ref index);
                case '[':
                    return ParseArray(json, ref index);
                case '"':
                    return ParseString(json, ref index);
                case 't':
                    Expect(json, ref index, "true");
                    return true;
                case 'f':
                    Expect(json, ref index, "false");
                    return false;
                case 'n':
                    Expect(json, ref index, "null");
                    return null;
                default:
                    return ParseNumber(json, ref index);
            }
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var result = new Dictionary<string, object>();
            index++; // consume '{'
            SkipWhitespace(json, ref index);

            if (Peek(json, index) == '}')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(json, ref index);
                var key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                ExpectChar(json, ref index, ':');
                var value = ParseValue(json, ref index);
                result[key] = value;

                SkipWhitespace(json, ref index);
                var next = Peek(json, index);
                if (next == ',')
                {
                    index++;
                    continue;
                }

                if (next == '}')
                {
                    index++;
                    break;
                }

                throw new MiniJsonException($"Expected ',' or '}}' at position {index}.");
            }

            return result;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var result = new List<object>();
            index++; // consume '['
            SkipWhitespace(json, ref index);

            if (Peek(json, index) == ']')
            {
                index++;
                return result;
            }

            while (true)
            {
                var value = ParseValue(json, ref index);
                result.Add(value);

                SkipWhitespace(json, ref index);
                var next = Peek(json, index);
                if (next == ',')
                {
                    index++;
                    continue;
                }

                if (next == ']')
                {
                    index++;
                    break;
                }

                throw new MiniJsonException($"Expected ',' or ']' at position {index}.");
            }

            return result;
        }

        private static string ParseString(string json, ref int index)
        {
            ExpectChar(json, ref index, '"');
            var builder = new StringBuilder();

            while (true)
            {
                if (index >= json.Length)
                {
                    throw new MiniJsonException("Unterminated string literal.");
                }

                var c = json[index++];
                if (c == '"')
                {
                    break;
                }

                if (c == '\\')
                {
                    if (index >= json.Length)
                    {
                        throw new MiniJsonException("Unterminated escape sequence.");
                    }

                    var escape = json[index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (index + 4 > json.Length)
                            {
                                throw new MiniJsonException("Invalid unicode escape sequence.");
                            }

                            var hex = json.Substring(index, 4);
                            index += 4;
                            builder.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            break;
                        default:
                            throw new MiniJsonException($"Invalid escape character '\\{escape}'.");
                    }

                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        private static double ParseNumber(string json, ref int index)
        {
            var start = index;
            while (index < json.Length && (char.IsDigit(json[index]) || json[index] is '-' or '+' or '.' or 'e' or 'E'))
            {
                index++;
            }

            var slice = json.Substring(start, index - start);
            if (slice.Length == 0)
            {
                throw new MiniJsonException($"Expected a value at position {index}.");
            }

            return double.Parse(slice, CultureInfo.InvariantCulture);
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }
        }

        private static char Peek(string json, int index)
        {
            return index < json.Length ? json[index] : '\0';
        }

        private static void ExpectChar(string json, ref int index, char expected)
        {
            if (index >= json.Length || json[index] != expected)
            {
                throw new MiniJsonException($"Expected '{expected}' at position {index}.");
            }

            index++;
        }

        private static void Expect(string json, ref int index, string literal)
        {
            if (index + literal.Length > json.Length || json.Substring(index, literal.Length) != literal)
            {
                throw new MiniJsonException($"Expected literal '{literal}' at position {index}.");
            }

            index += literal.Length;
        }
    }

    internal sealed class MiniJsonException : System.Exception
    {
        public MiniJsonException(string message) : base(message)
        {
        }
    }
}
