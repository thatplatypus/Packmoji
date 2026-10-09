using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Manifests
{
    /// <summary>The rules for the manifest's strings that are too small to deserve a type of their own.</summary>
    internal static class ManifestRules
    {
        public const int MaxDescriptionLength = 200;
        public const int MaxLinkNameLength = 64;

        /// <summary>The suffixes of an Emojicode source file, in the order the entry convention tries them. The compiler accepts these two and no other.</summary>
        public static IReadOnlyList<string> SourceSuffixes { get; } = [".emojic", ".🍇"];

        /// <summary>Whether a path names an Emojicode source file. A name that is a suffix and nothing more is not one: the compiler refuses it.</summary>
        public static bool IsSourceFile(string path)
        {
            var name = path[(path.LastIndexOf('/') + 1)..];
            return SourceSuffixes.Any(suffix => name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal));
        }

        public static Diagnostic? CheckDescription(string text)
        {
            string? problem = null;
            if (text.Length == 0)
            {
                problem = "it is empty";
            }
            else if (text.EnumerateRunes().Count() > MaxDescriptionLength)
            {
                problem = $"it is longer than {MaxDescriptionLength} characters";
            }
            else if (text.Any(char.IsControl))
            {
                problem = "it holds a line break or another control character";
            }
            else if (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]))
            {
                problem = "it begins or ends with a space";
            }

            return problem is null
                ? null
                : new Diagnostic(
                    DiagnosticCodes.DescriptionInvalid,
                    "The description is not valid.",
                    problem,
                    $"write one line of at most {MaxDescriptionLength} characters, or remove \"description\"");
        }

        public static Diagnostic? CheckLinkName(string text)
        {
            var valid = text.Length is > 0 and <= MaxLinkNameLength
                && text[0] != '-'
                && text[0] != '+'
                && text[0] != '.'
                && text.All(c => Ascii.IsLetter(c) || Ascii.IsDigit(c) || c is '_' or '+' or '.' or '-');

            return valid
                ? null
                : new Diagnostic(
                    DiagnosticCodes.LinkInvalid,
                    $"\"{text}\" is not a library name.",
                    "a library to link is named with letters, digits and _ + . - only, beginning with a letter, a digit or _, so that a manifest cannot hand flags or paths to the linker",
                    "name the library as the linker's -l option would, such as \"pthread\"");
        }
    }
}
