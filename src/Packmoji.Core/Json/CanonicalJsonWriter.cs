using System.Globalization;
using System.Text;

namespace Packmoji.Core.Json
{
    /// <summary>
    /// Writes the one layout Packmoji gives its files: two spaces, LF, every property and array entry
    /// on a line of its own, and a string escaped only where JSON demands it. System.Text.Json's writer
    /// escapes every emoji whichever encoder it is given, and a manifest whose entry file is
    /// <c>lib.🍇</c> should say so in a way a person can read. The method names are that writer's.
    /// </summary>
    internal sealed class CanonicalJsonWriter
    {
        private readonly StringBuilder _text = new();

        // How many entries each open object or array has so far, innermost on top.
        private readonly Stack<int> _entries = new();
        private bool _afterName;

        public void WriteStartObject() => Begin('{');

        public void WriteStartObject(string name)
        {
            WritePropertyName(name);
            WriteStartObject();
        }

        public void WriteEndObject() => End('}');

        public void WriteStartArray() => Begin('[');

        public void WriteStartArray(string name)
        {
            WritePropertyName(name);
            WriteStartArray();
        }

        public void WriteEndArray() => End(']');

        public void WritePropertyName(string name)
        {
            StartEntry();
            AppendString(name);
            _text.Append(": ");
            _afterName = true;
        }

        public void WriteStringValue(string value)
        {
            StartValue();
            AppendString(value);
        }

        public void WriteString(string name, string value)
        {
            WritePropertyName(name);
            WriteStringValue(value);
        }

        public void WriteBoolean(string name, bool value)
        {
            WritePropertyName(name);
            StartValue();
            _text.Append(value ? "true" : "false");
        }

        public void WriteNumber(string name, int value)
        {
            WritePropertyName(name);
            StartValue();
            _text.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void WriteStrings(string name, IEnumerable<string> values)
        {
            WriteStartArray(name);
            foreach (var value in values)
            {
                WriteStringValue(value);
            }

            WriteEndArray();
        }

        /// <summary>The finished text, ending in the one newline a text file ends with.</summary>
        public override string ToString() => _text + "\n";

        private void Begin(char open)
        {
            StartValue();
            _text.Append(open);
            _entries.Push(0);
        }

        private void End(char close)
        {
            if (_entries.Pop() > 0)
            {
                NewLine();
            }

            _text.Append(close);
        }

        // A value is the root, or the value of the name just written, or the next entry of an array.
        private void StartValue()
        {
            if (_afterName)
            {
                _afterName = false;
            }
            else if (_entries.Count > 0)
            {
                StartEntry();
            }
        }

        private void StartEntry()
        {
            var count = _entries.Pop();
            if (count > 0)
            {
                _text.Append(',');
            }

            _entries.Push(count + 1);
            NewLine();
        }

        private void NewLine() => _text.Append('\n').Append(' ', _entries.Count * 2);

        private void AppendString(string value)
        {
            _text.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c is '"' or '\\')
                {
                    _text.Append('\\').Append(c);
                }
                else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    _text.Append(c).Append(value[++i]);
                }
                else if (c < ' ' || char.IsSurrogate(c))
                {
                    // JSON requires a control character to be escaped. Half of a surrogate pair is not
                    // a character at all and could not be written as UTF-8, so it is escaped as well.
                    _text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    _text.Append(c);
                }
            }

            _text.Append('"');
        }
    }
}
