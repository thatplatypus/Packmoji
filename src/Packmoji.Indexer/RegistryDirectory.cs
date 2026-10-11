using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Registry;

namespace Packmoji.Indexer
{
    /// <summary>
    /// A registry as it is kept on a disk: a directory that holds the directory of the first format,
    /// and under that the two kinds of file and nothing else. What is read here may be a stranger's
    /// work, so a file that is not one of the two kinds at its own path, and a link, stop the read,
    /// and a link is never followed.
    /// </summary>
    internal static class RegistryDirectory
    {
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

        /// <summary>The registry in a directory. A directory that is not there, or that holds nothing, is a registry that has not begun.</summary>
        public static RegistryRead Read(string directory)
        {
            var packages = new List<RegistryPackage>();
            var refused = new List<RefusedReleases>();
            var problems = new List<string>();
            if (Directory.Exists(directory))
            {
                Walk(directory, "", packages, refused, problems);
            }

            return problems.Count > 0 ? new RegistryRead(null, problems) : new RegistryRead(new RegistryState(packages, refused), []);
        }

        /// <summary>
        /// Brings the files of a directory from one state of a registry to the next: what changed is
        /// written, what is no longer there is deleted, and a file that is as it should be is not touched.
        /// </summary>
        public static void Write(string directory, RegistryState before, RegistryState after)
        {
            var was = before.Files();
            var now = after.Files();
            foreach (var (path, text) in now)
            {
                var file = Local(directory, path);
                if (!was.TryGetValue(path, out var held) || !string.Equals(held, text, StringComparison.Ordinal) || !File.Exists(file))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllBytes(file, Utf8.GetBytes(text));
                }
            }

            foreach (var path in was.Keys.Where(path => !now.ContainsKey(path)))
            {
                File.Delete(Local(directory, path));
            }
        }

        private static string Local(string directory, string path) => Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));

        // In order of name at every level, so that what is wrong is said in the same order on every machine.
        private static void Walk(string directory, string under, List<RegistryPackage> packages, List<RefusedReleases> refused, List<string> problems)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                var path = under + Path.GetFileName(entry);
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    problems.Add($"{path} is a link, and a registry holds none.");
                }
                else if (attributes.HasFlag(FileAttributes.Directory))
                {
                    Walk(entry, path + "/", packages, refused, problems);
                }
                else if (RegistryPaths.TryParsePackage(path, out var name))
                {
                    if (Bytes(entry, path, problems) is { } bytes && Taken(RegistryPackageReader.Read(bytes, name, path), problems) is { } package)
                    {
                        packages.Add(package);
                    }
                }
                else if (RegistryPaths.TryParseRefused(path, out var repository))
                {
                    if (Bytes(entry, path, problems) is { } bytes && Taken(RefusedReleasesReader.Read(bytes, repository, path), problems) is { } file)
                    {
                        refused.Add(file);
                    }
                }
                else
                {
                    problems.Add($"{path} is not a file of a registry.");
                }
            }
        }

        // A file that is too large is not read at all: it is refused by its length.
        private static byte[]? Bytes(string file, string path, List<string> problems)
        {
            try
            {
                if (new FileInfo(file).Length > RegistryPackageReader.MaxBytes)
                {
                    problems.Add($"{path} is larger than {RegistryPackageReader.MaxBytes} bytes, which is the most a file of a registry may be.");
                    return null;
                }

                return File.ReadAllBytes(file);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{path} cannot be opened.");
                return null;
            }
        }

        private static T? Taken<T>(ReadResult<T> read, List<string> problems) where T : class
        {
            if (read.Succeeded)
            {
                return read.Value;
            }

            problems.AddRange(read.Diagnostics.Select(Said.Of));
            if (read.OmittedDiagnostics > 0)
            {
                problems.Add($"and {read.OmittedDiagnostics} more problems in the same file.");
            }

            return null;
        }
    }
}
