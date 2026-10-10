using System.Text;
using Packmoji.Core.Archives;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static partial class Sample
    {
        /// <summary>A file of a package, with text for its content.</summary>
        public static ArchiveFile File(string path, string text = "") => new(Path(path), Encoding.UTF8.GetBytes(text));

        /// <summary>
        /// The manifest of a library, as its author would write it. It says where the package lives
        /// when that is not a repository of its own name. Each dependency is written as <c>@owner/name@1.2</c>.
        /// </summary>
        public static string ManifestJson(string name, string version, string repository, params string[] dependencies)
        {
            var isDefault = Packmoji.Core.Identity.RepositoryRef.DefaultFor(Name(name)).ToString() == repository;
            var lines = new List<string>
            {
                $"\"name\": \"{name}\"",
                $"\"version\": \"{version}\"",
                "\"kind\": \"library\"",
                "\"emojicode\": \">=1.0.0-beta.2\"",
            };
            if (!isDefault)
            {
                lines.Add($"\"repository\": \"{repository}\"");
            }

            var asked = dependencies.Select(Asks).Select(dependency => $"\"{dependency.Name}\": \"{dependency.Requirement}\"");
            var table = dependencies.Length == 0 ? "" : $", \"dependencies\": {{ {string.Join(", ", asked)} }}";
            return $"{{ \"package\": {{ {string.Join(", ", lines)} }}{table} }}";
        }

        /// <summary>The least a package's archive can hold: its manifest and one source file.</summary>
        public static ArchiveFile[] SmallPackage() => [File("packmoji.json", Fixtures.MinimalManifest), File("src/lib.🍇", "🌍 🐇 👋 🍇 🍉\n")];
    }
}
