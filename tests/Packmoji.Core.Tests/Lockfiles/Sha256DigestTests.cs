using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Lockfiles
{
    public sealed class Sha256DigestTests
    {
        private const string Valid = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        [Fact]
        public void Sixty_four_lowercase_hexadecimal_digits_are_a_digest()
        {
            Sha256Digest.TryParse(Valid, out var digest, out var error).ShouldBeTrue();

            error.ShouldBeNull();
            digest.ShouldNotBeNull().Hex.ShouldBe(Valid);
            digest.ToString().ShouldBe(Valid);
        }

        [Theory]
        [InlineData("")]
        [InlineData("9f86d081")]
        [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0")]
        [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a080")]
        [InlineData("9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08")]
        [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0g")]
        [InlineData("sha256:9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b")]
        [InlineData("９f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")] // begins with a fullwidth nine
        public void Anything_else_is_refused(string text)
        {
            Sha256Digest.TryParse(text, out var digest, out var error).ShouldBeFalse();

            digest.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.Sha256Invalid);
        }

        [Fact]
        public void A_digest_in_capitals_gets_a_fix_that_is_the_lowercase_spelling()
        {
            Sha256Digest.TryParse(Valid.ToUpperInvariant(), out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Fix.ShouldContain(Valid);
        }

        [Fact]
        public void Two_digests_of_one_text_are_equal()
        {
            Sample.Sha('a').ShouldBe(Sample.Sha('a'));
            Sample.Sha('a').ShouldNotBe(Sample.Sha('b'));
        }

        [Fact]
        public void The_digest_of_some_bytes_is_their_sha_256_in_lowercase()
        {
            // The two examples every description of SHA-256 gives.
            Sha256Digest.Of([]).Hex.ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
            Sha256Digest.Of(Encoding.ASCII.GetBytes("abc")).Hex.ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        }

        [Fact]
        public void A_digest_made_from_bytes_is_the_digest_read_from_its_text()
        {
            var made = Sha256Digest.Of(Encoding.ASCII.GetBytes("abc"));

            Sha256Digest.TryParse(made.Hex, out var read, out _).ShouldBeTrue();
            read.ShouldBe(made);
        }
    }
}
