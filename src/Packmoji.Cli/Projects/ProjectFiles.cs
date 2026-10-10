using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;

namespace Packmoji.Cli.Projects
{
    /// <summary>
    /// Reads and writes the two files a project has. Each is written whole to a file beside it and
    /// then put in its place in one step, so that a pmj that is stopped, or two that run at once,
    /// can leave an old file or a new one and never half of one. When both are written, both are
    /// made ready before either is put in its place, and if the second cannot be put there the first
    /// is put back: a command that fails leaves the project as it found it.
    /// </summary>
    internal static class ProjectFiles
    {
        /// <summary>The directory of a project that pmj writes into, and so never reads a package from.</summary>
        public const string Target = "target";

        /// <summary>The directory of a project that holds the packages it is built with: the one place the compiler looks without being told to.</summary>
        public const string Packages = "packages";

        /// <summary>
        /// What is kept at the top of a project's directory and is no part of the project: what pmj
        /// writes, where built packages go, and git's own files. No pattern of a manifest is given
        /// the chance to select them.
        /// </summary>
        public static readonly string[] NotTheProject = [".git", Target, Packages];

        /// <summary>The manifest of the project in a directory, or why there is none to work with.</summary>
        public static Outcome<Manifest> ReadManifest(string directory)
        {
            var path = Path.Combine(directory, ManifestReader.FileName);
            if (!File.Exists(path))
            {
                return Outcome<Manifest>.Failed(new Diagnostic(
                    DiagnosticCodes.ProjectNotFound,
                    $"There is no {ManifestReader.FileName} here.",
                    $"this command works on a project, and \"{directory}\" is not one",
                    "run it in a project's directory, or make a project with pmj new or pmj init"));
            }

            return Read(path, ManifestReader.MaxBytes, bytes => ManifestReader.Read(bytes));
        }

        /// <summary>The project's lockfile, or null when it has none yet.</summary>
        public static Outcome<Lockfile>? ReadLockfile(string directory)
        {
            var path = Path.Combine(directory, LockfileReader.FileName);
            return File.Exists(path) ? Read(path, LockfileReader.MaxBytes, bytes => LockfileReader.Read(bytes)) : null;
        }

        /// <summary>Writes files of the project, all of them or none. Null when they were written, and the problem when one could not be.</summary>
        /// <param name="files">Each file's name and its whole text, in the order they are put in place.</param>
        public static Diagnostic? Write(string directory, params (string Name, string Text)[] files)
        {
            var ready = new List<(string Path, string Beside)>();
            var replaced = new List<(string Path, byte[]? Was)>();
            var writing = directory;
            try
            {
                foreach (var (name, text) in files)
                {
                    writing = Path.Combine(directory, name);
                    var beside = writing + ".tmp-" + Guid.NewGuid().ToString("N");
                    ready.Add((writing, beside));
                    File.WriteAllText(beside, text);
                }

                foreach (var (path, beside) in ready)
                {
                    writing = path;
                    var was = File.Exists(path) ? File.ReadAllBytes(path) : null;
                    File.Move(beside, path, overwrite: true);
                    replaced.Add((path, was));
                }

                return null;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                foreach (var (path, was) in Enumerable.Reverse(replaced))
                {
                    PutBack(path, was);
                }

                foreach (var (_, beside) in ready)
                {
                    TryDelete(beside);
                }

                return Unreadable(writing, "written", failure);
            }
        }

        /// <summary>
        /// The problem when one of the places in a project that pmj is about to write into is a
        /// symbolic link, or null when none is. A project's directory may have come from someone
        /// else, links and all, and what is cleared and written through a link is cleared and
        /// written wherever the link leads.
        /// </summary>
        /// <param name="places">Paths in the project, each with <c>/</c> between its parts.</param>
        public static Diagnostic? Linked(string directory, IEnumerable<string> places)
        {
            // Asked of the entry itself and not of what it leads to, so a link that leads nowhere is found too.
            var linked = places.FirstOrDefault(place => new FileInfo(Path.Combine(directory, place.Replace('/', Path.DirectorySeparatorChar))).LinkTarget is not null);
            return linked is null
                ? null
                : new Diagnostic(
                    DiagnosticCodes.ProjectUnreadable,
                    $"\"{linked}\" is a symbolic link, and pmj does not build through one.",
                    "a build clears what is at the places it writes to and then writes there, and a link can lead anywhere on this machine",
                    "delete the link, and pmj makes a directory in its place");
        }

        public static Diagnostic Unreadable(string path, string done, Exception failure) =>
            new(
                DiagnosticCodes.ProjectUnreadable,
                $"\"{path}\" could not be {done}.",
                failure.Message,
                "check that the file and its directory are there and are yours to use");

        private static Outcome<T> Read<T>(string path, int maxBytes, Func<ReadOnlyMemory<byte>, ReadResult<T>> read) where T : class
        {
            try
            {
                // One byte more than a file may be is enough for its reader to refuse it by its length.
                using var file = File.OpenRead(path);
                var bytes = new byte[Math.Min(file.Length, maxBytes + 1L)];
                file.ReadExactly(bytes);
                var result = read(bytes);
                return result.Succeeded ? Outcome<T>.Of(result.Value) : Outcome<T>.Failed(result.Diagnostics, result.OmittedDiagnostics);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return Outcome<T>.Failed(Unreadable(path, "read", failure));
            }
        }

        // A file that was put in its place before another could not be is made what it was again.
        private static void PutBack(string path, byte[]? was)
        {
            try
            {
                if (was is null)
                {
                    File.Delete(path);
                    return;
                }

                var beside = path + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllBytes(beside, was);
                File.Move(beside, path, overwrite: true);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // What cannot be put back stays as it was written, and the problem that is reported is the one that caused this.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // What could not be written may not be there to delete, and the problem that matters is already being reported.
            }
        }
    }
}
