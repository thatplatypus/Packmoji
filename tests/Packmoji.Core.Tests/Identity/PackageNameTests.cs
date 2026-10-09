using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Identity
{
    public sealed class PackageNameTests
    {
        [Theory]
        [InlineData("@thatplatypus/crypto", "thatplatypus", "crypto")]
        [InlineData("@a/b", "a", "b")]
        [InlineData("@my-org/emoji_crypto", "my-org", "emoji_crypto")]
        [InlineData("@9lives/x9", "9lives", "x9")]
        [InlineData("@a1-b2-c3/a1_b2_c3", "a1-b2-c3", "a1_b2_c3")]
        public void A_valid_name_is_parsed(string text, string scope, string name)
        {
            PackageName.TryParse(text, out var parsed, out var error).ShouldBeTrue();

            error.ShouldBeNull();
            parsed.ShouldNotBeNull();
            parsed.Scope.ShouldBe(scope);
            parsed.Name.ShouldBe(name);
            parsed.ImportName.ShouldBe(name);
            parsed.ToString().ShouldBe(text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("crypto")]
        [InlineData("thatplatypus/crypto")]
        [InlineData("@thatplatypus")]
        [InlineData("@thatplatypus/")]
        [InlineData("@/crypto")]
        [InlineData("@thatplatypus/crypto/extra")]
        [InlineData("@@thatplatypus/crypto")]
        [InlineData("@thatplatypus/9lives")]
        [InlineData("@thatplatypus/_crypto")]
        [InlineData("@thatplatypus/crypto_")]
        [InlineData("@thatplatypus/emoji__crypto")]
        [InlineData("@thatplatypus/emoji.crypto")]
        [InlineData("@thatplatypus/emoji crypto")]
        [InlineData("@thatplatypus/crypto ")]
        [InlineData(" @thatplatypus/crypto")]
        [InlineData("@-thatplatypus/crypto")]
        [InlineData("@thatplatypus-/crypto")]
        [InlineData("@that--platypus/crypto")]
        [InlineData("@that_platypus/crypto")]
        [InlineData("@thatplatypus/сrypto")] // the first letter is a Cyrillic es
        [InlineData("@thatplatypus/crypt０")] // the last character is a fullwidth zero
        [InlineData("@thatplatypus/crypto🍇")]
        public void An_invalid_name_is_refused(string text)
        {
            PackageName.TryParse(text, out var parsed, out var error).ShouldBeFalse();

            parsed.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.NameInvalid);
            error!.Location.ShouldBeNull();
        }

        [Fact]
        public void A_name_may_be_64_characters_and_no_more()
        {
            PackageName.TryParse("@a/" + new string('b', 64), out _, out _).ShouldBeTrue();
            PackageName.TryParse("@a/" + new string('b', 65), out _, out _).ShouldBeFalse();
        }

        [Fact]
        public void A_scope_may_be_39_characters_and_no_more()
        {
            PackageName.TryParse("@" + new string('a', 39) + "/b", out _, out _).ShouldBeTrue();
            PackageName.TryParse("@" + new string('a', 40) + "/b", out _, out _).ShouldBeFalse();
        }

        [Fact]
        public void Uppercase_is_refused_and_the_fix_is_the_lowercase_spelling()
        {
            PackageName.TryParse("@Thatplatypus/Crypto", out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.NameInvalid);
            error!.Reason.ShouldContain("lowercase");
            error.Fix.ShouldContain("\"@thatplatypus/crypto\"");
        }

        [Fact]
        public void A_hyphen_in_a_name_is_refused_and_the_fix_uses_an_underscore()
        {
            PackageName.TryParse("@thatplatypus/emoji-crypto", out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.NameInvalid);
            error!.Reason.ShouldContain("hyphen");
            error.Fix.ShouldContain("\"@thatplatypus/emoji_crypto\"");
        }

        [Theory]
        [InlineData("s")]
        [InlineData("runtime")]
        [InlineData("files")]
        [InlineData("sockets")]
        [InlineData("json")]
        [InlineData("testtube")]
        public void A_stock_package_name_is_reserved_under_any_scope(string name)
        {
            PackageName.TryParse($"@thatplatypus/{name}", out var parsed, out var error).ShouldBeFalse();

            parsed.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.NameReserved);
            error!.Reason.ShouldContain($"\"{name}\"");
        }

        [Fact]
        public void Two_parses_of_one_text_are_equal()
        {
            Sample.Name("@thatplatypus/crypto").ShouldBe(Sample.Name("@thatplatypus/crypto"));
            Sample.Name("@thatplatypus/crypto").ShouldNotBe(Sample.Name("@someone/crypto"));
        }

        [Fact]
        public void Names_are_ordered_by_their_full_text_ordinally()
        {
            var names = new[] { "@b/a", "@a/z", "@a/b", "@a-b/a" }.Select(Sample.Name).ToList();

            names.Sort();

            names.Select(name => name.ToString()).ShouldBe(["@a-b/a", "@a/b", "@a/z", "@b/a"]);
        }
    }
}
