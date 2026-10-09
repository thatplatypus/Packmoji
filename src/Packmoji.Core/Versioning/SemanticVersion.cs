using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Versioning
{
    /// <summary>
    /// A package version: SemVer 2.0.0 without build metadata. Two versions that differ only in build
    /// metadata have equal precedence, and a registry of immutable versions has no way to order them,
    /// so a package version never carries any.
    /// </summary>
    public sealed record SemanticVersion : IComparable<SemanticVersion>
    {
        public const int MaxLength = 64;

        private const string Shape = "a version is three numbers separated by dots, such as 1.2.3, with an optional pre-release such as -beta.1";
        private const string GenericFix = "write a version such as \"1.2.3\" or \"1.0.0-beta.1\"";

        private SemanticVersion(int major, int minor, int patch, string prerelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
        }

        public int Major { get; }

        public int Minor { get; }

        public int Patch { get; }

        /// <summary>The pre-release identifiers joined by dots. Empty for a release.</summary>
        public string Prerelease { get; }

        public bool IsPrerelease => Prerelease.Length > 0;

        public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

        public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

        public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

        public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

        public override string ToString()
        {
            var numbers = string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
            return IsPrerelease ? $"{numbers}-{Prerelease}" : numbers;
        }

        public int CompareTo(SemanticVersion? other)
        {
            if (other is null)
            {
                return 1;
            }

            var byNumbers = Major != other.Major ? Major.CompareTo(other.Major)
                : Minor != other.Minor ? Minor.CompareTo(other.Minor)
                : Patch.CompareTo(other.Patch);
            if (byNumbers != 0)
            {
                return byNumbers;
            }

            if (IsPrerelease != other.IsPrerelease)
            {
                return IsPrerelease ? -1 : 1;
            }

            return ComparePrerelease(Prerelease, other.Prerelease);
        }

        public static bool TryParse(string text, [NotNullWhen(true)] out SemanticVersion? version, [NotNullWhen(false)] out Diagnostic? error)
        {
            ArgumentNullException.ThrowIfNull(text);
            version = null;
            error = null;

            if (text.Length > MaxLength)
            {
                error = Invalid(text, $"a version is at most {MaxLength} characters", GenericFix);
                return false;
            }

            var plus = text.IndexOf('+');
            if (plus >= 0)
            {
                var withoutBuild = text[..plus];
                error = TryParseParts(withoutBuild, out _, out _)
                    ? new Diagnostic(
                        DiagnosticCodes.VersionBuildMetadata,
                        $"\"{text}\" carries build metadata.",
                        "two versions that differ only after the + have equal precedence, so a package version never has that part",
                        $"write \"{withoutBuild}\"")
                    : Invalid(text, Shape, GenericFix);
                return false;
            }

            if (!TryParseParts(text, out version, out var reason))
            {
                error = Invalid(text, reason, FixFor(text));
                return false;
            }

            return true;
        }

        private static bool TryParseParts(string text, [NotNullWhen(true)] out SemanticVersion? version, out string reason)
        {
            version = null;
            var dash = text.IndexOf('-');
            var numbers = (dash < 0 ? text : text[..dash]).Split('.');
            var prerelease = dash < 0 ? "" : text[(dash + 1)..];

            if (numbers.Length != 3)
            {
                reason = Shape;
                return false;
            }

            if (!TryParseNumber(numbers[0], out var major, out reason)
                || !TryParseNumber(numbers[1], out var minor, out reason)
                || !TryParseNumber(numbers[2], out var patch, out reason))
            {
                return false;
            }

            if (dash >= 0 && !IsPrereleaseValid(prerelease, out reason))
            {
                return false;
            }

            version = new SemanticVersion(major, minor, patch, prerelease);
            return true;
        }

        private static bool TryParseNumber(string text, out int value, out string reason)
        {
            value = 0;
            if (text.Length == 0 || !text.All(Ascii.IsDigit))
            {
                reason = Shape;
                return false;
            }

            if (text.Length > 1 && text[0] == '0')
            {
                reason = "a number in a version has no leading zeros";
                return false;
            }

            // An int has at most ten digits, so anything this short fits a long without overflow.
            if (text.Length > 10 || long.Parse(text, CultureInfo.InvariantCulture) > int.MaxValue)
            {
                reason = "a number in a version is at most 2147483647";
                return false;
            }

            value = int.Parse(text, CultureInfo.InvariantCulture);
            reason = "";
            return true;
        }

        private static bool IsPrereleaseValid(string prerelease, out string reason)
        {
            foreach (var identifier in prerelease.Split('.'))
            {
                if (identifier.Length == 0 || !identifier.All(c => Ascii.IsLetter(c) || Ascii.IsDigit(c) || c == '-'))
                {
                    reason = "a pre-release is identifiers of letters, digits and hyphens separated by dots, such as beta.1";
                    return false;
                }

                if (identifier.Length > 1 && identifier[0] == '0' && identifier.All(Ascii.IsDigit))
                {
                    reason = "a numeric pre-release identifier has no leading zeros";
                    return false;
                }
            }

            reason = "";
            return true;
        }

        private static int ComparePrerelease(string left, string right)
        {
            var leftIdentifiers = left.Split('.');
            var rightIdentifiers = right.Split('.');
            for (var i = 0; i < Math.Min(leftIdentifiers.Length, rightIdentifiers.Length); i++)
            {
                var order = CompareIdentifier(leftIdentifiers[i], rightIdentifiers[i]);
                if (order != 0)
                {
                    return order;
                }
            }

            return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
        }

        private static int CompareIdentifier(string left, string right)
        {
            var leftIsNumber = left.All(Ascii.IsDigit);
            var rightIsNumber = right.All(Ascii.IsDigit);
            if (leftIsNumber && rightIsNumber)
            {
                // Neither has a leading zero, so the longer number is the larger, and two of one
                // length compare digit by digit. No identifier is ever turned into an integer.
                return left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
            }

            if (leftIsNumber != rightIsNumber)
            {
                return leftIsNumber ? -1 : 1;
            }

            return string.CompareOrdinal(left, right);
        }

        private static string FixFor(string text)
        {
            if (text.Length > 1 && text[0] is 'v' or 'V' && TryParseParts(text[1..], out var withoutPrefix, out _))
            {
                return $"remove the leading \"{text[0]}\": write \"{withoutPrefix}\"";
            }

            if (TryParseParts(text + ".0", out var withPatch, out _))
            {
                return $"write all three numbers: \"{withPatch}\"";
            }

            return GenericFix;
        }

        private static Diagnostic Invalid(string text, string reason, string fix) =>
            new(DiagnosticCodes.VersionInvalid, $"\"{text}\" is not a valid version.", reason, fix);
    }
}
