using Packmoji.Core.Diagnostics;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Diagnostics
{
    public sealed class DiagnosticListTests
    {
        [Fact]
        public void A_diagnostic_that_there_is_no_room_for_is_counted_and_never_made()
        {
            var list = new DiagnosticList();
            var made = 0;

            for (var i = 0; i < 250; i++)
            {
                list.Add(() =>
                {
                    made++;
                    return new Diagnostic(DiagnosticCodes.ResolveVersionMissing, $"problem {made}", "a reason", "a fix");
                });
            }

            made.ShouldBe(DiagnosticList.Limit);
            list.Count.ShouldBe(DiagnosticList.Limit);
            list.Omitted.ShouldBe(150);
            list[99].Message.ShouldBe("problem 100");
        }
    }
}
