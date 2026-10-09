using System.Globalization;
using System.Text;

namespace Packmoji.Core
{
    /// <summary>
    /// Says which characters must not be taken from a file as they are. A control character can drive a
    /// terminal, a line separator turns one line into two, and a character that reverses the direction
    /// of text, or that cannot be seen at all, makes a name read as something it is not.
    /// </summary>
    internal static class TextSafety
    {
        private const int ZeroWidthJoiner = 0x200D;
        private const int FirstTag = 0xE0020;
        private const int LastTag = 0xE007F;

        public static bool IsUnsafe(Rune rune) => Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,

            // Emoji are built with two kinds of format character: the joiner between the parts of a
            // sequence, and the tags that spell out a flag. Emojicode's files are named in emoji, so
            // those stay. Every other format character is invisible to no good purpose.
            UnicodeCategory.Format => rune.Value != ZeroWidthJoiner && rune.Value is < FirstTag or > LastTag,

            _ => false,
        };

        public static bool HasUnsafe(string text)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                if (IsUnsafe(rune))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
