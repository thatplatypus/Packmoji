using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Identity
{
    public sealed class RepositoryRefTests
    {
        [Theory]
        [InlineData("github.com/thatplatypus/grapevine", "thatplatypus", "grapevine")]
        [InlineData("github.com/my-org/emoji_crypto", "my-org", "emoji_crypto")]
        [InlineData("github.com/a/b.c-d_e", "a", "b.c-d_e")]
        [InlineData("github.com/9lives/.github", "9lives", ".github")]
        public void A_valid_repository_is_parsed(string text, string owner, string name)
        {
            RepositoryRef.TryParse(text, out var repository, out var error).ShouldBeTrue();

            error.ShouldBeNull();
            repository.ShouldNotBeNull();
            repository.Owner.ShouldBe(owner);
            repository.Name.ShouldBe(name);
            repository.ToString().ShouldBe(text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("thatplatypus/grapevine")]
        [InlineData("https://github.com/thatplatypus/grapevine")]
        [InlineData("github.com/thatplatypus/grapevine/")]
        [InlineData("github.com/thatplatypus/grapevine.git")]
        [InlineData("github.com/thatplatypus/grapevine/tree/main")]
        [InlineData("github.com/thatplatypus")]
        [InlineData("github.com/thatplatypus/")]
        [InlineData("github.com//grapevine")]
        [InlineData("github.com/Thatplatypus/Grapevine")]
        [InlineData("GITHUB.COM/thatplatypus/grapevine")]
        [InlineData("gitlab.com/thatplatypus/grapevine")]
        [InlineData("github.com/thatplatypus/.")]
        [InlineData("github.com/thatplatypus/..")]
        [InlineData("github.com/that_platypus/grapevine")]
        [InlineData("github.com/thatplatypus/grape vine")]
        public void An_invalid_repository_is_refused(string text)
        {
            RepositoryRef.TryParse(text, out var repository, out var error).ShouldBeFalse();

            repository.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.RepositoryInvalid);
        }

        [Theory]
        [InlineData("https://github.com/Thatplatypus/Grapevine.git")]
        [InlineData("github.com/thatplatypus/grapevine/")]
        [InlineData("GITHUB.COM/thatplatypus/grapevine")]
        [InlineData("http://www.github.com/thatplatypus/grapevine")]
        public void The_fix_names_the_spelling_that_was_meant(string text)
        {
            RepositoryRef.TryParse(text, out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Fix.ShouldContain("\"github.com/thatplatypus/grapevine\"");
        }

        [Fact]
        public void A_repository_name_may_be_100_characters_and_no_more()
        {
            RepositoryRef.TryParse("github.com/a/" + new string('b', 100), out _, out _).ShouldBeTrue();
            RepositoryRef.TryParse("github.com/a/" + new string('b', 101), out _, out _).ShouldBeFalse();
        }

        [Fact]
        public void A_package_lives_by_default_in_a_repository_of_its_own_name_under_its_scope()
        {
            var repository = RepositoryRef.DefaultFor(Sample.Name("@thatplatypus/emoji_crypto"));

            repository.ToString().ShouldBe("github.com/thatplatypus/emoji_crypto");
            repository.ShouldBe(Sample.Repository("github.com/thatplatypus/emoji_crypto"));
        }

        [Fact]
        public void A_repository_belongs_to_a_package_whose_scope_is_its_owner()
        {
            var repository = Sample.Repository("github.com/thatplatypus/grapevine");

            repository.BelongsTo(Sample.Name("@thatplatypus/crypto")).ShouldBeTrue();
            repository.BelongsTo(Sample.Name("@someone/crypto")).ShouldBeFalse();
        }
    }
}
