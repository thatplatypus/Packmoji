using Packmoji.Cli.Projects;
using Packmoji.Core.Building;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Cli.Building
{
    /// <summary>
    /// The project's own <c>packages</c> directory, which is the one place the compiler looks without
    /// being told to. Every locked package is put there as a copy, so that a compiler run by hand in
    /// the project finds what pmj built, and a project needs nothing outside itself.
    /// </summary>
    /// <remarks>
    /// Someone may keep packages of their own in that directory, so pmj touches only what it put
    /// there. A folder is pmj's when it holds a stamp, and the stamp's key says which build it is.
    /// </remarks>
    internal static class PlacedPackages
    {
        public const string DirectoryName = "packages";

        /// <summary>
        /// Makes the directory hold exactly these packages of pmj's: puts in what is missing, replaces
        /// what is another build, and takes out what pmj put there for a package that is no longer
        /// wanted. Null when it does, and the problem when it cannot.
        /// </summary>
        public static Diagnostic? Place(string project, IReadOnlyList<PlacedPackage> wanted)
        {
            var packages = Path.Combine(project, DirectoryName);
            try
            {
                // Looked at before anything is changed, so that a refusal leaves the directory as it was.
                foreach (var package in wanted)
                {
                    var folder = Path.Combine(packages, package.Name);
                    if ((Directory.Exists(folder) || File.Exists(folder)) && !File.Exists(Path.Combine(folder, BuildStamp.FileName)))
                    {
                        return new Diagnostic(
                            DiagnosticCodes.PackagesForeign,
                            $"\"{DirectoryName}/{package.Name}\" is in the way of the package pmj built.",
                            $"it is in the project already and has no {BuildStamp.FileName}, so pmj did not put it there, and pmj changes nothing in \"{DirectoryName}\" that is not its own",
                            $"move it away or delete it: the compiler takes the first \"{package.Name}\" it finds, and that has to be the one that is locked");
                    }
                }

                foreach (var package in wanted.Where(package => !IsThere(Path.Combine(packages, package.Name), package)))
                {
                    var folder = Path.Combine(packages, package.Name);
                    var beside = folder + ".tmp-" + Guid.NewGuid().ToString("N");
                    Directory.CreateDirectory(beside);
                    foreach (var file in Directory.EnumerateFiles(package.From))
                    {
                        var copy = Path.Combine(beside, Path.GetFileName(file));
                        File.Copy(file, copy);

                        // What pmj keeps is marked as not to be written. A project's copy is the project's, to delete as it likes.
                        File.SetAttributes(copy, FileAttributes.Normal);
                    }

                    Remove(folder);
                    Directory.Move(beside, folder);
                }

                if (Directory.Exists(packages))
                {
                    var names = wanted.Select(package => package.Name).ToHashSet(StringComparer.Ordinal);
                    foreach (var folder in Directory.EnumerateDirectories(packages).Where(folder => !names.Contains(Path.GetFileName(folder)) && File.Exists(Path.Combine(folder, BuildStamp.FileName))).ToList())
                    {
                        Remove(folder);
                    }
                }

                return null;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return ProjectFiles.Unreadable(packages, "written", failure);
            }
        }

        // Whether a folder already holds this build of a package, whole.
        private static bool IsThere(string folder, PlacedPackage package)
        {
            var stamp = Path.Combine(folder, BuildStamp.FileName);
            return File.Exists(stamp)
                && BuildStamp.KeyIn(File.ReadAllBytes(stamp)) == package.Key
                && Directory.EnumerateFiles(package.From).All(file => File.Exists(Path.Combine(folder, Path.GetFileName(file))));
        }

        private static void Remove(string folder)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
