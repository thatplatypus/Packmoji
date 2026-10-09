using Packmoji.Core.Versioning;
using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static partial class Sample
    {
        public static VersionRequirement Requirement(string text)
        {
            VersionRequirement.TryParse(text, out var requirement, out var error).ShouldBeTrue(error?.Reason);
            return requirement!;
        }

        public static CompilerRequirement Compiler(string text)
        {
            CompilerRequirement.TryParse(text, out var requirement, out var error).ShouldBeTrue(error?.Reason);
            return requirement!;
        }
    }
}
