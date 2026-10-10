using System.Text;
using Packmoji.Core.Archives;
using Packmoji.Core.Manifests;
using Shouldly;

namespace Packmoji.Cli.Tests.TestSupport
{
    /// <summary>Packages for tests to publish and to depend on, written as their authors would write them.</summary>
    internal static class TestPackage
    {
        /// <summary>
        /// A manifest. It says where the package lives when that is not a repository of its own name.
        /// Each dependency is written <c>@owner/name@1.2</c>, and one that begins <c>dev:</c> is needed
        /// only to develop the package.
        /// </summary>
        public static string Manifest(string name, string version, string kind, string? repository, params string[] dependencies)
        {
            var package = new List<string> { $"\"name\": \"{name}\"", $"\"version\": \"{version}\"", $"\"kind\": \"{kind}\"", "\"emojicode\": \">=1.0.0-beta.2\"" };
            if (repository is not null && repository != "github.com/" + name[1..])
            {
                package.Add($"\"repository\": \"{repository}\"");
            }

            var tables = new StringBuilder();
            foreach (var (table, dev) in new[] { ("dependencies", false), ("devDependencies", true) })
            {
                var asked = dependencies
                    .Where(dependency => dependency.StartsWith("dev:", StringComparison.Ordinal) == dev)
                    .Select(dependency => dependency.Replace("dev:", ""))
                    .Select(dependency => $"\"{dependency[..dependency.LastIndexOf('@')]}\": \"{dependency[(dependency.LastIndexOf('@') + 1)..]}\"")
                    .ToList();
                if (asked.Count > 0)
                {
                    tables.Append($", \"{table}\": {{ {string.Join(", ", asked)} }}");
                }
            }

            return $"{{ \"package\": {{ {string.Join(", ", package)} }}{tables} }}\n";
        }

        /// <summary>The archive of a library as <c>pmj pack</c> writes it: its manifest, and one source file that says which version it is.</summary>
        public static byte[] Archive(string name, string version, string repository, params string[] dependencies) =>
            PackageArchive.Write(
            [
                File("packmoji.json", Manifest(name, version, "library", repository, dependencies)),
                File("src/lib.🍇", $"💭 {name} {version}\n"),
            ]);

        public static ArchiveFile File(string path, string text)
        {
            RelativePath.TryParse(path, out var parsed, out var error).ShouldBeTrue(error?.Reason);
            return new ArchiveFile(parsed!, Encoding.UTF8.GetBytes(text));
        }
    }
}
