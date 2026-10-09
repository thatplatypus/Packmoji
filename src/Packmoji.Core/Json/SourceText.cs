using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Json
{
    /// <summary>
    /// The bytes of one file, and the line and column of any byte in it. A column counts Unicode code
    /// points, not bytes and not UTF-16 units, so the column of something after an emoji is the one an
    /// editor shows.
    /// </summary>
    internal sealed class SourceText
    {
        private readonly ReadOnlyMemory<byte> _utf8;
        private readonly List<int> _lineStarts = [0];

        // Where the last lookup ended. The reader asks for places in rising order, so the next place is
        // nearly always further along the same line, and only the bytes between the two are counted.
        // Counting from the start of the line each time is quadratic in the length of the line: a file
        // of a few hundred kilobytes on one line would take a minute to read.
        private int _lastLine = -1;
        private int _lastOffset;
        private int _lastCodePoints;

        public SourceText(ReadOnlyMemory<byte> utf8, string file)
        {
            _utf8 = utf8;
            File = file;
            var bytes = utf8.Span;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\n')
                {
                    _lineStarts.Add(i + 1);
                }
            }
        }

        public string File { get; }

        public ReadOnlySpan<byte> Bytes => _utf8.Span;

        public SourceLocation Locate(long offset)
        {
            var target = (int)Math.Clamp(offset, 0L, _utf8.Length);
            var line = _lineStarts.BinarySearch(target);
            if (line < 0)
            {
                line = ~line - 1;
            }

            var codePoints = line == _lastLine && target >= _lastOffset
                ? _lastCodePoints + CodePoints(_lastOffset, target)
                : CodePoints(_lineStarts[line], target);

            _lastLine = line;
            _lastOffset = target;
            _lastCodePoints = codePoints;
            return new SourceLocation(File, line + 1, codePoints + 1);
        }

        /// <summary>The place of a line and a byte within it, both counted from zero, which is how the JSON reader reports an error.</summary>
        public SourceLocation Locate(long line, long byteInLine)
        {
            var start = _lineStarts[(int)Math.Clamp(line, 0L, _lineStarts.Count - 1)];
            return Locate(start + byteInLine);
        }

        public bool HasLineStartingWith(ReadOnlySpan<byte> prefix)
        {
            foreach (var start in _lineStarts)
            {
                if (Bytes[start..].StartsWith(prefix))
                {
                    return true;
                }
            }

            return false;
        }

        private int CodePoints(int from, int to)
        {
            var bytes = _utf8.Span;
            var count = 0;
            for (var i = from; i < to; i++)
            {
                // A byte of the form 10xxxxxx continues a code point. Every other byte begins one.
                if ((bytes[i] & 0xC0) != 0x80)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
