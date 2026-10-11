using Packmoji.Core.Diagnostics;
using Packmoji.Core.Registry;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// The list of the repositories a registry indexes. A person writes it and a pull request changes
    /// it, so it has one form, and what is wrong with a line is said at that line.
    /// </summary>
    public sealed class RepositoryListTests
    {
        private const string File = "registry-repositories.txt";

        private static string[] Read(string text) => RepositoryList.Read(text).ShouldSucceed().Repositories.Select(repository => repository.ToString()).ToArray();

        private static Diagnostic ShouldFailAtMark(string fixture, string code)
        {
            var marked = Marked.From(fixture);
            return RepositoryList.Read(marked.Text).ShouldFailAt(marked, code, File);
        }

        [Fact]
        public void A_list_is_one_repository_to_a_line() =>
            Read("github.com/emojicode/emojicode\ngithub.com/thatplatypus/crypto\ngithub.com/thatplatypus/grapevine\n")
                .ShouldBe(["github.com/emojicode/emojicode", "github.com/thatplatypus/crypto", "github.com/thatplatypus/grapevine"]);

        [Fact]
        public void A_line_that_begins_with_a_hash_and_a_line_with_nothing_on_it_are_for_a_person() =>
            Read("# The repositories of the registry.\n\n#github.com/thatplatypus/crypto\ngithub.com/thatplatypus/grapevine\n\n# The end.\n")
                .ShouldBe(["github.com/thatplatypus/grapevine"]);

        [Theory]
        [InlineData("")]
        [InlineData("\n")]
        [InlineData("# Nothing yet.\n")]
        public void A_list_with_no_repository_reads_as_none(string text) => Read(text).ShouldBeEmpty();

        [Fact]
        public void The_line_ends_of_Windows_and_a_last_line_with_no_end_read_the_same() =>
            Read("github.com/thatplatypus/crypto\r\ngithub.com/thatplatypus/grapevine").ShouldBe(["github.com/thatplatypus/crypto", "github.com/thatplatypus/grapevine"]);

        [Theory]
        [InlineData("§thatplatypus/grapevine\n")]
        [InlineData("§https://github.com/thatplatypus/grapevine\n")]
        [InlineData("§github.com/thatplatypus/Grapevine\n")]
        [InlineData("§github.com/thatplatypus/grapevine.git\n")]
        [InlineData("§ github.com/thatplatypus/grapevine\n")]
        [InlineData("§github.com/thatplatypus/grapevine \n")]
        [InlineData("§github.com/thatplatypus/grapevine # the first\n")]
        [InlineData("§  # indented\n")]
        [InlineData("github.com/thatplatypus/crypto\n§github.com/thatplatypus\n")]
        public void A_line_that_is_not_a_repository_as_a_manifest_writes_one_is_refused_at_that_line(string fixture) =>
            ShouldFailAtMark(fixture, DiagnosticCodes.RepositoryInvalid).Fix.ShouldNotBeNullOrWhiteSpace();

        [Fact]
        public void A_repository_that_is_there_twice_is_refused_at_the_second()
        {
            var diagnostic = ShouldFailAtMark("github.com/thatplatypus/grapevine\n§github.com/thatplatypus/grapevine\n", DiagnosticCodes.ListDuplicate);

            diagnostic.Message.ShouldContain("github.com/thatplatypus/grapevine");
            diagnostic.Reason.ShouldContain("line 1");
        }

        [Fact]
        public void A_repository_that_is_out_of_order_is_refused_and_told_what_it_comes_before()
        {
            var diagnostic = ShouldFailAtMark("github.com/thatplatypus/grapevine\n# next\n§github.com/thatplatypus/crypto\n", DiagnosticCodes.RegistryInvalid);

            diagnostic.Message.ShouldContain("github.com/thatplatypus/crypto");
            diagnostic.Fix.ShouldContain("before github.com/thatplatypus/grapevine");
        }

        [Fact]
        public void A_line_is_held_to_every_line_before_it_and_not_only_to_the_first()
        {
            var diagnostic = ShouldFailAtMark("github.com/thatplatypus/crypto\ngithub.com/thatplatypus/grapevine\n§github.com/thatplatypus/deflate\n", DiagnosticCodes.RegistryInvalid);

            diagnostic.Fix.ShouldContain("before github.com/thatplatypus/grapevine");
        }

        [Fact]
        public void Each_line_out_of_place_is_one_problem_and_a_line_in_its_place_after_one_is_none()
        {
            var result = RepositoryList.Read("github.com/thatplatypus/grapevine\ngithub.com/thatplatypus/crypto\ngithub.com/thatplatypus/zebra\ngithub.com/thatplatypus/deflate\n");

            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Location!.Line)).ShouldBe([(DiagnosticCodes.RegistryInvalid, 2), (DiagnosticCodes.RegistryInvalid, 4)]);
            result.Diagnostics[1].Fix.ShouldContain("before github.com/thatplatypus/grapevine");
        }

        [Fact]
        public void Every_line_that_is_wrong_is_said()
        {
            var result = RepositoryList.Read("github.com/thatplatypus/grapevine\nnot one\ngithub.com/thatplatypus/crypto\ngithub.com/thatplatypus/grapevine\n");

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Location!.Line)).ShouldBe(
            [
                (DiagnosticCodes.RepositoryInvalid, 2),
                (DiagnosticCodes.RegistryInvalid, 3),
                (DiagnosticCodes.ListDuplicate, 4),
            ]);
        }

        [Fact]
        public void A_list_over_a_mebibyte_is_refused_before_any_of_it_is_read()
        {
            var diagnostic = RepositoryList.Read(new string('#', RepositoryList.MaxBytes + 1)).Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();

            diagnostic.Code.ShouldBe(DiagnosticCodes.FileTooLarge);
            diagnostic.Reason.ShouldContain("1048576");
        }

        [Fact]
        public void The_list_in_the_repository_reads_and_holds_Grapevines_repository()
        {
            var text = System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, RepositoryList.FileName));

            Read(text).ShouldBe(["github.com/thatplatypus/grapevine"]);
            text.ShouldNotContain("\r");
            text.ShouldEndWith("\n");
            text.ShouldNotContain(char.ConvertFromUtf32(0x2014));
        }
    }
}
