using Packmoji.Core.Identity;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// A registry as it stands at one moment: the file of each package, and the file of what was
    /// refused in each repository. It is the whole of a registry, and a run makes the next one of it.
    /// </summary>
    public sealed class RegistryState
    {
        /// <param name="packages">The packages, none twice.</param>
        /// <param name="refused">What was refused, a repository at most once. One with nothing refused has no file, and is left out.</param>
        public RegistryState(IEnumerable<RegistryPackage> packages, IEnumerable<RefusedReleases> refused)
        {
            ArgumentNullException.ThrowIfNull(packages);
            ArgumentNullException.ThrowIfNull(refused);
            Packages = packages.OrderBy(package => package.Name).ToList();
            Refused = refused.Where(file => file.Releases.Count > 0).OrderBy(file => file.Repository.ToString(), StringComparer.Ordinal).ToList();
        }

        /// <summary>A registry that holds nothing yet.</summary>
        public static RegistryState Empty { get; } = new([], []);

        /// <summary>The packages, in order of name.</summary>
        public IReadOnlyList<RegistryPackage> Packages { get; }

        /// <summary>What was refused, in order of repository. A repository with nothing refused is not here.</summary>
        public IReadOnlyList<RefusedReleases> Refused { get; }

        /// <summary>What is held of a package, or null when no version of it has been indexed.</summary>
        public RegistryPackage? Package(PackageName name) => Packages.FirstOrDefault(package => package.Name == name);

        /// <summary>What was refused in a repository, or null when nothing was.</summary>
        public RefusedReleases? RefusedIn(RepositoryRef repository) => Refused.FirstOrDefault(file => file.Repository == repository);

        /// <summary>Every file of the registry: its path, and its text in the one form it is written in.</summary>
        public IReadOnlyDictionary<string, string> Files()
        {
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var package in Packages)
            {
                files.Add(RegistryPaths.Package(package.Name), RegistryPackageWriter.Write(package));
            }

            foreach (var file in Refused)
            {
                files.Add(RegistryPaths.Refused(file.Repository), RefusedReleasesWriter.Write(file));
            }

            return files;
        }
    }
}
