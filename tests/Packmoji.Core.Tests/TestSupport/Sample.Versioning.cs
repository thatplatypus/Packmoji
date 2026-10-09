using Packmoji.Core.Versioning;
using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static partial class Sample
    {
        public static SemanticVersion Version(string text)
        {
            SemanticVersion.TryParse(text, out var version, out var error).ShouldBeTrue(error?.Reason);
            return version!;
        }
    }
}
