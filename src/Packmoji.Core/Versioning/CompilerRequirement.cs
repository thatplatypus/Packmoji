using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Versioning
{
    /// <summary>
    /// The oldest Emojicode compiler a package builds with, written <c>&gt;=version</c>. Unlike a
    /// dependency requirement it has no compatibility line: a package cannot know which later compiler
    /// will break it, so there is no upper bound to state.
    /// </summary>
    public sealed record CompilerRequirement
    {
        private const string Operator = ">=";

        private CompilerRequirement(SemanticVersion minimum)
        {
            Minimum = minimum;
        }

        public SemanticVersion Minimum { get; }

        public bool IsSatisfiedBy(SemanticVersion compiler)
        {
            ArgumentNullException.ThrowIfNull(compiler);
            return compiler >= Minimum;
        }

        public override string ToString() => $"{Operator}{Minimum}";

        public static bool TryParse(string text, [NotNullWhen(true)] out CompilerRequirement? requirement, [NotNullWhen(false)] out Diagnostic? error)
        {
            ArgumentNullException.ThrowIfNull(text);
            requirement = null;
            error = null;

            if (text.StartsWith(Operator, StringComparison.Ordinal) && SemanticVersion.TryParse(text[Operator.Length..], out var minimum, out _))
            {
                requirement = new CompilerRequirement(minimum);
                return true;
            }

            var bare = text.Replace(" ", "", StringComparison.Ordinal).TrimStart('>', '=', '^', '~', 'v', 'V');
            var fix = SemanticVersion.TryParse(bare, out var intended, out _)
                ? $"write \"{Operator}{intended}\""
                : "write it as \">=1.0.0-beta.2\"";
            error = new Diagnostic(
                DiagnosticCodes.CompilerInvalid,
                $"\"{text}\" is not a valid compiler requirement.",
                "the emojicode key is >= followed by a full version, with no spaces: the oldest compiler the package builds with",
                fix);
            return false;
        }
    }
}
