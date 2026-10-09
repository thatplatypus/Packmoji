using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Manifests
{
    /// <summary>
    /// A pattern that selects files inside a package. It has <c>*</c>, <c>?</c> and <c>**</c> and
    /// nothing else, so that what a pattern selects is the same on every platform and in every tool.
    /// </summary>
    public sealed record GlobPattern
    {
        private GlobPattern(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public override string ToString() => Value;

        public static bool TryParse(string text, [NotNullWhen(true)] out GlobPattern? pattern, [NotNullWhen(false)] out Diagnostic? error)
        {
            ArgumentNullException.ThrowIfNull(text);
            var problem = PathRules.Problem(text, glob: true);
            if (problem is null)
            {
                pattern = new GlobPattern(text);
                error = null;
                return true;
            }

            var slashed = text.Replace('\\', '/');
            pattern = null;
            error = new Diagnostic(
                DiagnosticCodes.GlobInvalid,
                $"\"{text}\" is not a valid pattern.",
                problem,
                slashed != text && PathRules.Problem(slashed, glob: true) is null
                    ? $"write \"{slashed}\""
                    : "write a pattern inside the package such as \"src/**/*.emojic\"");
            return false;
        }

        /// <summary>For a pattern written in Packmoji's own code, where a mistake is a bug and not bad input.</summary>
        internal static GlobPattern Known(string value) =>
            TryParse(value, out var pattern, out _) ? pattern : throw new InvalidOperationException($"'{value}' is not a glob pattern.");
    }
}
