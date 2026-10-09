using Packmoji.Core.Identity;
using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    /// <summary>Makes valid values for tests that are about something else.</summary>
    internal static partial class Sample
    {
        public static PackageName Name(string text)
        {
            PackageName.TryParse(text, out var name, out var error).ShouldBeTrue(error?.Reason);
            return name!;
        }
    }
}
