using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests
{
    public sealed class AsciiTests
    {
        [Theory]
        [InlineData('0')]
        [InlineData('9')]
        public void An_ascii_digit_is_a_digit(char c) => Ascii.IsDigit(c).ShouldBeTrue();

        [Theory]
        [InlineData('٣')] // Arabic-Indic three, which char.IsDigit accepts
        [InlineData('３')] // fullwidth three
        [InlineData('a')]
        [InlineData('/')]
        [InlineData(':')]
        public void Nothing_else_is_a_digit(char c) => Ascii.IsDigit(c).ShouldBeFalse();

        [Theory]
        [InlineData('a')]
        [InlineData('z')]
        public void An_ascii_lowercase_letter_is_one(char c) => Ascii.IsLowerLetter(c).ShouldBeTrue();

        [Theory]
        [InlineData('а')] // Cyrillic a
        [InlineData('é')]
        [InlineData('A')]
        [InlineData('`')]
        [InlineData('{')]
        public void Nothing_else_is_a_lowercase_letter(char c) => Ascii.IsLowerLetter(c).ShouldBeFalse();

        [Theory]
        [InlineData('A', true)]
        [InlineData('Z', true)]
        [InlineData('É', false)]
        [InlineData('a', false)]
        [InlineData('@', false)]
        [InlineData('[', false)]
        public void Uppercase_means_ascii_uppercase(char c, bool expected) => Ascii.IsUpperLetter(c).ShouldBe(expected);

        [Theory]
        [InlineData('q', true)]
        [InlineData('Q', true)]
        [InlineData('7', false)]
        [InlineData('ß', false)]
        public void A_letter_is_an_ascii_letter_of_either_case(char c, bool expected) => Ascii.IsLetter(c).ShouldBe(expected);

        [Theory]
        [InlineData('0', true)]
        [InlineData('9', true)]
        [InlineData('a', true)]
        [InlineData('f', true)]
        [InlineData('g', false)]
        [InlineData('F', false)]
        [InlineData('ｆ', false)] // fullwidth f
        public void Hex_means_lowercase_ascii_hex(char c, bool expected) => Ascii.IsLowerHexDigit(c).ShouldBe(expected);
    }
}
