using Packmoji.Cli.Output;
using Packmoji.Cli.Projects;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Manifests;
using Packmoji.Core.Versioning;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// <c>pmj new</c> and <c>pmj init</c>: the files a project begins with. What is written compiles,
    /// packs and can be published as it stands, and the two source files were compiled with the one
    /// released compiler before they were put here.
    /// </summary>
    internal static class Scaffold
    {
        private const string FirstVersion = "0.1.0";
        private const string Compiler = ">=1.0.0-beta.2";

        /// <param name="inPlace">Whether the project is made in the working directory, as <c>pmj init</c> makes it, or in a new directory of the package's name.</param>
        public static int Run(PmjHost host, string name, bool library, bool inPlace)
        {
            if (!PackageName.TryParse(name, out var package, out var invalid))
            {
                return DiagnosticPrinter.Report(host, invalid);
            }

            var directory = inPlace ? host.WorkingDirectory : Path.Combine(host.WorkingDirectory, package.Name);
            if (inPlace ? File.Exists(Path.Combine(directory, ManifestReader.FileName)) : Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return DiagnosticPrinter.Report(host, new Diagnostic(
                    DiagnosticCodes.ProjectExists,
                    inPlace ? "There is a project here already." : $"\"{package.Name}\" is here already, and is not empty.",
                    inPlace ? $"this directory has a {ManifestReader.FileName}" : "a new project is made in a directory of its own, and pmj does not write into one that holds something",
                    inPlace ? "change the project that is here, or run pmj init somewhere else" : $"choose another name, or go into \"{package.Name}\" and run pmj init"));
            }

            var entry = library ? "src/lib.🍇" : "src/main.🍇";
            SemanticVersion.TryParse(FirstVersion, out var version, out _);
            CompilerRequirement.TryParse(Compiler, out var compiler, out _);
            RelativePath.TryParse(entry, out var entryPath, out _);
            var manifest = new Manifest(
                new PackageSection(package, version!, library ? PackageKind.Library : PackageKind.App, compiler!),
                Build: new BuildSection(entryPath));

            try
            {
                Directory.CreateDirectory(Path.Combine(directory, "src"));
                var written = new List<string>();
                Put(directory, ManifestReader.FileName, ManifestWriter.Write(manifest), written);
                Put(directory, entry, library ? LibrarySource : AppSource(package), written);
                Put(directory, ".gitignore", $"{ProjectFiles.Target}/\n{ProjectFiles.Packages}/\n", written);
                Put(directory, "README.md", $"# {package.Name}\n", written);

                host.Out.WriteLine($"Made {(library ? "the library" : "the application")} {package} in {(inPlace ? "this directory" : package.Name + "/")}:");
                foreach (var file in written)
                {
                    host.Out.WriteLine($"  {file}");
                }

                return ExitStatus.Success;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return DiagnosticPrinter.Report(host, ProjectFiles.Unreadable(directory, "written", failure));
            }
        }

        // A file that is there is someone's work, and is left as it is.
        private static void Put(string directory, string path, string text, List<string> written)
        {
            var target = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(target))
            {
                File.WriteAllText(target, text);
                written.Add(path);
            }
        }

        private static string AppSource(PackageName package) => $"🏁 🍇\n  😀 🔤Hello from {package}!🔤❗️\n🍉\n";

        private const string LibrarySource =
            "📗 Says hello. This is where the package begins: what it marks with 🌍 is what others can use. 📗\n" +
            "🌍 🐇 👋 🍇\n" +
            "  📗 A greeting for *name*. 📗\n" +
            "  🐇 ❗️ 💬 name 🔡 ➡️ 🔡 🍇\n" +
            "    ↩️ 🔤Hello, 🧲name🧲!🔤\n" +
            "  🍉\n" +
            "🍉\n";
    }
}
