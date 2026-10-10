using Packmoji.Core.Identity;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Identity
{
    /// <summary>
    /// The scopes a pmj may depend on, as whoever runs it has said. It is a limit for a machine that
    /// runs other people's projects, so what cannot be read as a list must never be read as no limit.
    /// </summary>
    public sealed class ScopeLimitTests
    {
        private static ScopeLimit Read(string? text)
        {
            ScopeLimit.TryParse(text, out var limit, out var reason).ShouldBeTrue(reason);
            return limit!;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void With_nothing_said_there_is_no_limit(string? text)
        {
            var limit = Read(text);

            limit.IsSet.ShouldBeFalse();
            limit.Allows(Sample.Name("@anyone/anything")).ShouldBeTrue();
            limit.AllowsOwner("anyone").ShouldBeTrue();
            ScopeLimit.None.IsSet.ShouldBeFalse();
        }

        [Fact]
        public void A_list_allows_the_scopes_it_names_and_no_other()
        {
            var limit = Read("thatplatypus,emojicode");

            limit.IsSet.ShouldBeTrue();
            limit.Allows(Sample.Name("@thatplatypus/grapevine")).ShouldBeTrue();
            limit.Allows(Sample.Name("@emojicode/extras")).ShouldBeTrue();
            limit.Allows(Sample.Name("@someone/thing")).ShouldBeFalse();
            limit.Allows(Sample.Name("@thatplatypus-too/thing")).ShouldBeFalse();
            limit.AllowsOwner("emojicode").ShouldBeTrue();
            limit.AllowsOwner("someone").ShouldBeFalse();
        }

        [Fact]
        public void Space_around_a_name_is_no_part_of_it_and_a_name_given_twice_is_one_name()
        {
            var limit = Read(" thatplatypus , emojicode,thatplatypus ");

            limit.Scopes.ShouldBe(["emojicode", "thatplatypus"]);
        }

        [Theory]
        [InlineData("thatplatypus", "thatplatypus")]
        [InlineData("thatplatypus,emojicode", "emojicode and thatplatypus")]
        [InlineData("c,a,b", "a, b and c")]
        public void For_a_problem_the_scopes_are_named_in_order_as_a_person_would_say_them(string text, string named)
        {
            Read(text).ToString().ShouldBe(named);
        }

        [Theory]
        [InlineData("thatplatypus,,emojicode", "it has a comma with no scope beside it")]
        [InlineData("thatplatypus,", "it has a comma with no scope beside it")]
        [InlineData(",", "it has a comma with no scope beside it")]
        [InlineData("   ", "it names no scope")]
        [InlineData("@thatplatypus", "\"@thatplatypus\" is not a scope: a scope is written without the @")]
        [InlineData("thatplatypus,Emojicode", "\"Emojicode\" is not a scope: a scope is a GitHub owner's name in lowercase, of letters, digits and single hyphens")]
        [InlineData("that platypus", "\"that platypus\" is not a scope: a scope is a GitHub owner's name in lowercase, of letters, digits and single hyphens")]
        [InlineData("*", "\"*\" is not a scope: a scope is a GitHub owner's name in lowercase, of letters, digits and single hyphens")]
        [InlineData("-x", "\"-x\" is not a scope: a scope is a GitHub owner's name in lowercase, of letters, digits and single hyphens")]
        public void What_cannot_be_read_as_a_list_of_scopes_is_refused_and_is_never_no_limit(string text, string why)
        {
            ScopeLimit.TryParse(text, out var limit, out var reason).ShouldBeFalse();

            limit.ShouldBeNull();
            reason.ShouldBe(why);
        }

        [Fact]
        public void A_package_outside_the_list_is_refused_in_words_for_someone_who_never_saw_the_list()
        {
            var problem = Read("thatplatypus,emojicode").Refuses(Sample.Name("@someone/thing"), "packmoji.json asks for it");

            problem.Code.ShouldBe("scope.not-allowed");
            problem.Message.ShouldBe("\"@someone/thing\" is outside the scopes pmj is limited to here.");
            problem.Reason.ShouldBe("packmoji.json asks for it, and PACKMOJI_SCOPES allows only emojicode and thatplatypus");
            problem.Fix.ShouldBe("depend only on packages of those scopes; the list is set by whoever runs pmj here");
        }

        [Fact]
        public void With_one_scope_allowed_the_fix_speaks_of_one()
        {
            var problem = Read("thatplatypus").Refuses(Sample.Name("@someone/thing"), "packmoji.lock holds it");

            problem.Reason.ShouldBe("packmoji.lock holds it, and PACKMOJI_SCOPES allows only thatplatypus");
            problem.Fix.ShouldBe("depend only on packages of that scope; the list is set by whoever runs pmj here");
        }
    }
}
