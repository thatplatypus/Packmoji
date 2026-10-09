using System.Globalization;
using System.Text;

namespace Packmoji.Core.Diagnostics
{
    /// <summary>
    /// Makes text fit to print. A diagnostic repeats what a file said, the file may be someone else's,
    /// and the diagnostic goes to a terminal: so a character that could drive the terminal or disguise
    /// the text is shown as its number, and text of absurd length is cut.
    /// </summary>
    internal static class Printable
    {
        public const int MaxLength = 1000;

        private const string Cut = "...";

        public static string Text(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (text.Length <= MaxLength && !TextSafety.HasUnsafe(text))
            {
                return text;
            }

            var shown = new StringBuilder(Math.Min(text.Length, MaxLength) + Cut.Length);
            foreach (var rune in text.EnumerateRunes())
            {
                if (shown.Length >= MaxLength)
                {
                    shown.Append(Cut);
                    break;
                }

                if (TextSafety.IsUnsafe(rune))
                {
                    shown.Append('\\').Append("u{").Append(rune.Value.ToString("X4", CultureInfo.InvariantCulture)).Append('}');
                }
                else
                {
                    shown.Append(rune.ToString());
                }
            }

            return shown.ToString();
        }
    }
}
