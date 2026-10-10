using Packmoji.Core.Versioning;

namespace Packmoji.Core.Building
{
    /// <summary>
    /// Reads the compiler's version out of its help text, which is the only place the compiler says
    /// it: <c>--version</c> is answered with "Flag could not be matched". The released compiler says
    /// "Emojicode Compiler 1.0 beta 2.", which is the version <c>1.0.0-beta.2</c>.
    /// </summary>
    public static class CompilerBanner
    {
        private const string Lead = "Emojicode Compiler ";

        /// <summary>
        /// The version a help text gives, or null when it has no banner of the one form that is
        /// understood: two or three numbers, and after them perhaps a word and a number, as in
        /// "1.0 beta 2". A banner of any other form is not guessed at.
        /// </summary>
        public static SemanticVersion? Version(string help)
        {
            ArgumentNullException.ThrowIfNull(help);
            var at = help.IndexOf(Lead, StringComparison.Ordinal);
            if (at < 0)
            {
                return null;
            }

            var line = help[(at + Lead.Length)..];
            var lineEnd = line.IndexOfAny(['\r', '\n']);
            line = lineEnd < 0 ? line : line[..lineEnd];

            // The banner is a sentence, with more after it or with nothing.
            var sentenceEnd = line.IndexOf(". ", StringComparison.Ordinal);
            var said = sentenceEnd >= 0 ? line[..sentenceEnd] : line.EndsWith('.') ? line[..^1] : line;

            var words = said.Split(' ');
            if (words.Length is not (1 or 3))
            {
                return null;
            }

            // Two numbers are a version whose third is 0. Whether what results is a version at all is the version's own to say.
            var text = words[0].Split('.').Length == 2 ? words[0] + ".0" : words[0];
            if (words.Length == 3)
            {
                if (words[1].Length == 0 || !words[1].All(Ascii.IsLowerLetter) || words[2].Length == 0 || !words[2].All(Ascii.IsDigit))
                {
                    return null;
                }

                text += $"-{words[1]}.{words[2]}";
            }

            return SemanticVersion.TryParse(text, out var version, out _) ? version : null;
        }
    }
}
