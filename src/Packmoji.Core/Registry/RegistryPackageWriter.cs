using Packmoji.Core.Json;
using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// Writes a package's file in its one form. A registry is kept under source control and its
    /// history is its record, so the same package has to be the same bytes whoever writes it and in
    /// whatever order its versions were found: everything that is a list is put in order here.
    /// </summary>
    public static class RegistryPackageWriter
    {
        public static string Write(RegistryPackage package)
        {
            ArgumentNullException.ThrowIfNull(package);
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();
            json.WriteNumber("version", RegistryPackage.FormatVersion);
            json.WriteString("name", package.Name.ToString());
            json.WriteStartArray("versions");
            foreach (var version in package.Versions.OrderBy(version => version.Version))
            {
                json.WriteStartObject();
                json.WriteString("version", version.Version.ToString());
                json.WriteString("status", RegistryStatuses.Name(version.Status));
                json.WriteString("source", version.Source.ToString());
                json.WriteString("sha256", version.Sha256.Hex);
                json.WriteNumber("size", version.Size);
                json.WriteString("verified", VerificationLevels.Name(version.Verified));
                json.WriteBoolean("immutable", version.IsImmutable);
                json.WriteString("published", RegistryTime.Write(version.Published));
                json.WriteString("indexed", RegistryTime.Write(version.Indexed));
                json.WriteStartObject("dependencies");
                foreach (var dependency in version.Dependencies.OrderBy(dependency => dependency.Name))
                {
                    json.WriteString(dependency.Name.ToString(), dependency.Requirement.Text);
                }

                json.WriteEndObject();
                if (version.Quarantined is { } quarantined)
                {
                    json.WriteStartObject("quarantined");
                    json.WriteString("seen", RegistryTime.Write(quarantined.Seen));
                    json.WriteString("sha256", quarantined.Sha256.Hex);
                    json.WriteEndObject();
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            return json.ToString();
        }
    }
}
