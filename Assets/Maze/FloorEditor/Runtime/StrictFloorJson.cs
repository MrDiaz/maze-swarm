using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Maze.FloorEditing
{
    // JsonUtility ignores unknown fields and silently defaults missing values. This small reader enforces
    // the external document contract BEFORE JsonUtility constructs the familiar serializable model.
    internal sealed class StrictFloorJson
    {
        readonly string text;
        int at;
        StrictFloorJson(string source) { text = source; }
        static readonly HashSet<string> RootFields = new HashSet<string> { "schemaVersion", "layoutId", "title", "rows", "objects", "sectorId", "floorNumber" };
        static readonly HashSet<string> ObjectFields = new HashSet<string> { "id", "assetId", "name", "x", "y", "z", "yaw", "scale", "footprintWidth", "footprintDepth", "maxHeight", "collision", "preserveBehavior", "legacyFit" };
        static readonly HashSet<string> ObjectNumbers = new HashSet<string> { "x", "y", "z", "yaw", "scale", "footprintWidth", "footprintDepth", "maxHeight" };
        public static void Check(string source)
        {
            var reader = new StrictFloorJson(source); reader.Root(); reader.White();
            if (reader.at != source.Length) reader.Fail("Unexpected trailing content.");
        }
        void Root()
        {
            var seen = Keys(RootFields, field => {
                switch (field)
                {
                    case "schemaVersion": case "floorNumber": Number(true); break;
                    case "sectorId": if (!Null()) String(); break;
                    case "rows": Array(() => String()); break;
                    case "objects": Array(() => Object()); break;
                    default: String(); break;
                }
            });
            foreach (var field in new[] { "schemaVersion", "layoutId", "title", "rows", "objects" }) if (!seen.Contains(field)) Fail("Missing required field '" + field + "'.");
        }
        void Object()
        {
            var seen = Keys(ObjectFields, field => { if (ObjectNumbers.Contains(field)) Number(false); else if ((field == "preserveBehavior" || field == "legacyFit")) Boolean(); else String(); });
            foreach (var field in ObjectFields) if (!seen.Contains(field)) Fail("Placed object missing field '" + field + "'.");
        }
        HashSet<string> Keys(HashSet<string> allowed, Action<string> value)
        {
            Eat('{'); var seen = new HashSet<string>(); White(); if (Take('}')) return seen;
            while (true)
            {
                string field = String();
                if (!allowed.Contains(field)) Fail("Unknown field '" + field + "'.");
                if (!seen.Add(field)) Fail("Duplicate field '" + field + "'.");
                Eat(':'); value(field); if (Take('}')) return seen; Eat(',');
            }
        }
        void Array(Action value)
        {
            Eat('['); if (Take(']')) return; int count = 0;
            while (true)
            {
                if (++count > 65536) Fail("Array exceeds safety limit.");
                value(); if (Take(']')) return; Eat(',');
            }
        }
        string String()
        {
            Eat('"'); var result = new StringBuilder();
            while (at < text.Length)
            {
                char c = text[at++]; if (c == '"') return result.ToString();
                if (c < 32) Fail("Unescaped control character in string.");
                if (c != '\\') { result.Append(c); continue; }
                if (at >= text.Length) Fail("Unfinished escape.");
                c = text[at++];
                switch (c)
                {
                    case '"': case '\\': case '/': result.Append(c); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        ushort code = 0;
                        if (at + 4 > text.Length || !ushort.TryParse(text.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) Fail("Invalid unicode escape.");
                        result.Append((char)code); at += 4; break;
                    default: Fail("Invalid escape."); break;
                }
            }
            Fail("Unterminated string."); return null;
        }
        void Boolean() { White(); if (at + 4 <= text.Length && text.Substring(at, 4) == "true") { at += 4; return; } if (at + 5 <= text.Length && text.Substring(at, 5) == "false") { at += 5; return; } Fail("Expected boolean."); }
        bool Null() { White(); if (at + 4 <= text.Length && text.Substring(at, 4) == "null") { at += 4; return true; } return false; }
        void Number(bool integer)
        {
            White(); int start = at; if (at < text.Length && text[at] == '-') at++;
            if (at >= text.Length) Fail("Expected number.");
            if (text[at] == '0') at++;
            else
            {
                if (text[at] < '1' || text[at] > '9') Fail("Expected JSON number.");
                while (at < text.Length && text[at] >= '0' && text[at] <= '9') at++;
            }
            bool fraction = false;
            if (at < text.Length && text[at] == '.')
            {
                fraction = true; at++; int digits = at;
                while (at < text.Length && text[at] >= '0' && text[at] <= '9') at++;
                if (at == digits) Fail("Fraction requires digits.");
            }
            if (at < text.Length && (text[at] == 'e' || text[at] == 'E'))
            {
                fraction = true; at++; if (at < text.Length && (text[at] == '-' || text[at] == '+')) at++;
                int digits = at; while (at < text.Length && text[at] >= '0' && text[at] <= '9') at++;
                if (at == digits) Fail("Exponent requires digits.");
            }
            string number = text.Substring(start, at - start);
            if (integer)
            { if (fraction || !int.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)) Fail("Expected integer within Int32 range."); }
            else if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) || float.IsInfinity(n) || float.IsNaN(n)) Fail("Number must be finite and within Single range.");
        }
        void White() { while (at < text.Length && (text[at] == ' ' || text[at] == '\t' || text[at] == '\r' || text[at] == '\n')) at++; }
        bool Take(char c) { White(); if (at < text.Length && text[at] == c) { at++; return true; } return false; }
        void Eat(char c) { if (!Take(c)) Fail("Expected '" + c + "'."); }
        void Fail(string message) { throw new FormatException(message + " At character " + at + "."); }
    }
}
