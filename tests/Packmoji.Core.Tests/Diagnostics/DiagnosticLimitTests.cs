using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Diagnostics
{
    public sealed class DiagnosticLimitTests
    {
        [Fact]
        public void A_manifest_with_a_problem_at_every_key_has_the_first_hundred_listed_and_the_rest_counted()
        {
            var keys = string.Join(",", Enumerable.Range(0, 10_000).Select(number => $"\"k{number}\": 0"));

            var result = ManifestReader.Read("{" + keys + "}");

            // Ten thousand unknown keys, and "package" is missing.
            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Count.ShouldBe(100);
            result.OmittedDiagnostics.ShouldBe(9_901);
            result.Diagnostics[0].Code.ShouldBe(DiagnosticCodes.KeyMissing);
            result.Diagnostics[99].Code.ShouldBe(DiagnosticCodes.KeyUnknown);
        }

        [Fact]
        public void A_lockfile_is_held_to_the_same_limit_and_every_listed_problem_keeps_its_fix()
        {
            var entries = string.Join(",", Enumerable.Repeat("7", 500));

            var result = LockfileReader.Read("{\"version\": 1, \"root\": {\"dependencies\": [], \"devDependencies\": []}, \"packages\": [" + entries + "]}");

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Count.ShouldBe(100);
            result.OmittedDiagnostics.ShouldBe(400);
            result.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Fix == LockfileReader.RegenerateFix);
        }

        [Fact]
        public void A_read_with_few_problems_or_none_omits_nothing()
        {
            ManifestReader.Read(Fixtures.MinimalManifest).OmittedDiagnostics.ShouldBe(0);
            ManifestReader.Read("{}").OmittedDiagnostics.ShouldBe(0);
            ManifestReader.Read("oops").OmittedDiagnostics.ShouldBe(0);
            LockfileReader.Read(Fixtures.Lockfile).OmittedDiagnostics.ShouldBe(0);
        }

        [Fact]
        public void A_syntax_error_after_many_problems_is_still_the_only_one_and_nothing_is_counted_as_omitted()
        {
            var repeated = string.Join(",", Enumerable.Repeat("\"k\": 0", 500));

            var result = ManifestReader.Read("{" + repeated + ", oops}");

            result.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe(DiagnosticCodes.JsonSyntax);
            result.OmittedDiagnostics.ShouldBe(0);
        }

        [Fact]
        public void The_list_keeps_a_hundred_and_counts_the_rest_and_starts_again_when_cleared()
        {
            var list = new DiagnosticList();
            for (var i = 0; i < 250; i++)
            {
                list.Add(new Diagnostic(DiagnosticCodes.KeyUnknown, $"problem {i}", "a reason", "a fix"));
            }

            list.Count.ShouldBe(DiagnosticList.Limit);
            list.Omitted.ShouldBe(150);
            list[0].Message.ShouldBe("problem 0");
            list[99].Message.ShouldBe("problem 99");
            list.Select(diagnostic => diagnostic.Message).Last().ShouldBe("problem 99");

            list.Clear();

            list.ShouldBeEmpty();
            list.Omitted.ShouldBe(0);
        }
    }
}
