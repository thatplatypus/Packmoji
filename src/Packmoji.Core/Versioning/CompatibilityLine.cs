using System.Globalization;

namespace Packmoji.Core.Versioning
{
    /// <summary>
    /// A run of versions that are expected to be compatible with one another: a major version from 1
    /// up, and a minor version below 1, where any minor release may break. A requirement only ever
    /// selects within one line, which is what lets a build hold a single copy of each package.
    /// </summary>
    public sealed record CompatibilityLine
    {
        private CompatibilityLine(int major, int? minor)
        {
            Major = major;
            Minor = minor;
        }

        public int Major { get; }

        /// <summary>Set only for major 0, where each minor version is a line of its own.</summary>
        public int? Minor { get; }

        public static CompatibilityLine Of(SemanticVersion version)
        {
            ArgumentNullException.ThrowIfNull(version);
            return version.Major == 0 ? new CompatibilityLine(0, version.Minor) : new CompatibilityLine(version.Major, null);
        }

        public bool Contains(SemanticVersion version) => Of(version) == this;

        public override string ToString() => Minor is { } minor
            ? string.Create(CultureInfo.InvariantCulture, $"0.{minor}.x")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.x");
    }
}
