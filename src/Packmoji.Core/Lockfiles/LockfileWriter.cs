using Packmoji.Core.Json;
using Packmoji.Core.Manifests;

namespace Packmoji.Core.Lockfiles
{
    /// <summary>
    /// Writes a lockfile in its one canonical form. A lockfile is committed and its diffs are read, so
    /// the same lockfile has to be the same bytes whoever writes it and in whatever order its packages
    /// were found: everything that is a list of names is sorted here, and nowhere else.
    /// </summary>
    public static class LockfileWriter
    {
        public static string Write(Lockfile lockfile)
        {
            ArgumentNullException.ThrowIfNull(lockfile);
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();
            json.WriteNumber("version", Lockfile.FormatVersion);

            json.WriteStartObject("root");
            json.WriteStrings("dependencies", Requirements(lockfile.Root.Dependencies));
            json.WriteStrings("devDependencies", Requirements(lockfile.Root.DevDependencies));
            json.WriteEndObject();

            json.WriteStartArray("packages");
            foreach (var package in lockfile.Packages.OrderBy(package => package.Name))
            {
                json.WriteStartObject();
                json.WriteString("name", package.Name.ToString());
                json.WriteString("version", package.Version.ToString());
                json.WriteString("source", package.Source.ToString());
                json.WriteString("releaseTag", package.ReleaseTag);
                json.WriteString("asset", package.Asset);
                json.WriteString("sha256", package.Sha256.Hex);
                json.WriteString("verified", VerificationLevels.Name(package.Verified));
                json.WriteStrings(
                    "dependencies",
                    package.Dependencies.OrderBy(dependency => dependency.Name).Select(dependency => $"{dependency.Name}@{dependency.Version}"));
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            return json.ToString();
        }

        private static IEnumerable<string> Requirements(IReadOnlyList<Dependency> requirements) =>
            requirements.OrderBy(requirement => requirement.Name).Select(requirement => $"{requirement.Name}@{requirement.Requirement.Text}");
    }
}
