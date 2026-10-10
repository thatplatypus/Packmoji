using Packmoji.Core.Building;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Versioning;

namespace Packmoji.Cli.Building
{
    /// <summary>
    /// What pmj keeps of built packages, for every project on the machine: each under the key that
    /// names all it was built from, so that a package is compiled once and never again for a project
    /// that locks the same thing.
    /// </summary>
    /// <remarks>
    /// Nothing here takes a lock, as nothing in the cache does. An entry is made in a directory beside
    /// its place and put there in one step, so it is there whole or not at all, and of two runs of
    /// pmj that build the same package the second finds it done.
    /// </remarks>
    internal sealed class BuiltStore(string home)
    {
        private readonly string _root = Path.Combine(home, "built");

        /// <summary>The directory of an entry. It holds one folder, named for the package, so that the entry itself is a place the compiler can be told to search.</summary>
        public string EntryDirectory(PackageName name, SemanticVersion version, Sha256Digest key) =>
            Path.Combine(_root, name.Scope, name.Name, version.ToString(), key.Hex);

        /// <summary>The folder that holds what others need of a built package: its interface, its archive, and its stamp.</summary>
        public string PackageDirectory(PackageName name, SemanticVersion version, Sha256Digest key) =>
            Path.Combine(EntryDirectory(name, version, key), name.Name);

        /// <summary>Whether a package has been built under a key. An entry counts when its stamp gives that key.</summary>
        public bool Holds(PackageName name, SemanticVersion version, Sha256Digest key)
        {
            var stamp = Path.Combine(PackageDirectory(name, version, key), BuildStamp.FileName);
            try
            {
                return File.Exists(stamp) && BuildStamp.KeyIn(File.ReadAllBytes(stamp)) == key;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                throw new PackageSourceException(Unusable(stamp, "read", failure));
            }
        }

        public StagedBuild Begin(PackageName name, SemanticVersion version, Sha256Digest key)
        {
            var root = EntryDirectory(name, version, key) + ".tmp-" + Guid.NewGuid().ToString("N");
            var staged = new StagedBuild(root, Path.Combine(root, name.Name), Path.Combine(root, "work"));
            try
            {
                Directory.CreateDirectory(staged.PackageDirectory);
                Directory.CreateDirectory(staged.WorkDirectory);
                return staged;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                throw new PackageSourceException(Unusable(root, "written", failure));
            }
        }

        /// <summary>Puts what was built in its place, with its stamp beside it.</summary>
        public void Keep(StagedBuild staged, BuildStamp stamp)
        {
            ArgumentNullException.ThrowIfNull(staged);
            ArgumentNullException.ThrowIfNull(stamp);
            var entry = EntryDirectory(stamp.Name, stamp.Version, stamp.Key);
            try
            {
                File.WriteAllText(Path.Combine(staged.PackageDirectory, BuildStamp.FileName), stamp.Write());
                Directory.Delete(staged.WorkDirectory, recursive: true);

                // What is in the way and gives no key is not an entry: something was stopped part way, or spoiled where it lay.
                if (Directory.Exists(entry) && !Holds(stamp.Name, stamp.Version, stamp.Key))
                {
                    Remove(entry);
                }

                try
                {
                    Directory.Move(staged.Root, entry);
                }
                catch (IOException) when (Directory.Exists(entry))
                {
                    // Another pmj built the same package first, and what it built is the same.
                    Discard(staged);
                    return;
                }

                // Marked only once they are in place: they are every project's from now on.
                foreach (var file in Directory.EnumerateFiles(PackageDirectory(stamp.Name, stamp.Version, stamp.Key)))
                {
                    File.SetAttributes(file, FileAttributes.ReadOnly);
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Discard(staged);
                throw new PackageSourceException(Unusable(entry, "written", failure));
            }
        }

        /// <summary>Throws away what was being built. What cannot be taken away is never read: it only takes room.</summary>
        public static void Discard(StagedBuild staged)
        {
            ArgumentNullException.ThrowIfNull(staged);
            try
            {
                Remove(staged.Root);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // The problem that is reported is the one that led here.
            }
        }

        // A file marked as not to be written cannot be deleted on every machine until the mark is off.
        private static void Remove(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(directory, recursive: true);
        }

        private static Diagnostic Unusable(string path, string done, Exception failure) =>
            new(
                DiagnosticCodes.BuiltUnusable,
                $"\"{path}\", among the built packages pmj keeps, could not be {done}.",
                failure.Message,
                "check that the directory is yours to use, or keep pmj's files somewhere else by setting PACKMOJI_HOME; what is kept there can be deleted at any time, and is built again");
    }
}
