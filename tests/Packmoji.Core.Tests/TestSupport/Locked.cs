using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Tests.TestSupport
{
    /// <summary>
    /// A lockfile for a test of a build, written a line to a package: <c>@owner/name 1.0.0</c>, and
    /// after a <c>&gt;</c> the packages it depends on, with commas between them.
    /// </summary>
    internal static class Locked
    {
        public static Lockfile Of(params string[] packages) => new(new RootRequirements([], []), packages.Select(Package).ToList());

        public static LockedPackage Package(string line)
        {
            var sides = line.Split('>');
            var (name, version) = Pin(sides[0]);
            var needs = sides.Length == 1 ? [] : sides[1].Split(',').Select(Pin).Select(pin => new LockedDependency(pin.Name, pin.Version)).ToList();
            return new LockedPackage(name, version, RepositoryRef.DefaultFor(name), Sample.DigestOf($"{name}@{version}"), VerificationLevel.Checksum, needs);
        }

        private static (PackageName Name, SemanticVersion Version) Pin(string text)
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (Sample.Name(parts[0]), Sample.Version(parts[1]));
        }
    }
}
