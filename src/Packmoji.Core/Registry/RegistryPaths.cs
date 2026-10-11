using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Identity;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// Where each file of a registry is, under the directory of its first format. A path is written
    /// with forward slashes, as an address is, whatever the machine writes its own paths with.
    /// </summary>
    /// <remarks>
    /// A change to the files that an older pmj could not read gets a directory of its own beside
    /// this one. Nothing is then taken away from a pmj that reads this one.
    /// </remarks>
    public static class RegistryPaths
    {
        /// <summary>The directory of the first format.</summary>
        public const string Root = "v1";

        /// <summary>The directory of the packages' files.</summary>
        public const string PackagesDirectory = Root + "/packages";

        /// <summary>The directory of the files of what was refused.</summary>
        public const string RefusedDirectory = Root + "/refused";

        private const string Extension = ".json";

        /// <summary>The path of a package's file: under its scope, and named for it.</summary>
        public static string Package(PackageName name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return $"{PackagesDirectory}/{name.Scope}/{name.Name}{Extension}";
        }

        /// <summary>The path of the file of what was refused in a repository: under its owner, and named for it.</summary>
        public static string Refused(RepositoryRef repository)
        {
            ArgumentNullException.ThrowIfNull(repository);
            return $"{RefusedDirectory}/{repository.Owner}/{repository.Name}{Extension}";
        }

        /// <summary>
        /// The package whose file is at a path. A name has one spelling, so a path that gives a name
        /// is exactly the path that name's file has.
        /// </summary>
        public static bool TryParsePackage(string path, [NotNullWhen(true)] out PackageName? name)
        {
            ArgumentNullException.ThrowIfNull(path);
            name = null;
            return TrySplit(path, PackagesDirectory, out var owner, out var named)
                && PackageName.TryParse($"@{owner}/{named}", out name, out _);
        }

        /// <summary>The repository whose refused releases are at a path. A repository has one spelling too.</summary>
        public static bool TryParseRefused(string path, [NotNullWhen(true)] out RepositoryRef? repository)
        {
            ArgumentNullException.ThrowIfNull(path);
            repository = null;
            return TrySplit(path, RefusedDirectory, out var owner, out var named)
                && RepositoryRef.TryParse($"{RepositoryRef.Host}/{owner}/{named}", out repository, out _);
        }

        // <directory>/<owner>/<name>.json, and nothing more or less.
        private static bool TrySplit(string path, string directory, out string owner, out string named)
        {
            owner = "";
            named = "";
            var prefix = directory + "/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(Extension, StringComparison.Ordinal))
            {
                return false;
            }

            var parts = path[prefix.Length..^Extension.Length].Split('/');
            if (parts.Length != 2)
            {
                return false;
            }

            owner = parts[0];
            named = parts[1];
            return true;
        }
    }
}
