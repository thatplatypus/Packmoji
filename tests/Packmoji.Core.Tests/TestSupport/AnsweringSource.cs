using Packmoji.Core.Identity;
using Packmoji.Core.Resolution;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Tests.TestSupport
{
    /// <summary>
    /// A package source that works out each answer when it is asked: for graphs too large to write
    /// down, and for sources that do what a source must not.
    /// </summary>
    internal sealed class AnsweringSource : IPackageSource
    {
        private readonly Func<PackageName, SemanticVersion, PublishedVersion?> _answer;

        public AnsweringSource(Func<PackageName, SemanticVersion, PublishedVersion?> answer)
        {
            _answer = answer;
        }

        /// <summary>How many versions have been asked for.</summary>
        public int Asked { get; private set; }

        public ValueTask<PublishedVersion?> FindAsync(PackageName name, SemanticVersion version, CancellationToken cancellationToken)
        {
            Asked++;
            return ValueTask.FromResult(_answer(name, version));
        }
    }
}
