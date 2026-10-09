using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Lockfiles
{
    /// <summary>
    /// Emojicode cannot build packages that import one another in a circle, and pmj never writes a
    /// lockfile that holds one. So a lockfile that does was not written by pmj as it stands, and it is
    /// refused where it is read, which needs nothing but the file.
    /// </summary>
    public sealed class LockfileCycleTests
    {
        private static string Pin(string name) => $"\"@thatplatypus/{name}@1.0.0\"";

        // An entry for @thatplatypus/<name> at 1.0.0. Each pin is given as it stands in the file, so that one can carry the mark.
        private static string Entry(string name, params string[] pins) =>
            "    {\n" +
            $"      \"name\": \"@thatplatypus/{name}\",\n" +
            "      \"version\": \"1.0.0\",\n" +
            $"      \"source\": \"github.com/thatplatypus/{name}\",\n" +
            $"      \"releaseTag\": \"{name}-v1.0.0\",\n" +
            $"      \"asset\": \"{name}-1.0.0.pmj.tar.gz\",\n" +
            $"      \"sha256\": \"{new string('a', 64)}\",\n" +
            "      \"verified\": \"checksum\",\n" +
            $"      \"dependencies\": [{string.Join(", ", pins)}]\n" +
            "    }";

        private static string Lock(string[] roots, params string[] entries) =>
            "{\n  \"version\": 1,\n  \"root\": {\n    \"dependencies\": [" +
            string.Join(", ", roots.Select(root => $"\"@thatplatypus/{root}@1.0\"")) +
            "],\n    \"devDependencies\": []\n  },\n  \"packages\": [\n" +
            string.Join(",\n", entries) +
            "\n  ]\n}\n";

        [Fact]
        public void A_circle_of_two_packages_is_refused_at_the_dependency_that_closes_it()
        {
            var marked = Marked.From(Lock(["a"], Entry("a", Pin("b")), Entry("b", "§" + Pin("a"))));

            var diagnostic = LockfileReader.Read(marked.Text).ShouldFailAt(marked, DiagnosticCodes.ResolveCycle, LockfileReader.FileName);

            diagnostic.Message.ShouldBe("\"@thatplatypus/a\" depends on itself through other packages.");
            diagnostic.Reason.ShouldEndWith(": @thatplatypus/a@1.0.0 → @thatplatypus/b@1.0.0 → @thatplatypus/a@1.0.0");
            diagnostic.Fix.ShouldBe(LockfileReader.RegenerateFix);
        }

        [Fact]
        public void A_circle_of_three_is_refused_where_the_last_of_them_leads_back_to_the_first()
        {
            var marked = Marked.From(Lock(
                ["b"],
                Entry("c", "§" + Pin("a")),
                Entry("a", Pin("b")),
                Entry("b", Pin("c"))));

            var diagnostic = LockfileReader.Read(marked.Text).ShouldFailAt(marked, DiagnosticCodes.ResolveCycle, LockfileReader.FileName);

            diagnostic.Reason.ShouldEndWith(": @thatplatypus/a@1.0.0 → @thatplatypus/b@1.0.0 → @thatplatypus/c@1.0.0 → @thatplatypus/a@1.0.0");
        }

        [Fact]
        public void A_circle_that_the_manifest_only_leads_into_is_refused_at_the_one_dependency_that_closes_it()
        {
            var marked = Marked.From(Lock(
                ["top"],
                Entry("top", Pin("x")),
                Entry("x", Pin("y")),
                Entry("y", Pin("leaf"), "§" + Pin("x")),
                Entry("leaf")));

            LockfileReader.Read(marked.Text).ShouldFailAt(marked, DiagnosticCodes.ResolveCycle, LockfileReader.FileName);
        }

        [Fact]
        public void Two_circles_are_two_problems()
        {
            var result = LockfileReader.Read(Lock(
                ["a", "x"],
                Entry("a", Pin("b")),
                Entry("b", Pin("a")),
                Entry("x", Pin("y")),
                Entry("y", Pin("x"))));

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe([DiagnosticCodes.ResolveCycle, DiagnosticCodes.ResolveCycle]);
            result.Diagnostics[0].ShouldBeComplete().Message.ShouldContain("\"@thatplatypus/a\"");
            result.Diagnostics[1].ShouldBeComplete().Message.ShouldContain("\"@thatplatypus/x\"");
        }

        [Fact]
        public void Packages_that_share_a_dependency_are_not_a_circle()
        {
            LockfileReader.Read(Lock(
                ["a"],
                Entry("a", Pin("b"), Pin("c")),
                Entry("b", Pin("d")),
                Entry("c", Pin("d")),
                Entry("d"))).ShouldSucceed();
        }

        [Fact]
        public void A_lockfile_that_does_not_hold_together_in_a_plainer_way_is_reported_for_that_alone()
        {
            var result = LockfileReader.Read(Lock(
                ["a"],
                Entry("a", Pin("b")),
                Entry("b", Pin("a")),
                Entry("stray")));

            result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe([DiagnosticCodes.LockUnreachable]);
        }

        [Fact]
        public void A_circle_of_thousands_of_packages_is_refused_in_a_few_words()
        {
            const int count = 3_000;
            var entries = Enumerable.Range(0, count).Select(number => Entry($"p{number}", Pin($"p{(number + 1) % count}"))).ToArray();

            var result = LockfileReader.Read(Lock(["p0"], entries));

            var diagnostic = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.ResolveCycle);
            diagnostic.Reason.ShouldContain("(2994 more)");
            diagnostic.Reason.ShouldEndWith("→ @thatplatypus/p0@1.0.0");
            diagnostic.Reason.Length.ShouldBeLessThan(500);
        }
    }
}
