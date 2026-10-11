using Packmoji.Core.Json;

namespace Packmoji.Core.Registry
{
    /// <summary>Writes the file of what was refused in a repository in its one form: the releases in order of tag.</summary>
    public static class RefusedReleasesWriter
    {
        public static string Write(RefusedReleases refused)
        {
            ArgumentNullException.ThrowIfNull(refused);
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();
            json.WriteNumber("version", RegistryPackage.FormatVersion);
            json.WriteString("repository", refused.Repository.ToString());
            json.WriteStartArray("releases");
            foreach (var release in refused.Releases.OrderBy(release => release.Tag, StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteString("tag", release.Tag);
                json.WriteString("sha256", release.Sha256.Hex);
                json.WriteString("seen", RegistryTime.Write(release.Seen));
                json.WriteString("code", release.Code);
                json.WriteString("why", release.Why);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            return json.ToString();
        }
    }
}
