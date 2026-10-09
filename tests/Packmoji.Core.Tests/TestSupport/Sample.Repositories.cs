using Packmoji.Core.Identity;
using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static partial class Sample
    {
        public static RepositoryRef Repository(string text)
        {
            RepositoryRef.TryParse(text, out var repository, out var error).ShouldBeTrue(error?.Reason);
            return repository!;
        }
    }
}
