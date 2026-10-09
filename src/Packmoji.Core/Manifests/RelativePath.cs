using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Manifests
{
    /// <summary>The path of one file or directory inside a package, with <c>/</c> between its parts.</summary>
    public sealed record RelativePath
    {
        private RelativePath(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public override string ToString() => Value;

        public static bool TryParse(string text, [NotNullWhen(true)] out RelativePath? path, [NotNullWhen(false)] out Diagnostic? error)
        {
            ArgumentNullException.ThrowIfNull(text);
            var problem = PathRules.Problem(text, glob: false);
            if (problem is null)
            {
                path = new RelativePath(text);
                error = null;
                return true;
            }

            var slashed = text.Replace('\\', '/');
            path = null;
            error = new Diagnostic(
                DiagnosticCodes.PathInvalid,
                $"\"{text}\" is not a valid path.",
                problem,
                slashed != text && PathRules.Problem(slashed, glob: false) is null
                    ? $"write \"{slashed}\""
                    : "write a path inside the package with / between its parts, such as \"src/lib.emojic\"");
            return false;
        }

        /// <summary>For a path written in Packmoji's own code, where a mistake is a bug and not bad input.</summary>
        internal static RelativePath Known(string value) =>
            TryParse(value, out var path, out _) ? path : throw new InvalidOperationException($"'{value}' is not a relative path.");
    }
}
