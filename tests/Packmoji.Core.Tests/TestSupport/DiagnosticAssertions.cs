using Packmoji.Core.Diagnostics;
using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static class DiagnosticAssertions
    {
        /// <summary>
        /// Holds a diagnostic to the rule every Packmoji error follows: it says what failed, why, and
        /// what to do next, it holds no em dash, and nothing in it could drive or deceive a terminal.
        /// </summary>
        public static Diagnostic ShouldBeComplete(this Diagnostic? diagnostic)
        {
            diagnostic.ShouldNotBeNull();
            diagnostic.Code.ShouldNotBeNullOrWhiteSpace();
            diagnostic.Message.ShouldNotBeNullOrWhiteSpace();
            diagnostic.Reason.ShouldNotBeNullOrWhiteSpace();
            diagnostic.Fix.ShouldNotBeNullOrWhiteSpace();
            (diagnostic.Message + diagnostic.Reason + diagnostic.Fix).ShouldNotContain(char.ConvertFromUtf32(0x2014));
            TextSafety.HasUnsafe(diagnostic.Message + diagnostic.Reason + diagnostic.Fix).ShouldBeFalse();
            return diagnostic;
        }
    }
}
