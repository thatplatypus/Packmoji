using Packmoji.Core.Building;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Json;
using Packmoji.Core.Manifests;

namespace Packmoji.Core.Reports
{
    /// <summary>What <c>pmj build</c> says to a tool.</summary>
    public static class BuildReport
    {
        /// <summary>
        /// One JSON object: <c>ok</c>, the problems found, the compiler that was used, where the
        /// project's packages are, each of them, and what was made of the project. A tool that
        /// compiles a project its own way gives the compiler <c>packagesDirectory</c> to search, and
        /// links with the archive in each package's <c>directory</c> and with its <c>link</c> libraries.
        /// </summary>
        /// <param name="compiler">Null when the build needed no compiler, or was stopped before it found one.</param>
        /// <param name="packagesDirectory">The one directory that holds a folder for every package.</param>
        /// <param name="packages">Every locked package, in the order they are to be listed. Empty when the build was stopped.</param>
        /// <param name="project">Null when the project itself was not built.</param>
        public static string Json(
            IReadOnlyList<Diagnostic> diagnostics,
            int omittedDiagnostics,
            CompilerIdentity? compiler,
            string packagesDirectory,
            IReadOnlyList<BuiltPackage> packages,
            BuiltProject? project)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);
            ArgumentNullException.ThrowIfNull(packagesDirectory);
            ArgumentNullException.ThrowIfNull(packages);
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();
            ReportJson.WriteOutcome(json, diagnostics, omittedDiagnostics);
            if (compiler is not null)
            {
                json.WriteStartObject("compiler");
                json.WriteString("path", compiler.Path);
                json.WriteString("version", compiler.Version.ToString());
                json.WriteString("sha256", compiler.Sha256.Hex);
                json.WriteEndObject();
            }

            json.WriteString("packagesDirectory", packagesDirectory);
            json.WriteStartArray("packages");
            foreach (var package in packages)
            {
                json.WriteStartObject();
                json.WriteString("name", package.Name.ToString());
                json.WriteString("version", package.Version.ToString());
                json.WriteString("bareName", package.Name.Name);
                json.WriteString("directory", package.Directory);
                json.WriteStrings("link", package.Link);
                json.WriteBoolean("built", package.Compiled);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            if (project is not null)
            {
                json.WriteStartObject("project");
                json.WriteString("name", project.Name.ToString());
                json.WriteString("version", project.Version.ToString());
                json.WriteString("kind", PackageKinds.Name(project.Kind));
                json.WriteString("output", project.Output);
                json.WriteEndObject();
            }

            json.WriteEndObject();
            return json.ToString();
        }
    }
}
