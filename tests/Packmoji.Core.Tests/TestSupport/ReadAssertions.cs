using Packmoji.Core.Diagnostics;
using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static class ReadAssertions
    {
        public static T ShouldSucceed<T>(this ReadResult<T> result) where T : class
        {
            result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message} {diagnostic.Reason}").ShouldBeEmpty();
            result.Succeeded.ShouldBeTrue();
            return result.Value!;
        }

        /// <summary>Holds a read to having failed with exactly one diagnostic, of the given code, at the fixture's mark.</summary>
        public static Diagnostic ShouldFailAt<T>(this ReadResult<T> result, Marked marked, string code, string file) where T : class
        {
            result.Succeeded.ShouldBeFalse();
            result.Value.ShouldBeNull();
            result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe([code]);
            var diagnostic = result.Diagnostics[0].ShouldBeComplete();
            diagnostic.Location.ShouldBe(new SourceLocation(file, marked.Line, marked.Column));
            return diagnostic;
        }
    }
}
