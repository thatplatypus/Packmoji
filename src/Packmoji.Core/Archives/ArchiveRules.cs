using Packmoji.Core.Manifests;

namespace Packmoji.Core.Archives
{
    /// <summary>
    /// What a set of files has to be before it is one package's archive. An archive is unpacked on
    /// machines other than the one that packed it, so two of its files must not be one file there,
    /// and none of its names may be something else there. The same rules hold when an archive is
    /// written and when one is read, so that nothing can be published that could not be installed.
    /// </summary>
    internal static class ArchiveRules
    {
        // CON, PRN, AUX and NUL, and COM1 to COM9 and LPT1 to LPT9: on Windows each of these is a
        // device whatever follows its first dot, and in any case.
        private static readonly HashSet<string> Devices = new(
            new[] { "CON", "PRN", "AUX", "NUL" }
                .Concat(Enumerable.Range(1, 9).SelectMany(number => new[] { $"COM{number}", $"LPT{number}" })),
            StringComparer.Ordinal);

        /// <summary>Every reason these files cannot be an archive. Empty when they can.</summary>
        public static IReadOnlyList<string> Problems(IReadOnlyList<(string Path, long Size)> files)
        {
            if (files.Count > PackageArchive.MaxFiles)
            {
                return [$"it holds more than {PackageArchive.MaxFiles} files"];
            }

            var problems = new List<string>();
            if (TooLarge(files) is { } tooLarge)
            {
                problems.Add(tooLarge);
            }

            if (!files.Any(file => file.Path == ManifestReader.FileName))
            {
                problems.Add($"it holds no {ManifestReader.FileName}, which says what package it is");
            }

            problems.AddRange(NameProblems(files.Select(file => file.Path).ToList()));
            return problems;
        }

        /// <summary>
        /// Why the archive of these files would be over its limit, or null when it would not be. An
        /// archive is not compressed, so its length follows from the paths and the lengths alone.
        /// </summary>
        public static string? TooLarge(IReadOnlyList<(string Path, long Size)> files)
        {
            var size = GzipFormat.SizeOf(TarFormat.SizeOf(files));
            return size > PackageArchive.MaxBytes
                ? $"its archive would be {size} bytes, and an archive is at most {PackageArchive.MaxBytes} bytes"
                : null;
        }

        /// <summary>Every reason these paths cannot all be files on one disk, whatever disk that is.</summary>
        public static IReadOnlyList<string> NameProblems(IReadOnlyList<string> paths)
        {
            var problems = new List<string>();

            // Case is folded one character at a time, which needs no culture data and is what a
            // disk that ignores case does.
            var spelled = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                if (!spelled.TryAdd(path.ToUpperInvariant(), path))
                {
                    var other = spelled[path.ToUpperInvariant()];
                    problems.Add(other == path ? $"\"{path}\" is in it twice" : $"\"{other}\" and \"{path}\" are the same but for case, and many disks could hold only one of them");
                }
            }

            foreach (var path in paths)
            {
                var parts = path.Split('/');
                for (var count = 1; count < parts.Length; count++)
                {
                    if (spelled.TryGetValue(string.Join('/', parts[..count]).ToUpperInvariant(), out var file))
                    {
                        problems.Add($"\"{file}\" is both a file and a directory, for \"{path}\" is inside it");
                    }
                }

                foreach (var part in parts)
                {
                    var dot = part.IndexOf('.');
                    if (Devices.Contains((dot < 0 ? part : part[..dot]).ToUpperInvariant()))
                    {
                        problems.Add($"\"{path}\" has the part \"{part}\", which names a device on Windows and cannot be a file there");
                    }
                }
            }

            return problems;
        }
    }
}
