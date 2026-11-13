using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Lbr.CadTools.Core
{
    /// <summary>Thrown for malformed JSON; the message includes the line and column.</summary>
    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Minimal JSON for the settings file, so the add-in needs no extra
    /// assemblies inside the SolidWorks process.
    ///
    /// Values map to: object -> Dictionary&lt;string, object&gt; (key order kept),
    /// array -> List&lt;object&gt;, string, double, bool, null. Comments are not
    /// standard JSON but "//" line comments are accepted so the file can explain itself.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            var parser = new Parser(text ?? "");
            parser.SkipWhitespace();
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
            {
                throw parser.Error("Unexpected text after the end of the JSON value");
            }
            return value;
        }

        /// <summary>Writes a value with 2-space indentation and LF line endings.</summary>
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, int indent)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case IEnumerable<KeyValuePair<string, object>> obj:
                {
                    List<KeyValuePair<string, object>> pairs = obj.ToList();
                    if (pairs.Count == 0)
                    {
                        sb.Append("{}");
                        break;
                    }
                    sb.Append("{\n");
                    for (int n = 0; n < pairs.Count; n++)
                    {
                        sb.Append(' ', (indent + 1) * 2);
                        WriteString(sb, pairs[n].Key);
                        sb.Append(": ");
                        WriteValue(sb, pairs[n].Value, indent + 1);
                        sb.Append(n < pairs.Count - 1 ? ",\n" : "\n");
                    }
                    sb.Append(' ', indent * 2).Append('}');
                    break;
                }
                case System.Collections.IEnumerable list:
                {
                    List<object> items = list.Cast<object>().ToList();
                    if (items.Count == 0)
                    {
                        sb.Append("[]");
                        break;
                    }
                    sb.Append("[\n");
                    for (int n = 0; n < items.Count; n++)
                    {
                        sb.Append(' ', (indent + 1) * 2);
                        WriteValue(sb, items[n], indent + 1);
                        sb.Append(n < items.Count - 1 ? ",\n" : "\n");
                    }
                    sb.Append(' ', indent * 2).Append(']');
                    break;
                }
                default:
                    throw new ArgumentException($"Cannot write {value.GetType().Name} as JSON");
            }
        }

        /// <summary>Strings are written as pure ASCII; anything else becomes \uXXXX.</summary>
        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c > 0x7E)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string text;
            private int pos;

            public Parser(string text)
            {
                this.text = text;
            }

            public bool AtEnd => pos >= text.Length;

            public JsonException Error(string message)
            {
                int line = 1, column = 1;
                for (int i = 0; i < pos && i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                }
                return new JsonException($"{message} (line {line}, column {column}).");
            }

            public void SkipWhitespace()
            {
                while (pos < text.Length)
                {
                    char c = text[pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF')
                    {
                        pos++;
                    }
                    else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
                    {
                        while (pos < text.Length && text[pos] != '\n')
                        {
                            pos++;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public object ParseValue()
            {
                if (AtEnd)
                {
                    throw Error("Unexpected end of file");
                }
                char c = text[pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ParseNumber();
                        }
                        throw Error($"Unexpected character '{c}'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0)
                {
                    throw Error($"Expected '{word}'");
                }
                pos += word.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>();
                pos++; // {
                SkipWhitespace();
                if (!AtEnd && text[pos] == '}')
                {
                    pos++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[pos] != '"')
                    {
                        throw Error("Expected a quoted property name");
                    }
                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || text[pos] != ':')
                    {
                        throw Error($"Expected ':' after \"{key}\"");
                    }
                    pos++;
                    SkipWhitespace();
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw Error("Unexpected end of file, missing '}'");
                    }
                    if (text[pos] == ',')
                    {
                        pos++;
                        continue;
                    }
                    if (text[pos] == '}')
                    {
                        pos++;
                        return result;
                    }
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                pos++; // [
                SkipWhitespace();
                if (!AtEnd && text[pos] == ']')
                {
                    pos++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw Error("Unexpected end of file, missing ']'");
                    }
                    if (text[pos] == ',')
                    {
                        pos++;
                        continue;
                    }
                    if (text[pos] == ']')
                    {
                        pos++;
                        return result;
                    }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                pos++; // opening quote
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Error("Unterminated string");
                    }
                    char c = text[pos++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }
                    if (c == '\n')
                    {
                        pos--;
                        throw Error("Unterminated string");
                    }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd)
                    {
                        throw Error("Unterminated string");
                    }
                    char e = text[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > text.Length || !int.TryParse(text.Substring(pos, 4), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out int code))
                            {
                                throw Error("Bad \\u escape");
                            }
                            sb.Append((char)code);
                            pos += 4;
                            break;
                        default:
                            pos--;
                            throw Error($"Bad escape '\\{e}' (in Windows paths, write \\\\ for each \\)");
                    }
                }
            }

            private double ParseNumber()
            {
                int start = pos;
                while (pos < text.Length && "+-0123456789.eE".IndexOf(text[pos]) >= 0)
                {
                    pos++;
                }
                string s = text.Substring(start, pos - start);
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    pos = start;
                    throw Error($"Bad number '{s}'");
                }
                return value;
            }
        }
    }
}
