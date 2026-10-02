using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rebuild.Analyzers
{
    /// <summary>
    /// Tiny JSON reader for the data generator (System.Text.Json cannot be loaded inside the compiler
    /// on every host). Numbers must be integers: game data is integer-only (ADR 0002).
    /// Objects keep key order; values are Dictionary/List/string/long/bool/null.
    /// </summary>
    internal sealed class MiniJson
    {
        private readonly string _s;
        private int _i;

        private MiniJson(string s) { _s = s; }

        public static object? Parse(string text)
        {
            var p = new MiniJson(text);
            p.SkipWs();
            var v = p.ReadValue();
            p.SkipWs();
            if (p._i != p._s.Length) throw p.Error("trailing characters");
            return v;
        }

        private FormatException Error(string msg)
        {
            int line = 1;
            for (int k = 0; k < _i && k < _s.Length; k++) if (_s[k] == '\n') line++;
            return new FormatException("line " + line + ": " + msg);
        }

        private void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }

        private object? ReadValue()
        {
            if (_i >= _s.Length) throw Error("unexpected end");
            char c = _s[_i];
            if (c == '{') return ReadObject();
            if (c == '[') return ReadArray();
            if (c == '"') return ReadString();
            if (c == '-' || (c >= '0' && c <= '9')) return ReadInteger();
            if (Match("true")) return true;
            if (Match("false")) return false;
            if (Match("null")) return null;
            throw Error("unexpected '" + c + "'");
        }

        private bool Match(string word)
        {
            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) return false;
            _i += word.Length;
            return true;
        }

        private List<KeyValuePair<string, object?>> ReadObject()
        {
            var result = new List<KeyValuePair<string, object?>>();
            _i++;
            SkipWs();
            if (_s[_i] == '}') { _i++; return result; }
            while (true)
            {
                SkipWs();
                if (_s[_i] != '"') throw Error("expected key");
                string key = ReadString();
                SkipWs();
                if (_s[_i] != ':') throw Error("expected ':'");
                _i++;
                SkipWs();
                foreach (var kv in result)
                    if (kv.Key == key) throw Error("duplicate key '" + key + "'");
                result.Add(new KeyValuePair<string, object?>(key, ReadValue()));
                SkipWs();
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == '}') { _i++; return result; }
                throw Error("expected ',' or '}'");
            }
        }

        private List<object?> ReadArray()
        {
            var result = new List<object?>();
            _i++;
            SkipWs();
            if (_s[_i] == ']') { _i++; return result; }
            while (true)
            {
                SkipWs();
                result.Add(ReadValue());
                SkipWs();
                if (_s[_i] == ',') { _i++; continue; }
                if (_s[_i] == ']') { _i++; return result; }
                throw Error("expected ',' or ']'");
            }
        }

        private string ReadString()
        {
            var sb = new StringBuilder();
            _i++;
            while (_i < _s.Length)
            {
                char c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = _s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'u':
                        sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _i += 4;
                        break;
                    default: throw Error("bad escape");
                }
            }
            throw Error("unterminated string");
        }

        private long ReadInteger()
        {
            int start = _i;
            if (_s[_i] == '-') _i++;
            while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') _i++;
            if (_i < _s.Length && (_s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E'))
                throw Error("fractional numbers are not allowed in game data; use integer percent or parts-per-65536");
            return long.Parse(_s.Substring(start, _i - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
    }
}
