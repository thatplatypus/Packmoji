using Packmoji.Core.Json;

namespace Packmoji.Core.Manifests
{
    /// <summary>
    /// Writes a manifest in the one form pmj gives it. The same manifest is the same bytes on every
    /// machine: keys in a fixed order, and dependencies sorted, so that two people who add the same
    /// dependency do not make two different files.
    /// </summary>
    public static class ManifestWriter
    {
        public static string Write(Manifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();

            WritePackage(json, manifest.Package);
            WriteDependencies(json, "dependencies", manifest.Dependencies);
            WriteDependencies(json, "devDependencies", manifest.DevDependencies);

            if (manifest.Build is { } build)
            {
                json.WriteStartObject("build");
                if (build.Entry is not null)
                {
                    json.WriteString("entry", build.Entry.Value);
                }

                if (build.Sources is not null)
                {
                    json.WriteStrings("sources", build.Sources.Select(pattern => pattern.Value));
                }

                json.WriteEndObject();
            }

            if (manifest.Native is { } native)
            {
                json.WriteStartObject("native");
                if (native.Sources is not null)
                {
                    json.WriteStrings("sources", native.Sources.Select(pattern => pattern.Value));
                }

                if (native.IncludeDirs is not null)
                {
                    json.WriteStrings("includeDirs", native.IncludeDirs.Select(path => path.Value));
                }

                if (native.Link is not null)
                {
                    json.WriteStrings("link", native.Link);
                }

                json.WriteEndObject();
            }

            if (manifest.Policy is { } policy)
            {
                json.WriteStartObject("policy");
                if (policy.RequireAttestation is { } requireAttestation)
                {
                    json.WriteBoolean("requireAttestation", requireAttestation);
                }

                json.WriteEndObject();
            }

            json.WriteEndObject();
            return json.ToString();
        }

        private static void WritePackage(CanonicalJsonWriter json, PackageSection package)
        {
            json.WriteStartObject("package");
            json.WriteString("name", package.Name.ToString());
            json.WriteString("version", package.Version.ToString());
            json.WriteString("kind", PackageKinds.Name(package.Kind));
            json.WriteString("emojicode", package.Emojicode.ToString());
            if (package.Description is not null)
            {
                json.WriteString("description", package.Description);
            }

            if (package.License is not null)
            {
                json.WriteString("license", package.License.Text);
            }

            if (package.Repository is not null)
            {
                json.WriteString("repository", package.Repository.ToString());
            }

            json.WriteEndObject();
        }

        private static void WriteDependencies(CanonicalJsonWriter json, string key, IReadOnlyList<Dependency>? dependencies)
        {
            if (dependencies is null)
            {
                return;
            }

            json.WriteStartObject(key);
            foreach (var dependency in dependencies.OrderBy(dependency => dependency.Name))
            {
                json.WriteString(dependency.Name.ToString(), dependency.Requirement.Text);
            }

            json.WriteEndObject();
        }
    }
}
