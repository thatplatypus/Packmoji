using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Versioning
{
    /// <summary>
    /// What a manifest asks of a dependency: a minimum version, on that version's compatibility line.
    /// There are no ranges and no operators, because resolution takes the highest minimum anyone asked
    /// for and nothing newer, which leaves an upper bound with nothing to do.
    /// </summary>
    public sealed record VersionRequirement
    {
        private const string Reason = "a requirement is a minimum version written as two or three numbers, such as 1.2 or 0.4.1, and there are no operators, ranges or wildcards";

        private VersionRequirement(string text, SemanticVersion minimum)
        {
            Text = text;
            Minimum = minimum;
        }

        /// <summary>The requirement as its author wrote it, so that a file can be written back unchanged.</summary>
        public string Text { get; }

        public SemanticVersion Minimum { get; }

        public CompatibilityLine Line => CompatibilityLine.Of(Minimum);

        public bool IsSatisfiedBy(SemanticVersion version)
        {
            ArgumentNullException.ThrowIfNull(version);
            return Line.Contains(version) && version >= Minimum;
        }

        /// <summary>
        /// Two requirements are equal when they ask for the same minimum, however they are spelled:
        /// <c>1.2</c> and <c>1.2.0</c> select the same versions.
        /// </summary>
        public bool Equals(VersionRequirement? other) => other is not null && Minimum == other.Minimum;

        public override int GetHashCode() => Minimum.GetHashCode();

        public override string ToString() => Text;

        public static bool TryParse(string text, [NotNullWhen(true)] out VersionRequirement? requirement, [NotNullWhen(false)] out Diagnostic? error)
        {
            ArgumentNullException.ThrowIfNull(text);
            requirement = null;
            error = null;

            var dash = text.IndexOf('-');
            var numbers = dash < 0 ? text : text[..dash];
            var dots = numbers.Count(c => c == '.');
            var twoNumbers = dots == 1 && dash < 0;

            if ((twoNumbers || dots == 2) && SemanticVersion.TryParse(twoNumbers ? text + ".0" : text, out var minimum, out _))
            {
                requirement = new VersionRequirement(text, minimum);
                return true;
            }

            error = new Diagnostic(DiagnosticCodes.RequirementInvalid, $"\"{text}\" is not a valid version requirement.", Reason, FixFor(text));
            return false;
        }

        private static string FixFor(string text)
        {
            var bare = text.TrimStart('^', '~', '>', '<', '=', 'v', 'V', ' ').TrimEnd(' ');
            if (bare != text && TryParse(bare, out _, out _))
            {
                return $"write \"{bare}\"";
            }

            if (bare.Length > 0 && bare.All(Ascii.IsDigit))
            {
                return $"write \"{bare}.0\"";
            }

            return "write a minimum such as \"1.2\" or \"0.4.1\"";
        }
    }
}
