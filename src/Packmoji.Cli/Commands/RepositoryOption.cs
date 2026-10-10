using Packmoji.Cli.Projects;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// What <c>--repository</c> is given: a place to look in as well. A name says who owns a package
    /// and not always where it is kept, and the lockfile is the only record of where a package was
    /// found. So every command that resolves can be told where to look, for the one case that cannot
    /// be worked out and for a project whose lockfile is gone.
    /// </summary>
    internal static class RepositoryOption
    {
        public const string Name = "--repository";

        /// <summary>The repositories that were named, or why one of them is not a repository.</summary>
        public static Outcome<IReadOnlyList<RepositoryRef>> Read(IReadOnlyList<string> given)
        {
            var repositories = new List<RepositoryRef>();
            var refused = new List<Diagnostic>();
            foreach (var text in given)
            {
                if (RepositoryRef.TryParse(text, out var repository, out var error))
                {
                    repositories.Add(repository);
                }
                else
                {
                    refused.Add(error);
                }
            }

            return refused.Count > 0 ? Outcome<IReadOnlyList<RepositoryRef>>.Failed(refused) : Outcome<IReadOnlyList<RepositoryRef>>.Of(repositories);
        }
    }
}
