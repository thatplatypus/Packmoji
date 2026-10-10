using System.Text;
using Packmoji.Core.Archives;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static partial class Sample
    {
        /// <summary>A file of a package, with text for its content.</summary>
        public static ArchiveFile File(string path, string text = "") => new(Path(path), Encoding.UTF8.GetBytes(text));

        /// <summary>The least a package's archive can hold: its manifest and one source file.</summary>
        public static ArchiveFile[] SmallPackage() => [File("packmoji.json", Fixtures.MinimalManifest), File("src/lib.🍇", "🌍 🐇 👋 🍇 🍉\n")];
    }
}
