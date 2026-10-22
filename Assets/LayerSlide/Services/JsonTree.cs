using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LayerSlide.Services
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>Parsed JSON value. Used only to check save files before JsonUtility sees them.</summary>
    public sealed class JsonNode
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        public string String;
        public readonly List<JsonNode> Items = new List<JsonNode>();
        public readonly Dictionary<string, JsonNode> Fields = new Dictionary<string, JsonNode>();

        public bool IsInteger => Kind == JsonKind.Number && Number == System.Math.Floor(Number)
            && Number >= long.MinValue && Number <= long.MaxValue;

        public JsonNode Get(string field) => Fields.TryGetValue(field, out var node) ? node : null;
    }

    /// <summary>Small strict JSON parser (RFC 8259 subset: no comments, no trailing commas, no duplicate keys).</summary>
    public static class JsonTree
    {
        public static bool TryParse(string text, out JsonNode root)
        {
            root = null;
            if (text == null) return false;
            int index = 0;
            if (!TryValue(text, ref index, out root, 0)) return false;
            SkipWhitespace(text, ref index);
            return index == text.Length;
        }

        const int MaxDepth = 64;

        static bool TryValue(string s, ref int i, out JsonNode node, int depth)
        {
            node = null;
            if (depth > MaxDepth) return false;
            SkipWhitespace(s, ref i);
            if (i >= s.Length) return false;

            switch (s[i])
            {
                case '{': return TryObject(s, ref i, out node, depth);
                case '[': return TryArray(s, ref i, out node, depth);
                case '"':
                    if (!TryString(s, ref i, out string value)) return false;
                    node = new JsonNode { Kind = JsonKind.String, String = value };
                    return true;
                case 't': return TryLiteral(s, ref i, "true", new JsonNode { Kind = JsonKind.Bool, Bool = true }, out node);
                case 'f': return TryLiteral(s, ref i, "false", new JsonNode { Kind = JsonKind.Bool, Bool = false }, out node);
                case 'n': return TryLiteral(s, ref i, "null", new JsonNode { Kind = JsonKind.Null }, out node);
                default: return TryNumber(s, ref i, out node);
            }
        }

        static bool TryObject(string s, ref int i, out JsonNode node, int depth)
        {
            node = new JsonNode { Kind = JsonKind.Object };
            i++;
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return true; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"' || !TryString(s, ref i, out string key)) return false;
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') return false;
                i++;
                if (!TryValue(s, ref i, out var value, depth + 1) || node.Fields.ContainsKey(key)) return false;
                node.Fields.Add(key, value);
                SkipWhitespace(s, ref i);
                if (i >= s.Length) return false;
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return true; }
                return false;
            }
        }

        static bool TryArray(string s, ref int i, out JsonNode node, int depth)
        {
            node = new JsonNode { Kind = JsonKind.Array };
            i++;
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return true; }

            while (true)
            {
                if (!TryValue(s, ref i, out var item, depth + 1)) return false;
                node.Items.Add(item);
                SkipWhitespace(s, ref i);
                if (i >= s.Length) return false;
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return true; }
                return false;
            }
        }

        static bool TryString(string s, ref int i, out string value)
        {
            value = null;
            var builder = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') { value = builder.ToString(); return true; }
                if (c < 0x20) return false;
                if (c != '\\') { builder.Append(c); continue; }
                if (i >= s.Length) return false;

                char escape = s[i++];
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
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            return false;
                        builder.Append((char)code);
                        i += 4;
                        break;
                    default: return false;
                }
            }
            return false;
        }

        static bool TryNumber(string s, ref int i, out JsonNode node)
        {
            node = null;
            int start = i;
            if (i < s.Length && s[i] == '-') i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
                i++;
            if (i == start) return false;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return false;
            node = new JsonNode { Kind = JsonKind.Number, Number = value };
            return true;
        }

        static bool TryLiteral(string s, ref int i, string literal, JsonNode value, out JsonNode node)
        {
            node = null;
            if (string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0) return false;
            i += literal.Length;
            node = value;
            return true;
        }

        static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }
    }
}
