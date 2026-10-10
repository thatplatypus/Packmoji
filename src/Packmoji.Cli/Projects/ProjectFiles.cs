using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;

namespace Packmoji.Cli.Projects
{
    /// <summary>
    /// Reads and writes the two files a project has. Each is written whole to a file beside it and
    /// then put in its place in one step, so that a pmj that is stopped, or two that run at once,
    /// can leave an old file or a new one and never half of one.
    /// </summary>
    internal static class ProjectFiles
    {
        /// <summary>The directory of a project that pmj writes into, and so never reads a package from.</summary>
        public const string Target = "target";

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

        /// <summary>Writes a file of the project. Null when it was written, and the problem when it could not be.</summary>
        public static Diagnostic? Write(string directory, string name, string text)
        {
            var path = Path.Combine(directory, name);
            var beside = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(beside, text);
                File.Move(beside, path, overwrite: true);
                return null;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                TryDelete(beside);
                return Unreadable(path, "written", failure);
            }
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
