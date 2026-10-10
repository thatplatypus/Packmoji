using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Reports
{
    /// <summary>One package that a new lockfile holds differently from the one before it.</summary>
    /// <param name="From">The version before. Null for a package that was added.</param>
    /// <param name="To">The version now. Null for a package that was removed.</param>
    public sealed record LockChange(LockChangeKind Kind, PackageName Name, SemanticVersion? From, SemanticVersion? To)
    {
        /// <summary>The change as a person is told it: <c>+ name version</c>, <c>- name version</c> or <c>~ name old to new</c>.</summary>
        public string Line => Kind switch
        {
            LockChangeKind.Added => $"+ {Name} {To}",
            LockChangeKind.Removed => $"- {Name} {From}",
            _ => $"~ {Name} {From} to {To}",
        };
    }
}
