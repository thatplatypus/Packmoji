using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Json;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Building
{
    /// <summary>
    /// What lies beside every built package and says what it was built from. In a project's own
    /// directory the stamp is also the mark that pmj put a folder there, and its key says which build
    /// of the package the folder holds.
    /// </summary>
    /// <param name="Key">The key the package was built under: see <see cref="BuildKey"/>.</param>
    /// <param name="Archive">The digest of the archive it was built from.</param>
    /// <param name="Compiler">The digest of the compiler's own file.</param>
    /// <param name="Dependencies">The key of each package it depends on directly, as it was built against.</param>
    /// <param name="Link">The libraries its manifest says a program that uses it is linked with, in the manifest's order.</param>
    public sealed record BuildStamp(
        Sha256Digest Key,
        PackageName Name,
        SemanticVersion Version,
        Sha256Digest Archive,
        SemanticVersion CompilerVersion,
        Sha256Digest Compiler,
        bool Optimized,
        IReadOnlyDictionary<PackageName, Sha256Digest> Dependencies,
        IReadOnlyList<string> Link)
    {
        public const string FileName = "pmj-build.json";

        public const int FormatVersion = 1;

        /// <summary>The stamp as the text of its file.</summary>
        public string Write()
        {
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();
            json.WriteNumber("version", FormatVersion);
            json.WriteString("key", Key.Hex);

            json.WriteStartObject("package");
            json.WriteString("name", Name.ToString());
            json.WriteString("version", Version.ToString());
            json.WriteString("sha256", Archive.Hex);
            json.WriteEndObject();

            json.WriteStartObject("compiler");
            json.WriteString("version", CompilerVersion.ToString());
            json.WriteString("sha256", Compiler.Hex);
            json.WriteEndObject();

            json.WriteBoolean("optimized", Optimized);
            json.WriteStartArray("dependencies");
            foreach (var (name, key) in Dependencies.OrderBy(dependency => dependency.Key))
            {
                json.WriteStartObject();
                json.WriteString("name", name.ToString());
                json.WriteString("key", key.Hex);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStrings("link", Link);
            json.WriteEndObject();
            return json.ToString();
        }

        /// <summary>
        /// The key that the bytes of a stamp give, or null when they are not a stamp this pmj reads:
        /// not JSON, another version, or no key. Nothing else in it is looked at, so that a later pmj
        /// may write more into a stamp without an earlier one taking the folder for someone else's.
        /// </summary>
        public static Sha256Digest? KeyIn(ReadOnlyMemory<byte> stamp)
        {
            var diagnostics = new DiagnosticList();
            var root = JsonTreeReader.Read(stamp, FileName, "", diagnostics);
            if (root is not { Kind: JsonKind.Object } || diagnostics.Count > 0)
            {
                return null;
            }

            var version = root.Members.Find(member => member.Name == "version")?.Value;
            var key = root.Members.Find(member => member.Name == "key")?.Value;
            return version is { Kind: JsonKind.Number, Text: "1" } && key is { Kind: JsonKind.String } && Sha256Digest.TryParse(key.Text, out var digest, out _)
                ? digest
                : null;
        }
    }
}
