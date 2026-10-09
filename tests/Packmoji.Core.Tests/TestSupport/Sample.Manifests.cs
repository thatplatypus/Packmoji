using Packmoji.Core.Manifests;
using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static partial class Sample
    {
        public static RelativePath Path(string text)
        {
            RelativePath.TryParse(text, out var path, out var error).ShouldBeTrue(error?.Reason);
            return path!;
        }

        public static GlobPattern Glob(string text)
        {
            GlobPattern.TryParse(text, out var pattern, out var error).ShouldBeTrue(error?.Reason);
            return pattern!;
        }

        public static SpdxExpression License(string text)
        {
            SpdxExpression.TryParse(text, out var expression, out var error).ShouldBeTrue(error?.Reason);
            return expression!;
        }
    }
}
