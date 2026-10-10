using Packmoji.Cli.Projects;
using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Versioning;

namespace Packmoji.Cli.Cache
{
    /// <summary>
    /// The cache: every archive pmj has downloaded, kept under its own digest, and beside each one
    /// the files it holds. The cache is shared by every project on the machine.
    /// </summary>
    /// <remarks>
    /// Nothing here takes a lock. A file's name is the digest of its content, and it is written
    /// beside its place and put there in one step: so two runs of pmj that want the same file write
    /// the same bytes, and whichever is second finds it done. What is read is held to its name, so a
    /// file that was spoiled where it lay is never used.
    /// </remarks>
    internal sealed class FileAssetStore : IAssetStore
    {
        private readonly string _root;

        /// <param name="home">The directory pmj keeps its own files in.</param>
        public FileAssetStore(string home)
        {
            _root = Path.Combine(home, "cache");
        }

        public async ValueTask<ReadOnlyMemory<byte>?> FindAsync(PackageName name, SemanticVersion version, Sha256Digest digest, CancellationToken cancellationToken)
        {
            var path = ArchivePath(name, version, digest);
            try
            {
                return await ReadSoundAsync(path, digest, cancellationToken) is { } archive ? archive : null;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                throw new PackageSourceException(Unusable(path, "read", failure));
            }
        }

        public async ValueTask KeepAsync(PackageName name, SemanticVersion version, Sha256Digest digest, ReadOnlyMemory<byte> archive, CancellationToken cancellationToken)
        {
            var path = ArchivePath(name, version, digest);
            var beside = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                if (await ReadSoundAsync(path, digest, cancellationToken) is not null)
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(beside, archive, cancellationToken);
                File.SetAttributes(beside, FileAttributes.ReadOnly);

                // What is in the way was spoiled, and on some machines a file marked as not to be written cannot be replaced.
                Discard(path);
                File.Move(beside, path, overwrite: true);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Discard(beside);
                throw new PackageSourceException(Unusable(path, "written", failure));
            }
        }

        /// <summary>
        /// The directory that holds an archive's files, unpacked there if they are not yet. The files
        /// come from an archive that was read, so each is a file with a path inside the package: no
        /// link, and nothing that climbs out.
        /// </summary>
        public string Unpack(PackageName name, SemanticVersion version, Sha256Digest digest, IReadOnlyList<ArchiveFile> files)
        {
            var directory = DirectoryPath(name, version, digest);
            if (Directory.Exists(directory))
            {
                return directory;
            }

            var beside = directory + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var root = Path.GetFullPath(beside) + Path.DirectorySeparatorChar;
                foreach (var file in files)
                {
                    var target = Path.GetFullPath(Path.Combine(beside, file.Path.Value.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"The path \"{file.Path}\" leaves the directory it is unpacked into.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    Create(target, file, name, version);
                }

                try
                {
                    Directory.Move(beside, directory);
                }
                catch (IOException) when (Directory.Exists(directory))
                {
                    // Another pmj unpacked the same archive first, and what it unpacked is the same.
                    DiscardDirectory(beside);
                    return directory;
                }

                // Marked only once they are in place, so that what lost the race above can be deleted on every machine.
                foreach (var file in FileTree.List(directory))
                {
                    File.SetAttributes(Path.Combine(directory, file.Path.Replace('/', Path.DirectorySeparatorChar)), FileAttributes.ReadOnly);
                }

                return directory;
            }
            catch (PackageSourceException)
            {
                DiscardDirectory(beside);
                throw;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                DiscardDirectory(beside);
                throw new PackageSourceException(Unusable(directory, "written", failure));
            }
        }

        /// <summary>
        /// Every way in which what the cache holds of a package is not what it should be: an archive
        /// that is not the bytes its name says, and unpacked files that are not the files of the
        /// archive beside them. Nothing is changed. None when the cache holds nothing of the package.
        /// </summary>
        public IReadOnlyList<Diagnostic> Examine(PackageName name, SemanticVersion version, Sha256Digest digest)
        {
            var path = ArchivePath(name, version, digest);
            var directory = DirectoryPath(name, version, digest);
            var what = $"\"{name}\" {version}";
            var problems = new List<Diagnostic>();
            try
            {
                byte[]? sound = null;
                var archive = new FileInfo(path);
                if (archive.Exists && archive.Length > PackageArchive.MaxBytes)
                {
                    problems.Add(Spoiled(what, path, $"\"{path}\" is {archive.Length} bytes, which is more than an archive may be"));
                }
                else if (archive.Exists)
                {
                    var bytes = File.ReadAllBytes(path);
                    var actual = Sha256Digest.Of(bytes);
                    if (actual == digest)
                    {
                        sound = bytes;
                    }
                    else
                    {
                        problems.Add(Spoiled(what, path, $"\"{path}\" is kept as the archive with the digest {digest}, and its bytes have the digest {actual}"));
                    }
                }

                if (Directory.Exists(directory) && problems.Count == 0)
                {
                    var difference = sound is null
                        ? "the archive they were unpacked from is no longer beside them, so there is nothing to hold them to"
                        : PackageArchive.Read(sound) is { Succeeded: true } files ? Difference(directory, files.Value) : null;
                    if (difference is not null)
                    {
                        problems.Add(Spoiled(what, directory, $"the files unpacked in \"{directory}\" cannot be trusted: {difference}"));
                    }
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                problems.Add(Unusable(path, "read", failure));
            }

            return problems;
        }

        // Never written over a file that is there. Two names that are different text can be one file
        // on a disk that ignores case or how a letter is written, and the second would otherwise take
        // the place of the first without a word.
        private static void Create(string target, ArchiveFile file, PackageName name, SemanticVersion version)
        {
            try
            {
                using var created = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created.Write(file.Content.Span);
            }
            catch (IOException) when (File.Exists(target))
            {
                throw new PackageSourceException(new Diagnostic(
                    DiagnosticCodes.ArchiveInvalid,
                    $"The archive of \"{name}\" {version} cannot be unpacked on this disk.",
                    $"\"{file.Path}\" is the same file here as another of the package's files: their names differ only in a way this disk does not keep",
                    "tell its author: no two files of a package may have names that differ only by case, or by how a letter is written"));
            }
        }

        public string DirectoryPath(PackageName name, SemanticVersion version, Sha256Digest digest) =>
            Path.Combine(_root, name.Scope, name.Name, version.ToString(), digest.Hex);

        private string ArchivePath(PackageName name, SemanticVersion version, Sha256Digest digest) =>
            DirectoryPath(name, version, digest) + AssetName.Suffix;

        // The file at a path when it is the archive its name says, and null when it is not there or is not that.
        private static async Task<byte[]?> ReadSoundAsync(string path, Sha256Digest digest, CancellationToken cancellationToken)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > PackageArchive.MaxBytes)
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return Sha256Digest.Of(bytes) == digest ? bytes : null;
        }

        // The first way in which a directory is not exactly the files given, or null when it is exactly them.
        // Each file is looked for by its own path, so that a disk which spells a name another way still finds it.
        private static string? Difference(string directory, IReadOnlyList<ArchiveFile> files)
        {
            foreach (var file in files)
            {
                var unpacked = new FileInfo(Path.Combine(directory, file.Path.Value.Replace('/', Path.DirectorySeparatorChar)));
                if (!unpacked.Exists)
                {
                    return $"\"{file.Path}\" is missing";
                }

                if (unpacked.LinkTarget is not null || unpacked.Length != file.Content.Length || !File.ReadAllBytes(unpacked.FullName).AsSpan().SequenceEqual(file.Content.Span))
                {
                    return $"\"{file.Path}\" is not the file the archive holds";
                }
            }

            var found = FileTree.List(directory).Count;
            return found == files.Count ? null : $"there are {found} files there, and the archive holds {files.Count}";
        }

        private static Diagnostic Spoiled(string what, string path, string reason) =>
            new(
                DiagnosticCodes.CacheUnusable,
                $"The cache's copy of {what} is not what it should be.",
                reason,
                $"delete \"{path}\", and run pmj install, which fetches the package again");

        private static void Discard(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // What cannot be taken away is found again by whatever is done next, which reports it.
            }
        }

        private static void DiscardDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // A directory left beside its place is never read: it only takes room until the cache is cleared.
            }
        }

        private static Diagnostic Unusable(string path, string done, Exception failure) =>
            new(
                DiagnosticCodes.CacheUnusable,
                $"\"{path}\" in the cache could not be {done}.",
                failure.Message,
                "check that the cache is yours to use, or keep it somewhere else by setting PACKMOJI_HOME");
    }
}
