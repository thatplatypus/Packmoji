using Packmoji.Core.Diagnostics;
using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static class DiagnosticAssertions
    {
        /// <summary>
        /// Holds a diagnostic to the rule every Packmoji error follows: it says what failed, why, and
        /// what to do next, and it holds no em dash.
        /// </summary>
        public static Diagnostic ShouldBeComplete(this Diagnostic? diagnostic)
        {
            diagnostic.ShouldNotBeNull();
            diagnostic.Code.ShouldNotBeNullOrWhiteSpace();
            diagnostic.Message.ShouldNotBeNullOrWhiteSpace();
            diagnostic.Reason.ShouldNotBeNullOrWhiteSpace();
            diagnostic.Fix.ShouldNotBeNullOrWhiteSpace();
            (diagnostic.Message + diagnostic.Reason + diagnostic.Fix).ShouldNotContain(char.ConvertFromUtf32(0x2014));
            return diagnostic;
        }
    }
}
