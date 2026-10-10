using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Archives
{
    /// <summary>
    /// A package version as it is published: one file, <c>&lt;name&gt;-&lt;version&gt;.pmj.tar.gz</c>,
    /// that holds the package's manifest and its sources. A digest is taken of its bytes and locked,
    /// so there is one form of it, and the same files give the same bytes whoever packs them.
    /// </summary>
    /// <remarks>
    /// Reading is as strict as writing. An archive comes from someone else and is unpacked on this
    /// machine, and the only archive that has to be read is one that pmj wrote: so what is read is
    /// written again, and has to come out the same, byte for byte. That refuses a link, a path that
    /// climbs out, a file that would run, and anything carried along beside the files, without a rule
    /// for each, because pmj writes none of them.
    /// </remarks>
    public static class PackageArchive
    {
        /// <summary>The most bytes an archive may be: 16 MiB.</summary>
        public const int MaxBytes = 16 * 1024 * 1024;

        /// <summary>The most files an archive may hold.</summary>
        public const int MaxFiles = 4096;

        /// <summary>
        /// Every reason a set of files cannot be an archive, as the end of a sentence that begins
        /// "The archive cannot be written because". Empty when it can.
        /// </summary>
        public static IReadOnlyList<string> Check(IReadOnlyList<ArchiveFile> files)
        {
            ArgumentNullException.ThrowIfNull(files);
            return ArchiveRules.Problems(files.Select(file => (file.Path.Value, (long)file.Content.Length)).ToList());
        }

        /// <summary>The archive of these files. They are to be checked first: files that cannot be an archive are the caller's mistake.</summary>
        public static byte[] Write(IReadOnlyList<ArchiveFile> files)
        {
            var problems = Check(files);
            if (problems.Count > 0)
            {
                throw new ArgumentException($"These files cannot be an archive: {problems[0]}.", nameof(files));
            }

            return GzipFormat.Store(TarFormat.Write(InOrder(files)));
        }

        /// <summary>The files of an archive, in order of path, or the one reason it is not an archive pmj reads.</summary>
        public static ReadResult<IReadOnlyList<ArchiveFile>> Read(ReadOnlyMemory<byte> archive)
        {
            if (archive.Length > MaxBytes)
            {
                return Refused($"it is larger than {MaxBytes} bytes");
            }

            if (GzipFormat.Unpack(archive.Span) is not { } tar)
            {
                return Refused("it is not a gzip file as pmj pack writes one: it is damaged, or something else made it, or something was added to it");
            }

            if (!TarFormat.TryParse(tar, out var entries, out var notTar))
            {
                return Refused(notTar);
            }

            var files = new List<ArchiveFile>(entries.Count);
            foreach (var (path, content) in entries)
            {
                if (!Manifests.RelativePath.TryParse(path, out var parsed, out var error))
                {
                    return Refused($"it holds the path \"{path}\", which is not one a package may have: {error.Reason}");
                }

                files.Add(new ArchiveFile(parsed, content));
            }

            var problems = Check(files);
            if (problems.Count > 0)
            {
                return Refused(problems[0]);
            }

            var ordered = InOrder(files);
            if (!TarFormat.Write(ordered).AsSpan().SequenceEqual(tar))
            {
                return Refused("it is not in the one form that pmj pack writes, though it holds only files");
            }

            return ReadResult<IReadOnlyList<ArchiveFile>>.Success(ordered);
        }

        private static List<ArchiveFile> InOrder(IReadOnlyList<ArchiveFile> files) =>
            files.OrderBy(file => file.Path.Value, PathOrder.Instance).ToList();

        private static ReadResult<IReadOnlyList<ArchiveFile>> Refused(string reason) =>
            ReadResult<IReadOnlyList<ArchiveFile>>.Failure(
                [
                    new Diagnostic(
                        DiagnosticCodes.ArchiveInvalid,
                        "The archive is not a package that pmj can read.",
                        reason,
                        "pack the package again with pmj pack, which writes the only archive pmj reads"),
                ],
                0);
    }
}
